namespace Rts.Sim.ViewApi;

/// <summary>What an <see cref="ImpactMarks"/> mark shows where a projectile landed (M4-V3).</summary>
public enum ImpactMarkKind : byte
{
    /// <summary>No mark (a free slot).</summary>
    None = 0,

    /// <summary>An aimed shot that hit: a short bright flash.</summary>
    Flash = 1,

    /// <summary>An aimed shot that missed and landed harmlessly: a small dust puff.</summary>
    Dust = 2,

    /// <summary>A lob's explosion: a burst sized by the projectile type.</summary>
    Burst = 3,
}
