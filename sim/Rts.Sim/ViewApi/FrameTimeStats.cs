using System;

namespace Rts.Sim.ViewApi;

/// <summary>Frame-time samples (milliseconds) as a fixed histogram: count, mean, worst and percentiles without storing every frame (M2-7 bench).</summary>
/// <remarks>
/// Bins are <see cref="BinMs"/> wide up to <see cref="MaxMs"/>; slower frames land in the last bin
/// (their exact value still counts toward <see cref="Average"/> and <see cref="Worst"/>). A
/// percentile is the upper edge of the bin it falls in, capped at <see cref="Worst"/>, so it is
/// accurate to <see cref="BinMs"/>. Allocates nothing after construction.
/// </remarks>
public sealed class FrameTimeStats
{
    /// <summary>Histogram resolution in milliseconds.</summary>
    public const double BinMs = 0.01;

    /// <summary>Upper edge of the histogram in milliseconds.</summary>
    public const double MaxMs = 250.0;

    private readonly int[] _bins = new int[(int)(MaxMs / BinMs)];
    private double _sum;

    /// <summary>Samples added.</summary>
    public int Count { get; private set; }

    /// <summary>Largest sample; 0 when empty.</summary>
    public double Worst { get; private set; }

    /// <summary>Mean sample; 0 when empty.</summary>
    public double Average => Count == 0 ? 0 : _sum / Count;

    /// <summary>Adds one frame time; negative or non-finite values are ignored.</summary>
    public void Add(double ms)
    {
        if (!(ms >= 0) || !double.IsFinite(ms)) return;
        int bin = (int)(ms / BinMs);
        _bins[Math.Min(bin, _bins.Length - 1)]++;
        _sum += ms;
        Count++;
        if (ms > Worst) Worst = ms;
    }

    /// <summary>The smallest bin edge at or below which at least <paramref name="p"/> (0-1) of the samples lie; 0 when empty.</summary>
    public double Percentile(double p)
    {
        if (Count == 0) return 0;
        p = Math.Clamp(double.IsNaN(p) ? 1 : p, 0, 1);
        long need = Math.Max(1, (long)Math.Ceiling(p * Count));
        long seen = 0;
        for (int i = 0; i < _bins.Length; i++)
        {
            seen += _bins[i];
            // The last bin holds every frame past MaxMs, so its edge is the worst sample.
            if (seen >= need) return i == _bins.Length - 1 ? Worst : Math.Min((i + 1) * BinMs, Worst);
        }
        return Worst;
    }

    /// <summary>Drops every sample.</summary>
    public void Clear()
    {
        Array.Clear(_bins);
        _sum = 0;
        Count = 0;
        Worst = 0;
    }
}
