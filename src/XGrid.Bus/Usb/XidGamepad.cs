using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using XGrid.Core.Snapshots;

namespace XGrid.Bus.Usb;

/// <summary>Outcome of one USB transfer stage offered to a device function.</summary>
public enum UsbTransferResult
{
    /// <summary>Stage completed; any payload bytes were consumed or produced.</summary>
    Ack,

    /// <summary>Function has nothing to offer yet; the host retries later.</summary>
    Nak,

    /// <summary>Request is unsupported; the endpoint stalls.</summary>
    Stall,
}

/// <summary>Decoded 8-byte USB setup packet.</summary>
/// <remarks>Creates a setup packet from its raw fields.</remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct UsbSetupPacket(byte requestType, byte request, ushort value, ushort index, ushort length)
{

    /// <summary>Gets bmRequestType (direction, type, recipient).</summary>
    public byte RequestType { get; } = requestType;

    /// <summary>Gets bRequest.</summary>
    public byte Request { get; } = request;

    /// <summary>Gets wValue.</summary>
    public ushort Value { get; } = value;

    /// <summary>Gets wIndex.</summary>
    public ushort Index { get; } = index;

    /// <summary>Gets wLength.</summary>
    public ushort Length { get; } = length;
}

/// <summary>USB device function behind an OHCI controller (control endpoint 0 plus interrupt endpoints).</summary>
public interface IUsbDeviceFunction
{
    /// <summary>Gets the currently assigned device address.</summary>
    byte Address { get; }

    /// <summary>Gets whether a configuration is active.</summary>
    bool Configured { get; }

    /// <summary>Gets recent request outcomes, for diagnostics.</summary>
    IReadOnlyList<string> RequestLog { get; }

    /// <summary>Resets function state (address zero, unconfigured).</summary>
    void Reset();

    /// <summary>Handles a setup stage; payload returns data-stage bytes for IN requests.</summary>
    UsbTransferResult HandleSetup(in UsbSetupPacket setup, out byte[] payload);

    /// <summary>Consumes a collected control-OUT data stage.</summary>
    UsbTransferResult CompleteControlOut(byte[] data);

    /// <summary>Handles a bulk/interrupt OUT transfer.</summary>
    UsbTransferResult HandleOut(byte endpoint, byte[] data);

    /// <summary>Produces bulk/interrupt IN bytes (up to length).</summary>
    UsbTransferResult HandleIn(byte endpoint, int length, out byte[] payload);

    /// <summary>Commits deferred state after a successful control status stage.</summary>
    void CompleteStatusStage();
}

/// <summary>
/// Xbox gamepad (XID) device function: VID 0x045E / PID 0x0202, interface
/// class 0x58, two interrupt endpoints (IN/OUT ep2, 32-byte packets), 20-byte
/// input reports. Descriptor bytes and the request matrix follow the
/// XboxDevWiki XID facts as implemented by Cxbx-Reloaded's XidGamepad
/// (standard descriptors plus HID GET/SET_REPORT, vendor XID descriptor and
/// GET_CAPABILITIES); no code is shared. Rumble output is accepted and
/// dropped; the input report defaults to neutral (no buttons, centered
/// sticks) and can be driven via <see cref="SetInput"/>.
/// </summary>
public sealed class XidGamepad : IUsbDeviceFunction, ISnapshotable
{
    // ReSharper disable once UnusedMember.Local
    private const byte InterruptEndpoint = 2;

    private const byte DescriptorDevice = 1;
    private const byte DescriptorConfiguration = 2;
    private const byte DescriptorString = 3;
    private const ushort DescriptorXid = 0x4200;

    private const int InputReportSize = 20;
    private const int OutputReportSize = 6;

    private static readonly byte[] DeviceDescriptor =
    [
        0x12, 0x01, 0x10, 0x01, 0x00, 0x00, 0x00, 0x40,
        0x5E, 0x04, 0x02, 0x02, 0x00, 0x01, 0x01, 0x02,
        0x03, 0x01,
    ];

    private static readonly byte[] ConfigDescriptor =
    [
        0x09, 0x02, 0x20, 0x00, 0x01, 0x01, 0x00, 0x80, 0x32,
        0x09, 0x04, 0x00, 0x00, 0x02, 0x58, 0x42, 0x00, 0x00,
        0x07, 0x05, 0x82, 0x03, 0x20, 0x00, 0x04,
        0x07, 0x05, 0x02, 0x03, 0x20, 0x00, 0x04,
    ];

    private static readonly byte[] XidDescriptor =
    [
        0x10, 0x42, 0x00, 0x01, 0x01, 0x01, 0x14, 0x06,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
    ];

    private static readonly byte[] LangDescriptor = [0x04, 0x03, 0x09, 0x04];

    private readonly byte[] _inputReport = new byte[InputReportSize];
    private readonly byte[] _outputReport = new byte[OutputReportSize];
    private readonly byte[] _inputCapabilities = new byte[InputReportSize];
    private readonly byte[] _outputCapabilities = new byte[OutputReportSize];
    private readonly List<string> _requestLog = [];
    private readonly byte[] _heldAnalog = new byte[8];
    private readonly short[] _heldSticks = new short[4];
    private ushort _heldButtons;
    private byte _pendingAddress = 0xFF;
    private byte _pendingConfig;
    private bool _hasPendingConfig;

    /// <summary>Creates a neutral (no input) gamepad.</summary>
    public XidGamepad()
    {
        Reset();
    }

    /// <summary>Gets the assigned USB address; it starts at 0 and only changes when a SET_ADDRESS completes its status stage.</summary>
    public byte Address { get; private set; }

    /// <summary>Gets whether a configuration is active; it is set when a deferred SET_CONFIGURATION completes its status stage.</summary>
    public bool Configured { get; private set; }

    /// <summary>Gets the recent request log (at most 128 entries), for diagnostics.</summary>
    public IReadOnlyList<string> RequestLog => _requestLog;

    /// <summary>Resets function state (address zero, unconfigured): clears the pending address/configuration and the rumble output report, rebuilds the neutral input report, and restores the input/output capability reports.</summary>
    public void Reset()
    {
        Address = 0;
        Configured = false;
        _pendingAddress = 0xFF;
        _hasPendingConfig = false;
        Array.Clear(_outputReport, 0, _outputReport.Length);
        BuildReport();
        _inputCapabilities[0] = 0;
        _inputCapabilities[1] = InputReportSize;
        for (int i = 2; i < _inputCapabilities.Length; i++)
        {
            _inputCapabilities[i] = 0xFF;
        }

        _outputCapabilities[0] = 0;
        _outputCapabilities[1] = OutputReportSize;
        for (int i = 2; i < _outputCapabilities.Length; i++)
        {
            _outputCapabilities[i] = 0xFF;
        }
    }

    /// <summary>Saves address/config, held input, reports, capabilities, pending setup, and the log.</summary>
    public void SaveState(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(SnapshotVersion);
        writer.Write(Address);
        writer.Write(Configured);
        writer.Write(_heldButtons);
        writer.Write(_heldAnalog);
        for (int i = 0; i < _heldSticks.Length; i++)
        {
            writer.Write(_heldSticks[i]);
        }

        writer.Write(_inputReport);
        writer.Write(_outputReport);
        writer.Write(_inputCapabilities);
        writer.Write(_outputCapabilities);
        writer.Write(_pendingAddress);
        writer.Write(_pendingConfig);
        writer.Write(_hasPendingConfig);
        writer.Write(_requestLog.Count);
        foreach (string entry in _requestLog)
        {
            writer.Write(entry);
        }
    }

    /// <summary>Restores address/config, held input, reports, capabilities, pending setup, and the log.</summary>
    public void LoadState(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        int version = reader.ReadInt32();
        if (version != SnapshotVersion)
        {
            throw new InvalidDataException($"Unsupported gamepad snapshot version {version}.");
        }

        Address = reader.ReadByte();
        Configured = reader.ReadBoolean();
        _heldButtons = reader.ReadUInt16();
        CopyBytes(reader, _heldAnalog, 8);
        for (int i = 0; i < _heldSticks.Length; i++)
        {
            _heldSticks[i] = reader.ReadInt16();
        }

        CopyBytes(reader, _inputReport, InputReportSize);
        CopyBytes(reader, _outputReport, OutputReportSize);
        CopyBytes(reader, _inputCapabilities, InputReportSize);
        CopyBytes(reader, _outputCapabilities, OutputReportSize);
        _pendingAddress = reader.ReadByte();
        _pendingConfig = reader.ReadByte();
        _hasPendingConfig = reader.ReadBoolean();
        int requests = reader.ReadInt32();
        if (requests < 0 || requests > 128)
        {
            throw new InvalidDataException("Bad gamepad log snapshot.");
        }

        _requestLog.Clear();
        for (int i = 0; i < requests; i++)
        {
            _requestLog.Add(reader.ReadString());
        }
    }

    private const int SnapshotVersion = 1;

    private static void CopyBytes(BinaryReader reader, byte[] target, int length)
    {
        byte[] data = reader.ReadBytes(length);
        if (data.Length != length)
        {
            throw new InvalidDataException("Truncated gamepad snapshot.");
        }

        Array.Copy(data, target, length);
    }

    /// <summary>Drives the input report (buttons, 8 analog bytes, 4 sticks).</summary>
    /// <remarks>The held state survives <see cref="Reset"/> (port resets during
    /// enumeration restore it), modelling a button held from boot.</remarks>
    public void SetInput(ushort buttons, ReadOnlySpan<byte> analogButtons, ReadOnlySpan<short> sticks)
    {
        _heldButtons = buttons;
        Array.Clear(_heldAnalog, 0, _heldAnalog.Length);
        analogButtons.Slice(0, Math.Min(analogButtons.Length, _heldAnalog.Length)).CopyTo(_heldAnalog);
        Array.Clear(_heldSticks, 0, _heldSticks.Length);
        sticks.Slice(0, Math.Min(sticks.Length, _heldSticks.Length)).CopyTo(_heldSticks);
        BuildReport();
    }

    private void BuildReport()
    {
        Array.Clear(_inputReport, 0, _inputReport.Length);
        _inputReport[0] = 0;
        _inputReport[1] = InputReportSize;
        _inputReport[2] = (byte)_heldButtons;
        _inputReport[3] = (byte)(_heldButtons >> 8);
        Array.Copy(_heldAnalog, 0, _inputReport, 4, _heldAnalog.Length);
        for (int i = 0; i < 4; i++)
        {
            _inputReport[12 + (i * 2)] = (byte)_heldSticks[i];
            _inputReport[13 + (i * 2)] = (byte)(_heldSticks[i] >> 8);
        }
    }

    /// <summary>Handles a setup stage: serves the descriptor requests (device, configuration, string, vendor XID descriptor), HID GET_REPORT/SET_REPORT, GET_CAPABILITIES, and the standard deferred SET_ADDRESS/SET_CONFIGURATION and feature requests, logging each request and outcome.</summary>
    /// <param name="setup">The decoded 8-byte setup packet.</param>
    /// <param name="payload">Receives the data-stage bytes for IN requests; empty for OUT/none.</param>
    /// <returns>Ack for supported requests, Stall otherwise (including oversized GET_REPORT buffers).</returns>
    public UsbTransferResult HandleSetup(in UsbSetupPacket setup, out byte[] payload)
    {
        UsbTransferResult result = HandleSetupCore(in setup, out payload);
        LogRequest(FormattableString.Invariant(
            $"setup {setup.RequestType:X2}/{setup.Request:X2} {setup.Value:X4} len={setup.Length} -> {result} [{payload.Length}]"));
        return result;
    }

    private UsbTransferResult HandleSetupCore(in UsbSetupPacket setup, out byte[] payload)
    {
        payload = [];
        if (setup.RequestType == 0x80 && setup.Request == 0x06)
        {
            return GetDescriptor((byte)(setup.Value >> 8), (byte)setup.Value, setup.Length, out payload);
        }

        if (setup.RequestType == 0xC1 && setup.Request == 0x06 && setup.Value == DescriptorXid)
        {
            payload = Slice(XidDescriptor, setup.Length);
            return UsbTransferResult.Ack;
        }

        if (setup.RequestType == 0xC1 && setup.Request == 0x01)
        {
            if (setup.Value == 0x0100)
            {
                payload = Slice(_inputCapabilities, setup.Length);
                return UsbTransferResult.Ack;
            }

            if (setup.Value == 0x0200)
            {
                payload = Slice(_outputCapabilities, setup.Length);
                return UsbTransferResult.Ack;
            }

            return UsbTransferResult.Stall;
        }

        if (setup.RequestType == 0xA1 && setup.Request == 0x01 && setup.Value == 0x0100)
        {
            // Real pads stall oversized buffers instead of truncating them.
            if (setup.Length > InputReportSize)
            {
                return UsbTransferResult.Stall;
            }

            payload = Slice(_inputReport, setup.Length);
            return UsbTransferResult.Ack;
        }

        if (setup.RequestType == 0x21 && setup.Request == 0x09 && setup.Value == 0x0200)
        {
            return setup.Length == OutputReportSize ? UsbTransferResult.Ack : UsbTransferResult.Stall;
        }

        switch ((setup.RequestType, setup.Request))
        {
            case (0x00, 0x05):
                _pendingAddress = (byte)setup.Value;
                return UsbTransferResult.Ack;
            case (0x00, 0x09):
                _pendingConfig = (byte)setup.Value;
                _hasPendingConfig = true;
                return UsbTransferResult.Ack;
            case (0x80, 0x08):
                payload = [Configured ? (byte)1 : (byte)0];
                return UsbTransferResult.Ack;
            case (0x80, 0x00):
            case (0x81, 0x00):
            case (0x82, 0x00):
                payload = [0, 0];
                return UsbTransferResult.Ack;
            case (0x00, 0x03):
            case (0x00, 0x01):
            case (0x01, 0x03):
            case (0x01, 0x01):
            case (0x02, 0x03):
            case (0x02, 0x01):
            case (0x80, 0x0A):
            case (0x81, 0x0A):
            case (0x00, 0x0B):
                return UsbTransferResult.Ack;
            default:
                return UsbTransferResult.Stall;
        }
    }

    /// <summary>Consumes a collected control-OUT data stage (HID SET_REPORT): a 6-byte payload is accepted into the output (rumble) report; any other length stalls. The request is logged.</summary>
    /// <param name="data">The data-stage bytes; only an exactly 6-byte buffer is accepted.</param>
    /// <returns>Ack for a 6-byte rumble report, Stall otherwise.</returns>
    public UsbTransferResult CompleteControlOut(byte[] data)
    {
        UsbTransferResult result = CompleteControlOutCore(data);
        LogRequest(FormattableString.Invariant($"cout len={data.Length} -> {result}"));
        return result;
    }

    private UsbTransferResult CompleteControlOutCore(byte[] data)
    {
        if (data.Length != OutputReportSize)
        {
            return UsbTransferResult.Stall;
        }

        Array.Copy(data, _outputReport, OutputReportSize);
        return UsbTransferResult.Ack;
    }

    /// <summary>Handles a bulk/interrupt OUT transfer: data written to the interrupt OUT endpoint is copied into the output (rumble) report (truncated/padded via copy length); other endpoints stall. The request is logged.</summary>
    /// <param name="endpoint">The endpoint the host wrote to (expected interrupt endpoint 2).</param>
    /// <param name="data">The OUT payload; empty payloads leave the report unchanged.</param>
    /// <returns>Ack on the interrupt OUT endpoint, Stall otherwise.</returns>
    public UsbTransferResult HandleOut(byte endpoint, byte[] data)
    {
        UsbTransferResult result = HandleOutCore(endpoint, data);
        LogRequest(FormattableString.Invariant($"out ep{endpoint} len={data.Length} -> {result}"));
        return result;
    }

    private UsbTransferResult HandleOutCore(byte endpoint, byte[] data)
    {
        if (endpoint != InterruptEndpoint)
        {
            return UsbTransferResult.Stall;
        }

        if (data.Length > 0)
        {
            Array.Clear(_outputReport, 0, _outputReport.Length);
            Array.Copy(data, _outputReport, Math.Min(data.Length, _outputReport.Length));
        }

        return UsbTransferResult.Ack;
    }

    /// <summary>Produces bulk/interrupt IN bytes: returns the current 20-byte input report (buttons, analogs, sticks) from the interrupt IN endpoint; other endpoints or non-positive lengths stall. The request is logged.</summary>
    /// <param name="endpoint">The endpoint the host read from (expected interrupt endpoint 2).</param>
    /// <param name="length">The requested byte count, capped at the report size.</param>
    /// <param name="payload">Receives the input-report bytes on Ack; empty on Stall.</param>
    /// <returns>Ack with the input report, or Stall for other endpoints or a non-positive length.</returns>
    public UsbTransferResult HandleIn(byte endpoint, int length, out byte[] payload)
    {
        UsbTransferResult result = HandleInCore(endpoint, length, out payload);
        LogRequest(FormattableString.Invariant($"in ep{endpoint} len={length} -> {result} [{payload.Length}]"));
        return result;
    }

    private UsbTransferResult HandleInCore(byte endpoint, int length, out byte[] payload)
    {
        payload = [];
        if (endpoint != InterruptEndpoint || length <= 0)
        {
            return UsbTransferResult.Stall;
        }

        payload = Slice(_inputReport, length);
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
        payload = [];
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
                    1 => Slice(StringDescriptor("XGrid"), length),
                    2 => Slice(StringDescriptor("Xbox Controller"), length),
                    3 => Slice(StringDescriptor("1"), length),
                    _ => [],
                };
                return index <= 3 ? UsbTransferResult.Ack : UsbTransferResult.Stall;
            default:
                return UsbTransferResult.Stall;
        }
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
