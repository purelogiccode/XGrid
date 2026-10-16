using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace XGrid.Bus.Net;

/// <summary>
/// Host-side TCP port-forwarding proxy: the host half of the future guest-TCP
/// path (research-notes §115). Accepts host loopback connections on
/// <see cref="ListenPort"/> and relays bytes bidirectionally to
/// <see cref="TargetHost"/>:<see cref="TargetPort"/>. The guest half — a TCP
/// state machine terminating Xbox segments inside <see cref="SocketNetworkBackend"/>,
/// whose streams would attach here — is intentionally not built until a title
/// speaks TCP (the dashboard's only frame is a DHCP DISCOVER, §115).
/// <see cref="Start"/> must be called once; <see cref="Stop"/>/dispose is
/// idempotent and not restartable.
/// </summary>
public sealed class TcpProxy : IDisposable
{
    private readonly TcpListener _listener;
    private readonly List<Socket> _sockets = new();
    private readonly Lock _gate = new();
    private Thread? _acceptThread;
    private int _boundPort;
    private long _connections;
    private long _failedConnections;
    private long _relayedBytes;
    private volatile bool _stopping;
    private bool _disposed;

    /// <summary>Creates a proxy bound to a host loopback port that forwards to <paramref name="targetHost"/>:<paramref name="targetPort"/>.</summary>
    public TcpProxy(int listenPort, string targetHost, int targetPort)
    {
        ArgumentNullException.ThrowIfNull(targetHost);
        if (listenPort is < 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(listenPort));
        }

        if (targetPort is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(targetPort));
        }

        TargetHost = targetHost;
        TargetPort = targetPort;
        _listener = new TcpListener(IPAddress.Loopback, listenPort);
        _boundPort = listenPort;
    }

    /// <summary>Gets the bound loopback port (the real port after <see cref="Start"/> when constructed with 0).</summary>
    public int ListenPort => _boundPort;

    /// <summary>Gets the configured forward target host.</summary>
    public string TargetHost { get; }

    /// <summary>Gets the configured forward target port.</summary>
    public int TargetPort { get; }

    /// <summary>Gets the number of accepted client connections.</summary>
    public long Connections => Interlocked.Read(ref _connections);

    /// <summary>Gets the number of accepted connections whose target connect failed.</summary>
    public long FailedConnections => Interlocked.Read(ref _failedConnections);

    /// <summary>Gets the total bytes relayed in both directions.</summary>
    public long RelayedBytes => Interlocked.Read(ref _relayedBytes);

    /// <summary>Starts the loopback listener and the accept loop.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_acceptThread is not null)
        {
            return;
        }

        _listener.Start();
        _boundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptThread = new Thread(AcceptLoop)
        {
            IsBackground = true,
            Name = "XGrid.TcpProxy",
        };
        _acceptThread.Start();
    }

    /// <summary>Stops the listener, closes live relays, and joins the accept thread.</summary>
    public void Stop()
    {
        _stopping = true;
        try
        {
            _listener.Stop();
        }
        catch (ObjectDisposedException)
        {
        }

        Socket[] snapshot;
        lock (_gate)
        {
            snapshot = _sockets.ToArray();
            _sockets.Clear();
        }

        foreach (Socket socket in snapshot)
        {
            socket.Dispose();
        }

        _acceptThread?.Join(2000);
        _acceptThread = null;
    }

    /// <summary>Marks the proxy disposed: stops the listener, closes live relays, and joins the
    /// accept thread via <see cref="Stop"/>. Repeated calls are ignored.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    private void AcceptLoop()
    {
        while (!_stopping)
        {
            Socket client;
            try
            {
                client = _listener.AcceptSocket();
            }
            catch (SocketException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            Interlocked.Increment(ref _connections);
            lock (_gate)
            {
                if (_stopping)
                {
                    client.Dispose();
                    break;
                }

                _sockets.Add(client);
            }

            Thread relay = new(() => RelaySession(client))
            {
                IsBackground = true,
            };
            relay.Start();
        }
    }

    private void RelaySession(Socket client)
    {
        var target = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            target.Connect(TargetHost, TargetPort);
        }
        catch (SocketException)
        {
            Interlocked.Increment(ref _failedConnections);
            target.Dispose();
            RemoveAndClose(client);
            return;
        }
        catch (ObjectDisposedException)
        {
            target.Dispose();
            RemoveAndClose(client);
            return;
        }

        lock (_gate)
        {
            if (_stopping)
            {
                target.Dispose();
                RemoveAndCloseLocked(client);
                return;
            }

            _sockets.Add(target);
        }

        Thread reverse = new(() => Pump(target, client))
        {
            IsBackground = true,
        };
        reverse.Start();
        Pump(client, target);
        RemoveAndClose(client);
        RemoveAndClose(target);
    }

    private void Pump(Socket from, Socket to)
    {
        byte[] buffer = new byte[8192];
        try
        {
            while (!_stopping)
            {
                int read = from.Receive(buffer);
                if (read <= 0)
                {
                    break;
                }

                int sent = 0;
                while (sent < read)
                {
                    int chunk = to.Send(buffer, sent, read - sent, SocketFlags.None);
                    if (chunk <= 0)
                    {
                        break;
                    }

                    sent += chunk;
                }

                Interlocked.Add(ref _relayedBytes, sent);
            }
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            try
            {
                to.Shutdown(SocketShutdown.Send);
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private void RemoveAndClose(Socket socket)
    {
        lock (_gate)
        {
            RemoveAndCloseLocked(socket);
        }
    }

    private void RemoveAndCloseLocked(Socket socket)
    {
        _sockets.Remove(socket);
        try
        {
            socket.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }

        socket.Dispose();
    }
}
