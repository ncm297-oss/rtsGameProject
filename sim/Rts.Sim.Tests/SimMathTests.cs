using Rts.Sim.Determinism;

namespace Rts.Sim.Tests;

public class SimMathTests
{
    private const double Tolerance = 1e-3;

    [Fact]
    public void SinCos_MatchMathWithinTolerance()
    {
        const int samples = 10_000;
        double maxSin = 0, maxCos = 0;
        for (int i = 0; i <= samples; i++)
        {
            float x = (float)(-4 * Math.PI + 8 * Math.PI * i / samples);
            maxSin = Math.Max(maxSin, Math.Abs(SimMath.Sin(x) - Math.Sin(x)));
            maxCos = Math.Max(maxCos, Math.Abs(SimMath.Cos(x) - Math.Cos(x)));
        }
        Assert.True(maxSin <= Tolerance, $"max Sin error {maxSin}");
        Assert.True(maxCos <= Tolerance, $"max Cos error {maxCos}");
    }

    [Fact]
    public void Atan2_MatchesMathWithinTolerance_IncludingAxesAndOrigin()
    {
        double max = 0;
        // -32..31 scaled: includes x = 0 and y = 0 rows, so the axes and (0, 0) are covered.
        for (int iy = -32; iy < 32; iy++)
        {
            for (int ix = -32; ix < 32; ix++)
            {
                float y = iy * 0.37f;
                float x = ix * 0.37f;
                double err = Math.Abs(SimMath.Atan2(y, x) - Math.Atan2(y, x));
                max = Math.Max(max, err);
            }
        }
        Assert.True(max <= Tolerance, $"max Atan2 error {max}");
        Assert.Equal(0f, SimMath.Atan2(0f, 0f));
    }

    [Fact]
    public void Atan2_AxisValuesAreExactQuadrants()
    {
        Assert.Equal(0f, SimMath.Atan2(0f, 1f));
        Assert.Equal(SimMath.HalfPi, SimMath.Atan2(1f, 0f));
        Assert.Equal(SimMath.Pi, SimMath.Atan2(0f, -1f));
        Assert.Equal(-SimMath.HalfPi, SimMath.Atan2(-1f, 0f));
    }

    [Fact]
    public void RepeatedCalls_AreBitIdentical()
    {
        for (int i = 0; i < 1000; i++)
        {
            float x = -20f + i * 0.04f;
            Assert.Equal(BitConverter.SingleToInt32Bits(SimMath.Sin(x)), BitConverter.SingleToInt32Bits(SimMath.Sin(x)));
            Assert.Equal(BitConverter.SingleToInt32Bits(SimMath.Cos(x)), BitConverter.SingleToInt32Bits(SimMath.Cos(x)));
            Assert.Equal(BitConverter.SingleToInt32Bits(SimMath.Atan2(x, 1.5f)), BitConverter.SingleToInt32Bits(SimMath.Atan2(x, 1.5f)));
        }
    }

    [Fact]
    public void Sqrt_MatchesMathF()
    {
        Assert.Equal(MathF.Sqrt(2f), SimMath.Sqrt(2f));
        Assert.Equal(3f, SimMath.Sqrt(9f));
    }
}
