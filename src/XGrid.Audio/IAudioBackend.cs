namespace XGrid.Audio;

/// <summary>
/// Host audio output for the MCPX APU: interleaved 16-bit signed stereo PCM at
/// 48 kHz, the format of the EP DSP's output FIFO (xemu's monitor frame is
/// <c>int16_t[256][2]</c>).
/// </summary>
public interface IAudioBackend
{
    /// <summary>Submits one block of interleaved 16-bit stereo PCM.</summary>
    /// <param name="samples">The sample bytes (little-endian 16-bit pairs).</param>
    void Submit(System.ReadOnlySpan<byte> samples);
}
