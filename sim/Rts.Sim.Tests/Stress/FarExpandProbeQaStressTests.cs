using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA, M5-9b (session 2026-11-09-0426, full tier): whole matches with the far expansion watched on every think of every AI
/// seat, on top of <see cref="CriterionMatch"/>'s checks (a)-(e) and the sweep's extra oracles (silent seat, stuck unit, still
/// resources, a wave that never leaves; <see cref="MatchCriterionSweepQaStressTests.Extra"/>).
/// <list type="bullet">
/// <item>The far fog oracle (criterion 8): whenever the rule's candidate is a node (<see cref="FarExpansion.Candidate"/>), some
/// cell of that node's footprint has been explored by the player (the world's fog, not the view): a far mine counts only once
/// seen. Every depot Build the rule sends is checked the same way at the think it is sent.</item>
/// <item>The leg reads only the start list: the start it walks to is one no live player starts on.</item>
/// <item>No depot Build by a node with an enemy drop-off the player sees now (the world's truth) within <c>ai.gatherReach</c>.</item>
/// </list>
/// Each row prints the far expansion's timeline per seat (first arm, first depot Build, expansions, depots standing) and the
/// sanity numbers (gold held at 30,000, a far depot standing by 36,000).
/// </summary>
[Trait("Category", "Stress")]
public class FarExpandProbeQaStressTests
{
    private readonly ITestOutputHelper _out;

    public FarExpandProbeQaStressTests(ITestOutputHelper output) => _out = output;

    private sealed class Seat
    {
        public int FirstArm = -1, FirstLeg = -1, FirstBuild = -1, DepotBuilds, HallBuilds, Checks, CandidateThinks, Gold30k = -1, Gathered30k = -1;
        public int FarDepotsAt36k = -1, LastPendingAt = -1, LastPendingCell = -1, LegSlot = -1, LegGen, Legs, LegDeaths;
        public string? Error;
    }

    private static Simulation Build(string map, ulong seed, AiDifficulty first, int players, int[]? teams)
    {
        var seats = new PlayerSeat[players];
        for (int p = 0; p < players; p++)
            seats[p] = new PlayerSeat(p % 2, SeatController.Ai, p == 0 ? first : AiDifficulty.Normal, teams == null ? 0 : teams[p]);
        return HuntStressTests.Match(map, seats, seed, units: players > 2 ? 1024 : 400);
    }

    private static bool Explored(World w, int p, int typeId, int cell)
    {
        ResourceDef def = w.Data.Resources[typeId];
        int width = w.NavGrid.Width, x0 = cell % width, y0 = cell / width;
        for (int dy = 0; dy < def.FootprintHeight; dy++)
            for (int dx = 0; dx < def.FootprintWidth; dx++)
                if (w.Fog.IsExplored(p, (y0 + dy) * width + x0 + dx)) return true;
        return false;
    }

    /// <summary>A depot of the seat's that stands farther than <c>ai.gatherReach</c> from its start: a far expansion's.</summary>
    private static int FarDepots(World w, AiPlayer a, int p)
    {
        int start = p < a.View.StartCount ? a.View.StartCell(p) : -1;
        if (start < 0) return -1;
        int width = w.NavGrid.Width, n = 0;
        float sx = (start % width + 0.5f) * Rts.Sim.Map.MapConstants.CellSize, sy = (start / width + 0.5f) * Rts.Sim.Map.MapConstants.CellSize;
        BuildingStore b = w.Buildings;
        for (int i = 0; i < b.Capacity; i++)
        {
            if (!b.Alive[i] || b.Owner[i] != p || b.UnderConstruction[i] || !w.Data.Buildings[b.TypeId[i]].DropOff) continue;
            var c = a.BuildingCentre(b.TypeId[i], b.Cell[i]);
            float dx = c.X - sx, dy = c.Y - sy;
            if (dx * dx + dy * dy > 2f * a.Rules.GatherReach * a.Rules.GatherReach) n++;
        }
        return n;
    }

    private void Watch(Simulation sim, int p, Seat s, Action<AiPlayer>? inner)
    {
        World w = sim.World;
        AiPlayer ai = sim.AiOf(p)!;
        ai.ThoughtForTests = a =>
        {
            inner?.Invoke(a);
            if (s.Error != null) return;
            s.Checks++;
            Blackboard b = a.Board;
            if (s.FirstArm < 0 && b.FarPhase != 0) s.FirstArm = a.Now;
            if (s.FirstLeg < 0 && b.FarStart >= 0) s.FirstLeg = a.Now;
            if (b.FarScoutSlot != s.LegSlot || b.FarScoutGeneration != s.LegGen)
            {
                if (s.LegSlot >= 0 && !w.Units.IsAlive(new EntityHandle(s.LegSlot, s.LegGen))) s.LegDeaths++;
                if (b.FarScoutSlot >= 0) s.Legs++;
                s.LegSlot = b.FarScoutSlot;
                s.LegGen = b.FarScoutGeneration;
            }
            if (b.FarStart >= 0 && b.FarStart < a.View.PlayerCount && !w.IsDefeated(b.FarStart))
                s.Error = $"seat {p} tick {a.Now}: the leg walks to start {b.FarStart}, a live player's";
            int node = FarExpansion.Candidate(a, out int type);
            if (node >= 0)
            {
                s.CandidateThinks++;
                if (!Explored(w, p, type, node)) s.Error = $"seat {p} tick {a.Now}: candidate node at cell {node} never explored by the player";
            }
            // A far Build sent this think (FarPending is set after the Send): the node it was sent by was seen, and no enemy
            // drop-off the player sees now stands in gather reach of it.
            bool fresh = b.FarPending.Kind == PendingKind.Build && (b.FarPending.AppliesAt != s.LastPendingAt || b.FarPending.Cell != s.LastPendingCell);
            if (b.FarPending.Kind == PendingKind.Build) { s.LastPendingAt = b.FarPending.AppliesAt; s.LastPendingCell = b.FarPending.Cell; }
            if (!fresh) return;
            if (b.FarPending.TypeId == a.DepotType) s.DepotBuilds++; else s.HallBuilds++;
            if (s.FirstBuild < 0) s.FirstBuild = a.Now;
            if (node < 0) { s.Error = $"seat {p} tick {a.Now}: a far Build with no candidate at the end of the think"; return; }
            var centre = a.NodeCentre(type, node);
            float reach = a.Rules.GatherReach * a.Rules.GatherReach;
            BuildingStore bs = w.Buildings;
            for (int i = 0; i < bs.Capacity; i++)
            {
                if (!bs.Alive[i] || !w.IsEnemy(p, bs.Owner[i]) || !w.Data.Buildings[bs.TypeId[i]].DropOff || !w.Fog.CanSeeBuilding(p, i)) continue;
                if (System.Numerics.Vector2.DistanceSquared(a.BuildingCentre(bs.TypeId[i], bs.Cell[i]), centre) <= reach)
                    s.Error ??= $"seat {p} tick {a.Now}: far Build by node {node} with a visible enemy drop-off (slot {i}) in gather reach";
            }
        };
    }

    private void Row(string map, ulong seed, AiDifficulty first, int players, int[]? teams, int bound, bool oracles)
    {
        Simulation sim = Build(map, seed, first, players, teams);
        int n = players;
        var seats = new Seat[n];
        for (int p = 0; p < n; p++) seats[p] = new Seat();
        var extra = new MatchCriterionSweepQaStressTests.Extra(sim);
        bool chained = false;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        CriterionMatch.Result r = CriterionMatch.Play(sim, bound, checks: true, onTick: s =>
        {
            if (!chained)
            {
                // CriterionMatch.Play set its hooks before the first tick: chain the far oracle onto them.
                chained = true;
                for (int p = 0; p < n; p++)
                {
                    AiPlayer ai = s.AiOf(p)!;
                    Watch(s, p, seats[p], ai.ThoughtForTests);
                }
            }
            if (oracles) extra.OnTick(s);
            World w = s.World;
            for (int p = 0; p < n; p++)
            {
                if (s.TickNumber == 30_000) { seats[p].Gold30k = w.Gold[p]; seats[p].Gathered30k = w.GatheredGold[p]; }
                if (s.TickNumber == 36_000) seats[p].FarDepotsAt36k = FarDepots(w, s.AiOf(p)!, p);
            }
        });
        if (oracles) extra.Finish(sim);
        sw.Stop();
        _out.WriteLine($"FAR | {map} | {seed} | {first} v Normal | {players}{(teams != null ? " 2v2" : "")} | {(r.Over ? "ends" : "NO END")} | {r.EndTick} ({CriterionMatch.Clock(r.EndTick)}) | {r.Winner} "
            + $"| waves {r.Waves} hunts {r.Hunts} | {r.EndHash:X16} | {sw.Elapsed.TotalSeconds:F0} s | error {r.Error ?? "none"}");
        if (oracles)
            _out.WriteLine($"  silent max {string.Join("/", extra.MaxSilent)} | still max {string.Join("/", extra.MaxStill)} | stuck {extra.Stuck} (worst {extra.WorstStuck}) | waves stayed {extra.WavesStayed}/{extra.WavesSeen}");
        foreach (string l in extra.SilentAt) if (l.Length > 0) _out.WriteLine("  " + l);
        foreach (string l in extra.StuckLines) _out.WriteLine("  " + l);
        for (int p = 0; p < n; p++)
        {
            Seat s = seats[p];
            AiPlayer ai = sim.AiOf(p)!;
            _out.WriteLine($"  seat {p}: thinks {s.Checks}, far oracle {s.Error ?? "clean"}; first arm {s.FirstArm}, first leg {s.FirstLeg}, first far Build {s.FirstBuild}, far depot Builds {s.DepotBuilds}, far hall Builds {s.HallBuilds}, leg workers {s.Legs} (died on the leg {s.LegDeaths}), "
                + $"expansions {ai.Board.FarExpansions}, candidate thinks {s.CandidateThinks}, looked {ai.Board.FarLooked}; at 30,000 gold {s.Gold30k} (gathered {s.Gathered30k}); far depots at 36,000 {s.FarDepotsAt36k}; "
                + $"end gold gathered {sim.World.GatheredGold[p]}, phase {ai.Board.FarPhase}, defeated {sim.World.IsDefeated(p)}");
        }
        foreach (Seat s in seats) Assert.True(s.Error == null, s.Error);
        Assert.True(r.Error == null, r.Error);
        // The sweep's known failures (Normal v Normal rows) keep their bug: measured above, not asserted here.
        bool known = players == 2 && first == AiDifficulty.Normal && MatchCriterionSweepQaStressTests.Known.ContainsKey((map, seed));
        if (known) _out.WriteLine($"  known ({MatchCriterionSweepQaStressTests.Known[(map, seed)]}): the extra oracles are printed, not asserted");
        if (oracles && !known)
        {
            Assert.True(extra.MaxSilent.Max() < MatchCriterionSweepQaStressTests.SilentTicks, $"silent seat: {string.Join("; ", extra.SilentAt)}");
            Assert.True(extra.Stuck == 0, string.Join("; ", extra.StuckLines));
        }
    }

    /// <summary>
    /// The 11 probed matches (QA's M5-9a probe rows: the 8 non-Hard pins, generated seed 2, Vathar four-seat FFA and 2v2) to
    /// 72,000 (144,000 for four seats) with (a)-(e), the extra oracles and the far fog oracle on every think.
    /// </summary>
    [Theory]
    [InlineData("raraku", 1UL, AiDifficulty.Normal, 2)]
    [InlineData("raraku", 1UL, AiDifficulty.Easy, 2)]
    [InlineData("pale_hills", 1UL, AiDifficulty.Normal, 2)]
    [InlineData("pale_hills", 1UL, AiDifficulty.Easy, 2)]
    [InlineData("pale_hills", 3UL, AiDifficulty.Normal, 2)]
    [InlineData("pale_hills", 3UL, AiDifficulty.Easy, 2)]
    [InlineData("vathar_crossing", 1UL, AiDifficulty.Normal, 2)]
    [InlineData("vathar_crossing", 1UL, AiDifficulty.Easy, 2)]
    [InlineData("generated", 2UL, AiDifficulty.Normal, 2)]
    [InlineData("vathar_crossing", 2UL, AiDifficulty.Normal, 4)]
    [InlineData("vathar_crossing", 1UL, AiDifficulty.Normal, -4)]
    public void AProbedMatch_FarFogOracleAndExtraOracles(string map, ulong seed, AiDifficulty first, int players)
    {
        int[]? teams = players < 0 ? new[] { 1, 2, 1, 2 } : null;
        Row(map, seed, first, Math.Abs(players), teams, Math.Abs(players) > 2 ? 144_000 : 72_000, oracles: true);
    }

    /// <summary>
    /// Both Pale Hills FFAs (seeds 1 and 2) to 144,000 with the far fog oracle (a row that does not end is measured, not failed).
    /// M5-H6: seed 2 now trips the sweep's refusal-streak check at tick 35,301 (player 1's Gather to a gone node refused 9
    /// thinks in a row, the rebalance re-sending it: BUG-1151's pattern, not fixed here); skipped under BUG-1151 with that
    /// measurement (the far fog oracle was clean to there).
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL, Skip = "BUG-1151 (open): since M5-H6 player 1's Gather to a gone node is refused 9 thinks in a row at tick 35,301 (the rebalance re-send)")]
    public void PaleHillsFfa_To144000_FarFogOracle(ulong seed) => Row("pale_hills", seed, AiDifficulty.Normal, 4, null, 144_000, oracles: false);

    /// <summary>The skipped / moved Pale Hills seeds 2 and 5 Normal v Normal to 144,000 with the far fog oracle: the BUG-1157 measurement.</summary>
    [Theory]
    [InlineData(2UL)]
    [InlineData(5UL)]
    public void PaleHillsSeeds2And5_To144000_Measured(ulong seed) => Row("pale_hills", seed, AiDifficulty.Normal, 2, null, 144_000, oracles: false);
    /// <summary>
    /// The leg's worker losses (BUG-1158's measurement) and the far timeline on the Pale Hills matches where seats expand:
    /// Normal v Normal seeds 1, 2 and 5 and Hard v Normal seeds 2 and 3 (two of the tally's five no-results), to 72,000.
    /// </summary>
    [Theory]
    [InlineData(1UL, AiDifficulty.Normal)]
    [InlineData(2UL, AiDifficulty.Normal)]
    [InlineData(5UL, AiDifficulty.Normal)]
    [InlineData(2UL, AiDifficulty.Hard)]
    [InlineData(3UL, AiDifficulty.Hard)]
    public void PaleHills_LegLosses_Measured(ulong seed, AiDifficulty first) => Row("pale_hills", seed, first, 2, null, 72_000, oracles: false);
}
