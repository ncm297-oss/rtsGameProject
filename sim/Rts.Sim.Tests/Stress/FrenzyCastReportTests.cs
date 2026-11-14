using System.Collections.Concurrent;
using System.Numerics;
using System.Text.Json;
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
/// M7-H2 (BUG-1340): "the army" is own army units not of the caster's type (the other Shaman is not the army the Frenzy is
/// for), and the think figures are split by phase: before and after the seat's first non-Shaman army unit. The landing
/// opening (D29's hand-over V24) is read from the data track's fixture, read-only, with the shipped <c>standard</c>
/// opening's own <c>reserveArmyShare</c> once it carries one (D31's 0.25), else D31's provisional 0.5. One more opening can
/// be measured from a file named by <see cref="OpeningVariable"/> (the data track's read-only run). Rule 41: these figures
/// are on this branch's rules; the data track's V24 re-measure on <c>main</c> is a different harness and not comparable.
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

    /// <summary>
    /// M7-H2: the environment variable naming one more opening to measure: a file holding one JSON object of an
    /// <c>ai.json</c> <c>openings[]</c> entry (absolute, or relative to <c>sim/Rts.Sim.Tests</c> or the repo root). It is
    /// put in as the Teblor's <c>standard</c> on the same copy and printed with the same columns. Unset: the three
    /// configurations only.
    /// </summary>
    internal const string OpeningVariable = "FRENZY_REPORT_OPENING";

    /// <summary>D31's provisional reserve share for the landing opening, used while the shipped <c>standard</c> carries none.</summary>
    internal const float FallbackLandingShare = 0.5f;

    /// <summary>One phase's think figures: before (0) or after (1) the seat's first non-Shaman army unit.</summary>
    internal sealed class Phase
    {
        public int ReadyThinks, NoArmy, EnemyRule, AnyFighting, ShamanAttacking;
        public int[] OwnNear = new int[Ns.Length], FightingNear = new int[Ns.Length];
        public List<float> Distances = new();
    }

    /// <summary>One match's figures for the Teblor seat (ticks; -1 never).</summary>
    internal sealed class Match
    {
        public string Config = "", Opponent = "";
        public ulong Seed;
        public int Seat, FirstShaman = -1, FirstArmy = -1, Shamans5, Shamans10, Shamans15, FirstCast = -1, AgeTwoAt = -1, CircleAt = -1;
        public Phase[] Phases = { new(), new() };
        public int Sent, Landed, DefeatedAt = -1, OpponentDefeatedAt = -1, Army15;
        public bool Lost => DefeatedAt >= 0;
        public bool Won => OpponentDefeatedAt >= 0;
        public int ReadyThinks => Phases[0].ReadyThinks + Phases[1].ReadyThinks;
    }

    /// <summary>
    /// The landing opening's reserve share: the shipped Teblor <c>standard</c> opening's own once it carries one (the landing
    /// ships V24 with D31's 0.25), else <see cref="FallbackLandingShare"/>. Looked up, so the column does not depend on a
    /// literal share or on the shipped opening being the pre-landing one.
    /// </summary>
    internal static float LandingShare()
    {
        GameData d = TestSim.Data;
        foreach (AiOpening o in d.Ai[d.FindFaction("teblor")].Openings)
            if (o.Id == "standard") return o.ReserveArmyShare ?? FallbackLandingShare;
        throw new InvalidOperationException("the shipped Teblor ai.json has no 'standard' opening");
    }

    /// <summary>The copy with the landing opening (the hand-over, V24) as the Teblor's <c>standard</c>, at <see cref="LandingShare"/>.</summary>
    internal static GameData LandingData()
    {
        JsonObject opening = TeblorOpeningHorizonReportTests.HandOver();
        opening["attack"]!["reserveArmyShare"] = (double)LandingShare();
        return AiScenes.DataWith(dir =>
        {
            BloodOilScenes.Edit(dir);
            dir.EditJson("factions/teblor/ai.json", root => PutInAsStandard(root, opening));
        });
    }

    /// <summary>Replaces the <c>standard</c> entry of a Teblor <c>ai.json</c> root (found by id, not position) with a copy of <paramref name="opening"/>, its id set to <c>standard</c>.</summary>
    internal static void PutInAsStandard(JsonNode root, JsonObject opening)
    {
        JsonArray openings = root["openings"]!.AsArray();
        for (int i = 0; i < openings.Count; i++)
        {
            if ((string?)openings[i]!["id"] != "standard") continue;
            JsonObject copy = JsonNode.Parse(opening.ToJsonString())!.AsObject();
            copy["id"] = "standard";
            openings[i] = copy;
            return;
        }
        throw new InvalidOperationException("the Teblor ai.json has no 'standard' opening to replace");
    }

    /// <summary>
    /// M7-H2: the configuration <see cref="OpeningVariable"/> names, or null when <paramref name="path"/> is unset or empty.
    /// A missing file, a file that is not one JSON object, and an opening the loader refuses each throw, naming the path.
    /// </summary>
    internal static (string Name, GameData Data)? FileOpening(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string full = ResolveOpeningPath(path);
        JsonObject opening;
        try
        {
            opening = JsonNode.Parse(File.ReadAllText(full)) as JsonObject
                ?? throw new InvalidOperationException($"{OpeningVariable}: '{path}' ({full}) is not one JSON object (an ai.json openings[] entry)");
        }
        catch (JsonException e)
        {
            throw new InvalidOperationException($"{OpeningVariable}: '{path}' ({full}) is not valid JSON: {e.Message}", e);
        }
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        BloodOilScenes.Edit(dir);
        dir.EditJson("factions/teblor/ai.json", root => PutInAsStandard(root, opening));
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        if (r.Data == null)
            throw new InvalidOperationException($"{OpeningVariable}: the opening in '{path}' ({full}) does not load as the Teblor's standard: {string.Join("; ", r.Errors)}");
        return ($"opening from {path}", r.Data);
    }

    private static string ResolveOpeningPath(string path)
    {
        if (Path.IsPathRooted(path))
            return File.Exists(path) ? path : throw new InvalidOperationException($"{OpeningVariable}: no file '{path}'");
        string root = TestDataDir.RepoRoot();
        string[] tried = { Path.Combine(root, "sim", "Rts.Sim.Tests", path), Path.Combine(root, path) };
        foreach (string candidate in tried)
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        throw new InvalidOperationException($"{OpeningVariable}: no file '{path}' (looked at {string.Join(" and ", tried)})");
    }

    /// <summary>The Teblor <c>standard</c> opening of <paramref name="data"/>.</summary>
    internal static AiOpening Standard(GameData data)
    {
        foreach (AiOpening o in data.Ai[data.FindFaction("teblor")].Openings)
            if (o.Id == "standard") return o;
        throw new InvalidOperationException("no Teblor 'standard' opening");
    }

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
        AbilityDef frenzy = data.Abilities[data.FindAbility(BloodOilScenes.FrenzyAbility)];
        Assert.Contains(data.Units[shaman].Abilities, a => a == frenzy.Id);
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
    /// the Frenzy ready by the tick its command applies in), and for the think the best of them on each figure. BUG-1340:
    /// the army is own army units not of the caster's type, so a second Shaman beside the first is not "the army"; the
    /// think counts in the phase it falls in (after the seat's first non-Shaman army unit, or before).
    /// </summary>
    private static void Measure(AiPlayer ai, Match m, int shaman, float r2, HashSet<int> castThisThink)
    {
        int armyN = 0;
        Vector2 sum = Vector2.Zero;
        bool anyFighting = false;
        for (int k = 0; k < ai.ArmyCount; k++)
        {
            if (!ai.View.TryGetUnit(ai.ArmySlots[k], out SeenUnit a) || a.TypeId == shaman) continue;
            armyN++;
            sum += a.Position;
            if (a.State == UnitState.Attacking) anyFighting = true;
        }
        if (armyN > 0 && m.FirstArmy < 0) m.FirstArmy = ai.Now;
        Vector2 centroid = armyN > 0 ? sum / armyN : Vector2.Zero;

        bool any = false, enemyRule = false, shamanAttacking = false;
        int ownBest = 0, fightBest = 0;
        float bestDistance = float.MaxValue;
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
            int own = 0, fighting = 0;
            for (int j = 0; j < ai.ArmyCount; j++)
            {
                if (!ai.View.TryGetUnit(ai.ArmySlots[j], out SeenUnit a) || a.TypeId == shaman) continue;
                if (Vector2.DistanceSquared(a.Position, u.Position) > r2) continue;
                own++;
                if (a.State == UnitState.Attacking) fighting++;
            }
            ownBest = Math.Max(ownBest, own);
            fightBest = Math.Max(fightBest, fighting);
            if (armyN > 0) bestDistance = Math.Min(bestDistance, Vector2.Distance(centroid, u.Position));
        }
        if (!any) return;
        Phase ph = m.Phases[m.FirstArmy >= 0 ? 1 : 0];
        ph.ReadyThinks++;
        if (armyN == 0) ph.NoArmy++;
        if (enemyRule) ph.EnemyRule++;
        if (anyFighting) ph.AnyFighting++;
        if (shamanAttacking) ph.ShamanAttacking++;
        for (int i = 0; i < Ns.Length; i++)
        {
            if (ownBest >= Ns[i]) ph.OwnNear[i]++;
            if (fightBest >= Ns[i]) ph.FightingNear[i]++;
        }
        if (bestDistance < float.MaxValue) ph.Distances.Add(bestDistance);
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

    private static string MeanMedian(IEnumerable<float> values)
    {
        float[] v = values.ToArray();
        return v.Length == 0 ? "-" : $"{v.Average():0.0} / {Median(v)}";
    }

    // A figure of one phase (0 or 1) or of both (-1), summed over the matches.
    private static IEnumerable<Phase> Of(IEnumerable<Match> g, int phase) =>
        g.SelectMany(m => phase < 0 ? m.Phases : new[] { m.Phases[phase] });

    private static string Sum(IEnumerable<Phase> ps, Func<Phase, int[]> pick) =>
        string.Join(" / ", Enumerable.Range(0, Ns.Length).Select(i => ps.Sum(p => pick(p)[i])));

    // The think columns: ready thinks, no army, enemies, own, fighting, any fight, shaman attacking, distance.
    private static string ThinkColumns(IEnumerable<Phase> phases)
    {
        List<Phase> ps = phases.ToList();
        return $"{ps.Sum(x => x.ReadyThinks)} | {ps.Sum(x => x.NoArmy)} | {ps.Sum(x => x.EnemyRule)} | {Sum(ps, x => x.OwnNear)} | {Sum(ps, x => x.FightingNear)} | "
            + $"{ps.Sum(x => x.AnyFighting)} | {ps.Sum(x => x.ShamanAttacking)} | {MeanMedian(ps.SelectMany(x => x.Distances))}";
    }

    /// <summary>Per match, then summed, then by phase, per configuration.</summary>
    internal void Print(List<Match> all, string[] order)
    {
        string n = string.Join("/", Ns.Select(x => $">={x}"));
        string thinkHeader = $"ready thinks | no army | enemies | own {n} | fighting {n} | any fight | shaman attacking | distance mean / median";
        output.WriteLine($"Ready thinks: Teblor thinks with a free Shaman (AbilityManager.Casters' own test) whose Frenzy is ready. The army (BUG-1340): own army units that are "
            + "not workers and not Shamans. Of the ready thinks: no army = the seat has no such unit; enemies = at least ai.castMinTargets enemies seen within the radius (today's rule); "
            + $"own {n} = army units within the radius; fighting {n} = of them attacking; any fight = an army unit attacking anywhere; "
            + "shaman attacking = the Shaman itself attacking; distance = the Shaman to the army's centroid (m; thinks with an army).");
        output.WriteLine("QA's re-measure (Stress/FrenzyCastReportQaStressTests) counts every ready, non-casting Shaman (not only free ones) and enemies from the world (not the "
            + "fogged view), and splits at 5:00 rather than at the first army unit, so its ready thinks and enemy column are not these; the army columns count the same units.");
        foreach (string c in order)
        {
            output.WriteLine("");
            output.WriteLine($"Configuration: {c}");
            output.WriteLine($"| seed | opp | seat | Age II | Shamans' Circle | first Shaman | first army unit | Shamans 5 / 10 / 15 | {thinkHeader} | casts sent | landed | first cast | defeated | won |");
            output.WriteLine("| --- " + string.Concat(Enumerable.Repeat("| --- ", 20)) + "|");
            foreach (Match m in all.Where(x => x.Config == c).OrderBy(x => x.Seed).ThenBy(x => x.Opponent).ThenBy(x => x.Seat))
                output.WriteLine($"| {m.Seed} | {m.Opponent} | {m.Seat} | {T(m.AgeTwoAt)} | {T(m.CircleAt)} | {T(m.FirstShaman)} | {T(m.FirstArmy)} | {m.Shamans5} / {m.Shamans10} / {m.Shamans15} | "
                    + $"{ThinkColumns(m.Phases)} | {m.Sent} | {m.Landed} | {T(m.FirstCast)} | {T(m.DefeatedAt)} | {(m.Won ? T(m.OpponentDefeatedAt) : "-")} |");
        }
        output.WriteLine("");
        output.WriteLine("Summed per configuration (32 matches each):");
        output.WriteLine($"| configuration | Age II by 15:00 | Shamans' Circle by 15:00 | matches with a Shaman | first Shaman (each) | Shamans alive 5 / 10 / 15 (sum) | first army unit (median; matches with one) | {thinkHeader} | casts sent | landed | casts / match | defeated by 15:00 | army pop 15:00 (sum) | wins |");
        output.WriteLine("| --- " + string.Concat(Enumerable.Repeat("| --- ", 20)) + "|");
        foreach (string c in order)
        {
            List<Match> g = all.Where(x => x.Config == c).ToList();
            int[] firsts = g.Where(x => x.FirstShaman >= 0).Select(x => x.FirstShaman).OrderBy(x => x).ToArray();
            string firstList = firsts.Length == 0 ? "never" : firsts.Length > 6 ? $"median {T(firsts[firsts.Length / 2])}" : string.Join(", ", firsts.Select(T));
            int[] armies = g.Where(x => x.FirstArmy >= 0).Select(x => x.FirstArmy).OrderBy(x => x).ToArray();
            string firstArmy = armies.Length == 0 ? "never" : $"{T(armies[armies.Length / 2])} ({armies.Length})";
            output.WriteLine($"| {c} | {g.Count(x => x.AgeTwoAt >= 0)} | {g.Count(x => x.CircleAt >= 0)} | {firsts.Length} of {g.Count} | {firstList} | "
                + $"{g.Sum(x => x.Shamans5)} / {g.Sum(x => x.Shamans10)} / {g.Sum(x => x.Shamans15)} | {firstArmy} | {ThinkColumns(Of(g, -1))} | {g.Sum(x => x.Sent)} | {g.Sum(x => x.Landed)} | "
                + $"{g.Average(x => x.Landed):0.00} | {g.Count(x => x.Lost)} | {g.Sum(x => x.Army15) / 2.0} | {g.Count(x => x.Won)} |");
        }
        output.WriteLine("");
        output.WriteLine("By phase (BUG-1340): the ready thinks before the seat's first non-Shaman army unit, and after it:");
        output.WriteLine($"| configuration | phase | {thinkHeader} |");
        output.WriteLine("| --- " + string.Concat(Enumerable.Repeat("| --- ", 9)) + "|");
        foreach (string c in order)
        {
            List<Match> g = all.Where(x => x.Config == c).ToList();
            output.WriteLine($"| {c} | before the first army unit | {ThinkColumns(Of(g, 0))} |");
            output.WriteLine($"| {c} | after it | {ThinkColumns(Of(g, 1))} |");
        }
        output.WriteLine("");
    }

    /// <summary>Shamans the diagnostic configuration spawns for the Teblor at 0:00 (not a design number: enough to see the rule at work all match).</summary>
    internal const int DiagnosticShamans = 2;

    /// <summary>
    /// Part A: the copy with the shipped opening, and with the landing opening (64 matches); then a diagnostic, the shipped
    /// opening with <see cref="DiagnosticShamans"/> Shamans spawned at 0:00 (32 more), to see what today's rule does when
    /// Shamans exist (the brief's first two rows are the measurement; the third only says whether the rule would be the next
    /// cause once the data track fields Shamans). M7-H2: and the opening <see cref="OpeningVariable"/> names, when set.
    /// </summary>
    [Fact]
    public void FrenzyCasts_TodaysRule_ShippedAndLandingOpening_Report()
    {
        float share = LandingShare();
        var configs = new List<(string Name, GameData Data, int StartShamans)>
        {
            ("shipped opening", BloodOilScenes.Data, 0), ($"landing opening (share {share})", LandingData(), 0),
            ($"diagnostic: {DiagnosticShamans} Shamans at 0:00", BloodOilScenes.Data, DiagnosticShamans),
        };
        string? path = Environment.GetEnvironmentVariable(OpeningVariable);
        if (FileOpening(path) is { } file) configs.Add((file.Name, file.Data, 0));
        Assert.True(configs[0].Data.FindStatus(BloodOilScenes.Status) >= 0);
        Assert.Equal(share, Standard(configs[1].Data).ReserveArmyShare);
        Assert.Equal(2, configs[0].Data.AiRules.CastMinTargets);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        List<Match> rows = PlayAll(configs);
        output.WriteLine($"M7-7 Part A: {rows.Count} matches to 15:00 in {clock.Elapsed.TotalSeconds:0} s (generated map, seeds 1-8, Normal v Normal, both seat orders)");
        output.WriteLine($"{OpeningVariable}: {(string.IsNullOrWhiteSpace(path) ? "unset" : path)}");
        output.WriteLine("Rule 41: measured on this branch (the sim's rules, the BloodOilScenes copy); the data track's V24 re-measure on main is another harness and not comparable.");
        Print(rows, configs.Select(c => c.Name).ToArray());
        Assert.Equal(32 * configs.Count, rows.Count);
        Assert.All(rows.Where(r => r.Config.StartsWith("diagnostic", StringComparison.Ordinal)), r => Assert.InRange(r.FirstShaman, 0, Tps));
    }
}
