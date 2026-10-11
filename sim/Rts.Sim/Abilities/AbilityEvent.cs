using System.Numerics;
using Rts.Sim.Entities;

namespace Rts.Sim.Abilities;

/// <summary>
/// One cast moment of the last tick (M4-4a): a cast started (the caster began standing) or resolved (the effects landed);
/// M4-H3 (BUG-0461): or was dropped without a new order (<paramref name="Dropped"/>). Read through
/// <see cref="World.AbilityEvents"/>; output for views, not state.
/// </summary>
/// <param name="Caster">The casting unit's handle.</param>
/// <param name="Owner">Its owner.</param>
/// <param name="Ability">Ability id (index into <c>GameData.Abilities</c>).</param>
/// <param name="Point">The target point (m).</param>
/// <param name="Resolved">False: the cast started this tick (or was dropped, see <paramref name="Dropped"/>); true: it resolved this tick.</param>
/// <param name="Dropped">
/// True (with <paramref name="Resolved"/> false): the cast was lost this tick with no cooldown and no new order: its walk into
/// range stopped short (arrived as near as the ground allows, or stuck), or something other than an order took the caster off
/// its cast. A new order's cancel raises nothing (the player chose it).
/// </param>
public readonly record struct AbilityEvent(EntityHandle Caster, int Owner, int Ability, Vector2 Point, bool Resolved, bool Dropped = false);
