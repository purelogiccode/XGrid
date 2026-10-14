using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using XGrid.Bus.Net;
using Xunit;

namespace XGrid.Bus.Tests;

public sealed class TcpProxyTests
{
    private static void RunEcho(TcpListener listener, ManualResetEventSlim stop)
    {
        while (!stop.IsSet)
        {
            Socket peer;
            try
            {
                peer = listener.AcceptSocket();
            }
            catch (SocketException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            try
            {
                byte[] buffer = new byte[8192];
                while (true)
                {
                    int read = peer.Receive(buffer);
                    if (read <= 0)
                    {
                        break;
                    }

                    int sent = 0;
                    while (sent < read)
                    {
                        sent += peer.Send(buffer, sent, read - sent, SocketFlags.None);
                    }
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
                peer.Dispose();
            }
        }
    }

    [Fact]
    public void LoopbackEcho_RelayedEndToEnd()
    {
        using var echo = new TcpListener(IPAddress.Loopback, 0);
        echo.Start();
        int echoPort = ((IPEndPoint)echo.LocalEndpoint).Port;
        using var echoStop = new ManualResetEventSlim(false);
        Thread echoThread = new(() => RunEcho(echo, echoStop))
        {
            IsBackground = true,
        };
        echoThread.Start();

        try
        {
            using var proxy = new TcpProxy(0, IPAddress.Loopback.ToString(), echoPort);
            proxy.Start();
            Assert.NotEqual(0, proxy.ListenPort);

            byte[] payload = new byte[4096];
            for (int i = 0; i < payload.Length; i++)
            {
                payload[i] = (byte)(i * 31);
            }

            using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            client.ReceiveTimeout = 5000;
            client.SendTimeout = 5000;
            client.Connect(IPAddress.Loopback, proxy.ListenPort);
            client.Send(payload);

            byte[] received = new byte[payload.Length];
            int got = 0;
            while (got < received.Length)
            {
                int n = client.Receive(received, got, received.Length - got, SocketFlags.None);
                Assert.True(n > 0, $"connection closed after {got} bytes");
                got += n;
            }

            Assert.Equal(payload, received);
            Assert.Equal(1, proxy.Connections);
            Assert.Equal(0, proxy.FailedConnections);
            Assert.True(
                SpinWait.SpinUntil(() => proxy.RelayedBytes >= payload.Length * 2, TimeSpan.FromSeconds(5)),
                $"relayed {proxy.RelayedBytes} of {payload.Length * 2} bytes after echo");
        }
        finally
        {
            echoStop.Set();
            echo.Stop();
            echoThread.Join(2000);
        }
    }

    [Fact]
    public void UnreachableTarget_CountsFailureAndClosesClient()
    {
        int deadPort;
        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            deadPort = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
        }

        using var proxy = new TcpProxy(0, IPAddress.Loopback.ToString(), deadPort);
        proxy.Start();

        using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        client.ReceiveTimeout = 5000;
        client.Connect(IPAddress.Loopback, proxy.ListenPort);

        int received;
        try
        {
            received = client.Receive(new byte[16]);
        }
        catch (SocketException)
        {
            received = -1;
        }

        Assert.True(received <= 0);
        Assert.Equal(1, proxy.Connections);
        Assert.Equal(1, proxy.FailedConnections);
        Assert.Equal(0, proxy.RelayedBytes);
    }

    [Fact]
    public void Dispose_StopsListener_ConnectRefused()
    {
        var proxy = new TcpProxy(0, IPAddress.Loopback.ToString(), 9);
        proxy.Start();
        int port = proxy.ListenPort;
        proxy.Dispose();

        using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        client.ReceiveTimeout = 5000;
        Assert.Throws<SocketException>(() => client.Connect(IPAddress.Loopback, port));
        Assert.Throws<ObjectDisposedException>(() => proxy.Start());
    }
}
