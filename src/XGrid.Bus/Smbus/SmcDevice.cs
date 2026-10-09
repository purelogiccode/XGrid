using System.Collections.Generic;

namespace XGrid.Bus.Smbus;

/// <summary>Xbox System Management Controller (PIC16LC) on the SMBus (address 0x10).</summary>
public sealed class SmcDevice : SmbusDevice
{
    /// <summary>SMC SMBus address.</summary>
    public const byte DeviceAddress = 0x10;

    /// <summary>Interrupt status flag for tray closed (set at realize, as in xemu).</summary>
    public const byte TrayClosedStatus = 0x02;

    /// <summary>Scratch value selecting the short boot animation (xemu `short-animation`).</summary>
    public const byte ScratchShortAnimation = 0x04;

    private readonly char[] _version = { 'P', '0', '1' };
    private readonly Dictionary<byte, int> _readCounts = new();
    private readonly Dictionary<byte, int> _writeCounts = new();
    private int _versionIndex;
    private byte _command;
    private byte _scratch = ScratchShortAnimation;
    private byte _error;
    private byte _interruptStatus = TrayClosedStatus;

    /// <summary>Creates the SMC with default register values.</summary>
    public SmcDevice()
        : base(DeviceAddress)
    {
    }

    /// <summary>Gets or sets the tray state register (0x40 = no media).</summary>
    public byte TrayState { get; set; } = 0x40;

    /// <summary>Gets read counts per command byte (diagnostics).</summary>
    public IReadOnlyDictionary<byte, int> ReadCounts => _readCounts;

    /// <summary>Gets write counts per command byte (diagnostics).</summary>
    public IReadOnlyDictionary<byte, int> WriteCounts => _writeCounts;

    /// <summary>Gets or sets the AV pack register (default HDTV).</summary>
    public byte AvPack { get; set; } = 0x01;

    /// <inheritdoc />
    public override bool SendByte(byte value)
    {
        _command = value;
        CountWrite(value);
        return true;
    }

    /// <inheritdoc />
    public override bool WriteByte(byte command, byte value)
    {
        _command = command;
        CountWrite(command);
        switch (command)
        {
            case 0x01:
                _versionIndex = value;
                break;
            case 0x0E:
                _error = value;
                break;
            case 0x1B:
                _scratch = value;
                break;
            default:
                break;
        }

        return true;
    }

    /// <inheritdoc />
    public override bool TryReceiveByte(out byte value)
    {
        value = ReadRegister(_command++);
        return true;
    }

    /// <inheritdoc />
    public override bool TryReadByte(byte command, out byte value)
    {
        value = ReadRegister(command);
        return true;
    }

    private void CountWrite(byte command)
    {
        _writeCounts.TryGetValue(command, out int count);
        _writeCounts[command] = count + 1;
    }

    private byte ReadRegister(byte command)
    {
        _readCounts.TryGetValue(command, out int count);
        _readCounts[command] = count + 1;
        switch (command)
        {
            case 0x01:
                return (byte)_version[_versionIndex++ % _version.Length];
            case 0x03:
                return TrayState;
            case 0x04:
                return AvPack;
            case 0x0F:
                return _error;
            case 0x11:
                {
                    byte value = _interruptStatus;
                    _interruptStatus = 0;
                    return value;
                }

            case 0x1B:
                return _scratch;
            case 0x1C:
                return 0x52;
            case 0x1D:
                return 0x72;
            case 0x1E:
                return 0xEA;
            case 0x1F:
                return 0x46;
            default:
                return 0;
        }
    }
}
