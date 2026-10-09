using Rts.Sim.Entities;

namespace Rts.Sim.ViewApi;

/// <summary>
/// The Attack order's feedback ring (M4-V2): which unit or building the player last ordered an attack on, and for how much
/// longer (view seconds) the ring shows on it. One mark at a time: a new order moves it. Presentation only, no
/// <c>World</c>; allocation-free.
/// </summary>
public sealed class TargetMark
{
    /// <summary>How long a mark shows, in seconds of view time.</summary>
    public const float DefaultSeconds = 0.5f;

    /// <summary>Creates an empty mark that lasts <paramref name="seconds"/> once set.</summary>
    public TargetMark(float seconds = DefaultSeconds) => Seconds = seconds;

    /// <summary>How long a mark shows, in seconds.</summary>
    public float Seconds { get; }

    /// <summary>The marked unit or building handle (default when none was ever set).</summary>
    public EntityHandle Target { get; private set; }

    /// <summary>True when <see cref="Target"/> is a building handle.</summary>
    public bool IsBuilding { get; private set; }

    /// <summary>Seconds of mark left (0: not shown).</summary>
    public float Left { get; private set; }

    /// <summary>True while the mark shows (time left).</summary>
    public bool Active => Left > 0f;

    /// <summary>Marks made so far.</summary>
    public int Marks { get; private set; }

    /// <summary>Marks <paramref name="target"/> for <see cref="Seconds"/> from now, replacing any mark.</summary>
    public void Mark(EntityHandle target, bool isBuilding)
    {
        Target = target;
        IsBuilding = isBuilding;
        Left = Seconds;
        Marks++;
    }

    /// <summary>One view frame, <paramref name="dt"/> seconds after the last (negative or NaN counts as 0).</summary>
    public void Update(float dt)
    {
        if (!(dt > 0f) || Left <= 0f) return;
        Left = dt >= Left ? 0f : Left - dt;
    }

    /// <summary>Ends the mark at once (its target died or was recycled).</summary>
    public void Clear() => Left = 0f;
}
