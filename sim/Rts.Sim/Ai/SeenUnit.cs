using System.Numerics;
using Rts.Sim.Entities;

namespace Rts.Sim.Ai;

/// <summary>
/// A unit as a <see cref="PlayerView"/> answers it (M5-1): one of the player's own, or an enemy's the player sees now.
/// Plain values copied out of the sim; holding one keeps nothing alive and grants no access.
/// </summary>
/// <param name="Handle">The unit's slot and generation (a later tick may hold another unit in the slot).</param>
/// <param name="Owner">The owning player.</param>
/// <param name="TypeId">Its unit type (<c>GameData.Units</c>).</param>
/// <param name="Position">Its position (m) at the current tick.</param>
/// <param name="Hp">Its hit points now.</param>
/// <param name="State">What it is doing (what its animation would show).</param>
public readonly record struct SeenUnit(EntityHandle Handle, int Owner, int TypeId, Vector2 Position, int Hp, UnitState State);
