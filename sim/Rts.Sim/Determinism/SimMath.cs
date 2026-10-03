using System;

namespace Rts.Sim.Determinism;

/// <summary>Deterministic trig built only from IEEE-exact float operations (+, -, *, /, floor, sqrt).</summary>
/// <remarks>
/// <c>Math.Sin</c>/<c>Atan2</c> may change between runtime versions, which would break golden replay
/// hashes. These polynomials give the same bits on every run. Accuracy (see SimMathTests):
/// Sin/Cos within ~4e-6 of the true value for |x| up to a few hundred radians (range reduction in
/// float loses accuracy beyond that); Atan2 within ~1.2e-5 rad.
/// </remarks>
public static class SimMath
{
    /// <summary>Pi as a float.</summary>
    public const float Pi = 3.14159265358979f;
    /// <summary>2 * Pi.</summary>
    public const float TwoPi = 6.28318530717959f;
    /// <summary>Pi / 2.</summary>
    public const float HalfPi = 1.57079632679490f;

    private const float InvTwoPi = 0.159154943091895f;

    // Taylor coefficients of sin up to x^9: max error ~3.6e-6 on [-pi/2, pi/2].
    private const float S3 = -1f / 6f;
    private const float S5 = 1f / 120f;
    private const float S7 = -1f / 5040f;
    private const float S9 = 1f / 362880f;

    // Abramowitz & Stegun 4.4.49 minimax atan on [0, 1]: max error 1e-5 rad.
    private const float A1 = 0.9998660f;
    private const float A3 = -0.3302995f;
    private const float A5 = 0.1801410f;
    private const float A7 = -0.0851330f;
    private const float A9 = 0.0208351f;

    /// <summary>Sine of an angle in radians.</summary>
    public static float Sin(float x)
    {
        // Reduce to [-pi, pi], then fold into [-pi/2, pi/2] where the polynomial is accurate.
        float r = x - MathF.Floor(x * InvTwoPi + 0.5f) * TwoPi;
        if (r > HalfPi) r = Pi - r;
        else if (r < -HalfPi) r = -Pi - r;
        float r2 = r * r;
        return r * (1f + r2 * (S3 + r2 * (S5 + r2 * (S7 + r2 * S9))));
    }

    /// <summary>Cosine of an angle in radians.</summary>
    public static float Cos(float x) => Sin(x + HalfPi);

    /// <summary>Angle of (x, y) in radians, in [-pi, pi]; returns 0 for (0, 0).</summary>
    public static float Atan2(float y, float x)
    {
        float ax = MathF.Abs(x);
        float ay = MathF.Abs(y);
        if (ax == 0f && ay == 0f) return 0f;
        bool steep = ay > ax;
        float t = steep ? ax / ay : ay / ax;
        float t2 = t * t;
        float a = t * (A1 + t2 * (A3 + t2 * (A5 + t2 * (A7 + t2 * A9))));
        if (steep) a = HalfPi - a;
        if (x < 0f) a = Pi - a;
        return y < 0f ? -a : a;
    }

    /// <summary>Square root (IEEE-exact, so safe to call directly; wrapped for one import point).</summary>
    public static float Sqrt(float x) => MathF.Sqrt(x);
}
