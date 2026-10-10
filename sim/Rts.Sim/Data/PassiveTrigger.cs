namespace Rts.Sim.Data;

/// <summary>When a unit passive acts (a unit's <c>passives[].trigger</c>, M4-4b-3a); spelled as <see cref="DataLimits.PassiveTriggerIds"/>.</summary>
public enum PassiveTrigger
{
    /// <summary>While the unit's hit points are below a fraction of its maximum, it has a status (Frenzy of the Apocalypse).</summary>
    WhenBelowHp = 0,

    /// <summary>When the unit dies, its owner's live units of listed types near it take a status (Martyrdom).</summary>
    OnDeath = 1,
}
