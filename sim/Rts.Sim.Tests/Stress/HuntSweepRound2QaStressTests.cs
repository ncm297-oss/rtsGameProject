using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;
using Rts.Sim.Vision;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA, M5-4d re-check round 2 (session 2026-11-06-0926), BUG-1107's second fix (the perimeter leg, the odd pass's cell-edge
/// midpoints, the true-least tie). Hide points the developer's rows don't name: interior points a quarter cell off both
/// lattices (18 m from every point of the two passes together), edge points a quarter cell along (the leg must see them), on
/// the 256 m maps and on Vathar's river band where it meets the border, and workers on high ground (Raraku's north terrace
/// corner and Pale Hills' level-2 plateau corner, which low ground can't see up onto). A raider that keeps interrupting the
/// leg (each sighting pulls the army inland) must not leave an edge stretch unseen. A save taken mid-leg and mid odd pass
/// follows the uninterrupted run; the leg's saved fields are range-checked on load.
/// </summary>
internal static class HuntSweepRound2Qa
{
    /// <summary>Per map the five hide points, in metres (cw: the scout cell, 51.2 m on every shipped map; e: 1.5 m off the border).</summary>
    internal static Vector2[] Hides(string map, float size, float cw)
    {
        const float e = 1.5f;
        return map switch
        {
            "raraku" => new[]
            {
                new Vector2(99, 19),                     // north terrace (level 1) corner: no even-pass point on level 1 within sight
                new Vector2(1.25f * cw, 3.25f * cw),     // interior, a quarter cell off both lattices
                new Vector2(3.75f * cw, e),              // north edge, a quarter cell short of a ring point
                new Vector2(e, 2.75f * cw),              // west edge
                new Vector2(4.25f * cw, size - e),       // south edge near the east corner
            },
            "pale_hills" => new[]
            {
                new Vector2(195, 193),                   // the NW hill's level-2 plateau, its far corner
                new Vector2(3.25f * cw, 6.75f * cw),     // interior quarter
                new Vector2(5.75f * cw, e),              // north edge quarter
                new Vector2(size - e, 2.25f * cw),       // east edge quarter
                new Vector2(1.25f * cw, size - e),       // south edge quarter
            },
            "vathar_crossing" => new[]
            {
                new Vector2(e, 5.25f * cw),              // the river band at the west border (level 0)
                new Vector2(size - e, 4.75f * cw),       // the river band at the east border
                new Vector2(6.75f * cw, 3.25f * cw),     // interior quarter (north bank)
                new Vector2(8.25f * cw, e),              // north edge quarter
                new Vector2(3.25f * cw, size - e),       // south edge quarter
            },
            _ => new[]
            {
                new Vector2(2.25f * cw, 1.25f * cw),
                new Vector2(3.25f * cw, e),
                new Vector2(size - e, 1.75f * cw),
                new Vector2(0.75f * cw, size - e),
                new Vector2(3.75f * cw, 3.25f * cw),
            },
        };
    }

    internal static Simulation Setup(string map, int aiSeat, ulong seed, out AiPlayer ai, out int human)
    {
        human = 1 - aiSeat;
        var seats = new PlayerSeat[2];
        seats[aiSeat] = new PlayerSeat(aiSeat, SeatController.Ai, AiDifficulty.Normal);
        seats[human] = new PlayerSeat(human, SeatController.Human, AiDifficulty.Normal);
        Simulation sim = HuntStressTests.Match(map, seats, seed);
        World w = sim.World;
        while (w.LiveUnits(human) == 0 && sim.TickNumber < 10) sim.Tick();
        ai = sim.AiOf(aiSeat)!;
        float size = w.NavGrid.Width * MapConstants.CellSize;
        int cols = ScoutManager.GridLines(w.NavGrid.Width, ai.Rules.ScoutGrid);
        Vector2[] hides = Hides(map, size, size / cols);
        int k = 0;
        for (int s = 0; s < w.Units.Capacity; s++)
            if (w.Units.Alive[s] && w.Units.Owner[s] == human)
                sim.Enqueue(Command.Move(human, new EntityHandle(s, w.Units.Generation[s]), hides[k++ % hides.Length]));
        Assert.Equal(5, k);
        return sim;
    }

    /// <summary>Hunt bookkeeping over a run: legs begun / finished round the ring / dropped, the highest pass, and ticks on an odd pass.</summary>
    internal sealed class LegLog
    {
        internal int Begun, Finished, Dropped, MaxPass, OddTicks;
        private int _prevRing = -1, _prevHunts;

        internal void Observe(AiPlayer ai)
        {
            Blackboard b = ai.Board;
            if (b.HuntRing >= 0 && _prevRing < 0) Begun++;
            if (b.HuntRing < 0 && _prevRing >= 0)
            {
                bool allRing = true;
                int cols = Cols(ai), rows = Rows(ai);
                for (int i = MilitaryManager.RingStart(cols, rows); i < b.HuntVisited.Length; i++) allRing &= b.HuntVisited[i];
                if (b.Hunting && b.HuntsStarted == _prevHunts && allRing) Finished++;
                else Dropped++;
            }
            MaxPass = Math.Max(MaxPass, b.HuntPass);
            if (b.Hunting && b.HuntPass % 2 == 1) OddTicks++;
            _prevRing = b.HuntRing;
            _prevHunts = b.HuntsStarted;
        }

        public override string ToString() => $"legs begun {Begun} finished {Finished} dropped {Dropped}; max pass {MaxPass}; ticks hunting on an odd pass {OddTicks}";
    }

    internal static int Cols(AiPlayer ai) => ScoutManager.GridLines((int)(ai.MapSize.X / MapConstants.CellSize), ai.Rules.ScoutGrid);

    internal static int Rows(AiPlayer ai) => ScoutManager.GridLines((int)(ai.MapSize.Y / MapConstants.CellSize), ai.Rules.ScoutGrid);
}

/// <summary>New hide points (quarter-cell interior / edge, the river band at the border, high ground), all four maps, either AI seat.</summary>
public class HuntSweepRound2HideQaTests
{
    private readonly ITestOutputHelper _out;
    public HuntSweepRound2HideQaTests(ITestOutputHelper o) => _out = o;

    public static TheoryData<string, int> Rows()
    {
        var rows = new TheoryData<string, int>();
        foreach (string map in new[] { "generated", "raraku", "pale_hills", "vathar_crossing" })
            for (int ai = 0; ai < 2; ai++) rows.Add(map, ai);
        return rows;
    }

    [Theory]
    [Trait("Category", "Stress")]
    [MemberData(nameof(Rows))]
    public void WorkersAtNewHidePoints_AreHuntedDown(string map, int aiSeat)
    {
        ulong seed = HuntSweepQa.SeedFor(aiSeat, 1, 0);
        Simulation sim = HuntSweepRound2Qa.Setup(map, aiSeat, seed, out AiPlayer ai, out int human);
        World w = sim.World;
        var log = new HuntSweepRound2Qa.LegLog();
        int firstHunt = -1;
        while (sim.TickNumber < 72_000 && !sim.MatchOver)
        {
            sim.Tick();
            log.Observe(ai);
            if (firstHunt < 0 && ai.Board.HuntsStarted > 0) firstHunt = sim.TickNumber;
        }
        int left = w.LiveUnits(human);
        string line = $"{map} ai {aiSeat} opening {ai.Opening.Id} seed {seed}: over {sim.MatchOver} at {HuntSweepQa.Clock(sim.TickNumber)} ({sim.TickNumber}); " +
            $"first hunt {(firstHunt < 0 ? "never" : HuntSweepQa.Clock(firstHunt))}, hunts {ai.Board.HuntsStarted}; {log}; left {left}" +
            (left > 0 ? $" at {HuntSweepQa.Where(sim, ai, human)}" : "");
        _out.WriteLine("NEWHIDE " + line);
        Assert.True(sim.MatchOver && w.IsDefeated(human), line);
    }
}

/// <summary>The leg against interruptions (a raider pulling the army inland, flat scene), a save mid-leg / mid odd pass, forged leg fields.</summary>
[Collection(SerialCollection.Name)]
public class HuntSweepRound2LegQaTests
{
    private readonly ITestOutputHelper _out;
    public HuntSweepRound2LegQaTests(ITestOutputHelper o) => _out = o;

    private static readonly Lazy<GameData> s_quiet = new(() => AiScenes.DataWith(dir =>
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            foreach (JsonNode? o in root["openings"]!.AsArray())
            {
                o!["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1));
                o["attack"] = new JsonObject { ["minArmyPop"] = 200, ["minArmyCost"] = 0 };
            }
        })));

    /// <summary>
    /// Flat 128 m scene, combat off, player 0 hunting with four Heavy Infantry from the west edge (the leg begins at once);
    /// player 1 is one Raider parked at the map centre. With <paramref name="interrupts"/> &gt; 0, while the army walks the leg
    /// the raider runs at it until seen, then back to the centre (the army chases the sighting inland, then resumes the leg),
    /// up to that many times. The leg must finish round the ring, and every border cell must have been visible to player 0
    /// at some tick of the leg (the leg's claim: it "traces every edge once a pass"). With <paramref name="interrupts"/> = -1
    /// the one sighting is far from the army instead: the raider, parked at (40, 90) m, steps into the sight of player 0's
    /// hall (west side) the moment the leg turns to its next ring point on the east edge; the army crosses the map to the
    /// sighting, then resumes the leg at that ring point, so the stretch of east edge before it is approached from inland.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)] // BUG-1111 fixed (M5-4d slice 2): the leg resumes at the ring point before HuntRing
    public void ARingLeg_InterruptedBySightings_StillSeesTheWholeBorder(int interrupts)
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), s_quiet.Value);
        World w = sim.World;
        sim.Tick();
        for (int i = 0; i < w.Buildings.Capacity; i++)
            if (w.Buildings.Alive[i] && w.Buildings.Owner[i] == 1) Assert.True(w.Buildings.Free(new EntityHandle(i, w.Buildings.Generation[i])));
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && (w.Units.Owner[i] == 1 || w.Units.Owner[i] == 0)) w.Units.Free(new EntityHandle(i, w.Units.Generation[i]));
        int n = AiScenes.FlatSize;
        bool far = interrupts < 0;
        Vector2 park = far ? new Vector2(40, 90) : w.NavGrid.CellCenter(n / 2, n / 2);
        (int hx, int hy) = AiScenes.HallAt(0);
        Vector2 home = w.NavGrid.CellCenter(hx, hy) + new Vector2(8, 0);
        int prevRing = -1;
        EntityHandle raider = CombatScenes.Place(sim, 1, TestSim.Data.FindUnit("whirlwind_raider"), park);
        var army = new List<EntityHandle>();
        for (int k = 0; k < 4; k++) army.Add(CombatScenes.Place(sim, 0, TestSim.Data.FindUnit("malazan_heavy_infantry"), w.NavGrid.CellCenter(1 + k % 2, 30 + k / 2)));
        AiPlayer ai = sim.AiOf(0)!;
        ai.Board.WavesStarted = 1;
        var border = new List<int>();
        for (int x = 0; x < n; x++) { border.Add(x); border.Add((n - 1) * n + x); }
        for (int y = 1; y < n - 1; y++) { border.Add(y * n); border.Add(y * n + n - 1); }
        var seen = new bool[n * n];
        int state = 0, done = 0, legStart = -1, legEnd = -1, lastResume = 0, dropped = 0;
        var trace = new List<string>();
        (int, int, int) last = (-2, -2, -2);
        while (sim.TickNumber < 40_000)
        {
            sim.Tick();
            Blackboard b = ai.Board;
            if ((b.HuntSweep, b.HuntRing, b.HuntRingLeft) != last)
            {
                last = (b.HuntSweep, b.HuntRing, b.HuntRingLeft);
                Vector2 c0 = Centroid(w, army);
                trace.Add($"t{sim.TickNumber} sweep {b.HuntSweep} ring {b.HuntRing} left {b.HuntRingLeft} target {b.WaveTargetPoint} army ({c0.X:F0},{c0.Y:F0}) raider s{state}");
            }
            if (legStart < 0 && b.HuntRing >= 0) legStart = sim.TickNumber;
            if (legStart >= 0)
            {
                byte[] vis = PlayerViewOracle.UnionVisibility(w, 0);
                foreach (int c in border) if (vis[c] == VisionConstants.Visible) seen[c] = true;
                if (!b.Hunting) dropped++;
                if (b.HuntRing < 0) { legEnd = sim.TickNumber; break; }
            }
            if (far)
            {
                // One sighting at home, the moment the leg turns to an east-edge ring point with the army on the east edge.
                if (state == 0 && done == 0 && b.HuntRing >= 0 && b.HuntRing != prevRing && prevRing >= 0 && Centroid(w, army).X > 110 && Centroid(w, army).Y > 10 && Centroid(w, army).Y < 110)
                {
                    sim.Enqueue(Command.Move(1, raider, home));
                    state = 1;
                }
                else if (state == 1 && b.Hunting && b.HuntSweep < 0) { sim.Enqueue(Command.Move(1, raider, park)); state = 2; done++; }
                else if (state == 2 && b.HuntSweep >= 0) state = 3;
                prevRing = b.HuntRing;
                continue;
            }
            // The raider: idle at the park -> run at the army (on the leg) -> seen: back to the park.
            if (state == 0 && done < interrupts && b.HuntRing >= 0 && b.HuntSweep == b.HuntRing && sim.TickNumber - lastResume > 100)
            {
                sim.Enqueue(Command.Move(1, raider, Centroid(w, army)));
                state = 1;
            }
            else if (state == 1 && b.Hunting && b.HuntSweep < 0)
            {
                sim.Enqueue(Command.Move(1, raider, park));
                state = 2;
                done++;
            }
            else if (state == 1 && sim.TickNumber % 20 == 0) sim.Enqueue(Command.Move(1, raider, Centroid(w, army)));
            else if (state == 2 && b.HuntSweep >= 0) { state = 0; lastResume = sim.TickNumber; }
            else if (state == 2 && sim.TickNumber % 20 == 0)
            {
                // Flee: to the far side of the centre from the army, out of its sight (a raider outruns Heavy Infantry).
                Vector2 away = park - Centroid(w, army);
                if (away.LengthSquared() > 1f) away = Vector2.Normalize(away);
                sim.Enqueue(Command.Move(1, raider, park + away * 45f));
            }
        }
        var unseen = new List<string>();
        foreach (int c in border) if (!seen[c]) unseen.Add($"({c % n}, {c / n})");
        foreach (string t in trace) _out.WriteLine(t);
        _out.WriteLine($"interrupts {done} / {interrupts}; leg {legStart} -> {legEnd} ({(legEnd - legStart) / 20} s); not hunting on {dropped} leg ticks; " +
            $"border cells never visible in the leg: {unseen.Count} / {border.Count}: {string.Join(" ", unseen.Take(40))}");
        Assert.True(legStart >= 0 && legEnd > legStart, "the leg never began or never finished");
        Assert.Equal(far ? 1 : interrupts, done);
        Assert.True(unseen.Count == 0, $"{unseen.Count} border cells never visible during a leg with {done} interruptions: {string.Join(" ", unseen.Take(40))}");
    }

    /// <summary>
    /// The leg's steps and a pass flip (marks cleared, <c>HuntPass</c> up, the odd lattice) allocate nothing: flat 128 m
    /// scene, combat off, player 0 hunting with four Heavy Infantry, player 1 a Raider sealed in a pocket of blocked cells
    /// (seen when passed, never reached). Probed over 600 ticks of the leg, then over 200 pass flips driven through NextSweep.
    /// </summary>
    [Fact]
    public void LegStepsAndAPassFlip_AllocateNothing()
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), s_quiet.Value);
        World w = sim.World;
        const int px = 44, py = 44, ring = 2;
        for (int y = py - ring; y <= py + ring; y++)
            for (int x = px - ring; x <= px + ring; x++)
                if (Math.Max(Math.Abs(x - px), Math.Abs(y - py)) == ring) w.NavGrid.SetResource(x, y, 1, 1);
        sim.Tick();
        for (int i = 0; i < w.Buildings.Capacity; i++)
            if (w.Buildings.Alive[i] && w.Buildings.Owner[i] == 1) Assert.True(w.Buildings.Free(new EntityHandle(i, w.Buildings.Generation[i])));
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && (w.Units.Owner[i] == 1 || w.Units.Owner[i] == 0)) w.Units.Free(new EntityHandle(i, w.Units.Generation[i]));
        CombatScenes.Place(sim, 1, TestSim.Data.FindUnit("whirlwind_raider"), w.NavGrid.CellCenter(px, py));
        for (int k = 0; k < 4; k++) CombatScenes.Place(sim, 0, TestSim.Data.FindUnit("malazan_heavy_infantry"), w.NavGrid.CellCenter(1 + k % 2, 30 + k / 2));
        AiPlayer ai = sim.AiOf(0)!;
        ai.Board.WavesStarted = 1;
        while (ai.Board.HuntRing < 0 && sim.TickNumber < 2000) sim.Tick();
        Assert.True(ai.Board.HuntRing >= 0, "no leg began");
        int ringBefore = ai.Board.HuntRingLeft;
        Action leg = () => { for (int t = 0; t < 600; t++) sim.Tick(); };
        AllocationProbe.AssertZero(leg, _out);
        int ringAfter = ai.Board.HuntRing >= 0 ? ai.Board.HuntRingLeft : 0;
        _out.WriteLine($"leg: ring points left {ringBefore} -> {ringAfter}");
        Assert.True(ringAfter < ringBefore, "no leg step inside the probe");
        // The pass flip, driven directly (in this scene the sealed Raider's sightings restart the hunt before a pass ends):
        // every point marked, NextSweep clears the marks, counts the pass and picks from the other lattice.
        bool[] visited = ai.Board.HuntVisited;
        int passBefore = ai.Board.HuntPass, picks = 0;
        Vector2 from = new(20, 100);
        Action flip = () =>
        {
            for (int r = 0; r < 200; r++)
            {
                for (int i = 0; i < visited.Length; i++) visited[i] = true;
                picks += MilitaryManager.NextSweep(ai, from, -1) >= 0 ? 1 : 0;
            }
        };
        AllocationProbe.AssertZero(flip, _out);
        _out.WriteLine($"pass {passBefore} -> {ai.Board.HuntPass} over {picks} flips");
        Assert.True(ai.Board.HuntPass >= passBefore + 200, "NextSweep did not count the passes");
    }

    private static Vector2 Centroid(World w, List<EntityHandle> units)
    {
        Vector2 sum = Vector2.Zero;
        foreach (EntityHandle h in units) sum += w.Units.Position[h.Index];
        return sum / units.Count;
    }

    /// <summary>
    /// The round-2 hides, AI seat 0: saved at the first tick a leg is half walked and at the first tick of an odd pass with an
    /// interior target (the generated map reaches one; Pale Hills ends on its first pass); each load equals the run field for
    /// field at once (the leg / pass fields included, by reflection), is hash-equal every tick to the end, and field-equal there.
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData("generated", true)]
    [InlineData("pale_hills", false)]
    public void ASaveMidLeg_AndMidOddPass_FollowsTheRunToTheEnd(string map, bool odd)
    {
        Simulation sim = HuntSweepRound2Qa.Setup(map, 0, HuntSweepQa.SeedFor(0, 1, 0), out AiPlayer ai, out _);
        var loads = new List<(string What, Simulation Sim)>();
        bool midLeg = false, midOdd = false;
        int ringCount = ai.Board.HuntVisited.Length - MilitaryManager.RingStart(HuntSweepRound2Qa.Cols(ai), HuntSweepRound2Qa.Rows(ai));
        while (sim.TickNumber < 72_000 && !sim.MatchOver)
        {
            sim.Tick();
            Blackboard b = ai.Board;
            string? what = null;
            if (!midLeg && b.Hunting && b.HuntRing >= 0 && b.HuntRingLeft <= ringCount / 2) { midLeg = true; what = "mid-leg"; }
            else if (!midOdd && b.Hunting && b.HuntPass % 2 == 1 && b.HuntSweep >= 0 && b.HuntSweep < MilitaryManager.RingStart(HuntSweepRound2Qa.Cols(ai), HuntSweepRound2Qa.Rows(ai)))
            { midOdd = true; what = "mid odd pass"; }
            if (what != null)
            {
                Simulation l = SaveTestRun.Load(TestSim.Data, SaveTestRun.Save(sim));
                SaveTestRun.AssertSameState(sim, l, what + " just loaded");
                loads.Add(($"{what} @{sim.TickNumber} ring {b.HuntRing} left {b.HuntRingLeft} pass {b.HuntPass} sweep {b.HuntSweep}", l));
            }
            foreach ((string w, Simulation l) in loads)
            {
                if (l.TickNumber < sim.TickNumber) l.Tick();
                Assert.True(l.StateHash() == sim.StateHash(), $"{w}: diverged at tick {sim.TickNumber}");
            }
        }
        _out.WriteLine($"ended {sim.MatchOver} at {HuntSweepQa.Clock(sim.TickNumber)}; loads: {string.Join(", ", loads.Select(l => l.What))}");
        Assert.True(midLeg, "no leg was ever half walked");
        Assert.True(!odd || midOdd, "no odd pass with an interior target before the end");
        foreach ((string what, Simulation l) in loads) SaveTestRun.AssertSameState(sim, l, what + " at the end");
    }

    /// <summary>
    /// The HUNT section's leg / pass fields (the last 16 bytes before <c>END!</c> with one AI seat: ring, ring left, pass,
    /// hunts started) forged: a ring with nothing left, a ring index past the points, ring left past the points, a negative
    /// pass, each Malformed; the unforged save loads.
    /// </summary>
    [Fact]
    public void ForgedLegFields_AreMalformed()
    {
        byte[] good = SaveBytes.SmallAi();
        int end = good.Length - SaveFormat.ChecksumLength - 4;
        Assert.Equal("END!"u8.ToArray(), good[end..(end + 4)]);
        Simulation loaded = SaveTestRun.Load(TestSim.Data, good);
        AiPlayer ai = loaded.AiOf(0)!;
        int len = ai.Board.HuntVisited.Length;
        Assert.Equal(ai.Board.HuntsStarted, BinaryPrimitives.ReadInt32LittleEndian(good.AsSpan(end - 4)));
        Assert.Equal(ai.Board.HuntPass, BinaryPrimitives.ReadInt32LittleEndian(good.AsSpan(end - 8)));
        Assert.Equal(ai.Board.HuntRingLeft, BinaryPrimitives.ReadInt32LittleEndian(good.AsSpan(end - 12)));
        Assert.Equal(ai.Board.HuntRing, BinaryPrimitives.ReadInt32LittleEndian(good.AsSpan(end - 16)));
        foreach ((int offset, int value, string what) in new[]
        {
            (end - 12, 3, "ring left 3 with no ring"),
            (end - 16, len, "ring index = point count"),
            (end - 8, -1, "pass -1"),
        })
        {
            byte[] bad = (byte[])good.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(offset), value);
            Assert.True(SaveTestRun.TryLoad(TestSim.Data, SaveBytes.Rechecksum(bad)) == SaveError.Malformed, what);
        }
        byte[] both = (byte[])good.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(both.AsSpan(end - 16), len - 1);
        BinaryPrimitives.WriteInt32LittleEndian(both.AsSpan(end - 12), len + 1);
        Assert.True(SaveTestRun.TryLoad(TestSim.Data, SaveBytes.Rechecksum(both)) == SaveError.Malformed, "ring left past the point count");
    }
}
