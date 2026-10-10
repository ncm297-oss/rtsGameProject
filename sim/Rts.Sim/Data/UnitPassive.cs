using System.Collections.Immutable;

namespace Rts.Sim.Data;

/// <summary>One entry of a unit's <c>passives</c> (M4-4b-3a), converted to sim units; the fields its trigger doesn't use are 0 / empty.</summary>
public readonly record struct UnitPassive
{
    /// <summary>When it acts.</summary>
    public PassiveTrigger Trigger { get; init; }
    /// <summary>WhenBelowHp: the status is on while hit points are below this fraction (0-1, exclusive) of the maximum; 0 otherwise.</summary>
    public float HpFraction
    {
        get => _hpFraction;
        init
        {
            _hpFraction = value;
            _hpLine = Abilities.PassiveSystem.LineOf(value);
        }
    }

    /// <summary>
    /// WhenBelowHp (BUG-0420): <see cref="HpFraction"/> in whole millionths (<c>PassiveSystem.LineScale</c>), rounded to the
    /// nearest, so the line test is an exact integer compare. Set with <see cref="HpFraction"/> (a <c>with</c> copy too), so
    /// it always follows from that hashed field; not hashed itself.
    /// </summary>
    internal int HpLine => _hpLine;

    private readonly float _hpFraction;
    private readonly int _hpLine;
    /// <summary>The status id it applies (index into <see cref="GameData.Statuses"/>).</summary>
    public int Status { get; init; }
    /// <summary>The status's magnitude, by the status kind's rules (as <see cref="AbilityEffect.Magnitude"/>; 0 for a blind).</summary>
    public float Magnitude { get; init; }
    /// <summary>OnDeath: how long the status lasts, in ticks (at least 1); 0 otherwise.</summary>
    public int DurationTicks { get; init; }
    /// <summary>OnDeath: the units whose center is within this many meters of the dying unit take it (the edge counts); 0 otherwise.</summary>
    public float Radius { get; init; }
    /// <summary>OnDeath: the unit type ids (own faction, file order) that take it; empty otherwise.</summary>
    public ImmutableArray<int> Units { get; init; }
}
