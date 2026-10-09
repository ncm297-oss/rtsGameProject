using Rts.Sim.Entities;

namespace Rts.Sim.Combat;

/// <summary>A hit queued in phase 10 at a swing's wind-up point and applied in phase 11 (M4-1); scratch, never hashed.</summary>
/// <param name="Attacker">The swinging unit (it may die before the hit applies; the hit still lands).</param>
/// <param name="AttackerOwner">Its owner, credited with a kill.</param>
/// <param name="Victim">The target unit or building.</param>
/// <param name="IsBuilding">True when <paramref name="Victim"/> is a building handle.</param>
/// <param name="Damage">The hit's damage (<see cref="DamageCalc"/>), at least 1.</param>
/// <param name="AttackerLevel">
/// The level the attacker struck from (M4-3a): its cell's at the melee hit, the firing level for a projectile; a hit from
/// above its victim reveals the attacker (<see cref="Vision.VisionSystem.OnHit"/>). -1: none known, no reveal.
/// </param>
/// <param name="AttackerIsBuilding">
/// True for a tower's shot (M4-3b): <paramref name="Attacker"/> is a building handle. It reveals nothing (reveals are per
/// unit), is never anyone's <c>LastAttacker</c> and starts no retaliation.
/// </param>
internal readonly record struct PendingHit(EntityHandle Attacker, int AttackerOwner, EntityHandle Victim, bool IsBuilding, int Damage, int AttackerLevel = -1, bool AttackerIsBuilding = false);
