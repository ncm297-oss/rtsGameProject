using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M5-H5 re-check round 1 (session 2026-11-07-2306): attacks on the BUG-1148 fix, the excused-retry flag
/// (<c>PendingOrder.Slot == 1</c>) on the hall rebuild's Build at the lost hall's anchor. Flat scene (combat off), player 0
/// the Malazan AI with RebuildTests' data.
/// </summary>
public class HallRetryM5H5QaTests
{
    private readonly ITestOutputHelper _out;

    public HallRetryM5H5QaTests(ITestOutputHelper output) => _out = output;

    private static int Keep => TestSim.Data.FindBuilding("malazan_garrison_keep");

    private static readonly Lazy<GameData> s_workers = new(() => AiScenes.DataWithAi("malazan", root =>
        AiScenes.Opening(root)["buildOrder"] = AiScenes.BuildOrder(("time", 0, "train", "malazan_laborer", 20))));

    private static Simulation Scene()
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), s_workers.Value);
        BuildMaps.Give(sim, 0, 2000, 2000);
        return sim;
    }

    private static void NextThink(Simulation sim)
    {
        int think = sim.AiOf(0)!.ThinkTicks;
        do sim.Tick(); while ((sim.TickNumber - 1) % think != 0);
    }

    private static EntityHandle Own(World w, int type)
    {
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 0 && w.Buildings.TypeId[k] == type) return new EntityHandle(k, w.Buildings.Generation[k]);
        return default;
    }

    private static int AtAnchor(List<Command> log, AiPlayer ai, int razed, int anchor)
    {
        int n = 0;
        foreach (Command c in log)
            if (c.Tick > razed && c.Kind == CommandKind.Build && c.TypeId == Keep && c.Position == ai.CellCentre(anchor)) n++;
        return n;
    }

    /// <summary>One fuzz run: 1-3 enemy units parked at random points of the lost footprint, seen or not, leaving after a few thinks or never.</summary>
    private (ulong Hash, int AtAnchor, int SiteCell, int Anchor) ParkedRun(int seed)
    {
        var rng = new Random(seed); // test-side scenario generator, seeded
        Simulation sim = Scene();
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        List<Command> log = AiScenes.Log(sim, 0);
        for (int k = 0; k < 10; k++) NextThink(sim);
        EntityHandle hall = Own(w, Keep);
        int anchor = w.Buildings.Cell[hall.Index];
        Rts.Sim.Combat.CombatSystem.FootprintRect(w, Keep, anchor, out Vector2 min, out Vector2 max);
        int razed = sim.TickNumber;
        Assert.True(w.Buildings.Free(hall));
        int count = 1 + rng.Next(3);
        bool hidden = rng.Next(2) == 0;
        int[] leaveOptions = { -1, 1, 2, 3, 6 };
        int leaveAfter = leaveOptions[rng.Next(leaveOptions.Length)];
        var parked = new EntityHandle[count];
        for (int u = 0; u < count; u++)
        {
            var at = new Vector2(min.X + 0.3f + (float)rng.NextDouble() * (max.X - min.X - 0.6f), min.Y + 0.3f + (float)rng.NextDouble() * (max.Y - min.Y - 0.6f));
            parked[u] = CombatScenes.Place(sim, 1, CombatScenes.HeavyInfantry, at);
        }
        EntityHandle site = default;
        for (int k = 0; k < 25 && site.Generation == 0; k++)
        {
            if (k == leaveAfter)
                foreach (EntityHandle p in parked) w.Units.Free(p);
            NextThink(sim);
            if (!hidden && (leaveAfter < 0 || k < leaveAfter))
                foreach (EntityHandle p in parked) CombatScenes.Spot(sim, 0, p);
            site = Own(w, Keep);
        }
        int n = AtAnchor(log, ai, razed, anchor);
        int siteCell = site.Generation == 0 ? -1 : w.Buildings.Cell[site.Index];
        _out.WriteLine($"seed {seed}: {count} unit(s), hidden {hidden}, leave after {leaveAfter}: {n} Keep Builds at the anchor, site {siteCell} (anchor {anchor})");
        for (int t = 0; t < 200; t++) sim.Tick();
        return (sim.StateHash(), n, siteCell, anchor);
    }

    /// <summary>
    /// Seeded fuzz of the parked-unit case: whatever the number, place, visibility and leave time of the parked units, the
    /// hall goes up within 25 thinks, the old anchor takes at most two Keep Builds (the first and the excused retry), and a
    /// second identical run gives the same state hash.
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void ParkedUnitsOnTheLostHallsAnchor_Fuzz_TheHallGoesUp_AtMostTwoBuildsThere_Deterministic()
    {
        for (int seed = 1; seed <= 16; seed++)
        {
            var a = ParkedRun(seed);
            var b = ParkedRun(seed);
            Assert.Equal(a.Hash, b.Hash);
            Assert.True(a.SiteCell >= 0, $"seed {seed}: no Keep site in 25 thinks");
            Assert.True(a.AtAnchor <= 2, $"seed {seed}: {a.AtAnchor} Keep Builds at the occupied old anchor");
        }
    }

    /// <summary>
    /// A site razed before its Build is judged, twice running at the old anchor: the first is excused (back to the old spot,
    /// the developer's row), the second, being the flagged retry, is marked, and the hall goes up a cell off. The hall must
    /// still go up, once, and no more than three Keep Builds are sent at the old anchor.
    /// </summary>
    [Fact]
    public void AHallSite_RazedBeforeTheJudgeTwiceRunning_TheHallStillGoesUp()
    {
        Simulation sim = Scene();
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        List<Command> log = AiScenes.Log(sim, 0);
        for (int k = 0; k < 10; k++) NextThink(sim);
        EntityHandle hall = Own(w, Keep);
        int anchor = w.Buildings.Cell[hall.Index];
        int razed = sim.TickNumber;
        Assert.True(w.Buildings.Free(hall));
        int[] cells = new int[2];
        for (int r = 0; r < 2; r++)
        {
            EntityHandle s = default;
            for (int t = 0; t < 2000 && s.Generation == 0; t++)
            {
                sim.Tick();
                s = Own(w, Keep);
            }
            Assert.True(s.Generation != 0, $"no Keep site, round {r}");
            cells[r] = w.Buildings.Cell[s.Index];
            Assert.True(w.Buildings.Free(s));
        }
        EntityHandle site = default;
        for (int t = 0; t < 2000 && site.Generation == 0; t++)
        {
            sim.Tick();
            site = Own(w, Keep);
        }
        int n = AtAnchor(log, ai, razed, anchor);
        _out.WriteLine($"razed sites at {cells[0]}, {cells[1]}; third site {(site.Generation == 0 ? -1 : w.Buildings.Cell[site.Index])}; anchor {anchor}; {n} Keep Builds at the anchor; anchor bad {ai.Board.IsBad(Keep, anchor)}");
        Assert.Equal(anchor, cells[0]);
        Assert.Equal(anchor, cells[1]);
        Assert.True(site.Generation != 0, "no third Keep site");
        Assert.True(n <= 3, $"{n} Keep Builds at the anchor");
        for (int k = 0; k < 10; k++) NextThink(sim);
        Assert.Equal(site, Own(w, Keep)); // one hall, no second Build
    }

    /// <summary>
    /// The flag on a hall that stands again: the parked unit leaves while the flagged retry is in flight, so the retry is
    /// accepted at the old anchor; the economy's house rule then judges the flagged order (the flag is ignored there), the
    /// anchor carries no mark, and no further Keep Build is sent. A save/load taken at the judge think follows the original.
    /// </summary>
    [Fact]
    public void TheParkedUnitLeaves_WhileTheFlaggedRetryIsInFlight_TheHallStandsAtTheOldAnchor()
    {
        Simulation sim = Scene();
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        List<Command> log = AiScenes.Log(sim, 0);
        for (int k = 0; k < 10; k++) NextThink(sim);
        EntityHandle hall = Own(w, Keep);
        int anchor = w.Buildings.Cell[hall.Index];
        Rts.Sim.Combat.CombatSystem.FootprintRect(w, Keep, anchor, out Vector2 min, out Vector2 max);
        int razed = sim.TickNumber;
        Assert.True(w.Buildings.Free(hall));
        EntityHandle parked = CombatScenes.Place(sim, 1, CombatScenes.HeavyInfantry, (min + max) * 0.5f);
        NextThink(sim);
        NextThink(sim);
        Assert.Equal(1, ai.Board.HousePending.Slot);
        Assert.Equal(anchor, ai.Board.HousePending.Cell);
        w.Units.Free(parked); // gone before the retry applies
        byte[] bytes = SaveTestRun.Save(sim);
        Simulation load = SaveTestRun.Load(s_workers.Value, bytes);
        for (int t = 0; t < 10 * ai.ThinkTicks; t++)
        {
            sim.Tick();
            load.Tick();
            Assert.True(sim.StateHash() == load.StateHash(), $"diverged at tick {sim.TickNumber}");
        }
        EntityHandle site = Own(w, Keep);
        Assert.True(site.Generation != 0, "no Keep site");
        Assert.Equal(anchor, w.Buildings.Cell[site.Index]);
        Assert.False(ai.Board.IsBad(Keep, anchor));
        Assert.Equal(0, ai.Board.HousePending.Slot); // judged and cleared, or a fresh house Build
        Assert.Equal(2, AtAnchor(log, ai, razed, anchor));
    }
}
