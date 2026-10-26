// SPDX-License-Identifier: GPL-2.0-or-later
// Ported from xemu 0.8.139 hw/xbox/mcpx/apu/vp/svf.h and hw/xbox/mcpx/apu/vp/hrtf.h.
// svf.h: adapted from the SWH LADSPA plugins (Steve Harris, andy@vellocet), GPLv2.
// hrtf.h: Copyright (c) 2025 Matt Borgerson.

using System;
using System.Runtime.InteropServices;

namespace XGrid.Audio.Vp;

/// <summary>State-variable filter output tap (xemu <c>svf.h</c> F_* constants).</summary>
internal enum SvFilterType
{
    /// <summary>Low pass (F_LP).</summary>
    LowPass = 1,

    /// <summary>High pass (F_HP).</summary>
    HighPass = 2,

    /// <summary>Band pass (F_BP).</summary>
    BandPass = 3,

    /// <summary>Band reject / notch (F_BR).</summary>
    BandReject = 4,

    /// <summary>All pass with resonance (F_AP).</summary>
    AllPass = 5,
}

/// <summary>
/// The voice-processor low-pass filter (xemu <c>svf.h</c>): a Chamberlin
/// state-variable filter with the reference's light cubic waveshaping on the
/// band-pass state. One instance per voice and channel.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal struct SvFilter
{
    /// <summary>Filter frequency coefficient.</summary>
    public float F;

    /// <summary>Damping coefficient.</summary>
    public float Q;

    /// <summary>Input normalisation.</summary>
    public float Qnrm;

    /// <summary>High-pass state/output.</summary>
    public float H;

    /// <summary>Band-pass state/output.</summary>
    public float B;

    /// <summary>Low-pass state/output.</summary>
    public float L;

    /// <summary>Peaking output.</summary>
    public float P;

    /// <summary>Notch output.</summary>
    public float N;

    private SvFilterType _type;

    /// <summary>Stores the filter parameters (xemu <c>setup_svf</c>).</summary>
    /// <param name="fc">Cutoff coefficient (already normalised).</param>
    /// <param name="q">Damping coefficient (already normalised).</param>
    /// <param name="type">Output tap.</param>
    public void Setup(float fc, float q, SvFilterType type)
    {
        F = fc;
        Q = q;
        Qnrm = (float)Math.Sqrt((q / 2.0) + 0.01);
        _type = type;
    }

    /// <summary>Runs one sample through the filter (xemu <c>run_svf</c>, F_R = 1).</summary>
    /// <param name="input">The input sample.</param>
    /// <returns>The selected tap's output.</returns>
    public float Run(float input)
    {
        float value = Qnrm * input;

        // Very slight waveshape for extra stability.
        B -= B * B * B * 0.001f;
        H = value - L - (Q * B);
        B += F * H;
        L += F * B;
        N = L + H;
        P = L - H;
        return _type switch
        {
            SvFilterType.LowPass => L,
            SvFilterType.HighPass => H,
            SvFilterType.BandPass => B,
            SvFilterType.BandReject => N,
            _ => P,
        };
    }
}

/// <summary>
/// The voice processor's head-related transfer function filter (xemu
/// <c>hrtf.h</c>): a 31-tap HRIR convolution per channel with an interaural
/// time difference and smoothed parameter transitions.
/// </summary>
internal sealed class HrtfFilter
{
    /// <summary>Taps in the head-related impulse response.</summary>
    public const int NumTaps = 31;

    /// <summary>Maximum interaural delay in samples.</summary>
    public const int MaxDelaySamples = 42;

    /// <summary>History length (taps plus the maximum delay).</summary>
    public const int BufferLength = NumTaps + MaxDelaySamples;

    private const float ParamSmoothAlpha = 0.01f;

    private readonly float[] _buffer0 = new float[BufferLength];
    private readonly float[] _buffer1 = new float[BufferLength];
    private readonly float[] _coeffCur0 = new float[NumTaps];
    private readonly float[] _coeffCur1 = new float[NumTaps];
    private readonly float[] _coeffTar0 = new float[NumTaps];
    private readonly float[] _coeffTar1 = new float[NumTaps];
    private int _bufPos;
    private float _itdCur;
    private float _itdTar;

    /// <summary>Clears the sample history but keeps the parameter targets.</summary>
    public void ClearHistory()
    {
        _bufPos = 0;
        Array.Clear(_buffer0);
        Array.Clear(_buffer1);
    }

    /// <summary>Clears every field (xemu <c>hrtf_filter_init</c>).</summary>
    public void Reset()
    {
        ClearHistory();
        Array.Clear(_coeffCur0);
        Array.Clear(_coeffCur1);
        Array.Clear(_coeffTar0);
        Array.Clear(_coeffTar1);
        _itdCur = 0f;
        _itdTar = 0f;
    }

    /// <summary>
    /// Stores new HRIR coefficients and interaural delay, normalising each
    /// channel's coefficients to unity gain (xemu <c>hrtf_filter_set_target_params</c>).
    /// </summary>
    /// <param name="left">Left-channel coefficients.</param>
    /// <param name="right">Right-channel coefficients.</param>
    /// <param name="itd">Interaural time difference in samples.</param>
    public void SetTargetParams(ReadOnlySpan<float> left, ReadOnlySpan<float> right, float itd)
    {
        _itdTar = MathF.Max(-MaxDelaySamples, MathF.Min(itd, MaxDelaySamples));

        for (int ch = 0; ch < 2; ch++)
        {
            float[] coeff = ch == 0 ? _coeffTar0 : _coeffTar1;
            ReadOnlySpan<float> source = ch == 0 ? left : right;
            source[..NumTaps].CopyTo(coeff);

            float sum = 0.0f;
            for (int k = 0; k < NumTaps; k++)
            {
                sum += MathF.Abs(coeff[k]);
            }

            // The reference breaks out of the channel loop, not just this
            // iteration, when a channel needs no normalisation.
            if (sum == 0.0f || sum == 1.0f)
            {
                break;
            }

            for (int k = 0; k < NumTaps; k++)
            {
                coeff[k] /= sum;
            }
        }
    }

    /// <summary>
    /// Runs one frame of 32 samples through the filter in place (xemu
    /// <c>hrtf_filter_process</c>).
    /// </summary>
    /// <param name="samples">Interleaved stereo samples (32 frames).</param>
    public void Process(Span<float> samples)
    {
        for (int n = 0; n < ApuVpFrame.SamplesPerFrame; n++)
        {
            StepParameters();

            for (int ch = 0; ch < 2; ch++)
            {
                float[] buf = ch == 0 ? _buffer0 : _buffer1;
                float[] coeff = ch == 0 ? _coeffCur0 : _coeffCur1;

                buf[_bufPos] = samples[(n * 2) + ch];

                float delay = _itdCur * (ch == 0 ? 1.0f : -1.0f);
                if (delay < 0.0f)
                {
                    delay = 0.0f;
                }

                int whole = (int)delay;
                float frac = delay - whole;

                float acc = 0.0f;
                for (int k = 0; k < NumTaps; k++)
                {
                    int index1 = ((_bufPos - whole - k) % BufferLength + BufferLength) % BufferLength;
                    float value = buf[index1];

                    if (frac > 0.0f)
                    {
                        int index2 = ((index1 - 1) % BufferLength + BufferLength) % BufferLength;
                        value = (value * (1f - frac)) + (buf[index2] * frac);
                    }

                    acc += coeff[k] * value;
                }

                samples[(n * 2) + ch] = acc;
            }

            _bufPos = (_bufPos + 1) % BufferLength;
        }
    }

    private void StepParameters()
    {
        for (int ch = 0; ch < 2; ch++)
        {
            float[] cur = ch == 0 ? _coeffCur0 : _coeffCur1;
            float[] tar = ch == 0 ? _coeffTar0 : _coeffTar1;
            for (int k = 0; k < NumTaps; k++)
            {
                cur[k] += ParamSmoothAlpha * (tar[k] - cur[k]);
            }
        }

        _itdCur += ParamSmoothAlpha * (_itdTar - _itdCur);
    }
}
