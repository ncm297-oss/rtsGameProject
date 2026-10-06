using System;

namespace Rts.Sim.ViewApi;

/// <summary>The last N tick costs in milliseconds (view-side wall-clock samples), oldest first, for the debug overlay's tick-time graph (M2-5).</summary>
/// <remarks>Fixed capacity; once full, each <see cref="Add"/> drops the oldest sample. Allocates nothing after construction.</remarks>
public sealed class TickTimeRing
{
    /// <summary>Samples the overlay's graph keeps: 6 s of ticks at 20 Hz.</summary>
    public const int DefaultCapacity = 120;

    /// <summary>The tick budget the graph marks, in milliseconds (docs/03 "Testing strategy": average tick under 4 ms).</summary>
    public const double BudgetMs = 4.0;

    private readonly double[] _samples;
    private int _next;

    /// <summary>Creates an empty ring holding up to <paramref name="capacity"/> samples.</summary>
    public TickTimeRing(int capacity = DefaultCapacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _samples = new double[capacity];
    }

    /// <summary>Most samples held.</summary>
    public int Capacity => _samples.Length;

    /// <summary>Samples held (at most <see cref="Capacity"/>).</summary>
    public int Count { get; private set; }

    /// <summary>Samples added since construction, including dropped ones.</summary>
    public long Total { get; private set; }

    /// <summary>Appends a sample, dropping the oldest when full.</summary>
    public void Add(double ms)
    {
        _samples[_next] = ms;
        _next = (_next + 1) % _samples.Length;
        if (Count < _samples.Length) Count++;
        Total++;
    }

    /// <summary>Sample <paramref name="i"/>, 0 = oldest held, <see cref="Count"/> - 1 = newest.</summary>
    public double this[int i]
    {
        get
        {
            if ((uint)i >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(i));
            int start = Count < _samples.Length ? 0 : _next;
            return _samples[(start + i) % _samples.Length];
        }
    }

    /// <summary>Mean of the held samples; 0 when empty.</summary>
    public double Average
    {
        get
        {
            if (Count == 0) return 0;
            double sum = 0;
            for (int i = 0; i < Count; i++) sum += _samples[i];
            return sum / Count;
        }
    }

    /// <summary>Largest held sample; 0 when empty.</summary>
    public double Worst
    {
        get
        {
            double worst = 0;
            for (int i = 0; i < Count; i++) worst = Math.Max(worst, _samples[i]);
            return worst;
        }
    }

    /// <summary>Drops every sample.</summary>
    public void Clear()
    {
        Count = 0;
        _next = 0;
    }
}
