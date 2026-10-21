using System;

namespace XGrid.Nv2a;

/// <summary>
/// Reconstructs the fixed-function specular exponent from the four
/// <c>SET_SPECULAR_PARAMS</c> curve words, transcribed from the reference
/// (xemu 0.8.137 <c>hw/xbox/nv2a/pgraph/pgraph.c</c>
/// <c>reconstruct_specular_power</c> + the coefficient tables below).
/// </summary>
/// <remarks>
/// The Xbox driver does not upload the power itself: it uploads four curve
/// coefficients and the hardware derives the power with a piecewise fit (the
/// step-point table selects the segment, the coefficient table the fit). The
/// arithmetic stays in <see cref="float"/> like the reference's
/// <c>struct CurveCoefficients</c> (all members are <c>float</c>), so the
/// reconstruction is bit-exact.
/// </remarks>
public static class SpecularPowerCurve
{
    private static readonly float[] StepPoints =
    [
        -0.022553957999f,
        -0.421539008617f,
        -0.678715527058f,
        -0.838916420937f,
        -0.961754500866f,
        -0.990773200989f,
        -0.994858562946f,
        -0.996561050415f,
        -0.999547004700f,
    ];

    private static readonly float[] CoefficientA =
    [
        1.000108475163f,
        1.199164441703f,
        8.653441252033f,
        -531.307758450301f,
        -4.662713151292f,
        -124.435242105211f,
        10672560.259502287954f,
        -51973801.463933646679f,
        972270.324080004124f,
    ];

    private static readonly float[] CoefficientB =
    [
        -9.838607076280f,
        -3.292603784852f,
        29.189473787191f,
        117.398468683934f,
        1.221108944572f,
        35.401219563514f,
        21565843.555823743343f,
        -104199997.554352939129f,
        2025882.096547174733f,
    ];

    private static readonly float[] CoefficientC =
    [
        54.829089549713f,
        7.799987995214f,
        43.586027561823f,
        113.155490738338f,
        1.217360986939f,
        35.408114377045f,
        10894794.336297152564f,
        -52225454.356278456748f,
        1054898.052467488218f,
    ];

    // Segments 3..5 use the saturation-growth-rate fit instead of the quadratic.
    private const int FirstGrowthRateSegment = 3;
    private const int LastGrowthRateSegment = 5;

    /// <summary>
    /// Reconstructs the specular power from the curve words (the reference
    /// reads <c>params[0]</c> and <c>params[3]</c>; the other two feed the
    /// low-power shortcut).
    /// </summary>
    /// <param name="parameters">The <c>SET_SPECULAR_PARAMS</c> words (at least four).</param>
    public static float Reconstruct(ReadOnlySpan<float> parameters)
    {
        if (parameters.Length < 4)
        {
            throw new ArgumentException("four specular curve words are required", nameof(parameters));
        }

        // Values < 1.0 result in a positive c1 and c2 - 2*c0 is very close to
        // the original power (reference comment).
        if (parameters[1] > 0f && parameters[2] < 1f)
        {
            return parameters[2] - (parameters[0] * 2f);
        }

        float c0 = parameters[0];
        float c3 = parameters[3];

        // A positive coefficient cannot come from the DirectX driver; the
        // reference returns a tiny power so the shader stays finite.
        if (c0 > 0f || c3 > 0f)
        {
            return 0.0001f;
        }

        float power = ReconstructOne(c0);
        float halfPower = ReconstructOne(c3);

        // The half-power curve extends the range beyond 1250 for driver
        // generated values; low-power inverted pairs are excluded by the
        // high |c0| test (reference comment).
        if (power == 0f || (halfPower > power && c0 < -0.1f))
        {
            return halfPower * 2f;
        }

        return power;
    }

    private static float ReconstructOne(float c0)
    {
        for (int i = 0; i < StepPoints.Length; i++)
        {
            if (c0 > StepPoints[i])
            {
                return i is >= FirstGrowthRateSegment and <= LastGrowthRateSegment
                    ? (CoefficientA[i] * c0) / (CoefficientB[i] + (CoefficientC[i] * c0))
                    : CoefficientA[i] + (CoefficientB[i] * c0) + (CoefficientC[i] * c0 * c0);
            }
        }

        return 0f;
    }
}
