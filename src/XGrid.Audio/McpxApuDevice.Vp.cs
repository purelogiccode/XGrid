// SPDX-License-Identifier: GPL-2.0-or-later
// Ported from xemu 0.8.139 hw/xbox/mcpx/apu/vp/vp.c (the per-frame voice
// processor: envelopes, sample fetch, resampling, filtering and mix-bin
// accumulation).
// Original copyright (c) 2012 espes; (c) 2018-2019 Jannik Vogel;
// (c) 2019-2025 Matt Borgerson.

using System;
using System.IO;
using XGrid.Audio.Vp;

namespace XGrid.Audio;

/// <summary>
/// The voice processor's per-frame mix path (xemu <c>mcpx_apu_vp_frame</c> and
/// <c>voice_process</c>). This is the half of the APU that turns the guest's
/// voice descriptors into the 32 mix bins the GP DSP microcode consumes; the
/// front-end PIO and the voice-list walk live in the main file.
/// </summary>
public sealed partial class McpxApuDevice
{
    private const uint VoiceCfgVbin = 0x00;
    private const uint VoiceCfgEnv0 = 0x08;
    private const uint VoiceCfgEnv1 = 0x10;
    private const uint VoiceCfgMisc = 0x18;
    private const uint VoiceCfgHrtfTarget = 0x1C;
    private const uint VoiceCurEcnt = 0x34;
    private const uint VoiceTarVola = 0x60;
    private const uint VoiceTarVolb = 0x64;
    private const uint VoiceTarVolc = 0x68;
    private const uint VoiceTarLfoEnv = 0x6C;
    private const uint VoiceTarFca = 0x74;

    private const uint VoiceFmtStereo = 1u << 27;
    private const uint VoiceFmtClearMix = 1u << 26;
    private const uint VoiceFmtMultipass = 1u << 21;
    private const uint VoiceFmtSampleSize = 0x3u << 28;
    private const uint VoiceFmtContainerSize = 0x3u << 30;
    private const uint VoiceFmtSamplesPerBlock = 0x1Fu << 16;
    private const uint VoiceFmtV6Bin = 0x1Fu << 0;
    private const uint VoiceFmtV7Bin = 0x1Fu << 5;

    private const uint VoiceParOffsetEaLevel = 0xFF000000;
    private const uint VoiceParNextEfLevel = 0xFF000000;
    private const uint VoiceCurEcntEaCount = 0x0000FFFF;
    private const uint VoiceCurEcntEfCount = 0xFFFF0000;

    private const uint VoiceEnv0AttackRate = 0xFFF;
    private const uint VoiceEnv0DelayTime = 0xFFFu << 12;
    private const uint VoiceEnv0PitchScale = 0xFFu << 24;
    private const uint VoiceEnvaDecayRate = 0xFFF;
    private const uint VoiceEnvaHoldTime = 0xFFFu << 12;
    private const uint VoiceEnvaSustainLevel = 0xFFu << 24;
    private const uint VoiceEnvfHoldTime = 0xFFFu << 12;
    private const uint VoiceMiscEfReleaseRate = 0xFFF;
    private const uint VoiceMiscFmode = 3u << 16;
    private const uint VoiceLfoEnvEaReleaseRate = 0xFFF;

    private const uint VoiceVolaVolume0 = 0x0000FFF0;
    private const uint VoiceVolaVolume1 = 0xFFF00000;
    private const uint VoiceVolaVolume6Low = 0x0000000F;
    private const uint VoiceVolaVolume7Low = 0x000F0000;
    private const uint VoiceVolbVolume2 = 0x0000FFF0;
    private const uint VoiceVolbVolume3 = 0xFFF00000;
    private const uint VoiceVolbVolume6Mid = 0x0000000F;
    private const uint VoiceVolbVolume7Mid = 0x000F0000;
    private const uint VoiceVolcVolume4 = 0x0000FFF0;
    private const uint VoiceVolcVolume5 = 0xFFF00000;
    private const uint VoiceVolcVolume6High = 0x0000000F;
    private const uint VoiceVolcVolume7High = 0x000F0000;

    private const uint VoicePitchMask = 0xFFFF0000;
    private const uint VoiceVbin0 = 0x1Fu << 0;
    private const uint VoiceVbin1 = 0x1Fu << 5;
    private const uint VoiceVbin2 = 0x1Fu << 10;
    private const uint VoiceVbin3 = 0x1Fu << 16;
    private const uint VoiceVbin4 = 0x1Fu << 21;
    private const uint VoiceVbin5 = 0x1Fu << 26;
    private const uint VoiceTarFcaFc0 = 0x0000FFFF;
    private const uint VoiceTarFcaFc1 = 0xFFFF0000;
    private const uint VoiceHrtfNullHandle = 0xFFFF;

    /// <summary>APU frames of 32 samples in one XGrid APU frame of 256 samples.</summary>
    private const int VpFramesPerDeviceFrame = (int)FrameSamples / ApuVpFrame.SamplesPerFrame;

    private const int SslPageSize = 4096;

    private readonly float[] _mixBins = new float[ApuVpFrame.MixBinCount * ApuVpFrame.SamplesPerFrame];
    private readonly float[] _vpSamples = new float[ApuVpFrame.SamplesPerFrame * 2];
    private readonly byte[] _vpAdpcmBlock = new byte[ApuVpAdpcm.BlockBytes * 32];
    private readonly short[] _vpAdpcmDecoded = new short[65 * 2];
    private readonly SvFilter[] _vpSvf = new SvFilter[ApuVpFrame.MaxVoices * 2];
    private readonly HrtfFilter?[] _vpHrtf = new HrtfFilter?[ApuVpFrame.Max3dVoices];
    private readonly VpResampler?[] _vpResamplers = new VpResampler?[ApuVpFrame.MaxVoices];
    private readonly VpMixerState _vpState = new();

    /// <summary>
    /// Mixer-only state the reference keeps in <c>MCPXAPUVPState</c>: the
    /// submix/HRTF headroom, the HRTF submixes, the coefficient table and the
    /// per-voice filter history.
    /// </summary>
    private sealed class VpMixerState
    {
        /// <summary>Per-voice HRIR coefficients and interaural delay (xemu <c>vp.hrtf.entries</c>).</summary>
        public float[]?[] HrirLeft { get; } = new float[128][];

        /// <summary>Right-channel HRIR coefficients.</summary>
        public float[]?[] HrirRight { get; } = new float[128][];

        /// <summary>Per-entry interaural delay.</summary>
        public float[] Itd { get; } = new float[128];

        /// <summary>Current HRTF coefficient entry being written.</summary>
        public int CurrentEntry;

        /// <summary>Headroom shift applied to 3D voices (xemu <c>hrtf_headroom</c>).</summary>
        public byte HrtfHeadroom;

        /// <summary>Mix bins the four HRTF channels feed (xemu <c>hrtf_submix</c>).</summary>
        public byte[] HrtfSubmix { get; } = new byte[4];

        /// <summary>Per-bin headroom shift (xemu <c>submix_headroom</c>).</summary>
        public byte[] SubmixHeadroom { get; } = new byte[ApuVpFrame.MixBinCount];

        /// <summary>Returns (allocating on demand) an HRIR entry's coefficient arrays.</summary>
        /// <param name="entry">The entry index.</param>
        /// <returns>The left and right coefficient arrays.</returns>
        public (float[] Left, float[] Right) GetEntry(int entry)
        {
            HrirLeft[entry] ??= new float[HrtfFilter.NumTaps];
            HrirRight[entry] ??= new float[HrtfFilter.NumTaps];
            return (HrirLeft[entry]!, HrirRight[entry]!);
        }
    }

    /// <summary>Per-voice streaming resampler state (replaces libsamplerate).</summary>
    private sealed class VpResampler
    {
        /// <summary>Current source frame, left.</summary>
        public float CurrentLeft;

        /// <summary>Current source frame, right.</summary>
        public float CurrentRight;

        /// <summary>Next source frame, left.</summary>
        public float NextLeft;

        /// <summary>Next source frame, right.</summary>
        public float NextRight;

        /// <summary>Fractional position between the current and next source frame.</summary>
        public double Fraction;

        /// <summary>True once the first source frames have been pulled.</summary>
        public bool Primed;
    }

    /// <summary>
    /// Runs one voice-processor frame (xemu <c>mcpx_apu_vp_frame</c>): walks the
    /// 2D, 3D and multipass voice lists, mixes every active voice into
    /// <see cref="_mixBins"/> and reports idle voices to the front end.
    /// </summary>
    private void ProcessVpFrame()
    {
        Array.Clear(_mixBins);

        for (int list = 0; list < VoiceListTop.Length; list++)
        {
            uint currentReg = VoiceListCurrent[list] >> 2;
            uint nextReg = VoiceListNext[list] >> 2;
            _regs[currentReg] = _regs[VoiceListTop[list] >> 2];

            for (uint i = 0; _regs[currentReg] != HandleMask16; i++)
            {
                if (i >= MaxVoices)
                {
                    Log($"voice list overflow list={list} top={_regs[VoiceListTop[list] >> 2]:X} v={_regs[currentReg]:X}");
                    break;
                }

                uint voice = _regs[currentReg];
                _regs[nextReg] = GetVoiceMask(voice, VoiceTarPitchLink, HandleMask16);
                if (!IsVoiceActive(voice))
                {
                    DeliverIdleVoice((ushort)voice);
                }
                else
                {
                    VoiceProcess(voice, list);
                }

                _regs[currentReg] = _regs[nextReg];
            }
        }
    }

    private bool IsVoiceActive(uint voice)
    {
        return (GetVoiceMask(voice, VoiceParState, uint.MaxValue) & VoiceStateActiveVoice) != 0;
    }

    /// <summary>
    /// Mixes one active voice into the frame's mix bins (xemu <c>voice_process</c>).
    /// </summary>
    /// <param name="voice">The voice handle.</param>
    /// <param name="voiceList">The list the voice was found on (0=2D, 1=3D, 2=MP).</param>
    private void VoiceProcess(uint voice, int voiceList)
    {
        uint fmt = GetVoiceMask(voice, VoiceCfgFmt, uint.MaxValue);
        bool stereo = (fmt & VoiceFmtStereo) != 0;
        int channels = stereo ? 2 : 1;

        if ((GetVoiceMask(voice, VoiceParState, VoiceStatePaused)) != 0)
        {
            return;
        }

        float efValue = StepEnvelope(
            voice, VoiceCfgEnv1, VoiceCfgEnvf, VoiceCfgMisc, VoiceMiscEfReleaseRate,
            VoiceParNext, VoiceParNextEfLevel, VoiceCurEcntEfCount, VoiceStateEfCur);

        int pitch = (short)GetVoiceMask(voice, VoiceTarPitchLink, VoicePitchMask);
        int pitchScale = (sbyte)GetVoiceMask(voice, VoiceCfgEnv0, VoiceEnv0PitchScale);
        float rate = (float)(1.0 / MathF.Pow(2.0f, (pitch + (pitchScale * 32 * efValue)) / 4096.0f));

        float eaValue = StepEnvelope(
            voice, VoiceCfgEnv0, VoiceCfgEnva, VoiceTarLfoEnv, VoiceLfoEnvEaReleaseRate,
            VoiceParOffset, VoiceParOffsetEaLevel, VoiceCurEcntEaCount, VoiceStateEaCur);

        Array.Clear(_vpSamples);

        if ((fmt & VoiceFmtMultipass) != 0)
        {
            GetMultipassSamples(voice, fmt);
        }
        else
        {
            int sampleCount = 0;
            while (sampleCount < ApuVpFrame.SamplesPerFrame)
            {
                if (!IsVoiceActive(voice))
                {
                    return;
                }

                int count = VoiceResample(
                    voice,
                    _vpSamples.AsSpan(sampleCount * 2),
                    ApuVpFrame.SamplesPerFrame - sampleCount,
                    rate);
                if (count < 0)
                {
                    break;
                }

                sampleCount += count;
            }
        }

        if (!IsVoiceActive(voice))
        {
            return;
        }

        Span<int> bin = stackalloc int[8];
        bin[0] = (int)GetVoiceMask(voice, VoiceCfgVbin, VoiceVbin0);
        bin[1] = (int)GetVoiceMask(voice, VoiceCfgVbin, VoiceVbin1);
        bin[2] = (int)GetVoiceMask(voice, VoiceCfgVbin, VoiceVbin2);
        bin[3] = (int)GetVoiceMask(voice, VoiceCfgVbin, VoiceVbin3);
        bin[4] = (int)GetVoiceMask(voice, VoiceCfgVbin, VoiceVbin4);
        bin[5] = (int)GetVoiceMask(voice, VoiceCfgVbin, VoiceVbin5);
        bin[6] = (int)GetVoiceMask(voice, VoiceCfgFmt, VoiceFmtV6Bin);
        bin[7] = (int)GetVoiceMask(voice, VoiceCfgFmt, VoiceFmtV7Bin);

        if (voice < ApuVpFrame.Max3dVoices)
        {
            bin[0] = _vpState.HrtfSubmix[0];
            bin[1] = _vpState.HrtfSubmix[1];
            bin[2] = _vpState.HrtfSubmix[2];
            bin[3] = _vpState.HrtfSubmix[3];
        }

        Span<int> vol = stackalloc int[8];
        vol[0] = (int)GetVoiceMask(voice, VoiceTarVola, VoiceVolaVolume0);
        vol[1] = (int)GetVoiceMask(voice, VoiceTarVola, VoiceVolaVolume1);
        vol[2] = (int)GetVoiceMask(voice, VoiceTarVolb, VoiceVolbVolume2);
        vol[3] = (int)GetVoiceMask(voice, VoiceTarVolb, VoiceVolbVolume3);
        vol[4] = (int)GetVoiceMask(voice, VoiceTarVolc, VoiceVolcVolume4);
        vol[5] = (int)GetVoiceMask(voice, VoiceTarVolc, VoiceVolcVolume5);
        vol[6] = (int)GetVoiceMask(voice, VoiceTarVolc, VoiceVolcVolume6High) << 8;
        vol[6] |= (int)GetVoiceMask(voice, VoiceTarVolb, VoiceVolbVolume6Mid) << 4;
        vol[6] |= (int)GetVoiceMask(voice, VoiceTarVola, VoiceVolaVolume6Low);
        vol[7] = (int)GetVoiceMask(voice, VoiceTarVolc, VoiceVolcVolume7High) << 8;
        vol[7] |= (int)GetVoiceMask(voice, VoiceTarVolb, VoiceVolbVolume7Mid) << 4;
        vol[7] |= (int)GetVoiceMask(voice, VoiceTarVola, VoiceVolaVolume7Low);

        uint fmode = GetVoiceMask(voice, VoiceCfgMisc, VoiceMiscFmode);
        bool lowPass = voice < ApuVpFrame.Max3dVoices
            ? fmode == 1
            : stereo ? fmode == 1 : (fmode & 1) != 0;

        if (lowPass)
        {
            for (int ch = 0; ch < 2; ch++)
            {
                int fc = (short)GetVoiceMask(voice, VoiceTarFca + ((uint)(ch % channels) * 4), VoiceTarFcaFc0);
                float fcValue = Clamp((float)Math.Pow(2, fc / 4096.0), 0.003906f, 1.0f);
                int q = (int)GetVoiceMask(voice, VoiceTarFca + ((uint)(ch % channels) * 4), VoiceTarFcaFc1);
                float qValue = Clamp((float)(q / (1.0 * 0x8000)), 0.079407f, 1.0f);

                ref SvFilter filter = ref _vpSvf[(voice * 2) + (uint)ch];
                filter.Setup(fcValue, qValue, SvFilterType.LowPass);
                for (int i = 0; i < ApuVpFrame.SamplesPerFrame; i++)
                {
                    float value = filter.Run(_vpSamples[(i * 2) + ch]);
                    _vpSamples[(i * 2) + ch] = MathF.Min(MathF.Max(value, -1.0f), 1.0f);
                }
            }
        }

        if (voice < ApuVpFrame.Max3dVoices && HrtfEnabled)
        {
            uint handle = GetVoiceMask(voice, VoiceCfgHrtfTarget, VoiceHrtfNullHandle);
            if (handle != VoiceHrtfNullHandle)
            {
                HrtfFilter filter = GetHrtfFilter(voice);
                filter.Process(_vpSamples);
            }
        }

        for (int b = 0; b < 8; b++)
        {
            float gain = eaValue;
            float headroom = voice < ApuVpFrame.Max3dVoices && b < 4
                ? 1 << _vpState.HrtfHeadroom
                : 1 << _vpState.SubmixHeadroom[bin[b]];
            gain *= Attenuate(vol[b]) / headroom;

            int channel = b % channels;
            int binBase = bin[b] * ApuVpFrame.SamplesPerFrame;
            for (int i = 0; i < ApuVpFrame.SamplesPerFrame; i++)
            {
                _mixBins[binBase + i] += gain * _vpSamples[(i * 2) + channel];
            }
        }
    }

    /// <summary>
    /// Feeds a multipass voice from the mix bin its sub-voices were mixed into
    /// (xemu <c>get_multipass_samples</c>).
    /// </summary>
    /// <param name="voice">The voice handle.</param>
    /// <param name="fmt">The voice's CFG_FMT word.</param>
    private void GetMultipassSamples(uint voice, uint fmt)
    {
        int bin = (int)GetVoiceMask(voice, VoiceCfgFmt, VoiceFmtSamplesPerBlock);
        for (int i = 0; i < ApuVpFrame.SamplesPerFrame; i++)
        {
            float value = _mixBins[(bin * ApuVpFrame.SamplesPerFrame) + i];
            _vpSamples[i * 2] = value;
            _vpSamples[(i * 2) + 1] = value;
        }

        if ((fmt & VoiceFmtClearMix) != 0)
        {
            Array.Clear(_mixBins, bin * ApuVpFrame.SamplesPerFrame, ApuVpFrame.SamplesPerFrame);
        }
    }

    /// <summary>
    /// Steps one envelope phase (xemu <c>voice_step_envelope</c>) and returns the
    /// phase's gain in [0, 1].
    /// </summary>
    /// <param name="voice">The voice handle.</param>
    /// <param name="reg0">Register carrying the attack rate (and delay time).</param>
    /// <param name="regA">Register carrying hold/decay/sustain parameters.</param>
    /// <param name="releaseReg">Register carrying the release rate.</param>
    /// <param name="releaseMask">Mask of the release-rate field.</param>
    /// <param name="levelReg">Register the current envelope level is stored in.</param>
    /// <param name="levelMask">Mask of the level field.</param>
    /// <param name="countMask">Mask of this envelope's counter field.</param>
    /// <param name="currentMask">Mask of this envelope's phase field.</param>
    /// <returns>The envelope gain.</returns>
    private float StepEnvelope(
        uint voice,
        uint reg0,
        uint regA,
        uint releaseReg,
        uint releaseMask,
        uint levelReg,
        uint levelMask,
        uint countMask,
        uint currentMask)
    {
        uint current = GetVoiceMask(voice, VoiceParState, currentMask);

        switch (current)
        {
            case ApuVpFrame.EnvelopeOff:
                SetVoiceMask(voice, VoiceCurEcnt, countMask, 0);
                SetVoiceMask(voice, levelReg, levelMask, 0xFF);
                return 1.0f;

            case ApuVpFrame.EnvelopeDelay:
                {
                    uint count = GetVoiceMask(voice, VoiceCurEcnt, countMask);
                    SetVoiceMask(voice, levelReg, levelMask, 0x00);
                    if (count == 0)
                    {
                        current++;
                        SetVoiceMask(voice, VoiceParState, currentMask, current);
                        count = 0;
                    }
                    else
                    {
                        count--;
                    }

                    SetVoiceMask(voice, VoiceCurEcnt, countMask, count);
                    return 0.0f;
                }

            case ApuVpFrame.EnvelopeAttack:
                {
                    uint count = GetVoiceMask(voice, VoiceCurEcnt, countMask);
                    uint attackRate = GetVoiceMask(voice, reg0, VoiceEnv0AttackRate);

                    float value;
                    if (attackRate == 0)
                    {
                        // The hardware crackles on a zero attack rate; the
                        // reference reports full scale.
                        value = 255.0f;
                    }
                    else if (count <= attackRate * 16)
                    {
                        value = (int)(count * 0xFF) / (int)(attackRate * 16);
                    }
                    else
                    {
                        value = 255.0f;
                    }

                    SetVoiceMask(voice, levelReg, levelMask, (uint)value);
                    if (count == attackRate * 16)
                    {
                        current++;
                        SetVoiceMask(voice, VoiceParState, currentMask, current);
                        count = GetVoiceMask(voice, regA, VoiceEnvaHoldTime) * 16;
                    }
                    else
                    {
                        count++;
                    }

                    SetVoiceMask(voice, VoiceCurEcnt, countMask, count);
                    return value / 255.0f;
                }

            case ApuVpFrame.EnvelopeHold:
                {
                    uint count = GetVoiceMask(voice, VoiceCurEcnt, countMask);
                    SetVoiceMask(voice, levelReg, levelMask, 0xFF);
                    if (count == 0)
                    {
                        current++;
                        SetVoiceMask(voice, VoiceParState, currentMask, current);
                        count = GetVoiceMask(voice, regA, VoiceEnvaDecayRate) * 16;
                    }
                    else
                    {
                        count--;
                    }

                    SetVoiceMask(voice, VoiceCurEcnt, countMask, count);
                    return 1.0f;
                }

            case ApuVpFrame.EnvelopeDecay:
                {
                    uint count = GetVoiceMask(voice, VoiceCurEcnt, countMask);
                    uint decayRate = GetVoiceMask(voice, regA, VoiceEnvaDecayRate);
                    uint sustainLevel = GetVoiceMask(voice, regA, VoiceEnvaSustainLevel);

                    float value;
                    if (decayRate == 0)
                    {
                        value = 0.0f;
                    }
                    else
                    {
                        value = 255.0f * MathF.Pow(
                            0.99988799f,
                            ((int)(decayRate * 16) - (int)count) * 4096 / (int)decayRate);
                    }

                    if (value <= (sustainLevel + 0.2f) || value > 255.0f)
                    {
                        current++;
                        SetVoiceMask(voice, VoiceParState, currentMask, current);
                    }
                    else
                    {
                        count--;
                        SetVoiceMask(voice, VoiceCurEcnt, countMask, count);
                        SetVoiceMask(voice, levelReg, levelMask, (uint)value);
                    }

                    return value / 255.0f;
                }

            case ApuVpFrame.EnvelopeSustain:
                {
                    uint sustainLevel = GetVoiceMask(voice, regA, VoiceEnvaSustainLevel);
                    SetVoiceMask(voice, VoiceCurEcnt, countMask, 0);
                    SetVoiceMask(voice, levelReg, levelMask, sustainLevel);
                    return sustainLevel / 255.0f;
                }

            case ApuVpFrame.EnvelopeRelease:
                {
                    uint count = GetVoiceMask(voice, VoiceCurEcnt, countMask);
                    uint releaseRate = GetVoiceMask(voice, releaseReg, releaseMask);
                    if (releaseRate == 0)
                    {
                        count = 0;
                    }

                    float value = 0;
                    if (count == 0)
                    {
                        current++;
                        SetVoiceMask(voice, VoiceParState, currentMask, current);
                    }
                    else
                    {
                        float pos = Clamp((float)(1 - (count / (releaseRate * 16.0))), 0, 1);
                        uint level = GetVoiceMask(voice, levelReg, levelMask);
                        value = MathF.Pow((float)Math.E, (float)(-6.91 * pos)) * level;
                        count--;
                        SetVoiceMask(voice, VoiceCurEcnt, countMask, count);
                    }

                    return value / 255.0f;
                }

            case ApuVpFrame.EnvelopeForceRelease:
                if (countMask == VoiceCurEcntEaCount)
                {
                    VoiceOff(voice);
                }

                return 0.0f;

            default:
                // The reference asserts; a well-formed guest never gets here.
                return 0.0f;
        }
    }

    /// <summary>
    /// Resamples one voice's source samples to the frame rate (replaces the
    /// reference's libsamplerate callback). At a unity rate the source samples
    /// pass through untouched; otherwise the voice is linearly interpolated,
    /// which is what the reference notes the hardware may well do.
    /// </summary>
    /// <param name="voice">The voice handle.</param>
    /// <param name="destination">Interleaved destination samples.</param>
    /// <param name="requested">Output frames requested.</param>
    /// <param name="rate">Output frames per source frame.</param>
    /// <returns>The frames written, or -1 when the voice has no more source samples.</returns>
    private int VoiceResample(uint voice, Span<float> destination, int requested, float rate)
    {
        if (rate == 1.0f)
        {
            int count = VoiceGetSamples(voice, destination, requested);
            if (count < 0)
            {
                return -1;
            }

            if (count < requested)
            {
                destination[(count * 2)..(requested * 2)].Clear();
            }

            return requested;
        }

        VpResampler resampler = _vpResamplers[voice] ??= new VpResampler();
        if (!resampler.Primed)
        {
            if (!PullSourceFrame(voice, out resampler.CurrentLeft, out resampler.CurrentRight))
            {
                return -1;
            }

            if (!PullSourceFrame(voice, out resampler.NextLeft, out resampler.NextRight))
            {
                resampler.NextLeft = resampler.CurrentLeft;
                resampler.NextRight = resampler.CurrentRight;
            }

            resampler.Fraction = 0;
            resampler.Primed = true;
        }

        double step = 1.0 / rate;
        int produced = 0;
        while (produced < requested)
        {
            float fraction = (float)resampler.Fraction;
            destination[produced * 2] = resampler.CurrentLeft
                + ((resampler.NextLeft - resampler.CurrentLeft) * fraction);
            destination[(produced * 2) + 1] = resampler.CurrentRight
                + ((resampler.NextRight - resampler.CurrentRight) * fraction);
            produced++;

            resampler.Fraction += step;
            while (resampler.Fraction >= 1.0)
            {
                resampler.CurrentLeft = resampler.NextLeft;
                resampler.CurrentRight = resampler.NextRight;
                if (!PullSourceFrame(voice, out resampler.NextLeft, out resampler.NextRight))
                {
                    // The reference zero-fills a starved frame and keeps the
                    // output length, so the voice goes silent rather than short.
                    destination[(produced * 2)..(requested * 2)].Clear();
                    resampler.Primed = false;
                    return requested;
                }

                resampler.Fraction -= 1.0;
            }
        }

        return requested;
    }

    private bool PullSourceFrame(uint voice, out float left, out float right)
    {
        Span<float> frame = stackalloc float[2];
        int count = VoiceGetSamples(voice, frame, 1);
        left = frame[0];
        right = frame[1];
        return count > 0;
    }

    /// <summary>
    /// Fetches up to <paramref name="numSamplesRequested"/> source frames for a
    /// voice (xemu <c>voice_get_samples</c>): PCM or ADPCM, buffer or stream,
    /// walking the SSL segment list and reporting completion.
    /// </summary>
    /// <param name="voice">The voice handle.</param>
    /// <param name="samples">Interleaved destination.</param>
    /// <param name="numSamplesRequested">Frames requested.</param>
    /// <returns>Frames produced, or -1 when the voice ended.</returns>
    private int VoiceGetSamples(uint voice, Span<float> samples, int numSamplesRequested)
    {
        uint fmt = GetVoiceMask(voice, VoiceCfgFmt, uint.MaxValue);
        bool stereo = (fmt & VoiceFmtStereo) != 0;
        int channels = stereo ? 2 : 1;
        uint sampleSize = GetVoiceMask(voice, VoiceCfgFmt, VoiceFmtSampleSize);
        int containerSizeIndex = (int)GetVoiceMask(voice, VoiceCfgFmt, VoiceFmtContainerSize);
        ReadOnlySpan<int> containerSizes = [1, 2, 0, 4];
        int containerSize = containerSizes[containerSizeIndex];
        bool stream = (fmt & VoiceFmtDataType) != 0;
        bool paused = (GetVoiceMask(voice, VoiceParState, VoiceStatePaused)) != 0;
        bool loop = (fmt & VoiceFmtLoop) != 0;
        uint ebo = GetVoiceMask(voice, VoiceParNext, OffsetMask24);
        uint cbo = GetVoiceMask(voice, VoiceParOffset, OffsetMask24);
        uint lbo = GetVoiceMask(voice, VoiceCurPshSample, OffsetMask24);
        uint bufferBase = GetVoiceMask(voice, VoiceCurPslStart, OffsetMask24);
        int samplesPerBlock = (int)GetVoiceMask(voice, VoiceCfgFmt, VoiceFmtSamplesPerBlock) + 1;
        bool persist = (fmt & VoiceFmtPersist) != 0;

        // Cleared when the first sample is played.
        SetVoiceMask(voice, VoiceParState, VoiceStateNewVoice, 0);

        if (paused)
        {
            return -1;
        }

        int sslIndex = 0;
        uint segmentOffset = 0;
        int segmentLength = 0;

        if (stream)
        {
            if (!persist)
            {
                uint envelope = GetVoiceMask(voice, VoiceParState, VoiceStateEaCur);
                if (envelope < ApuVpFrame.EnvelopeRelease)
                {
                    VoiceOff(voice);
                    return -1;
                }
            }

            sslIndex = _sslIndex[voice];
            uint page = _sslBase[(voice * 2) + sslIndex] + (uint)_sslSeg[voice];
            int count = _sslCount[(voice * 2) + sslIndex];
            if (count == 0)
            {
                SetVoiceMask(voice, VoiceParOffset, OffsetMask24, 0);
                _sslSeg[voice] = 0;
                if (!persist)
                {
                    _sslIndex[voice] = 0;
                    VoiceOff(voice);
                }
                else
                {
                    WriteNotifierStatus(voice, ApuVpFrame.NotifierSslaDone + sslIndex);
                }

                return -1;
            }

            uint address = _regs[VpSslAddr >> 2] + (page * 8);
            segmentOffset = _memory.Read32(address);
            segmentLength = (int)_memory.Read32(address + 4);

            int segLen = segmentLength & 0xFFFF;
            int segContainer = (segmentLength >> 16) & 3;

            // The segment descriptor is authoritative for the block shape.
            containerSizeIndex = segContainer;
            if (segContainer == 2)
            {
                sampleSize = 2;
            }

            containerSize = containerSizes[containerSizeIndex];
            ebo = (uint)(segLen > 0 ? segLen - 1 : 0);
        }

        bool adpcm = containerSizeIndex == 2;
        int blockSize = adpcm ? ApuVpAdpcm.BlockBytes : containerSize;
        blockSize *= samplesPerBlock;

        int adpcmBlockIndex = -1;
        int sampleCount = 0;
        for (; sampleCount < numSamplesRequested && cbo <= ebo; sampleCount++, cbo++)
        {
            if (adpcm)
            {
                int blockIndex = (int)(cbo / ApuVpAdpcm.SamplesPerBlock);
                int blockPosition = (int)(cbo % ApuVpAdpcm.SamplesPerBlock);
                if (adpcmBlockIndex != blockIndex)
                {
                    uint linearAddress = (uint)(blockIndex * blockSize);
                    if (stream)
                    {
                        uint address = segmentOffset + linearAddress;
                        for (int word = 0; word < blockSize / 4; word++)
                        {
                            uint value = _memory.Read32(address + ((uint)word * 4));
                            _vpAdpcmBlock[(word * 4) + 0] = (byte)value;
                            _vpAdpcmBlock[(word * 4) + 1] = (byte)(value >> 8);
                            _vpAdpcmBlock[(word * 4) + 2] = (byte)(value >> 16);
                            _vpAdpcmBlock[(word * 4) + 3] = (byte)(value >> 24);
                        }
                    }
                    else
                    {
                        linearAddress += bufferBase;
                        for (int word = 0; word < 9 * samplesPerBlock; word++)
                        {
                            uint address = GetDataPointer(linearAddress);
                            uint value = _memory.Read32(address);
                            _vpAdpcmBlock[(word * 4) + 0] = (byte)value;
                            _vpAdpcmBlock[(word * 4) + 1] = (byte)(value >> 8);
                            _vpAdpcmBlock[(word * 4) + 2] = (byte)(value >> 16);
                            _vpAdpcmBlock[(word * 4) + 3] = (byte)(value >> 24);
                            linearAddress += 4;
                        }
                    }

                    ApuVpAdpcm.Decode(
                        _vpAdpcmDecoded,
                        _vpAdpcmBlock.AsSpan(0, blockSize),
                        channels);
                    adpcmBlockIndex = blockIndex;
                }

                samples[sampleCount * 2] = ApuVpFrame.Int16ToFloat((uint)_vpAdpcmDecoded[blockPosition * channels]);
                if (stereo)
                {
                    samples[(sampleCount * 2) + 1] = ApuVpFrame.Int16ToFloat(
                        (uint)_vpAdpcmDecoded[(blockPosition * channels) + 1]);
                }
            }
            else
            {
                uint address = stream
                    ? segmentOffset + (cbo * (uint)blockSize)
                    : GetDataPointer(bufferBase + (cbo * (uint)blockSize));

                for (int channel = 0; channel < channels; channel++)
                {
                    float value = sampleSize switch
                    {
                        0 => ApuVpFrame.UInt8ToFloat(_memory.Read8(address)),
                        1 => ApuVpFrame.Int16ToFloat(_memory.Read32(address) & 0xFFFF),
                        2 => ApuVpFrame.Int24ToFloat(_memory.Read32(address)),
                        _ => ApuVpFrame.Int32ToFloat(_memory.Read32(address)),
                    };
                    samples[(sampleCount * 2) + channel] = value;
                    address += (uint)containerSize;
                }
            }

            if (!stereo)
            {
                samples[(sampleCount * 2) + 1] = samples[sampleCount * 2];
            }
        }

        if (cbo >= ebo)
        {
            if (stream)
            {
                _sslSeg[voice] += 1;
                cbo = 0;
                if (_sslSeg[voice] < _sslCount[(voice * 2) + sslIndex])
                {
                    // The next segment in this SSL continues on the next call.
                }
                else
                {
                    _sslIndex[voice] = (sslIndex + 1) % ApuVpFrame.SslPerVoice;
                    _sslSeg[voice] = 0;
                    WriteNotifierStatus(voice, ApuVpFrame.NotifierSslaDone + sslIndex);
                    SegmentCompletions++;
                }
            }
            else if (loop)
            {
                cbo = lbo;
            }
            else
            {
                cbo = ebo;
                VoiceOff(voice);
            }
        }

        SetVoiceMask(voice, VoiceParOffset, OffsetMask24, cbo);
        return sampleCount;
    }

    /// <summary>
    /// Resolves a voice buffer offset through the guest's SGE table (xemu
    /// <c>get_data_ptr</c>).
    /// </summary>
    /// <param name="address">The linear buffer offset.</param>
    /// <returns>The guest physical address.</returns>
    private uint GetDataPointer(uint address)
    {
        uint entry = address / SslPageSize;
        uint prdAddress = _memory.Read32(_regs[VpSgeAddr >> 2] + (entry * 8));
        return prdAddress + (address % SslPageSize);
    }

    private static float Clamp(float value, float min, float max)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }

    /// <summary>Volume attenuation from the 12-bit volume field (xemu <c>attenuate</c>).</summary>
    /// <param name="volume">The 12-bit volume.</param>
    /// <returns>The linear gain.</returns>
    private static float Attenuate(int volume)
    {
        int field = volume & 0xFFF;
        return field == 0xFFF ? 0.0f : MathF.Pow(10.0f, (float)(field / (64.0 * -20.0)));
    }

    /// <summary>
    /// Stores two HRIR coefficient pairs into the current HRTF entry (xemu
    /// <c>NV1BA0_PIO_SET_HRIR</c>).
    /// </summary>
    /// <param name="coefficientIndex">First coefficient slot the method writes.</param>
    /// <param name="value">The packed left/right byte pairs.</param>
    private void WriteHrirCoefficients(int coefficientIndex, uint value)
    {
        (float[] left, float[] right) = _vpState.GetEntry(_vpState.CurrentEntry);
        if (coefficientIndex < HrtfFilter.NumTaps)
        {
            left[coefficientIndex] = ApuVpFrame.Int8ToFloat((sbyte)(value & 0xFF));
            right[coefficientIndex] = ApuVpFrame.Int8ToFloat((sbyte)((value >> 8) & 0xFF));
        }

        if (coefficientIndex + 1 < HrtfFilter.NumTaps)
        {
            left[coefficientIndex + 1] = ApuVpFrame.Int8ToFloat((sbyte)((value >> 16) & 0xFF));
            right[coefficientIndex + 1] = ApuVpFrame.Int8ToFloat((sbyte)((value >> 24) & 0xFF));
        }
    }

    /// <summary>
    /// Stores the last HRIR coefficient pair and the entry's interaural delay
    /// (xemu <c>NV1BA0_PIO_SET_HRIR_X</c>).
    /// </summary>
    /// <param name="value">The packed coefficients and 6.9 fixed-point delay.</param>
    private void WriteHrirTail(uint value)
    {
        (float[] left, float[] right) = _vpState.GetEntry(_vpState.CurrentEntry);
        left[HrtfFilter.NumTaps - 1] = ApuVpFrame.Int8ToFloat((sbyte)(value & 0xFF));
        right[HrtfFilter.NumTaps - 1] = ApuVpFrame.Int8ToFloat((sbyte)((value >> 8) & 0xFF));
        _vpState.Itd[_vpState.CurrentEntry] = ApuVpFrame.S6p9ToFloat(value >> 16);
    }

    /// <summary>
    /// Points a 3D voice's HRTF filter at a coefficient entry (xemu
    /// <c>hrtf_filter_set_target_params</c> on <c>SET_VOICE_TAR_HRTF</c>).
    /// </summary>
    /// <param name="voice">The voice handle.</param>
    /// <param name="entry">The HRTF coefficient entry.</param>
    private void ApplyHrtfTarget(uint voice, int entry)
    {
        if (entry < 0 || entry >= _vpState.HrirLeft.Length)
        {
            return;
        }

        (float[] left, float[] right) = _vpState.GetEntry(entry);
        GetHrtfFilter(voice).SetTargetParams(left, right, _vpState.Itd[entry]);
    }

    private HrtfFilter GetHrtfFilter(uint voice)
    {
        HrtfFilter? filter = _vpHrtf[voice];
        if (filter is null)
        {
            filter = new HrtfFilter();
            _vpHrtf[voice] = filter;
        }

        return filter;
    }

    /// <summary>
    /// Clears a voice's filter history and resampler position (xemu
    /// <c>voice_reset_filters</c> on VOICE_ON).
    /// </summary>
    /// <param name="voice">The voice handle.</param>
    private void ResetVoiceFilters(uint voice)
    {
        _vpSvf[voice * 2] = default;
        _vpSvf[(voice * 2) + 1] = default;
        if (voice < ApuVpFrame.Max3dVoices)
        {
            _vpHrtf[voice]?.ClearHistory();
        }

        if (_vpResamplers[voice] is { } resampler)
        {
            resampler.Primed = false;
        }
    }

    /// <summary>Gets or sets a value indicating whether the HRTF stage runs (xemu <c>g_config.audio.hrtf</c>).</summary>
    public bool HrtfEnabled { get; set; } = true;

    /// <summary>Appends the voice-processor mixer state to a snapshot.</summary>
    /// <param name="writer">The snapshot writer.</param>
    private void SaveVpState(BinaryWriter writer)
    {
        writer.Write(_vpState.CurrentEntry);
        writer.Write(_vpState.HrtfHeadroom);
        for (int i = 0; i < _vpState.HrtfSubmix.Length; i++)
        {
            writer.Write(_vpState.HrtfSubmix[i]);
        }

        for (int i = 0; i < _vpState.SubmixHeadroom.Length; i++)
        {
            writer.Write(_vpState.SubmixHeadroom[i]);
        }

        writer.Write(HrtfEnabled);

        for (int entry = 0; entry < _vpState.HrirLeft.Length; entry++)
        {
            float[]? left = _vpState.HrirLeft[entry];
            writer.Write(left is not null);
            if (left is not null)
            {
                for (int k = 0; k < HrtfFilter.NumTaps; k++)
                {
                    writer.Write(left[k]);
                    writer.Write(_vpState.HrirRight[entry]![k]);
                }
            }

            writer.Write(_vpState.Itd[entry]);
        }

        for (int voice = 0; voice < ApuVpFrame.MaxVoices; voice++)
        {
            VpResampler? resampler = _vpResamplers[voice];
            writer.Write(resampler is not null);
            if (resampler is not null)
            {
                writer.Write(resampler.CurrentLeft);
                writer.Write(resampler.CurrentRight);
                writer.Write(resampler.NextLeft);
                writer.Write(resampler.NextRight);
                writer.Write(resampler.Fraction);
                writer.Write(resampler.Primed);
            }
        }
    }

    /// <summary>Restores the voice-processor mixer state from a snapshot.</summary>
    /// <param name="reader">The snapshot reader.</param>
    private void LoadVpState(BinaryReader reader)
    {
        _vpState.CurrentEntry = reader.ReadInt32();
        _vpState.HrtfHeadroom = reader.ReadByte();
        for (int i = 0; i < _vpState.HrtfSubmix.Length; i++)
        {
            _vpState.HrtfSubmix[i] = reader.ReadByte();
        }

        for (int i = 0; i < _vpState.SubmixHeadroom.Length; i++)
        {
            _vpState.SubmixHeadroom[i] = reader.ReadByte();
        }

        HrtfEnabled = reader.ReadBoolean();

        for (int entry = 0; entry < _vpState.HrirLeft.Length; entry++)
        {
            bool present = reader.ReadBoolean();
            if (present)
            {
                (float[] left, float[] right) = _vpState.GetEntry(entry);
                for (int k = 0; k < HrtfFilter.NumTaps; k++)
                {
                    left[k] = reader.ReadSingle();
                    right[k] = reader.ReadSingle();
                }
            }
            else
            {
                _vpState.HrirLeft[entry] = null;
                _vpState.HrirRight[entry] = null;
            }

            _vpState.Itd[entry] = reader.ReadSingle();
        }

        for (int voice = 0; voice < ApuVpFrame.MaxVoices; voice++)
        {
            bool present = reader.ReadBoolean();
            if (!present)
            {
                _vpResamplers[voice] = null;
                continue;
            }

            VpResampler resampler = _vpResamplers[voice] ??= new VpResampler();
            resampler.CurrentLeft = reader.ReadSingle();
            resampler.CurrentRight = reader.ReadSingle();
            resampler.NextLeft = reader.ReadSingle();
            resampler.NextRight = reader.ReadSingle();
            resampler.Fraction = reader.ReadDouble();
            resampler.Primed = reader.ReadBoolean();
        }
    }
}
