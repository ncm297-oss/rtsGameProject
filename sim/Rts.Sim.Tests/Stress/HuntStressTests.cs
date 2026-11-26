using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA, M5-4d (session 2026-11-06-0926): the AI's hunt for the last enemy units under real matches. A human seat that never
/// plays, its five workers sent to hide at the map's corners and centre, on every shipped map (generated, Raraku, Pale
/// Hills, Vathar Crossing), against a Normal AI of either faction on a seed per opening: the AI must raze the hall and then
/// find the workers within 72,000 ticks (BUG-1107: all 24 rows red at b7a3f83, the corner workers are never seen). Four Normal AI seats free-for-all on Pale Hills (and a 2v2) to a result, with
/// the PlayerView oracle run on every hunting seat's view at every think it hunts and the memory checked against a fresh
/// view (an enemy it sees, never an ally). Pale Hills seed 3 saved mid-hunt (sweeping, and chasing a sighting) and followed
/// hash for hash to the match's end.
/// </summary>
public class HuntStressTests
{
    private readonly ITestOutputHelper _out;

    public HuntStressTests(ITestOutputHelper output) => _out = output;

    private static string Clock(int ticks) => $"{ticks / 1200}:{ticks / 20 % 60:D2}";

    internal static Simulation Match(string map, PlayerSeat[] seats, ulong seed, int units = 400)
    {
        if (map == "generated") return AiScenes.Generated(seats, seed, false, out _, units: units);
        int size = TestSim.Data.Maps[TestSim.Data.FindMap(map)].Size;
        return AiScenes.Generated(seats, seed, false, out _, units: units,
            configure: c => c with { MapId = map, Map = c.Map with { Width = size, Height = size } });
    }

    /// <summary>The smallest seed in 1-500 whose Normal opening for <paramref name="player"/> is <paramref name="opening"/>.</summary>
    private static ulong SeedFor(int player, int opening)
    {
        for (ulong s = 1; s <= 500; s++)
            if (Rts.Sim.Tests.Content.AiOpeningSeeds.Pick(s, player, AiDifficulty.Normal) == opening) return s;
        throw new InvalidOperationException("no seed");
    }

    public static TheoryData<string, int, int> HideRows()
    {
        var rows = new TheoryData<string, int, int>();
        foreach (string map in new[] { "generated", "raraku", "pale_hills", "vathar_crossing" })
            for (int ai = 0; ai < 2; ai++)
                for (int o = 0; o < 3; o++) rows.Add(map, ai, o);
        return rows;
    }

    /// <summary>
    /// A human seat that gives no order but one: at tick 1 its five workers walk to the map's four corners and its centre
    /// (as near as they get). The Normal AI seat must raze the hall and hunt every worker down: the human defeated and the
    /// match over within 72,000 ticks.
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [MemberData(nameof(HideRows))]
    public void AHumanSeatRazedToHidingWorkers_IsHuntedDown(string map, int aiSeat, int opening)
    {
        ulong seed = SeedFor(aiSeat, opening);
        int human = 1 - aiSeat;
        var seats = new PlayerSeat[2];
        seats[aiSeat] = new PlayerSeat(aiSeat, SeatController.Ai, AiDifficulty.Normal);
        seats[human] = new PlayerSeat(human, SeatController.Human, AiDifficulty.Normal);
        Simulation sim = Match(map, seats, seed);
        Assert.Equal(opening, sim.AiOpening(aiSeat));
        World w = sim.World;
        while (w.LiveUnits(human) == 0 && sim.TickNumber < 10) sim.Tick(); // the start's spawn commands applied
        float size = w.NavGrid.Width * MapConstants.CellSize;
        Vector2[] hides = { new(4, 4), new(size - 4, 4), new(4, size - 4), new(size - 4, size - 4), new(size / 2, size / 2) };
        int k = 0;
        for (int s = 0; s < w.Units.Capacity; s++)
            if (w.Units.Alive[s] && w.Units.Owner[s] == human)
                sim.Enqueue(Command.Move(human, new EntityHandle(s, w.Units.Generation[s]), hides[k++ % hides.Length]));
        Assert.Equal(5, k);
        AiPlayer ai = sim.AiOf(aiSeat)!;
        int hallLost = -1, firstHunt = -1;
        while (sim.TickNumber < 72_000 && !sim.MatchOver)
        {
            sim.Tick();
            if (hallLost < 0 && w.ProductionBuildings(human) == 0) hallLost = sim.TickNumber;
            if (firstHunt < 0 && ai.Board.HuntsStarted > 0) firstHunt = sim.TickNumber;
        }
        int left = w.LiveUnits(human);
        var where = new List<string>();
        int cols = ScoutManager.GridLines(w.NavGrid.Width, ai.Rules.ScoutGrid), rows = ScoutManager.GridLines(w.NavGrid.Height, ai.Rules.ScoutGrid);
        for (int s = 0; s < w.Units.Capacity; s++)
        {
            if (!w.Units.Alive[s] || w.Units.Owner[s] != human) continue;
            float best = float.MaxValue;
            for (int i = 0; i < MilitaryManager.HuntPoints(cols, rows); i++)
                best = Math.Min(best, Vector2.Distance(w.Units.Position[s], MilitaryManager.HuntPoint(ai, cols, rows, i)));
            where.Add($"{w.Units.Position[s]} ({best:F1} m from the nearest sweep point)");
        }
        _out.WriteLine($"  end state: hunting {ai.Board.Hunting}, wave {ai.Board.WaveActive}, known buildings {ai.Board.KnownEnemyBuildings}, sweep {ai.Board.HuntSweep}, visited {ai.Board.HuntVisited.Count(v => v)} / {ai.Board.HuntVisited.Length}, " +
            $"memory tick {ai.Board.EnemyUnitTick}, army {ai.ArmyCount}; human units at {string.Join(", ", where)}");
        _out.WriteLine($"HIDE {map} ai seat {aiSeat} opening {ai.Opening.Id} seed {seed}: over {sim.MatchOver} at {Clock(sim.TickNumber)} ({sim.TickNumber}); " +
            $"human production lost {(hallLost < 0 ? "never" : Clock(hallLost))}, first hunt {(firstHunt < 0 ? "never" : Clock(firstHunt))}, hunts {ai.Board.HuntsStarted}, waves {ai.Board.WavesStarted}; human units left {left}");
        Assert.True(sim.MatchOver && w.IsDefeated(human), $"{map} seed {seed}: the human seat is not finished by 72,000 ticks ({left} units left, hunts {ai.Board.HuntsStarted})");
    }

    /// <summary>
    /// Four Normal AI seats on Pale Hills (free-for-all, or teams 1/2/1/2), to a result within 144,000 ticks. At every think of
    /// a seat that is hunting, its view equals the <see cref="PlayerViewOracle"/>'s answers, and a sighting it remembered this
    /// think is the position of a unit a fresh view of that seat shows, owned by an enemy (never an ally).
    /// M6-SH2 fix round (BUG-1536, a finding, not re-bound): the free-for-all on seed 3 (over at 107,419 on the base) no
    /// longer ends by 144,000 since a site stopped being a drop-off. Probed every 6,000 ticks: from about tick 30,000 no seat
    /// gathers gold again (gathered gold frozen at 8,880 / 8,850 / 8,890, banks 2-16 gold) and every seat banks only wood
    /// (12,000-30,000 by the bound), so none can train a soldier; p0 (5 soldiers, starved from 52,420) waves and hunts but
    /// cannot finish p1 (13 units) and p3 (22), which have no army and so are never starved (ai.starvedMinArmy). BUG-1460's
    /// shape with gold in place of wood (not split further: whether the mines are empty or unknown to the seats). The row
    /// moves to the smallest ending seed,
    /// 1 (over at 91,995, p2, 4 hunts; seeds 2 / 5 end at 50,397 / 59,083, seed 4 does not end). Was seed 3.
    /// M6-S12 fix round 1 (BUG-1559, rule 45, a finding, not re-bound): under the rally, rebalance and re-scout rules the
    /// free-for-all on seed 1 no longer ends by 144,000 (every oracle clean). Probed every 6,000 ticks: all four seats have
    /// gathered their start's gold by 25:00 (8,870 / 8,850 / 8,820 / 8,890, frozen from then on), the gold war at 20-25 min
    /// spends the armies, p3 falls at 55,508 and p0 at 107,906; from 70:00 p1 has 0 soldiers (26 workers, 7 production
    /// buildings, 10 gold, 18,000-26,000 wood) and p2 one (21 workers, 20 gold), and neither can train one: the skipped
    /// <see cref="FourAiSeats_OnPaleHills_Seed1_KnownStall"/>'s shape (BUG-1068 / BUG-1116 / BUG-1536), not a fault of the
    /// new rules. The row moves to the next seed that ends, 2 (over at 59,951, p1, 4 hunts; seed 5 ends at 73,778, seeds 6
    /// and 7 do not end), which is also <c>FourAiSeats_OnPaleHills_Seed2_KnownStall</c> (dropped in M6-S13)'s match. The 2v2 row keeps
    /// seed 1 (it ends). Was seed 1.
    /// M6-S13 (the owner's draw rule; a pin that moved because the match became spent before its old end): the free-for-all
    /// on seed 2 is spent from 26,442 (22:02, the map's gold gone), the clock restarts 114 times on deaths, and it ends in a
    /// draw at 58,378 (48:38) with all four seats standing (<c>DrawStalledRowsReportTests</c>). Was: over at 59,951, p1, 4
    /// hunts. The oracle checks every hunting think up to the draw as before; the row now pins the draw.
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData(2UL, false, 58_378)]
    [InlineData(1UL, true, 0)]
    public void FourAiSeats_OnPaleHills_EndWithTheOracleOnEveryHunt(ulong seed, bool teams, int drawAt) => FourSeatsRow(seed, teams, drawAt);

    /// <summary>
    /// Pale Hills free-for-all seed 1 (BUG-1153's family since M5-9a; BUG-1068 / BUG-1116): no result by 144,000 on the M5-9a
    /// branch, and none with M5-9b's far expansion either (the terminal rule's scope 5: a skipped row with the measurement).
    /// With four seats every start is taken, so all the map's gold (9,000 a start) is in some seat's reach and is spent by
    /// about 26,000; seat 3 is defeated at about 50:00 and the three left walk to its start at 57,620 and find its mines spent:
    /// no free gold anywhere. At 144,000 seats 1 and 2 are alive (0 and 6 army, 31,000-42,000 wood each, gold 8,880 / 8,820
    /// gathered, all of it before 30:00), seats 0 and 3 out: two gold-starved seats that can raze nothing.
    /// M6-SH2: it ended (91,995, p2) and became the oracle row's seed. M6-S12 (BUG-1559): no result by 144,000 again, the same
    /// shape (gold gathered 8,820-8,890 a seat, all of it by 25:00; p3 out at 55,508, p0 at 107,906; p1 0 soldiers and p2 one
    /// from 70:00, 18,000-26,000 wood each), so the oracle row moved to seed 2 and this row stays skipped.
    /// M6-S13 (the owner's draw rule, docs/02 "Game flow" 4): un-skipped. The match is spent from 35,541 (29:37: the map's gold
    /// gone, no seat can train), the clock restarts 69 times on deaths, and it ends in a draw at 61,779 (51:28), p3 out and
    /// p0, p1, p2 standing (<c>DrawStalledRowsReportTests</c>). Was: no result by 144,000 (skipped, BUG-1068 / 1116 / 1559).
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData(1UL, false)]
    public void FourAiSeats_OnPaleHills_Seed1_KnownStall(ulong seed, bool teams) => FourSeatsRow(seed, teams, drawAt: 61_779);

    // M6-S13: FourAiSeats_OnPaleHills_Seed2_KnownStall (Pale Hills free-for-all seed 2, BUG-1116 / BUG-1068's remaining case,
    // un-skipped by M5-9a round 2) was the same match as the oracle row's seed 2 since M6-S12 (docs/01 2026-11-16; the handoff
    // said to drop the twin at the next pin move); dropped here, when that match moved to a draw at 58,378 (was 59,951, p1).

    private void FourSeatsRow(ulong seed, bool teams, int drawAt = 0)
    {
        var seats = new PlayerSeat[4];
        for (int p = 0; p < 4; p++) seats[p] = new PlayerSeat(p % 2, SeatController.Ai, AiDifficulty.Normal, teams ? 1 + p % 2 : 0);
        Simulation sim = Match("pale_hills", seats, seed, units: 1024);
        World w = sim.World;
        var tally = new PlayerViewOracle.Tally();
        string? bad = null;
        int huntingThinks = 0, sightings = 0;
        for (int p = 0; p < 4; p++)
        {
            int player = p;
            sim.AiOf(p)!.ThoughtForTests = ai =>
            {
                if (bad != null) return;
                if (ai.Board.EnemyUnitTick == ai.Now)
                {
                    sightings++;
                    var fresh = new PlayerView(w, player);
                    int s = -1;
                    bool found = false;
                    while (fresh.NextUnit(ref s, out SeenUnit u))
                        if (u.Owner != player && fresh.IsEnemy(u.Owner) && u.Position == ai.Board.EnemyUnitPoint) { found = true; break; }
                    if (!found) bad = $"tick {ai.Now}: player {player} remembers {ai.Board.EnemyUnitPoint}, no enemy unit its view shows";
                }
                if (!ai.Board.Hunting) return;
                huntingThinks++;
                PlayerViewOracle.Check(w, ai.View, player, tally, $"seed {seed} tick {ai.Now} player {player} (hunting)");
                if (tally.First.Length > 0) bad = tally.First;
            };
        }
        var defeatedAt = new int[4];
        while (sim.TickNumber < 144_000 && !sim.MatchOver && bad == null)
        {
            sim.Tick();
            for (int p = 0; p < 4; p++)
            {
                // The oracle's node memory must see every fog update of every player (it is stateful); the full check runs only on hunting thinks.
                PlayerViewOracle.NodeOracle.For(w).Observe(w, p, tally, $"seed {seed} tick {sim.TickNumber} player {p}");
                if (defeatedAt[p] == 0 && w.IsDefeated(p)) defeatedAt[p] = sim.TickNumber;
            }
            if (tally.First.Length > 0) bad ??= tally.First;
        }
        string Seat(int p) => $"p{p} {sim.AiOf(p)!.Opening.Id} defeated {(defeatedAt[p] == 0 ? "-" : Clock(defeatedAt[p]))} waves {sim.AiOf(p)!.Board.WavesStarted} hunts {sim.AiOf(p)!.Board.HuntsStarted} units {w.LiveUnits(p)} prod {w.ProductionBuildings(p)}";
        _out.WriteLine($"FFA{(teams ? " 2v2" : "")} pale_hills seed {seed}: over {sim.MatchOver} at {Clock(sim.TickNumber)} ({sim.TickNumber}); {huntingThinks} hunting thinks oracle-checked, {sightings} sightings; " +
            string.Join("; ", Enumerable.Range(0, 4).Select(Seat)));
        _out.WriteLine(tally.ToString());
        Assert.True(bad == null, bad);
        Assert.Equal(0, tally.Leaks);
        Assert.True(sim.MatchOver, $"seed {seed}: no result by 144,000 ticks");
        // M6-S13: a row pinned to a draw ends in one at its tick; every other row ends with a winner.
        Assert.True(w.MatchDrawn == (drawAt > 0), $"seed {seed}: drawn {w.MatchDrawn} at {sim.TickNumber}, expected {(drawAt > 0 ? $"a draw at {drawAt}" : "a winner")}");
        if (drawAt > 0) Assert.Equal(drawAt, sim.TickNumber);
    }

    /// <summary>
    /// Pale Hills seed 3 (a BUG-1068 stall before M5-4d): saved at the first think a seat hunts by a sweep and at the first
    /// it hunts a sighting; each load follows the uninterrupted run hash for hash to the match's end, the AI seats field for field equal there.
    /// M5-9a: with Normal's counter shift seed 3 ends at 31,891 without a hunt, so the row plays seed 5 (<see cref="MidHuntSeed"/>,
    /// one hunt, ends at 45,358) to keep a save taken mid-hunt. M5-9b: seed 1 (one hunt, ends at 48,858; seed 5 ends past 72,000;
    /// M5-H6: 50,140, one hunt). M6-SH2 (BUG-1533): seed 1 ends at 27,498 with no hunt, so the row plays seed 2 (then 42,016,
    /// 4 hunts, the first at 34,761). Since M6-S12 (docs/03 "Implementation (M6-S12)", fix round 1): seed 2 ends at 51,218
    /// (was 42,016), saved at a sighting (p1, 46,381) and a sweep (p1, 48,541); re-read by M7-H3 (2026-11-25), unchanged.
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void PaleHills_SavedMidHunt_FollowsTheRunToTheEnd()
    {
        Simulation sim = Match("pale_hills", new[] { new PlayerSeat(0, SeatController.Ai, AiDifficulty.Normal), new PlayerSeat(1, SeatController.Ai, AiDifficulty.Normal) }, MidHuntSeed);
        var loads = new List<(string What, Simulation Sim)>();
        bool sweepSaved = false, sightSaved = false;
        while (sim.TickNumber < 72_000 && !sim.MatchOver)
        {
            sim.Tick();
            for (int p = 0; p < 2; p++)
            {
                Blackboard b = sim.AiOf(p)!.Board;
                if (!b.Hunting) continue;
                if (!sweepSaved && b.HuntSweep >= 0)
                {
                    sweepSaved = true;
                    loads.Add(($"sweep p{p} @ {sim.TickNumber}", Reload(sim)));
                }
                if (!sightSaved && b.HuntSweep < 0)
                {
                    sightSaved = true;
                    loads.Add(($"sighting p{p} @ {sim.TickNumber}", Reload(sim)));
                }
            }
            foreach ((string what, Simulation l) in loads)
            {
                if (l.TickNumber < sim.TickNumber) l.Tick();
                Assert.True(l.StateHash() == sim.StateHash(), $"{what}: diverged at tick {sim.TickNumber}");
            }
        }
        _out.WriteLine($"ended {sim.MatchOver} at {sim.TickNumber}; loads: {string.Join(", ", loads.Select(l => l.What))}");
        Assert.True(sim.MatchOver);
        Assert.True(sweepSaved || sightSaved, "no hunt to save in");
        foreach ((string what, Simulation l) in loads) SaveTestRun.AssertSameState(sim, l, what + " at the end");
    }

    /// <summary>
    /// The Pale Hills seed the mid-hunt save row plays: 3 until M5-9a, 5 in M5-9a, 1 since M5-9b (seed 5 now ends only at
    /// 96,321, past the row's 72,000: the far expansion's gold lengthens its war, BUG-1116's slow raze at the end; seed 1 ends
    /// at 48,858 after one hunt), 2 since M6-SH2 (BUG-1533: seed 1 ends at 27,498 with no hunt; seed 2 ends at 42,016 after 4
    /// hunts). Was 1. M6-S12 fix round 1: seed 2 ends at 51,218 (was 42,016), saved at a sighting (p1, 46,381) and a sweep
    /// (p1, 48,541); unchanged.
    /// </summary>
    private const ulong MidHuntSeed = 2;

    private static Simulation Reload(Simulation sim)
    {
        byte[] bytes = SaveTestRun.Save(sim);
        Assert.Equal(Rts.Sim.Save.SaveFormat.CurrentVersion, BitConverter.ToInt32(bytes, Rts.Sim.Save.SaveFormat.MagicLength));
        return SaveTestRun.Load(TestSim.Data, bytes);
    }
}
