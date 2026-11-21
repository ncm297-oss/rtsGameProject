namespace Rts.Sim.Data;

/// <summary>When a unit passive acts (a unit's <c>passives[].trigger</c>, M4-4b-3a); spelled as <see cref="DataLimits.PassiveTriggerIds"/>.</summary>
public enum PassiveTrigger
{
    /// <summary>While the unit's hit points are below a fraction of its maximum, it has a status (Frenzy of the Apocalypse).</summary>
    WhenBelowHp = 0,

    /// <summary>When the unit dies, its owner's live units of listed types near it take a status (Martyrdom).</summary>
    OnDeath = 1,

    /// <summary>M7-2: each hit the unit lands on a unit that survives it (a melee hit or its projectile's direct landing) gives the victim a status (the war-dog's Hamstring).</summary>
    OnHit = 2,

    /// <summary>
    /// M8-3: each hit the unit deals is multiplied by <c>1 + min(n, maxStacks) x damageBonus</c>, n its owner's other live
    /// units of the listed types within the radius at the hit's moment (the Hound of Shadow's Pack Hunter).
    /// </summary>
    NearbyOwn = 3,
}
