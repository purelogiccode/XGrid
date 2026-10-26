// SPDX-License-Identifier: GPL-2.0-or-later
// Ported from xemu 0.8.139 hw/xbox/mcpx/apu/apu_regs.h, hw/xbox/mcpx/apu/vp/vp.c
// and hw/xbox/mcpx/apu/fpconv.h.
// Original copyright (c) 2012 espes; (c) 2018-2019 Jannik Vogel;
// (c) 2019-2025 Matt Borgerson; fpconv.h (c) 2020-2025 Matt Borgerson.

namespace XGrid.Audio.Vp;

/// <summary>
/// The voice-processor frame geometry and the reference's sample conversions
/// (xemu <c>apu_regs.h</c> and <c>fpconv.h</c>).
/// </summary>
internal static class ApuVpFrame
{
    /// <summary>Samples the voice processor produces per frame (xemu NUM_SAMPLES_PER_FRAME).</summary>
    public const int SamplesPerFrame = 32;

    /// <summary>Mix bins the voice processor mixes into (xemu NUM_MIXBINS).</summary>
    public const int MixBinCount = 32;

    /// <summary>Voice slots (xemu MCPX_HW_MAX_VOICES).</summary>
    public const int MaxVoices = 256;

    /// <summary>Voices that go through the HRTF path (xemu MCPX_HW_MAX_3D_VOICES).</summary>
    public const int Max3dVoices = 64;

    /// <summary>Bytes of per-voice state in guest RAM (xemu NV_PAVS_SIZE).</summary>
    public const uint VoiceSize = 0x80;

    /// <summary>Stream segments (SSL A/B) per voice.</summary>
    public const int SslPerVoice = 2;

    /// <summary>Notifier index of the first SSL completion (xemu MCPX_HW_NOTIFIER_SSLA_DONE).</summary>
    public const int NotifierSslaDone = 0;

    /// <summary>Notifier block base offset in 16-byte units (xemu MCPX_HW_NOTIFIER_BASE_OFFSET).</summary>
    public const uint NotifierBaseOffset = 2;

    /// <summary>Notifier slots per voice (xemu MCPX_HW_NOTIFIER_COUNT).</summary>
    public const uint NotifierCountPerVoice = 4;

    /// <summary>Notifier status byte for a completed operation (NV1BA0_NOTIFICATION_STATUS_DONE_SUCCESS).</summary>
    public const byte NotificationDoneSuccess = 0x01;

    /// <summary>Envelope-state values (NV_PAVS_VOICE_PAR_STATE_EFCUR_*).</summary>
    public const uint EnvelopeOff = 0;
    /// <summary>Delay phase.</summary>
    public const uint EnvelopeDelay = 1;
    /// <summary>Attack phase.</summary>
    public const uint EnvelopeAttack = 2;
    /// <summary>Hold phase.</summary>
    public const uint EnvelopeHold = 3;
    /// <summary>Decay phase.</summary>
    public const uint EnvelopeDecay = 4;
    /// <summary>Sustain phase.</summary>
    public const uint EnvelopeSustain = 5;
    /// <summary>Release phase.</summary>
    public const uint EnvelopeRelease = 6;
    /// <summary>Force-release phase (turns the voice off).</summary>
    public const uint EnvelopeForceRelease = 7;

    /// <summary>Converts a signed 8-bit sample (xemu <c>int8_to_float</c>).</summary>
    /// <param name="value">The signed byte value.</param>
    /// <returns>The normalised sample.</returns>
    public static float Int8ToFloat(int value)
    {
        return value / 128.0f;
    }

    /// <summary>Converts an unsigned 8-bit sample (xemu <c>uint8_to_float</c>).</summary>
    /// <param name="value">The byte value.</param>
    /// <returns>The normalised sample.</returns>
    public static float UInt8ToFloat(uint value)
    {
        return (float)(((int)value - 0x80) / (1.0 * 0x80));
    }

    /// <summary>Converts a signed 16-bit sample (xemu <c>int16_to_float</c>).</summary>
    /// <param name="value">The word value.</param>
    /// <returns>The normalised sample.</returns>
    public static float Int16ToFloat(uint value)
    {
        return (float)((short)value / (1.0 * 0x8000));
    }

    /// <summary>Converts a signed 6.9 fixed-point value (xemu <c>s6p9_to_float</c>).</summary>
    /// <param name="value">The fixed-point value.</param>
    /// <returns>The sample.</returns>
    public static float S6p9ToFloat(uint value)
    {
        return (short)value / 512.0f;
    }

    /// <summary>Converts a signed 32-bit sample (xemu <c>int32_to_float</c>).</summary>
    /// <param name="value">The word value.</param>
    /// <returns>The normalised sample.</returns>
    public static float Int32ToFloat(uint value)
    {
        return (float)((int)value / (1.0 * 0x80000000));
    }

    /// <summary>Converts a signed 24-bit sample held in the low bits (xemu <c>int24_to_float</c>).</summary>
    /// <param name="value">The word value.</param>
    /// <returns>The normalised sample.</returns>
    public static float Int24ToFloat(uint value)
    {
        return Int32ToFloat(value << 8);
    }
}
