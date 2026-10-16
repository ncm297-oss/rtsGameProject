using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.Ai;

/// <summary>
/// The AI's abilities (M5-4c, docs/03 "Implementation (M5-4c)"), last in a think, only when the difficulty's
/// <c>useAbilities</c> is on. Every own unit whose type has abilities, free (not casting or walking to a cast, no AI command
/// pending, not the scout, not fleeing, not a wave member owed its Move home), gets at most one <c>UseAbility</c> a think:
/// its first ability, in list order, that is ready and has a target.
/// <list type="bullet">
/// <item><b>Target ground.</b> The enemy units seen within <c>range + radius</c> of the caster; the centroid of those of them
/// within <c>radius</c> of their centroid, pulled back to <c>range</c> of the caster; cast there when at least
/// <c>ai.castMinTargets</c> enemies seen are within <c>radius</c> of it. An ability that hits buildings, with no such group:
/// the footprint centre of the nearest enemy building seen now (no ghost) within <c>range + radius</c>, pulled back alike.</item>
/// <item><b>Self / aura, summon.</b> At the caster, when <c>ai.castMinTargets</c> enemies seen are within <c>radius</c> of it.</item>
/// <item><b>Friendly fire.</b> An ability whose damage reaches own units is never cast with an own unit within <c>radius</c>
/// of the point (the caster included).</item>
/// <item><b>No stacking (BUG-0702).</b> A ground ability whose effects don't add up (no damage and no spawn: its statuses
/// and zones refresh rather than stack) is not cast within its <c>radius</c> of a point a caster was sent the same ability at
/// whose effect still runs (sent + cast time + duration); that caster tries its next ability, or waits.</item>
/// <item><b>Judging.</b> A cast is judged at a later think by the ability's ready tick: moved, it landed. One that didn't is
/// sent again, at most <see cref="MaxTries"/> times at the same point (within a cell), then left alone until the point moves.</item>
/// </list>
/// A full queue ends the pass; the rest go next think.
/// </summary>
internal static class AbilityManager
{
    /// <summary>
    /// Casts sent at one point (within a cell) without the cooldown moving before the AI stops sending it there (a cast
    /// refused or lost every time). Not a balance number: a guard against re-sending an order that does nothing.
    /// </summary>
    internal const int MaxTries = 3;

    // A point pulled back to the range stays this far inside it, so float rounding never leaves the caster a walk.
    private const float RangeSlack = 0.05f;

    /// <summary>Running ground casts the no-stacking rule remembers (BUG-0702); past it a new one isn't recorded. A buffer size, not a balance number.</summary>
    internal const int MaxRecentCasts = 64;

    /// <summary>One cast a think at most per free own caster (see the type's summary).</summary>
    internal static void Run(AiPlayer ai)
    {
        if (!ai.Knobs.UseAbilities) return;
        ai.Board.SeenEnemyCount = -1; // filled at the first free caster
        ForgetEnded(ai.Board, ai.Stamp);
        if (Casters(ai, ai.ArmySlots, ai.ArmyCount)) Casters(ai, ai.WorkerSlots, ai.WorkerCount);
    }

    /// <summary>The casters among the first <paramref name="count"/> of <paramref name="slots"/>; false when the queue filled.</summary>
    private static bool Casters(AiPlayer ai, int[] slots, int count)
    {
        Blackboard board = ai.Board;
        for (int k = 0; k < count; k++)
        {
            int slot = slots[k];
            if (!ai.View.TryGetUnit(slot, out SeenUnit u)) continue;
            UnitDef def = ai.Data.Units[u.TypeId];
            int generation = u.Handle.Generation;
            if (def.Abilities.Length == 0 || u.State == UnitState.Casting || ai.View.TryGetCast(slot, out _)) continue;
            if (ai.OrderedNotApplied(slot, generation) || ai.IsScout(slot, generation) || ai.IsFleeing(slot, generation)) continue;
            if (board.WaveGeneration[slot] == generation && board.WaveFlag[slot] == MilitaryManager.ToRally) continue;
            bool mine = board.CastGeneration[slot] == generation;
            // The last cast judged: the cooldown moved, so it landed and the tries start again.
            if (mine && ai.View.TryGetAbilityReady(slot, board.CastIndex[slot], out int was) && was > board.CastReadyAt[slot]) board.CastTries[slot] = 0;
            for (int a = 0; a < def.Abilities.Length; a++)
            {
                if (!ai.View.TryGetAbilityReady(slot, a, out int ready) || ready > ai.Stamp) continue;
                if (board.SeenEnemyCount < 0) SeeEnemies(ai);
                AbilityDef ability = ai.Data.Abilities[def.Abilities[a]];
                if (!Target(ai, u.Position, ability, out Vector2 point)) continue;
                bool same = mine && board.CastIndex[slot] == a
                    && Vector2.DistanceSquared(point, board.CastAt[slot]) <= Map.MapConstants.CellSize * Map.MapConstants.CellSize;
                if (same && board.CastTries[slot] >= MaxTries) continue; // left alone until the point moves
                bool ground = ability.Kind is not (AbilityKind.SelfAura or AbilityKind.Summon) && !Stacks(ability);
                if (ground && SentNear(board, def.Abilities[a], point, ability.Radius)) continue;
                if (!ai.Send(Command.UseAbility(ai.Player, u.Handle, a, point))) return false;
                if (ground && board.RecentCastCount < MaxRecentCasts)
                {
                    int n = board.RecentCastCount++;
                    board.RecentCastAbility[n] = def.Abilities[a];
                    board.RecentCastAt[n] = point;
                    board.RecentCastUntil[n] = ai.Stamp + ability.CastTicks + ability.DurationTicks;
                }
                ai.MarkOrdered(slot, generation, 0);
                board.CastGeneration[slot] = generation;
                board.CastIndex[slot] = (byte)a;
                board.CastTries[slot] = (byte)((same ? board.CastTries[slot] : 0) + 1);
                board.CastReadyAt[slot] = ready;
                board.CastAt[slot] = point;
                board.CastsSent++;
                break;
            }
        }
        return true;
    }

    /// <summary>Whether two casts of <paramref name="a"/> on one group add up: it deals damage or spawns (BUG-0702).</summary>
    private static bool Stacks(AbilityDef a)
    {
        foreach (AbilityEffect fx in a.Effects)
            if (fx.Kind is AbilityEffectKind.Damage or AbilityEffectKind.Spawn) return true;
        return false;
    }

    /// <summary>Whether a running cast of ability <paramref name="ability"/> was sent to a point within <paramref name="radius"/> of <paramref name="point"/>.</summary>
    private static bool SentNear(Blackboard board, int ability, Vector2 point, float radius)
    {
        for (int k = 0; k < board.RecentCastCount; k++)
            if (board.RecentCastAbility[k] == ability && Vector2.DistanceSquared(board.RecentCastAt[k], point) <= radius * radius) return true;
        return false;
    }

    /// <summary>Drops the remembered casts whose effect has ended by tick <paramref name="now"/>, keeping the rest in order.</summary>
    private static void ForgetEnded(Blackboard board, int now)
    {
        int kept = 0;
        for (int k = 0; k < board.RecentCastCount; k++)
        {
            if (board.RecentCastUntil[k] <= now) continue;
            board.RecentCastAbility[kept] = board.RecentCastAbility[k];
            board.RecentCastAt[kept] = board.RecentCastAt[k];
            board.RecentCastUntil[kept++] = board.RecentCastUntil[k];
        }
        board.RecentCastCount = kept;
    }

    /// <summary>Fills <see cref="Blackboard.SeenEnemies"/> with the enemy units the player sees now.</summary>
    private static void SeeEnemies(AiPlayer ai)
    {
        Blackboard board = ai.Board;
        int n = 0, s = -1;
        while (ai.View.NextUnit(ref s, out SeenUnit u))
            if (u.Owner != ai.Player) board.SeenEnemies[n++] = u.Position;
        board.SeenEnemyCount = n;
    }

    /// <summary>Where caster at <paramref name="at"/> casts <paramref name="a"/> now (see the type's summary); false for nowhere.</summary>
    private static bool Target(AiPlayer ai, Vector2 at, AbilityDef a, out Vector2 point)
    {
        int min = ai.Rules.CastMinTargets;
        float r2 = a.Radius * a.Radius;
        bool friendly = FriendlyFire(a);
        point = ai.ClampToMap(at); // the door wants a point on the map; a self / aura or summon resolves at the caster anyway
        if (a.Kind is AbilityKind.SelfAura or AbilityKind.Summon) return Count(ai, at, r2) >= min && !(friendly && OwnWithin(ai, at, r2));
        Blackboard board = ai.Board;
        float reach = (a.Range + a.Radius) * (a.Range + a.Radius);
        Vector2 sum = Vector2.Zero;
        int near = 0;
        for (int e = 0; e < board.SeenEnemyCount; e++)
        {
            if (Vector2.DistanceSquared(board.SeenEnemies[e], at) > reach) continue;
            sum += board.SeenEnemies[e];
            near++;
        }
        if (near >= min)
        {
            Vector2 centroid = sum / near, group = Vector2.Zero;
            int inGroup = 0;
            for (int e = 0; e < board.SeenEnemyCount; e++)
            {
                Vector2 p = board.SeenEnemies[e];
                if (Vector2.DistanceSquared(p, at) > reach || Vector2.DistanceSquared(p, centroid) > r2) continue;
                group += p;
                inGroup++;
            }
            if (inGroup > 0)
            {
                if (Aim(ai, at, group / inGroup, a.Range, out point) && Count(ai, point, r2) >= min && !(friendly && OwnWithin(ai, point, r2))) return true;
            }
        }
        if (!a.HitsBuildings || !NearestEnemyBuilding(ai, at, reach, out Vector2 centre)) return false;
        return Aim(ai, at, centre, a.Range, out point) && !(friendly && OwnWithin(ai, point, r2));
    }

    /// <summary>
    /// <paramref name="to"/>, or the point on the way to it from <paramref name="from"/> just inside <paramref name="range"/>,
    /// kept on the map (the door refuses a point off it; BUG-0700: a unit spawned off the map moved the centroid there); false
    /// when that leaves it out of range.
    /// </summary>
    private static bool Aim(AiPlayer ai, Vector2 from, Vector2 to, float range, out Vector2 point)
    {
        Vector2 d = to - from;
        float length = d.Length();
        float max = range > RangeSlack ? range - RangeSlack : 0f;
        point = ai.ClampToMap(length <= max ? to : from + d * (max / length));
        return Vector2.DistanceSquared(point, from) <= range * range;
    }

    /// <summary>The enemy units seen within √<paramref name="r2"/> of <paramref name="p"/>.</summary>
    private static int Count(AiPlayer ai, Vector2 p, float r2)
    {
        Blackboard board = ai.Board;
        int n = 0;
        for (int e = 0; e < board.SeenEnemyCount; e++)
            if (Vector2.DistanceSquared(board.SeenEnemies[e], p) <= r2) n++;
        return n;
    }

    /// <summary>Whether an own live unit stands within √<paramref name="r2"/> of <paramref name="p"/>.</summary>
    private static bool OwnWithin(AiPlayer ai, Vector2 p, float r2) =>
        OwnWithin(ai, ai.ArmySlots, ai.ArmyCount, p, r2) || OwnWithin(ai, ai.WorkerSlots, ai.WorkerCount, p, r2);

    private static bool OwnWithin(AiPlayer ai, int[] slots, int count, Vector2 p, float r2)
    {
        for (int k = 0; k < count; k++)
            if (ai.View.TryGetUnit(slots[k], out SeenUnit u) && Vector2.DistanceSquared(u.Position, p) <= r2) return true;
        return false;
    }

    /// <summary>The footprint centre of the enemy building seen now (no ghost) nearest <paramref name="at"/> within √<paramref name="reach2"/> of it (slot order on ties).</summary>
    private static bool NearestEnemyBuilding(AiPlayer ai, Vector2 at, float reach2, out Vector2 centre)
    {
        centre = default;
        float best = float.MaxValue;
        int s = -1;
        while (ai.View.NextBuilding(ref s, out SeenBuilding b))
        {
            if (b.Owner == ai.Player) continue;
            Vector2 c = ai.BuildingCentre(b.TypeId, b.Cell);
            float d = Vector2.DistanceSquared(c, at);
            if (d > reach2 || d >= best) continue;
            best = d;
            centre = c;
        }
        return best < float.MaxValue;
    }

    /// <summary>Whether a damage effect of <paramref name="a"/> reaches the caster owner's units: its friendly fire, or <c>affects</c> all units.</summary>
    private static bool FriendlyFire(AbilityDef a)
    {
        foreach (AbilityEffect fx in a.Effects)
            if (fx.Kind == AbilityEffectKind.Damage && (fx.FriendlyFire > 0f || a.Affects == AbilityAffects.AllUnits)) return true;
        return false;
    }
}
