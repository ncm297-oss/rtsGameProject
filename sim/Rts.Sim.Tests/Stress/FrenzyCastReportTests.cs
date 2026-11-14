using System.Collections.Concurrent;
using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Content;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// M7-7 Part A (Request 53): why the Normal Teblor AI almost never casts Blood-oil Frenzy. On the
/// <see cref="BloodOilScenes"/> copy, the Normal Teblor AI against a Normal Malazan and a Normal Whirlwind, both seat orders,
/// seeds 1-8, on the generated map, to 15:00 (32 matches a configuration). At every Teblor think the report finds the
/// Shamans the ability manager would consider (alive, free by its own rule, the Frenzy ready) and measures the three
/// candidate causes there: too few Shamans (when the first one appears, how many live), the Shaman away from the army (own
/// army units within the ability's radius, the distance to the army's centroid), and the rule (today's: at least
/// <c>ai.castMinTargets</c> enemies seen within the radius; the alternative Request 53 proposes: own army units attacking
/// within it). Then the casts sent and landed, and the match's outcome. A measurement (no bound): only preconditions are
/// asserted. Finding (2026-11-13, docs/03 "Implementation (M7-7)"): too few Shamans, not the rule.
/// The landing opening (D29's hand-over V24 with D31's <c>reserveArmyShare</c> 0.5 on it) is read from the data track's
/// fixture, read-only. Rule 41: these figures are on this branch's rules; the data track's V24 re-measure on <c>main</c> is
/// a different harness and not comparable.
/// </summary>
[Trait("Category", "Stress")]
public class FrenzyCastReportTests(ITestOutputHelper output)
{
    private const int Tps = SimConstants.TicksPerSecond;
    private const int Five = 6_000, Ten = 12_000, Fifteen = 18_000;

    /// <summary>The "N" columns: own army units (or of them attacking) within the radius, at least this many.</summary>
    internal static readonly int[] Ns = { 1, 2, 3, 4 };

    private static readonly string[] Opponents = { "malazan", "whirlwind" };
    private static readonly ulong[] Seeds = { 1, 2, 3, 4, 5, 6, 7, 8 };

    /// <summary>One match's figures for the Teblor seat (ticks; -1 never).</summary>
    internal sealed class Match
    {
        public string Config = "", Opponent = "";
        public ulong Seed;
        public int Seat, FirstShaman = -1, Shamans5, Shamans10, Shamans15, FirstCast = -1, AgeTwoAt = -1, CircleAt = -1;
        public int ReadyThinks, EnemyRule, AnyFighting, ShamanAttacking, DistanceCount;
        public int[] OwnNear = new int[Ns.Length], FightingNear = new int[Ns.Length];
        public double DistanceSum;
        public List<float> Distances = new();
        public int Sent, Landed, DefeatedAt = -1, OpponentDefeatedAt = -1, Army15;
        public bool Lost => DefeatedAt >= 0;
        public bool Won => OpponentDefeatedAt >= 0;
    }

    /// <summary>The copy with the landing opening (the hand-over, V24) as the Teblor's, its own reserve share 0.5 (D31's pick).</summary>
    internal static GameData LandingData() => AiScenes.DataWith(dir =>
    {
        BloodOilScenes.Edit(dir);
        JsonObject opening = TeblorOpeningHorizonReportTests.HandOver();
        opening["attack"]!["reserveArmyShare"] = 0.5;
        dir.EditJson("factions/teblor/ai.json", root => root["openings"]![0] = JsonNode.Parse(opening.ToJsonString()));
    });

    /// <summary>Plays one match to 15:00 and measures the Teblor seat at every think.</summary>
    internal static Match Play(string config, GameData data, string opponent, ulong seed, bool teblorSecond, int startShamans = 0)
    {
        int teblor = data.FindFaction("teblor");
        PlayerSeat t = new(teblor, SeatController.Ai, AiDifficulty.Normal);
        PlayerSeat o = new(data.FindFaction(opponent), SeatController.Ai, AiDifficulty.Normal);
        PlayerSeat[] seats = teblorSecond ? new[] { o, t } : new[] { t, o };
        int p = teblorSecond ? 1 : 0;
        Simulation sim = AiScenes.Generated(seats, seed, false, out _, data: data);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(p)!;
        Assert.Equal("standard", ai.Opening.Id);
        int shaman = data.FindUnit("teblor_shaman");
        AbilityDef frenzy = data.Abilities[data.Units[shaman].Abilities[0]];
        Assert.Equal("blood_oil_frenzy", frenzy.Key);
        float r2 = frenzy.Radius * frenzy.Radius;
        var m = new Match { Config = config, Opponent = opponent, Seed = seed, Seat = p };

        // The ability manager's own sends this think: the caster was free (it runs last, so no later order hides it).
        var castThisThink = new HashSet<int>();
        ai.SentForTests = c =>
        {
            if (c.Kind != CommandKind.UseAbility) return;
            castThisThink.Add(c.Unit.Index);
            if (m.FirstCast < 0) m.FirstCast = c.Tick;
        };
        ai.ThoughtForTests = a =>
        {
            Measure(a, m, shaman, r2, castThisThink);
            castThisThink.Clear();
        };

        int circle = data.FindBuilding("teblor_shamans_circle");
        var readyWas = new int[w.Units.Capacity];
        var generationWas = new int[w.Units.Capacity];
        while (sim.TickNumber < Fifteen)
        {
            sim.Tick();
            int now = sim.TickNumber;
            if (startShamans > 0 && SpawnShamans(sim, p, shaman, startShamans)) startShamans = 0;
            if (m.AgeTwoAt < 0 && w.Age(p) >= 2) m.AgeTwoAt = now;
            if (m.CircleAt < 0 && now % Tps == 0 && Has(w, p, circle)) m.CircleAt = now;
            if (m.DefeatedAt < 0 && w.IsDefeated(p)) m.DefeatedAt = now;
            if (m.OpponentDefeatedAt < 0 && w.IsDefeated(1 - p)) m.OpponentDefeatedAt = now;
            int alive = 0;
            var us = w.Units;
            for (int s = 0; s < us.Capacity; s++)
            {
                if (!us.Alive[s] || us.Owner[s] != p || us.TypeId[s] != shaman) continue;
                alive++;
                // A landed cast moves the ready tick (0 from the spawn); a reused slot starts the mark again.
                if (us.Generation[s] != generationWas[s]) (generationWas[s], readyWas[s]) = (us.Generation[s], 0);
                int ready = us.AbilityReadyTick[s * DataLimits.MaxUnitAbilities];
                if (ready > readyWas[s]) m.Landed++;
                readyWas[s] = ready;
            }
            if (m.FirstShaman < 0 && alive > 0) m.FirstShaman = now;
            if (now == Five) m.Shamans5 = alive;
            if (now == Ten) m.Shamans10 = alive;
            if (now == Fifteen) m.Shamans15 = alive;
        }
        m.Sent = ai.Board.CastsSent;
        m.Army15 = Army(w, p);
        return m;
    }

    /// <summary>
    /// At the end of a think: the Shamans the ability manager considered (alive; free by its rule, or sent a cast this think;
    /// the Frenzy ready by the tick its command applies in), and for the think the best of them on each figure.
    /// </summary>
    private static void Measure(AiPlayer ai, Match m, int shaman, float r2, HashSet<int> castThisThink)
    {
        bool any = false, enemyRule = false, shamanAttacking = false;
        int ownBest = 0, fightBest = 0;
        float bestDistance = float.MaxValue;
        bool anyFighting = false;
        for (int k = 0; k < ai.ArmyCount; k++)
            if (ai.View.TryGetUnit(ai.ArmySlots[k], out SeenUnit f) && f.State == UnitState.Attacking) anyFighting = true;
        for (int k = 0; k < ai.ArmyCount; k++)
        {
            int slot = ai.ArmySlots[k];
            if (!ai.View.TryGetUnit(slot, out SeenUnit u) || u.TypeId != shaman) continue;
            if (!castThisThink.Contains(slot) && !Free(ai, slot, u)) continue;
            if (!ai.View.TryGetAbilityReady(slot, 0, out int ready) || ready > ai.Stamp) continue;
            any = true;
            if (u.State == UnitState.Attacking) shamanAttacking = true;
            int enemies = 0, s = -1;
            while (ai.View.NextUnit(ref s, out SeenUnit e))
                if (ai.View.IsEnemy(e.Owner) && Vector2.DistanceSquared(e.Position, u.Position) <= r2) enemies++;
            if (enemies >= ai.Rules.CastMinTargets) enemyRule = true;
            int own = 0, fighting = 0, armyN = 0;
            Vector2 sum = Vector2.Zero;
            for (int j = 0; j < ai.ArmyCount; j++)
            {
                int other = ai.ArmySlots[j];
                if (other == slot || !ai.View.TryGetUnit(other, out SeenUnit a)) continue;
                sum += a.Position;
                armyN++;
                if (Vector2.DistanceSquared(a.Position, u.Position) > r2) continue;
                own++;
                if (a.State == UnitState.Attacking) fighting++;
            }
            ownBest = Math.Max(ownBest, own);
            fightBest = Math.Max(fightBest, fighting);
            if (armyN > 0) bestDistance = Math.Min(bestDistance, Vector2.Distance(sum / armyN, u.Position));
        }
        if (!any) return;
        m.ReadyThinks++;
        if (enemyRule) m.EnemyRule++;
        if (anyFighting) m.AnyFighting++;
        if (shamanAttacking) m.ShamanAttacking++;
        for (int i = 0; i < Ns.Length; i++)
        {
            if (ownBest >= Ns[i]) m.OwnNear[i]++;
            if (fightBest >= Ns[i]) m.FightingNear[i]++;
        }
        if (bestDistance < float.MaxValue)
        {
            m.DistanceSum += bestDistance;
            m.DistanceCount++;
            m.Distances.Add(bestDistance);
        }
    }

    /// <summary>The ability manager's "free" (AbilityManager.Casters): not casting or walking to a cast, no command pending, not the scout, not fleeing, not owed its Move home.</summary>
    private static bool Free(AiPlayer ai, int slot, SeenUnit u)
    {
        int generation = u.Handle.Generation;
        if (u.State == UnitState.Casting || ai.View.TryGetCast(slot, out _)) return false;
        if (ai.OrderedNotApplied(slot, generation) || ai.IsScout(slot, generation) || ai.IsFleeing(slot, generation)) return false;
        return !(ai.Board.WaveGeneration[slot] == generation && ai.Board.WaveFlag[slot] == MilitaryManager.ToRally);
    }

    /// <summary>The diagnostic configuration's Shamans: spawned by command beside the Teblor's first worker, so they join its army at the next think.</summary>
    private static bool SpawnShamans(Simulation sim, int p, int shaman, int count)
    {
        var us = sim.World.Units;
        for (int s = 0; s < us.Capacity; s++)
        {
            if (!us.Alive[s] || us.Owner[s] != p) continue;
            for (int k = 0; k < count; k++) sim.Enqueue(Command.SpawnUnit(p, shaman, us.Position[s] + new Vector2(-0.5f, 0.8f + 0.9f * k)));
            return true;
        }
        return false;
    }

    private static bool Has(World w, int p, int type)
    {
        var bs = w.Buildings;
        for (int s = 0; s < bs.Capacity; s++)
            if (bs.Alive[s] && bs.Owner[s] == p && bs.TypeId[s] == type && !bs.UnderConstruction[s]) return true;
        return false;
    }

    private static int Army(World w, int player)
    {
        var u = w.Units;
        int half = 0;
        for (int s = 0; s < u.Capacity; s++)
            if (u.Alive[s] && u.Owner[s] == player && w.Data.Units[u.TypeId[s]].Slot != UnitSlot.Worker) half += w.Data.Units[u.TypeId[s]].HalfPop;
        return half;
    }

    /// <summary>The 32 matches of <paramref name="configs"/> each, in parallel (each match is its own sim).</summary>
    internal static List<Match> PlayAll(IEnumerable<(string Name, GameData Data, int StartShamans)> configs)
    {
        var jobs = new List<(string, GameData, string, ulong, bool, int)>();
        foreach (var c in configs)
            foreach (ulong seed in Seeds)
                foreach (string o in Opponents)
                    foreach (bool second in new[] { false, true }) jobs.Add((c.Name, c.Data, o, seed, second, c.StartShamans));
        var rows = new Match[jobs.Count];
        var errors = new ConcurrentQueue<string>();
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, i =>
        {
            var j = jobs[i];
            try { rows[i] = Play(j.Item1, j.Item2, j.Item3, j.Item4, j.Item5, j.Item6); }
            catch (Exception e) { errors.Enqueue($"{j.Item1} seed {j.Item4} v {j.Item3} second {j.Item5}: {e}"); }
        });
        Assert.True(errors.IsEmpty, string.Join("\n", errors));
        return rows.ToList();
    }

    private static string T(int tick) => tick < 0 ? "never" : $"{tick / Tps / 60}:{tick / Tps % 60:00}";

    private static string Median(IEnumerable<float> values)
    {
        float[] v = values.OrderBy(x => x).ToArray();
        return v.Length == 0 ? "-" : $"{v[v.Length / 2]:0.0}";
    }

    private static string Own(Match m) => string.Join(" / ", m.OwnNear);

    private static string Fight(Match m) => string.Join(" / ", m.FightingNear);

    private static string Sum(IEnumerable<Match> g, Func<Match, int[]> pick) =>
        string.Join(" / ", Enumerable.Range(0, Ns.Length).Select(i => g.Sum(m => pick(m)[i])));

    /// <summary>Per match, then summed, then the outcome row per configuration.</summary>
    internal void Print(List<Match> all, string[] order)
    {
        string n = string.Join("/", Ns.Select(x => $">={x}"));
        output.WriteLine($"Ready thinks: Teblor thinks with a free Shaman whose Frenzy is ready. Of them: enemies = at least ai.castMinTargets seen enemies within the radius (today's rule); "
            + $"own {n} = own army units (not the caster, not workers) within the radius; fighting {n} = of them attacking; any fight = an own army unit attacking anywhere; "
            + "shaman attacking = the Shaman itself attacking; distance = the Shaman to its army's centroid (m; mean / median).");
        foreach (string c in order)
        {
            output.WriteLine("");
            output.WriteLine($"Configuration: {c}");
            output.WriteLine($"| seed | opp | seat | Age II | Shamans' Circle | first Shaman | Shamans 5 / 10 / 15 | ready thinks | enemies | own {n} | fighting {n} | any fight | shaman attacking | distance | casts sent | landed | first cast | defeated | won |");
            output.WriteLine("| --- " + string.Concat(Enumerable.Repeat("| --- ", 18)) + "|");
            foreach (Match m in all.Where(x => x.Config == c).OrderBy(x => x.Seed).ThenBy(x => x.Opponent).ThenBy(x => x.Seat))
                output.WriteLine($"| {m.Seed} | {m.Opponent} | {m.Seat} | {T(m.AgeTwoAt)} | {T(m.CircleAt)} | {T(m.FirstShaman)} | {m.Shamans5} / {m.Shamans10} / {m.Shamans15} | {m.ReadyThinks} | {m.EnemyRule} | "
                    + $"{Own(m)} | {Fight(m)} | {m.AnyFighting} | {m.ShamanAttacking} | {(m.DistanceCount == 0 ? "-" : $"{m.DistanceSum / m.DistanceCount:0.0} / {Median(m.Distances)}")} | "
                    + $"{m.Sent} | {m.Landed} | {T(m.FirstCast)} | {T(m.DefeatedAt)} | {(m.Won ? T(m.OpponentDefeatedAt) : "-")} |");
        }
        output.WriteLine("");
        output.WriteLine("Summed per configuration (32 matches each):");
        output.WriteLine($"| configuration | Age II by 15:00 | Shamans' Circle by 15:00 | matches with a Shaman | first Shaman (median) | Shamans alive 5 / 10 / 15 (sum) | ready thinks | enemies | own {n} | fighting {n} | any fight | shaman attacking | distance mean / median | casts sent | landed | casts / match | defeated by 15:00 | army pop 15:00 (sum) | wins |");
        output.WriteLine("| --- " + string.Concat(Enumerable.Repeat("| --- ", 18)) + "|");
        foreach (string c in order)
        {
            List<Match> g = all.Where(x => x.Config == c).ToList();
            int[] firsts = g.Where(x => x.FirstShaman >= 0).Select(x => x.FirstShaman).OrderBy(x => x).ToArray();
            int dc = g.Sum(x => x.DistanceCount);
            output.WriteLine($"| {c} | {g.Count(x => x.AgeTwoAt >= 0)} | {g.Count(x => x.CircleAt >= 0)} | {firsts.Length} of {g.Count} | {(firsts.Length == 0 ? "never" : T(firsts[firsts.Length / 2]))} | "
                + $"{g.Sum(x => x.Shamans5)} / {g.Sum(x => x.Shamans10)} / {g.Sum(x => x.Shamans15)} | {g.Sum(x => x.ReadyThinks)} | {g.Sum(x => x.EnemyRule)} | "
                + $"{Sum(g, x => x.OwnNear)} | {Sum(g, x => x.FightingNear)} | {g.Sum(x => x.AnyFighting)} | {g.Sum(x => x.ShamanAttacking)} | "
                + $"{(dc == 0 ? "-" : $"{g.Sum(x => x.DistanceSum) / dc:0.0} / {Median(g.SelectMany(x => x.Distances))}")} | {g.Sum(x => x.Sent)} | {g.Sum(x => x.Landed)} | "
                + $"{g.Average(x => x.Landed):0.00} | {g.Count(x => x.Lost)} | {g.Sum(x => x.Army15) / 2.0} | {g.Count(x => x.Won)} |");
        }
        output.WriteLine("");
    }

    /// <summary>Shamans the diagnostic configuration spawns for the Teblor at 0:00 (not a design number: enough to see the rule at work all match).</summary>
    internal const int DiagnosticShamans = 2;

    /// <summary>
    /// Part A: the copy with the shipped opening, and with the landing opening (64 matches); then a diagnostic, the shipped
    /// opening with <see cref="DiagnosticShamans"/> Shamans spawned at 0:00 (32 more), to see what today's rule does when
    /// Shamans exist (the brief's first two rows are the measurement; the third only says whether the rule would be the next
    /// cause once the data track fields Shamans).
    /// </summary>
    [Fact]
    public void FrenzyCasts_TodaysRule_ShippedAndLandingOpening_Report()
    {
        var configs = new (string, GameData, int)[]
        {
            ("shipped opening", BloodOilScenes.Data, 0), ("landing opening", LandingData(), 0), ($"diagnostic: {DiagnosticShamans} Shamans at 0:00", BloodOilScenes.Data, DiagnosticShamans),
        };
        Assert.True(configs[0].Item2.FindStatus(BloodOilScenes.Status) >= 0);
        Assert.Equal(0.5f, configs[1].Item2.Ai[configs[1].Item2.FindFaction("teblor")].Openings[0].ReserveArmyShare);
        Assert.Equal(2, configs[0].Item2.AiRules.CastMinTargets);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        List<Match> rows = PlayAll(configs);
        output.WriteLine($"M7-7 Part A: {rows.Count} matches to 15:00 in {clock.Elapsed.TotalSeconds:0} s (generated map, seeds 1-8, Normal v Normal, both seat orders)");
        output.WriteLine("Rule 41: measured on this branch (the sim's rules, the BloodOilScenes copy); the data track's V24 re-measure on main is another harness and not comparable.");
        Print(rows, configs.Select(c => c.Item1).ToArray());
        Assert.Equal(96, rows.Count);
        Assert.All(rows.Where(r => r.Config.StartsWith("diagnostic", StringComparison.Ordinal)), r => Assert.InRange(r.FirstShaman, 0, Tps));
    }
}
