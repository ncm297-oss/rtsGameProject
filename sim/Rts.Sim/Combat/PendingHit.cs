using Rts.Sim.Entities;

namespace Rts.Sim.Combat;

/// <summary>A hit queued in phase 10 at a swing's wind-up point and applied in phase 11 (M4-1); scratch, never hashed.</summary>
/// <param name="Attacker">The swinging unit (it may die before the hit applies; the hit still lands).</param>
/// <param name="AttackerOwner">Its owner, credited with a kill.</param>
/// <param name="Victim">The target unit or building.</param>
/// <param name="IsBuilding">True when <paramref name="Victim"/> is a building handle.</param>
/// <param name="Damage">The hit's damage (<see cref="DamageCalc"/>), at least 1.</param>
internal readonly record struct PendingHit(EntityHandle Attacker, int AttackerOwner, EntityHandle Victim, bool IsBuilding, int Damage);
