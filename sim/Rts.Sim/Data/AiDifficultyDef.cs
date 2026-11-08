namespace Rts.Sim.Data;

/// <summary>One difficulty's knobs from a faction's <c>ai.json</c> <c>difficulties</c> (M5-1, docs/02 "AI opponent"), converted to sim units.</summary>
public readonly record struct AiDifficultyDef
{
    /// <summary>Ticks between two AI thinks (<c>thinkInterval</c> s; at least 1 tick).</summary>
    public int ThinkTicks { get; init; }

    /// <summary>How late a build-order step may run, in ticks (<c>buildOrderSlack</c> s).</summary>
    public int BuildOrderSlackTicks { get; init; }

    /// <summary>Scales the attack thresholds' wave size (above 0, at most <see cref="DataLimits.MaxAiWaveSizeMultiplier"/>).</summary>
    public float WaveSizeMultiplier { get; init; }

    /// <summary>Extra gather rate as a fraction (0-1; docs/02: Hard 0.25).</summary>
    public float GatherBonus { get; init; }

    /// <summary>Whether its army focuses fire.</summary>
    public bool FocusFire { get; init; }

    /// <summary>Whether it pulls wounded units back.</summary>
    public bool PullBackWounded { get; init; }

    /// <summary>Whether it casts abilities.</summary>
    public bool UseAbilities { get; init; }

    /// <summary>The earliest match time it expands, in ticks (<c>expandAfter</c> s).</summary>
    public int ExpandAfterTicks { get; init; }

    /// <summary>
    /// M5-9a: how far its army mix shifts toward what counters the enemy units it has seen (<c>counterStrength</c>, 0-1; 0 is
    /// no shift): a composition unit's weight is multiplied by 1 + this x the share of the remembered enemy its slot counters.
    /// </summary>
    public float CounterStrength { get; init; }
}
