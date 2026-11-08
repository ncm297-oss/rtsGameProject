using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Vision;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-5 (criterion 5, docs/03 "Implementation (M5-5)"): plays an AI-only match headless to its end or a bound, folding the
/// per-tick state hash, and (with checks on) asserts "without errors" on every tick and think: (a) no exception; (b) the
/// <see cref="PlayerViewOracle"/> clean on every think of every seat (and its node memory every tick); (c) the census
/// (<c>KnownEnemyBuildings</c> = the enemy buildings the seat sees plus its ghosts of undefeated enemies, and no wave left on
/// a defeated seat's ghost); (d) every live unit and building on the map, finite, with a live generation and hit points in
/// range; (e) every command phase 1 drops (<see cref="Simulation.RefusedForTests"/>) counted by kind, only the documented
/// kinds allowed, and no AI command refused more than <see cref="MaxStreak"/> times in a row on the same target. The checks
/// only read: a match with them on folds the same hashes as one with them off.
/// </summary>
internal static class CriterionMatch
{
    /// <summary>
    /// The most thinks in a row one seat's command on one target may be refused (the target: kind, unit, attack target, type
    /// and point; a Build's or Gather's worker left out, since the AI re-sends those with another worker), several refusals
    /// of it at one tick counting once, any acceptance of it ending the run. Measured at most 1 on the six rows (docs/03
    /// "Implementation (M5-5)"); 8 leaves room for that and still catches BUG-1151's loop (a gatherer moved by the rebalance
    /// to a remembered mine that is gone, re-sent every think: the 9th think in a row at tick 18,421 on the generated map,
    /// seed 4, which is not a row).
    /// </summary>
    internal const int MaxStreak = 8;

    /// <summary>
    /// The refusal kinds a match may show (<c>kind:refusal[:detail]</c>), each with its cause in docs/03 "Implementation
    /// (M5-5)". The AI judges every order from its view a tick before it applies, so the world can differ by then.
    /// </summary>
    internal static readonly string[] Documented =
    {
        // A Build on a cell that became blocked, or that the AI's site check never judges (M5-2: PlayerView.CanPlace leaves
        // out units in the way and the never-seal rule; the executor's retry mark moves the next try on): a unit stood in the
        // footprint, the site would seal ground off, or another seat's site (an ally picking the same anchor in the same
        // tick, in a 2v2) took the cell first.
        "Build:BuildRefused:UnitInTheWay", "Build:BuildRefused:SealsGround", "Build:BuildRefused:Blocked",
        // A Train over the cap or without the cost (the brief's; no row shows one: the reserve and the queue check hold).
        "Train:TrainRefused:QueueFull", "Train:TrainRefused:CannotAfford",
        // An order on a unit that died the tick it was sent, or at a target that did.
        "Move:UnitGone", "AttackMove:UnitGone", "Attack:UnitGone", "Gather:UnitGone", "Build:UnitGone", "Stop:UnitGone",
        "UseAbility:UnitGone", "Attack:AttackTargetInvalid",
        // A Gather to a node the seat remembers that is gone (BUG-0570: dropped at apply, the node marked so no worker goes again).
        "Gather:NoResourceNode",
    };

    /// <summary>What one match came to.</summary>
    internal sealed class Result
    {
        public bool Over;
        public int EndTick;
        public ulong Fold, EndHash;
        public string Winner = "none";
        public int Hunts, Waves;
        public int[] DefeatedAt = Array.Empty<int>();
        public readonly SortedDictionary<string, int> Refusals = new(StringComparer.Ordinal);
        public int LongestStreak;
        public string LongestStreakAt = "";
        public long Thinks, OracleChecks, CensusChecks, EntityChecks;
        public string? Error;

        /// <summary>The line two processes compare: the fold, the end hash and tick, the winner and the hunts.</summary>
        public string TwinLine => $"{Fold:X16} {EndHash:X16} {EndTick} {Winner} {Hunts}";

        public string RefusalText => Refusals.Count == 0 ? "none" : string.Join(", ", Refusals.Select(kv => $"{kv.Key} {kv.Value}"));
    }

    /// <summary>
    /// A match on <paramref name="map"/> (<c>generated</c> or a map file id) of <paramref name="players"/> Normal AI seats
    /// (factions alternate Malazan / Whirlwind, starts in seat order), <paramref name="teams"/> per seat (0 = none); unit
    /// capacity 400 for two seats, 1,024 for more (the M5-H5 four-seat rows').
    /// </summary>
    internal static Simulation Build(string map, ulong seed, int players = 2, int[]? teams = null, AiDifficulty difficulty = AiDifficulty.Normal)
    {
        var seats = new PlayerSeat[players];
        for (int p = 0; p < players; p++) seats[p] = new PlayerSeat(p % 2, SeatController.Ai, difficulty, teams == null ? 0 : teams[p]);
        return Stress.HuntStressTests.Match(map, seats, seed, units: players > 2 ? 1024 : 400);
    }

    /// <summary>
    /// Plays <paramref name="sim"/> until the match is over or <paramref name="bound"/> ticks. With <paramref name="checks"/>
    /// the error checks (a)-(e) run and the first failure is in <see cref="Result.Error"/> (the match stops there);
    /// <paramref name="line"/> gets the 6,000-tick lines. <paramref name="onTick"/> runs after each tick (a save's follower).
    /// </summary>
    internal static Result Play(Simulation sim, int bound, bool checks, Action<string>? line = null, Action<Simulation>? onTick = null)
    {
        World w = sim.World;
        int n = w.Config.PlayerCount;
        var r = new Result { DefeatedAt = new int[n] };
        var tally = new PlayerViewOracle.Tally();
        var hasher = new Determinism.StateHasher();
        string? bad = null;
        // (e): a refusal's key, matched against the AI's sent commands once they applied.
        var refused = new Dictionary<(int, CommandKind, EntityHandle, EntityHandle, int, Vector2), int>();
        var streak = new Dictionary<(int, CommandKind, EntityHandle, EntityHandle, int, Vector2), int>();
        var sent = new List<Command>();
        if (checks)
        {
            sim.RefusedForTests = (c, why, detail) =>
            {
                string kind = $"{c.Kind}:{why}{Detail(why, detail)}";
                r.Refusals[kind] = r.Refusals.TryGetValue(kind, out int k) ? k + 1 : 1;
                if (Array.IndexOf(Documented, kind) < 0) bad ??= $"tick {c.Tick}: player {c.Player}'s {c.Kind} refused as {kind}, a kind not documented (unit {c.Unit}, type {c.TypeId}, at {c.Position})";
                var key = Key(c);
                refused[key] = refused.TryGetValue(key, out int m) ? m + 1 : 1;
            };
            for (int p = 0; p < n; p++)
            {
                AiPlayer? ai = sim.AiOf(p);
                if (ai == null) continue;
                int player = p;
                ai.SentForTests = c => sent.Add(c);
                ai.ThoughtForTests = a =>
                {
                    r.Thinks++;
                    if (bad != null) return;
                    bad = Census(w, a, player, r);
                    r.OracleChecks++;
                    PlayerViewOracle.Check(w, a.View, player, tally, $"tick {a.Now} player {player}");
                    if (tally.First.Length > 0) bad ??= tally.First;
                };
            }
        }
        var waving = new bool[n];
        while (!sim.MatchOver && sim.TickNumber < bound && bad == null)
        {
            int tick = sim.TickNumber;
            try
            {
                sim.Tick();
            }
            catch (Exception e)
            {
                bad = $"tick {tick}: {e}"; // (a)
                break;
            }
            hasher.Add(sim.StateHash());
            onTick?.Invoke(sim);
            for (int p = 0; p < n; p++)
            {
                if (r.DefeatedAt[p] == 0 && w.IsDefeated(p)) r.DefeatedAt[p] = sim.TickNumber;
                AiPlayer? ai = sim.AiOf(p);
                if (ai == null) continue;
                if (ai.Board.WaveActive && !waving[p]) r.Waves++;
                waving[p] = ai.Board.WaveActive;
            }
            if (checks)
            {
                for (int p = 0; p < n; p++) PlayerViewOracle.NodeOracle.For(w).Observe(w, p, tally, $"tick {sim.TickNumber} player {p}");
                if (tally.First.Length > 0) bad ??= tally.First;
                bad ??= Entities(w, r);
                bad ??= Streaks(sim, sent, refused, streak, r);
            }
            if (sim.TickNumber % 6000 == 0 && line != null)
            {
                var parts = new List<string>();
                for (int p = 0; p < n; p++)
                    parts.Add($"p{p} {(w.IsDefeated(p) ? "out" : "in")} gold {w.GatheredGold[p]} wood {w.GatheredWood[p]} army {sim.AiOf(p)?.ArmyCount ?? 0} prod {w.ProductionBuildings(p)}");
                line($"{Clock(sim.TickNumber)} ({sim.TickNumber}): hash {sim.StateHash():X16}; {string.Join("; ", parts)}");
            }
        }
        r.Error = bad;
        r.Over = sim.MatchOver;
        r.EndTick = sim.TickNumber;
        r.Fold = hasher.Value;
        r.EndHash = sim.StateHash();
        r.Winner = Winner(w);
        for (int p = 0; p < n; p++) r.Hunts += sim.AiOf(p)?.Board.HuntsStarted ?? 0;
        sim.RefusedForTests = null;
        for (int p = 0; p < n; p++)
        {
            AiPlayer? ai = sim.AiOf(p);
            if (ai == null) continue;
            ai.SentForTests = null;
            ai.ThoughtForTests = null;
        }
        return r;
    }

    internal static string Clock(int ticks) => $"{ticks / 1200}:{ticks / 20 % 60:D2}";

    /// <summary>The undefeated side: <c>pN</c> (no teams) or <c>tN</c>, <c>none</c> with two or more left or none.</summary>
    internal static string Winner(World w)
    {
        if (!w.MatchOver) return "none";
        string? side = null;
        for (int p = 0; p < w.Config.PlayerCount; p++)
        {
            if (w.IsDefeated(p) || !w.EverBuilt(p)) continue;
            int team = w.Config.SeatOf(p).Team;
            string s = team == 0 ? $"p{p}" : $"t{team}";
            if (side != null && side != s) return "none";
            side = s;
        }
        return side ?? "none";
    }

    private static string Detail(CommandRefusal why, int detail) => why switch
    {
        CommandRefusal.BuildRefused => ":" + (PlacementError)detail,
        CommandRefusal.TrainRefused => ":" + (TrainError)detail,
        CommandRefusal.ResearchRefused => ":" + (ResearchError)detail,
        _ => "",
    };

    private static (int, CommandKind, EntityHandle, EntityHandle, int, Vector2) Key(in Command c) =>
        (c.Player, c.Kind, c.Kind is CommandKind.Build or CommandKind.Gather ? default : c.Unit, c.Target, c.TypeId, c.Position);

    /// <summary>(c): the census of player <paramref name="p"/> at a think, from the world's own defeat flags.</summary>
    private static string? Census(World w, AiPlayer ai, int p, Result r)
    {
        r.CensusChecks++;
        int expected = 0, s = -1;
        while (ai.View.NextBuilding(ref s, out SeenBuilding b))
            if (b.Owner != p && w.IsEnemy(p, b.Owner)) expected++;
        s = -1;
        while (ai.View.NextGhost(ref s, out BuildingGhost g))
            if (w.IsEnemy(p, g.Owner) && !w.IsDefeated(g.Owner)) expected++;
        if (ai.Board.KnownEnemyBuildings != expected)
            return $"tick {ai.Now}: player {p} KnownEnemyBuildings {ai.Board.KnownEnemyBuildings}, the world's count {expected}";
        Blackboard bb = ai.Board;
        if (bb.WaveActive && !bb.Hunting && bb.WaveTarget.Generation != 0
            && ai.View.TryGetGhost(bb.WaveTarget.Index, out BuildingGhost tg) && tg.Generation == bb.WaveTarget.Generation && w.IsDefeated(tg.Owner))
            return $"tick {ai.Now}: player {p}'s wave walks to defeated player {tg.Owner}'s ghost";
        return null;
    }

    /// <summary>(d): every live unit and building on the map, finite, its generation live, its hit points in range; every unit's target handle in range.</summary>
    private static string? Entities(World w, Result r)
    {
        float width = w.NavGrid.Width * Map.MapConstants.CellSize, height = w.NavGrid.Height * Map.MapConstants.CellSize;
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            r.EntityChecks++;
            Vector2 at = u.Position[i];
            if (!float.IsFinite(at.X) || !float.IsFinite(at.Y) || at.X < 0f || at.Y < 0f || at.X >= width || at.Y >= height)
                return $"tick {w.TickNumber}: unit {i} (player {u.Owner[i]}) at {at}, off the map or not finite";
            if (!float.IsFinite(u.Velocity[i].X) || !float.IsFinite(u.Velocity[i].Y) || !float.IsFinite(u.Facing[i]))
                return $"tick {w.TickNumber}: unit {i}'s velocity {u.Velocity[i]} or facing {u.Facing[i]} not finite";
            if (u.Generation[i] <= 0 || !u.IsAlive(new EntityHandle(i, u.Generation[i])))
                return $"tick {w.TickNumber}: unit {i} alive with generation {u.Generation[i]}";
            if (u.Hp[i] <= 0 || u.Hp[i] > w.MaxHp(i))
                return $"tick {w.TickNumber}: unit {i} alive with {u.Hp[i]} hp of {w.MaxHp(i)}";
            if (u.Owner[i] < 0 || u.Owner[i] >= w.Config.PlayerCount)
                return $"tick {w.TickNumber}: unit {i} owned by {u.Owner[i]}";
            EntityHandle t = u.Target[i];
            if (t.Generation != 0 && (uint)t.Index >= (uint)(u.TargetIsBuilding[i] ? w.Buildings.Capacity : u.Capacity))
                return $"tick {w.TickNumber}: unit {i}'s target {t} out of range";
        }
        BuildingStore b = w.Buildings;
        int cells = w.NavGrid.Width * w.NavGrid.Height;
        for (int k = 0; k < b.Capacity; k++)
        {
            if (!b.Alive[k]) continue;
            r.EntityChecks++;
            if ((uint)b.Cell[k] >= (uint)cells) return $"tick {w.TickNumber}: building {k} at cell {b.Cell[k]}, off the map";
            if (b.Generation[k] <= 0 || !b.IsAlive(b.HandleOf(k))) return $"tick {w.TickNumber}: building {k} alive with generation {b.Generation[k]}";
            int max = w.Data.Buildings[b.TypeId[k]].Hp;
            if (b.Hp[k] <= 0 || b.Hp[k] > max) return $"tick {w.TickNumber}: building {k} alive with {b.Hp[k]} hp of {max}";
            if (b.Owner[k] < 0 || b.Owner[k] >= w.Config.PlayerCount) return $"tick {w.TickNumber}: building {k} owned by {b.Owner[k]}";
        }
        return null;
    }

    /// <summary>(e): every AI command that applied this tick: a refused one lengthens its target's run (once a tick), an accepted one ends it.</summary>
    private static string? Streaks(Simulation sim, List<Command> sent, Dictionary<(int, CommandKind, EntityHandle, EntityHandle, int, Vector2), int> refused,
        Dictionary<(int, CommandKind, EntityHandle, EntityHandle, int, Vector2), int> streak, Result r)
    {
        int applied = sim.TickNumber - 1;
        string? bad = null;
        int keep = 0;
        for (int j = 0; j < sent.Count; j++)
        {
            Command c = sent[j];
            if (c.Tick > applied)
            {
                sent[keep++] = c;
                continue;
            }
            var key = Key(c);
            if (!refused.TryGetValue(key, out int m) || m == 0)
            {
                streak.Remove(key); // accepted
                continue;
            }
            // The first refusal of this target this tick lengthens its run; the rest of the tick's (a batch of workers sent
            // to one gone node in one think) are the same try. -1 marks the target counted.
            if (m > 0)
            {
                int run = streak.TryGetValue(key, out int s) ? s + 1 : 1;
                streak[key] = run;
                if (run > r.LongestStreak)
                {
                    r.LongestStreak = run;
                    r.LongestStreakAt = $"tick {applied}: player {c.Player} {c.Kind} unit {c.Unit} type {c.TypeId} at {c.Position}";
                }
                if (run > MaxStreak) bad ??= $"{r.LongestStreakAt}: refused {run} thinks in a row";
                refused[key] = -1;
            }
        }
        sent.RemoveRange(keep, sent.Count - keep);
        refused.Clear(); // a refusal no AI command matches (the setup's spawns) ends with its tick
        return bad;
    }
}
