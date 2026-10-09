using System.Numerics;
using Rts.Sim.Entities;

namespace Rts.Sim.Abilities;

/// <summary>
/// One cast moment of the last tick (M4-4a): a cast started (the caster began standing) or resolved (the effects landed).
/// Read through <see cref="World.AbilityEvents"/>; output for views, not state.
/// </summary>
/// <param name="Caster">The casting unit's handle.</param>
/// <param name="Owner">Its owner.</param>
/// <param name="Ability">Ability id (index into <c>GameData.Abilities</c>).</param>
/// <param name="Point">The target point (m).</param>
/// <param name="Resolved">False: the cast started this tick; true: it resolved this tick.</param>
public readonly record struct AbilityEvent(EntityHandle Caster, int Owner, int Ability, Vector2 Point, bool Resolved);
