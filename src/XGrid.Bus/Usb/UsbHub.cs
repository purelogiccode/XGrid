using System;
using System.Collections.Generic;
using System.IO;
using XGrid.Core.Snapshots;

namespace XGrid.Bus.Usb;

/// <summary>USB hub function that routes transfers to devices on its downstream ports.</summary>
public interface IUsbDeviceHub : IUsbDeviceFunction
{
    /// <summary>Finds a downstream device by address, considering enabled ports only.</summary>
    IUsbDeviceFunction? FindDevice(byte address);
}

/// <summary>
/// USB 1.1 hub (class 9) following xemu's QEMU <c>dev-hub.c</c> model: port
/// status/change bits, hub class requests (descriptor, status, set/clear
/// features), downstream enable gating, and the interrupt status report
/// bitmap on endpoint 1. xemu attaches a 4-port daughterboard hub to USB0
/// root port 1 and a 3-port hub per controller port with the XID gamepad on
/// downstream port 1. Address assignment is deferred to the control status
/// stage, as on real hardware.
/// </summary>
public sealed class UsbHub : IUsbDeviceHub, ISnapshotable
{
    /// <summary>Maximum downstream port count (QEMU MAX_PORTS).</summary>
    public const int MaxPorts = 8;

    private const byte StatusEndpoint = 1;

    private const byte DescriptorDevice = 1;
    private const byte DescriptorConfiguration = 2;
    private const byte DescriptorString = 3;

    private const ushort PortStatConnection = 0x0001;
    private const ushort PortStatEnable = 0x0002;
    private const ushort PortStatSuspend = 0x0004;
    private const ushort PortStatReset = 0x0010;
    private const ushort PortStatPower = 0x0100;
    private const ushort PortStatLowSpeed = 0x0200;

    private const ushort PortChangeConnection = 0x0001;
    private const ushort PortChangeEnable = 0x0002;
    private const ushort PortChangeSuspend = 0x0004;
    private const ushort PortChangeOvercurrent = 0x0008;
    private const ushort PortChangeReset = 0x0010;
    private const ushort PortChangeMask = 0x001F;

    private const ushort FeaturePortEnable = 1;
    private const ushort FeaturePortSuspend = 2;
    private const ushort FeaturePortReset = 4;
    private const ushort FeaturePortPower = 8;
    private const ushort FeaturePortChangeConnection = 16;
    private const ushort FeaturePortChangeEnable = 17;
    private const ushort FeaturePortChangeSuspend = 18;
    private const ushort FeaturePortChangeOvercurrent = 19;
    private const ushort FeaturePortChangeReset = 20;

    private static readonly byte[] DeviceDescriptor =
    {
        0x12, 0x01, 0x10, 0x01, 0x09, 0x00, 0x00, 0x08,
        0x09, 0x04, 0xAA, 0x55, 0x01, 0x01, 0x01, 0x02,
        0x03, 0x01,
    };

    private static readonly byte[] ConfigDescriptor =
    {
        0x09, 0x02, 0x19, 0x00, 0x01, 0x01, 0x00, 0xE0, 0x00,
        0x09, 0x04, 0x00, 0x00, 0x01, 0x09, 0x00, 0x00, 0x00,
        0x07, 0x05, 0x81, 0x03, 0x02, 0x00, 0xFF,
    };

    private static readonly byte[] LangDescriptor = { 0x04, 0x03, 0x09, 0x04 };

    private static readonly byte[] HubStatus = new byte[4];

    private sealed class HubPort
    {
        public IUsbDeviceFunction? Device;
        public ushort Status;
        public ushort Change;
    }

    private readonly HubPort[] _ports;
    private readonly byte[] _hubDescriptor;
    private readonly List<string> _requestLog = new();
    private bool _remoteWakeup;
    private byte _pendingAddress = 0xFF;
    private byte _pendingConfig;
    private bool _hasPendingConfig;

    /// <summary>Creates a hub with the given downstream port count.</summary>
    public UsbHub(int portCount)
    {
        if (portCount < 1 || portCount > MaxPorts)
        {
            throw new ArgumentOutOfRangeException(nameof(portCount));
        }

        _ports = new HubPort[portCount];
        for (int i = 0; i < portCount; i++)
        {
            _ports[i] = new HubPort();
        }

        _hubDescriptor = BuildHubDescriptor(portCount);
        Reset();
    }

    /// <summary>Gets the number of downstream ports.</summary>
    public int PortCount => _ports.Length;

    /// <summary>Gets the assigned USB address; it starts at 0 and only changes when a SET_ADDRESS completes its status stage.</summary>
    public byte Address { get; private set; }

    /// <summary>Gets whether a configuration is active; it is set when a deferred SET_CONFIGURATION completes its status stage.</summary>
    public bool Configured { get; private set; }

    /// <summary>Gets the recent hub request log (at most 128 entries), for diagnostics.</summary>
    public IReadOnlyList<string> RequestLog => _requestLog;

    /// <summary>Gets the downstream device attached to a port, for diagnostics.</summary>
    public IUsbDeviceFunction? GetPortDevice(int index)
    {
        return (uint)index < (uint)_ports.Length ? _ports[index].Device : null;
    }

    /// <summary>Gets the raw port status bits, for diagnostics.</summary>
    public ushort GetPortStatus(int index)
    {
        return (uint)index < (uint)_ports.Length ? _ports[index].Status : (ushort)0;
    }

    /// <summary>Gets the raw port change bits, for diagnostics.</summary>
    public ushort GetPortChange(int index)
    {
        return (uint)index < (uint)_ports.Length ? _ports[index].Change : (ushort)0;
    }

    /// <summary>Attaches a downstream device to a port, reporting a connect change.</summary>
    public void Attach(int index, IUsbDeviceFunction device)
    {
        ArgumentNullException.ThrowIfNull(device);
        if ((uint)index >= (uint)_ports.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        _ports[index].Device = device;
        PortUpdate(index);
    }

    /// <summary>Resets function state (address zero, unconfigured): clears remote-wakeup and the pending address/configuration, and returns every downstream port to the powered state with status and change bits cleared.</summary>
    public void Reset()
    {
        Address = 0;
        Configured = false;
        _remoteWakeup = false;
        _pendingAddress = 0xFF;
        _hasPendingConfig = false;
        for (int i = 0; i < _ports.Length; i++)
        {
            HubPort port = _ports[i];
            port.Status = 0;
            port.Change = 0;
            PortSet(port, PortStatPower);
            PortUpdate(i);
        }
    }

    /// <summary>Saves address/config, per-port status/change, pending setup, and cascaded devices.</summary>
    /// <remarks>Topology is caller-owned (same hub/port wiring on load); each downstream
    /// device is cascaded when snapshotable. Quiesce: save at a frame boundary with no
    /// control transfer mid-status-stage.</remarks>
    public void SaveState(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(SnapshotVersion);
        writer.Write(Address);
        writer.Write(Configured);
        writer.Write(_remoteWakeup);
        writer.Write(_pendingAddress);
        writer.Write(_pendingConfig);
        writer.Write(_hasPendingConfig);
        writer.Write(_ports.Length);
        for (int i = 0; i < _ports.Length; i++)
        {
            writer.Write(_ports[i].Status);
            writer.Write(_ports[i].Change);
            writer.Write(_ports[i].Device is not null);
            if (_ports[i].Device is ISnapshotable snapshotable)
            {
                snapshotable.SaveState(writer);
            }
            else if (_ports[i].Device is not null)
            {
                throw new InvalidDataException($"USB hub port {i} device is not snapshotable.");
            }
        }

        writer.Write(_requestLog.Count);
        foreach (string entry in _requestLog)
        {
            writer.Write(entry);
        }
    }

    /// <summary>Restores address/config, per-port status/change, pending setup, and cascaded devices.</summary>
    public void LoadState(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        int version = reader.ReadInt32();
        if (version != SnapshotVersion)
        {
            throw new InvalidDataException($"Unsupported USB hub snapshot version {version}.");
        }

        Address = reader.ReadByte();
        Configured = reader.ReadBoolean();
        _remoteWakeup = reader.ReadBoolean();
        _pendingAddress = reader.ReadByte();
        _pendingConfig = reader.ReadByte();
        _hasPendingConfig = reader.ReadBoolean();
        int ports = reader.ReadInt32();
        if (ports != _ports.Length)
        {
            throw new InvalidDataException("USB hub port-count mismatch on snapshot load.");
        }

        for (int i = 0; i < _ports.Length; i++)
        {
            _ports[i].Status = reader.ReadUInt16();
            _ports[i].Change = reader.ReadUInt16();
            bool present = reader.ReadBoolean();
            if (present != (_ports[i].Device is not null))
            {
                throw new InvalidDataException($"USB hub port {i} topology mismatch on snapshot load.");
            }

            if (_ports[i].Device is ISnapshotable snapshotable)
            {
                snapshotable.LoadState(reader);
            }
        }

        int requests = reader.ReadInt32();
        if (requests < 0 || requests > 128)
        {
            throw new InvalidDataException("Bad USB hub log snapshot.");
        }

        _requestLog.Clear();
        for (int i = 0; i < requests; i++)
        {
            _requestLog.Add(reader.ReadString());
        }
    }

    private const int SnapshotVersion = 1;

    /// <summary>Finds a downstream device by address, considering enabled ports only and recursing into attached child hubs.</summary>
    /// <param name="address">The USB address to look up.</param>
    /// <returns>The device function at that address, or null when no enabled port leads to it.</returns>
    public IUsbDeviceFunction? FindDevice(byte address)
    {
        for (int i = 0; i < _ports.Length; i++)
        {
            HubPort port = _ports[i];
            if ((port.Status & PortStatEnable) == 0 || port.Device is null)
            {
                continue;
            }

            if (port.Device.Address == address)
            {
                return port.Device;
            }

            if (port.Device is IUsbDeviceHub child)
            {
                IUsbDeviceFunction? found = child.FindDevice(address);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    /// <summary>Handles a setup stage: serves standard requests (descriptor, status, feature, deferred SET_ADDRESS/SET_CONFIGURATION) and hub-class requests (hub/port descriptors, port status and feature control), logging each request and outcome.</summary>
    /// <param name="setup">The decoded 8-byte setup packet.</param>
    /// <param name="payload">Receives the data-stage bytes for IN requests; empty for OUT/none.</param>
    /// <returns>Ack for supported requests, Stall otherwise.</returns>
    public UsbTransferResult HandleSetup(in UsbSetupPacket setup, out byte[] payload)
    {
        UsbTransferResult result = HandleSetupCore(in setup, out payload);
        LogRequest(FormattableString.Invariant(
            $"setup {setup.RequestType:X2}/{setup.Request:X2} {setup.Value:X4} idx={setup.Index} len={setup.Length} -> {result} [{payload.Length}]"));
        return result;
    }

    private UsbTransferResult HandleSetupCore(in UsbSetupPacket setup, out byte[] payload)
    {
        payload = Array.Empty<byte>();
        switch ((setup.RequestType, setup.Request))
        {
            case (0x80, 0x06):
                return GetDescriptor((byte)(setup.Value >> 8), (byte)setup.Value, setup.Length, out payload);
            case (0x00, 0x05):
                _pendingAddress = (byte)setup.Value;
                return UsbTransferResult.Ack;
            case (0x00, 0x09) when setup.Value <= 1:
                _pendingConfig = (byte)setup.Value;
                _hasPendingConfig = true;
                return UsbTransferResult.Ack;
            case (0x80, 0x08):
                payload = new[] { Configured ? (byte)1 : (byte)0 };
                return UsbTransferResult.Ack;
            case (0x80, 0x00):
                payload = new[] { (byte)(1 | (_remoteWakeup ? 2 : 0)), (byte)0 };
                return UsbTransferResult.Ack;
            case (0x00, 0x01) when setup.Value == 1:
                _remoteWakeup = false;
                return UsbTransferResult.Ack;
            case (0x00, 0x03) when setup.Value == 1:
                _remoteWakeup = true;
                return UsbTransferResult.Ack;
            case (0x81, 0x0A):
                payload = new byte[] { 0 };
                return UsbTransferResult.Ack;
            case (0x01, 0x0B) when setup.Value == 0:
                return UsbTransferResult.Ack;
            case (0xA0, 0x06):
                payload = Slice(_hubDescriptor, setup.Length);
                return UsbTransferResult.Ack;
            case (0xA0, 0x00):
                payload = Slice(HubStatus, setup.Length);
                return UsbTransferResult.Ack;
            case (0xA3, 0x00) when setup.Index >= 1 && setup.Index <= PortCount:
                payload = PortStatus((int)setup.Index - 1, setup.Length);
                return UsbTransferResult.Ack;
            case (0x20, 0x01):
            case (0x20, 0x03):
                return setup.Value <= 1 ? UsbTransferResult.Ack : UsbTransferResult.Stall;
            case (0x23, 0x03) when setup.Index >= 1 && setup.Index <= PortCount:
                return SetPortFeature((int)setup.Index - 1, setup.Value);
            case (0x23, 0x01) when setup.Index >= 1 && setup.Index <= PortCount:
                return ClearPortFeature((int)setup.Index - 1, setup.Value);
            case (0x02, 0x01) when setup.Value != 0 || setup.Index == 0x81:
                return UsbTransferResult.Ack;
            default:
                return UsbTransferResult.Stall;
        }
    }

    /// <summary>Consumes a collected control-OUT data stage. The hub has no control-OUT data stage, so the request is logged and always stalls.</summary>
    /// <param name="data">The data-stage bytes, which are discarded.</param>
    /// <returns>Always Stall.</returns>
    public UsbTransferResult CompleteControlOut(byte[] data)
    {
        LogRequest(FormattableString.Invariant($"cout len={data.Length} -> {UsbTransferResult.Stall}"));
        return UsbTransferResult.Stall;
    }

    /// <summary>Handles a bulk/interrupt OUT transfer. The hub exposes no OUT endpoint, so the transfer is logged and always stalls.</summary>
    /// <param name="endpoint">The endpoint the host wrote to.</param>
    /// <param name="data">The OUT payload, which is discarded.</param>
    /// <returns>Always Stall.</returns>
    public UsbTransferResult HandleOut(byte endpoint, byte[] data)
    {
        LogRequest(FormattableString.Invariant($"out ep{endpoint} len={data.Length} -> {UsbTransferResult.Stall}"));
        return UsbTransferResult.Stall;
    }

    /// <summary>Produces bulk/interrupt IN bytes for the interrupt status endpoint: returns the per-port change bitmap (one bit per downstream port with a pending change) when anything changed, or Nak when nothing has; other endpoints stall.</summary>
    /// <param name="endpoint">The endpoint the host read from.</param>
    /// <param name="length">The requested byte count; 1 collapses the bitmap to a single byte.</param>
    /// <param name="payload">Receives the change bitmap bytes on Ack; empty on Nak or Stall.</param>
    /// <returns>Ack with the bitmap, Nak when no port has a pending change, or Stall for other endpoints or too-short reads.</returns>
    public UsbTransferResult HandleIn(byte endpoint, int length, out byte[] payload)
    {
        payload = Array.Empty<byte>();
        if (endpoint != StatusEndpoint)
        {
            LogRequest(FormattableString.Invariant($"in ep{endpoint} len={length} -> {UsbTransferResult.Stall} [0]"));
            return UsbTransferResult.Stall;
        }

        int count = (_ports.Length + 1 + 7) / 8;
        if (length == 1)
        {
            count = 1;
        }
        else if (length < count)
        {
            LogRequest(FormattableString.Invariant($"in ep{endpoint} len={length} -> {UsbTransferResult.Stall} [0]"));
            return UsbTransferResult.Stall;
        }

        ushort status = 0;
        for (int i = 0; i < _ports.Length; i++)
        {
            if (_ports[i].Change != 0)
            {
                status |= (ushort)(1 << (i + 1));
            }
        }

        if (status == 0)
        {
            LogRequest(FormattableString.Invariant($"in ep{endpoint} len={length} -> {UsbTransferResult.Nak} [0]"));
            return UsbTransferResult.Nak;
        }

        byte[] result = new byte[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = (byte)(status >> (i * 8));
        }

        payload = result;
        LogRequest(FormattableString.Invariant($"in ep{endpoint} len={length} -> {UsbTransferResult.Ack} [{count}]"));
        return UsbTransferResult.Ack;
    }

    /// <summary>Commits deferred state after a successful control status stage: applies the pending SET_ADDRESS and SET_CONFIGURATION and logs the outcome.</summary>
    public void CompleteStatusStage()
    {
        if (_pendingAddress != 0xFF)
        {
            Address = _pendingAddress;
            _pendingAddress = 0xFF;
        }

        if (_hasPendingConfig)
        {
            Configured = _pendingConfig != 0;
            _hasPendingConfig = false;
        }

        LogRequest(FormattableString.Invariant($"status addr={Address} cfg={Configured}"));
    }

    private static UsbTransferResult GetDescriptor(byte type, byte index, ushort length, out byte[] payload)
    {
        payload = Array.Empty<byte>();
        switch (type)
        {
            case DescriptorDevice:
                payload = Slice(DeviceDescriptor, length);
                return UsbTransferResult.Ack;
            case DescriptorConfiguration:
                payload = Slice(ConfigDescriptor, length);
                return UsbTransferResult.Ack;
            case DescriptorString:
                payload = index switch
                {
                    0 => Slice(LangDescriptor, length),
                    1 => Slice(StringDescriptor("QEMU"), length),
                    2 => Slice(StringDescriptor("QEMU USB Hub"), length),
                    3 => Slice(StringDescriptor("314159"), length),
                    _ => Array.Empty<byte>(),
                };
                return index <= 3 ? UsbTransferResult.Ack : UsbTransferResult.Stall;
            default:
                return UsbTransferResult.Stall;
        }
    }

    private byte[] PortStatus(int index, int length)
    {
        HubPort port = _ports[index];
        byte[] result =
        {
            (byte)port.Status,
            (byte)(port.Status >> 8),
            (byte)port.Change,
            (byte)(port.Change >> 8),
        };
        return Slice(result, length);
    }

    private UsbTransferResult SetPortFeature(int index, ushort feature)
    {
        HubPort port = _ports[index];
        switch (feature)
        {
            case FeaturePortSuspend:
                port.Status |= PortStatSuspend;
                return UsbTransferResult.Ack;
            case FeaturePortReset:
                PortSet(port, PortStatReset);
                PortClear(port, PortStatReset);
                if (port.Device is not null)
                {
                    port.Device.Reset();
                    PortSet(port, PortStatEnable);
                }

                return UsbTransferResult.Ack;
            case FeaturePortPower:
                // The xemu hub is created without port power switching.
                return UsbTransferResult.Ack;
            default:
                return UsbTransferResult.Stall;
        }
    }

    private UsbTransferResult ClearPortFeature(int index, ushort feature)
    {
        HubPort port = _ports[index];
        switch (feature)
        {
            case FeaturePortEnable:
                port.Status &= unchecked((ushort)~PortStatEnable);
                return UsbTransferResult.Ack;
            case FeaturePortChangeEnable:
                port.Change &= unchecked((ushort)~PortChangeEnable);
                return UsbTransferResult.Ack;
            case FeaturePortSuspend:
                PortClear(port, PortStatSuspend);
                return UsbTransferResult.Ack;
            case FeaturePortChangeSuspend:
                port.Change &= unchecked((ushort)~PortChangeSuspend);
                return UsbTransferResult.Ack;
            case FeaturePortChangeConnection:
                port.Change &= unchecked((ushort)~PortChangeConnection);
                return UsbTransferResult.Ack;
            case FeaturePortChangeOvercurrent:
                port.Change &= unchecked((ushort)~PortChangeOvercurrent);
                return UsbTransferResult.Ack;
            case FeaturePortChangeReset:
                port.Change &= unchecked((ushort)~PortChangeReset);
                return UsbTransferResult.Ack;
            case FeaturePortPower:
                // The xemu hub is created without port power switching.
                return UsbTransferResult.Ack;
            default:
                return UsbTransferResult.Stall;
        }
    }

    private void PortUpdate(int index)
    {
        HubPort port = _ports[index];
        if (port.Device is null)
        {
            return;
        }

        PortSet(port, PortStatConnection);
        PortClear(port, PortStatLowSpeed);
    }

    private static void PortSet(HubPort port, ushort status)
    {
        if ((port.Status & status) == status)
        {
            return;
        }

        port.Status |= status;
        if ((status & PortChangeMask) != 0)
        {
            port.Change |= status;
        }
    }

    private static void PortClear(HubPort port, ushort status)
    {
        if ((port.Status & status) == 0)
        {
            return;
        }

        port.Status &= unchecked((ushort)~status);
        if ((status & PortChangeMask) != 0)
        {
            port.Change |= status;
        }
    }

    private static byte[] BuildHubDescriptor(int portCount)
    {
        int removableBytes = (portCount + 1 + 7) / 8;
        int powerMaskBytes = (portCount + 7) / 8;
        byte[] descriptor = new byte[7 + removableBytes + powerMaskBytes];
        descriptor[0] = (byte)descriptor.Length;
        descriptor[1] = 0x29;
        descriptor[2] = (byte)portCount;
        descriptor[3] = 0x0A;
        descriptor[4] = 0x00;
        descriptor[5] = 0x01;
        descriptor[6] = 0x00;
        for (int i = 0; i < powerMaskBytes; i++)
        {
            descriptor[7 + removableBytes + i] = 0xFF;
        }

        return descriptor;
    }

    private void LogRequest(string entry)
    {
        if (_requestLog.Count >= 128)
        {
            _requestLog.RemoveAt(0);
        }

        _requestLog.Add(entry);
    }

    private static byte[] Slice(byte[] source, int length)
    {
        int count = Math.Min(length, source.Length);
        byte[] result = new byte[count];
        Array.Copy(source, result, count);
        return result;
    }

    private static byte[] StringDescriptor(string text)
    {
        byte[] result = new byte[2 + (text.Length * 2)];
        result[0] = (byte)result.Length;
        result[1] = 0x03;
        for (int i = 0; i < text.Length; i++)
        {
            result[2 + (i * 2)] = (byte)text[i];
            result[3 + (i * 2)] = 0;
        }

        return result;
    }
}
