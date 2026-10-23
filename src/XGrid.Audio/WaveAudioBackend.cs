using System;
using System.IO;
using System.Text;

namespace XGrid.Audio;

/// <summary>
/// Host audio sink that records the APU's PCM stream to a RIFF/WAVE file
/// (48 kHz, 16-bit stereo). The file is a real host sink for the ported EP
/// output path: it can be played back or diffed byte-for-byte in a test.
/// </summary>
public sealed class WaveAudioBackend : IAudioBackend, IDisposable
{
    /// <summary>Sample rate written into the WAVE header.</summary>
    public const int SampleRate = 48000;

    /// <summary>Channel count written into the WAVE header.</summary>
    public const int Channels = 2;

    /// <summary>Bits per sample written into the WAVE header.</summary>
    public const int BitsPerSample = 16;

    private const int HeaderBytes = 44;

    private readonly FileStream _stream;
    private readonly BinaryWriter _writer;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="WaveAudioBackend"/> class.</summary>
    /// <param name="path">The output WAVE file path.</param>
    public WaveAudioBackend(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        _writer = new BinaryWriter(_stream, Encoding.ASCII, leaveOpen: true);
        WriteHeader(dataBytes: 0);
    }

    /// <summary>Gets the number of PCM bytes submitted so far.</summary>
    public long BytesWritten { get; private set; }

    /// <summary>Gets the number of stereo frames submitted so far.</summary>
    public long Frames => BytesWritten / (Channels * (BitsPerSample / 8));

    /// <inheritdoc/>
    public void Submit(ReadOnlySpan<byte> samples)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _writer.Write(samples);
        BytesWritten += samples.Length;
    }

    /// <summary>Patches the RIFF and data sizes and closes the file.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stream.Position = 0;
        WriteHeader(BytesWritten);
        _writer.Flush();
        _writer.Dispose();
        _stream.Dispose();
    }

    private void WriteHeader(long dataBytes)
    {
        int blockAlign = Channels * (BitsPerSample / 8);
        _writer.Write("RIFF"u8);
        _writer.Write((uint)(HeaderBytes - 8 + dataBytes));
        _writer.Write("WAVE"u8);
        _writer.Write("fmt "u8);
        _writer.Write(16u);
        _writer.Write((ushort)1);
        _writer.Write((ushort)Channels);
        _writer.Write((uint)SampleRate);
        _writer.Write((uint)(SampleRate * blockAlign));
        _writer.Write((ushort)blockAlign);
        _writer.Write((ushort)BitsPerSample);
        _writer.Write("data"u8);
        _writer.Write((uint)dataBytes);
    }
}
