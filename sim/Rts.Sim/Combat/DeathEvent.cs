using System.Numerics;
using Rts.Sim.Entities;

namespace Rts.Sim.Combat;

/// <summary>One killing blow of the last tick (M4-1): what died, whose it was, who killed it, and where. Read through <see cref="World.Deaths"/>.</summary>
/// <param name="Victim">The dead unit's or building's handle (stale by the time it is read).</param>
/// <param name="IsBuilding">True for a building, false for a unit.</param>
/// <param name="VictimType">Unit or building type id.</param>
/// <param name="VictimOwner">The player who lost it.</param>
/// <param name="KillerOwner">The player whose unit landed the blow; -1 for an expiry.</param>
/// <param name="Position">Where it died (m): a unit's position, a building's footprint center.</param>
/// <param name="Expired">
/// M4-4b-3b: a summoned unit whose lifetime ran out (phase 5), not a kill: no killer (<paramref name="KillerOwner"/> -1), no
/// kill or loss counted, no <c>onDeath</c> passive. A view may show it differently from a death.
/// </param>
public readonly record struct DeathEvent(EntityHandle Victim, bool IsBuilding, int VictimType, int VictimOwner, int KillerOwner, Vector2 Position, bool Expired = false);
