using System.Numerics;

namespace Rts.Sim.Combat;

/// <summary>One projectile that landed in the last tick (M4-2b), for the view's impact effects. Read through <see cref="World.Impacts"/>.</summary>
/// <param name="Position">The impact point (m): where the target was when the shot was fired.</param>
/// <param name="ProjectileTypeId">Projectile type id (<see cref="Data.GameData.Projectiles"/>).</param>
/// <param name="Owner">The player who fired it.</param>
/// <param name="Hit">True when it struck: an aimed shot that hit its target, or a lob (which always explodes). False for an aimed miss, which lands harmlessly.</param>
public readonly record struct ProjectileImpact(Vector2 Position, int ProjectileTypeId, int Owner, bool Hit);
