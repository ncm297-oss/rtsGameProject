using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA, M5-4d slice 2 (session 2026-11-07-0236): the rebuild and high-ground look rules attacked at their edges on the flat
/// AI scene (combat off: the command log is what is checked). Rebuild: a hall razed with no worker, with one, twice, its
/// site razed as it goes up; a Barracks razed each time it is placed (no Build spam). Look: a level-2 target reached from
/// level 0 over two ramps; every ramp top of the target's ground blocked by a forest (the "every member stuck" rule);
/// the target razed while the wave walks to the look point; a save at the look point and one with the hall banked for.
/// </summary>
public class RebuildLookQaTests
{
    private readonly ITestOutputHelper _out;

    public RebuildLookQaTests(ITestOutputHelper output) => _out = output;

    private static int Barracks => TestSim.Data.FindBuilding("malazan_barracks");

    private static int Keep => TestSim.Data.FindBuilding("malazan_garrison_keep");

    private static int Heavy => TestSim.Data.FindUnit("malazan_heavy_infantry");

    private static readonly Lazy<GameData> s_workers = new(() => AiScenes.DataWithAi("malazan", root =>
        AiScenes.Opening(root)["buildOrder"] = AiScenes.BuildOrder(("time", 0, "train", "malazan_laborer", 20))));

    private static readonly Lazy<GameData> s_barracks = new(() => AiScenes.DataWithAi("malazan", root =>
        AiScenes.Opening(root)["buildOrder"] = AiScenes.BuildOrder(("time", 0, "build", "malazan_barracks", 0), ("time", 3600, "train", "malazan_laborer", 1))));

    private static readonly Lazy<GameData> s_look = new(() => AiScenes.DataWithAi("malazan", root =>
    {
        AiScenes.Opening(root)["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1));
        AiScenes.Opening(root)["attack"] = new JsonObject { ["minArmyPop"] = 1, ["minArmyCost"] = 0 };
    }));

    private static Simulation Scene(GameData data)
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), data);
        BuildMaps.Give(sim, 0, 20000, 20000);
        return sim;
    }

    private static void NextThink(Simulation sim)
    {
        int think = sim.AiOf(0)!.ThinkTicks;
        do sim.Tick(); while ((sim.TickNumber - 1) % think != 0);
    }

    private static EntityHandle Own(World w, int player, int type)
    {
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == player && w.Buildings.TypeId[k] == type) return new EntityHandle(k, w.Buildings.Generation[k]);
        return default;
    }

    private static int Count(World w, int player, int type)
    {
        int n = 0;
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == player && w.Buildings.TypeId[k] == type) n++;
        return n;
    }

    private static List<EntityHandle> Units(World w, int player)
    {
        var list = new List<EntityHandle>();
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == player) list.Add(new EntityHandle(i, w.Units.Generation[i]));
        return list;
    }

    private static EntityHandle UntilPlaced(Simulation sim, int type, bool finished, int limit = 8000)
    {
        World w = sim.World;
        for (int t = 0; t < limit; t++)
        {
            EntityHandle b = Own(w, 0, type);
            if (b.Generation != 0 && (!finished || !w.Buildings.UnderConstruction[b.Index])) return b;
            sim.Tick();
        }
        Assert.Fail($"no {(finished ? "finished " : "")}{TestSim.Data.Buildings[type].Key} by tick {sim.TickNumber}");
        return default;
    }

    // ---------------------------------------------------------------- rebuild

    /// <summary>The hall razed and every unit of the seat gone: nothing to build with, so nothing is sent (no exception, no Build, no Train).</summary>
    [Fact]
    public void AHallRazed_WithNoWorkerLeft_SendsNothing()
    {
        Simulation sim = Scene(s_workers.Value);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        for (int k = 0; k < 10; k++) NextThink(sim);
        List<Command> log = AiScenes.Log(sim, 0);
        EntityHandle hall = Own(w, 0, Keep);
        int anchor = w.Buildings.Cell[hall.Index];
        Assert.True(w.Buildings.Free(hall));
        foreach (EntityHandle u in Units(w, 0)) w.Units.Free(u);
        for (int k = 0; k < 30; k++) NextThink(sim);
        Assert.DoesNotContain(log, c => c.Kind is CommandKind.Build or CommandKind.Train);
        Assert.Equal(0, Count(w, 0, Keep));
        Assert.True(ai.LostHallCell == anchor || ai.LostHallCell == -1, $"LostHallCell {ai.LostHallCell}, anchor {anchor}");
    }

    /// <summary>The hall razed with a single worker left: that worker rebuilds it at the old anchor, and the hall finishes there.</summary>
    [Fact]
    public void AHallRazed_WithOneWorkerLeft_IsRebuiltAtItsAnchor()
    {
        Simulation sim = Scene(s_workers.Value);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        for (int k = 0; k < 10; k++) NextThink(sim);
        List<EntityHandle> units = Units(w, 0);
        for (int k = 1; k < units.Count; k++) w.Units.Free(units[k]);
        EntityHandle hall = Own(w, 0, Keep);
        int anchor = w.Buildings.Cell[hall.Index];
        List<Command> log = AiScenes.Log(sim, 0);
        int razed = sim.TickNumber;
        Assert.True(w.Buildings.Free(hall));
        EntityHandle again = UntilPlaced(sim, Keep, finished: true, limit: 20000);
        Assert.Equal(anchor, w.Buildings.Cell[again.Index]);
        Command build = Assert.Single(log, c => c.Kind == CommandKind.Build && c.TypeId == Keep);
        Assert.Equal(ai.CellCentre(anchor), build.Position);
        _out.WriteLine($"razed {razed}, Build sent {build.Tick}, finished by {sim.TickNumber}");
    }

    /// <summary>
    /// Razed twice, and its site razed as it went up: each loss is followed by one Keep Build at the same anchor (no spam),
    /// and the last stands at that anchor.
    /// </summary>
    [Fact]
    public void AHallRazedTwice_AndItsSiteRazed_IsRebuiltOnceALoss_AtTheSameAnchor()
    {
        Simulation sim = Scene(s_workers.Value);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        for (int k = 0; k < 10; k++) NextThink(sim);
        EntityHandle hall = Own(w, 0, Keep);
        int anchor = w.Buildings.Cell[hall.Index];
        List<Command> log = AiScenes.Log(sim, 0);
        int losses = 0;
        Assert.True(w.Buildings.Free(hall)); losses++;
        EntityHandle site = UntilPlaced(sim, Keep, finished: false);
        // Two thinks on, so its Build is judged accepted first (a site razed before the judge reads as a refused Build and
        // marks the anchor, sending the next rebuild a cell or so off: the judge's rule, noted in the QA report).
        NextThink(sim);
        NextThink(sim);
        Assert.True(w.Buildings.Alive[site.Index] && w.Buildings.UnderConstruction[site.Index], "precondition: the site still stands unfinished");
        Assert.Equal(anchor, w.Buildings.Cell[site.Index]);
        Assert.True(w.Buildings.Free(site)); losses++;
        EntityHandle second = UntilPlaced(sim, Keep, finished: true, limit: 20000);
        Assert.Equal(anchor, w.Buildings.Cell[second.Index]);
        Assert.True(w.Buildings.Free(second)); losses++;
        EntityHandle third = UntilPlaced(sim, Keep, finished: true, limit: 20000);
        Assert.Equal(anchor, w.Buildings.Cell[third.Index]);
        for (int k = 0; k < 10; k++) NextThink(sim);
        List<Command> builds = log.Where(c => c.Kind == CommandKind.Build && c.TypeId == Keep).ToList();
        Assert.Equal(losses, builds.Count);
        Assert.All(builds, c => Assert.Equal(ai.CellCentre(anchor), c.Position));
        Assert.Equal(1, Count(w, 0, Keep));
        Assert.Equal(anchor, ai.TownHallCell);
    }

    /// <summary>
    /// Rebuild loop: the Barracks razed each time its site appears, eight times. Each loss draws exactly one Barracks Build
    /// (none while one is in flight or standing), the step ends done, and the seat ends with one Barracks.
    /// </summary>
    [Fact]
    public void ABarracksRazedEachTimeItIsPlaced_DrawsOneBuildALoss()
    {
        Simulation sim = Scene(s_barracks.Value);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        List<Command> log = AiScenes.Log(sim, 0);
        const int Losses = 8;
        for (int i = 0; i < Losses; i++)
        {
            EntityHandle b = UntilPlaced(sim, Barracks, finished: false);
            Assert.True(w.Buildings.Free(b));
        }
        UntilPlaced(sim, Barracks, finished: true, limit: 20000);
        for (int k = 0; k < 10; k++) NextThink(sim);
        int builds = log.Count(c => c.Kind == CommandKind.Build && c.TypeId == Barracks);
        _out.WriteLine($"{builds} Barracks Builds for {Losses} losses; step done {ai.Board.StepDone[0]}, queued {ai.Board.StepQueued[0]}");
        Assert.Equal(Losses + 1, builds);
        Assert.True(ai.Board.StepDone[0]);
        Assert.Equal(1, Count(w, 0, Barracks));
    }

    /// <summary>
    /// Save with the hall rebuild banked for (no Keep, its cost not in the bank, the reserve the hall's): the load remembers
    /// the anchor (the -(anchor + 2) encoding of <c>TownHallCell</c>), follows the run hash for hash for 1,000 ticks after the
    /// bank is filled in both, and both rebuild at the old anchor.
    /// </summary>
    [Fact]
    public void ASaveWithTheHallBankedFor_RemembersTheAnchor_AndFollowsTheRun()
    {
        GameData data = s_workers.Value;
        Simulation sim = Scene(data);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        for (int k = 0; k < 10; k++) NextThink(sim);
        EntityHandle hall = Own(w, 0, Keep);
        int anchor = w.Buildings.Cell[hall.Index];
        Assert.True(w.Buildings.Free(hall));
        BuildMaps.SetTotals(sim, 0, 0, 0);
        bool banked = false;
        ai.ThoughtForTests = a => banked |= a.HallReserved;
        NextThink(sim);
        NextThink(sim);
        Assert.True(banked, "the hall is not banked for");
        Assert.Equal(anchor, ai.LostHallCell);
        byte[] bytes = SaveTestRun.Save(sim);
        Simulation load = SaveTestRun.Load(data, bytes);
        Assert.Equal(anchor, load.AiOf(0)!.LostHallCell);
        SaveTestRun.AssertSameState(sim, load, "hall banked for");
        BuildingDef keep = data.Buildings[Keep];
        BuildMaps.Give(sim, 0, keep.CostGold, keep.CostWood);
        BuildMaps.Give(load, 0, keep.CostGold, keep.CostWood);
        for (int t = 0; t < 1000; t++)
        {
            sim.Tick();
            load.Tick();
            Assert.True(sim.StateHash() == load.StateHash(), $"diverged at tick {sim.TickNumber}");
        }
        Assert.Equal(anchor, w.Buildings.Cell[Own(w, 0, Keep).Index]);
        Assert.Equal(anchor, load.World.Buildings.Cell[Own(load.World, 0, Keep).Index]);
    }

    // ---------------------------------------------------------------- look

    /// <summary>
    /// Player 1's base on a level-2 knoll (x 46-61, y 18-42) inside a level-1 plateau (x 40-62, y 14-46): one ramp 0->1 at
    /// x 36-39, y 28-33 rising east, and one ramp 1->2 at x 42-45 on the same rows. Player 0 (level 0, x 8) knows the hall
    /// as a ghost (a billet on the knoll saw it, then went).
    /// </summary>
    internal static Heightmap KnollMap()
    {
        int n = AiScenes.FlatSize;
        var levels = new byte[n * n];
        var elevations = new float[n * n];
        float lh = MapConstants.LevelHeight;
        for (int y = 14; y <= 46; y++)
            for (int x = 40; x < n - 1; x++)
            {
                byte l = (byte)(x >= 46 && x <= 61 && y >= 18 && y <= 42 ? 2 : 1);
                levels[y * n + x] = l;
                elevations[y * n + x] = l * lh;
            }
        for (int y = 28; y <= 33; y++)
        {
            for (int x = 36; x < 40; x++) elevations[y * n + x] = lh * (x - 36 + 1) / 5f; // level 0, rising
            for (int x = 42; x < 46; x++)
            {
                levels[y * n + x] = 1;
                elevations[y * n + x] = lh + lh * (x - 42 + 1) / 5f; // level 1, rising
            }
        }
        return new Heightmap(n, n, levels, elevations);
    }

    private static Simulation LookScene(Heightmap map, int billetCell, out EntityHandle hall, out List<EntityHandle> army, Action<Simulation>? before = null)
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), s_look.Value, map: map);
        World w = sim.World;
        hall = default;
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 1) hall = new EntityHandle(k, w.Buildings.Generation[k]);
        Assert.True(w.Buildings.Spawn(0, TestSim.Data.FindBuilding("malazan_billet"), billetCell, out EntityHandle billet));
        sim.Tick();
        Assert.True(w.Buildings.Free(billet));
        do sim.Tick(); while (sim.TickNumber % 4 != 2);
        Assert.True(sim.AiOf(0)!.View.TryGetGhost(hall.Index, out _), "precondition: the hall is a ghost");
        before?.Invoke(sim);
        army = new List<EntityHandle>();
        for (int k = 0; k < 4; k++) army.Add(CombatScenes.Place(sim, 0, Heavy, w.NavGrid.CellCenter(18 + k, 28)));
        return sim;
    }

    /// <summary>
    /// A level-2 target from level 0, two ramps between: the first point is a ramp top of the knoll (level 2, beside a level-1
    /// ramp cell), the wave climbs both ramps to it, hands over, and is sent on to the hall.
    /// </summary>
    [Fact]
    public void ALevel2Target_FromLevel0_LooksFromTheKnollsRampTop_ThenGoesOn()
    {
        Simulation sim = LookScene(KnollMap(), 25 * AiScenes.FlatSize + 57, out EntityHandle hall, out List<EntityHandle> army);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        NavGrid g = w.NavGrid;
        Assert.Equal(2, g.LevelAt(52, 30));
        List<Command> log = AiScenes.Log(sim, 0);
        NextThink(sim);
        Assert.True(MilitaryManager.Looking(ai), "no look point for a level-2 target from level 0");
        Vector2 look = ai.Board.WaveTargetPoint;
        Assert.True(g.WorldToCell(look, out int lx, out int ly));
        Assert.Equal(2, g.LevelAt(lx, ly));
        Assert.Equal(w.Plateaus.At(w.Buildings.Cell[hall.Index]), w.Plateaus.At(ly * g.Width + lx));
        Assert.True((g.FlagsAt(lx - 1, ly) & NavFlags.Ramp) != 0 && g.LevelAt(lx - 1, ly) == 1, $"look ({lx}, {ly}) is not the knoll's ramp top");
        int handover = -1;
        float closest = float.MaxValue;
        for (int t = 0; t < 4000 && handover < 0; t++)
        {
            sim.Tick();
            foreach (EntityHandle h in army)
                if (w.Units.Alive[h.Index]) closest = Math.Min(closest, Vector2.Distance(w.Units.Position[h.Index], look));
            if (!MilitaryManager.Looking(ai)) handover = sim.TickNumber;
        }
        _out.WriteLine($"look ({lx}, {ly}); handed over at {handover}; closest member {closest:F1} m");
        Assert.True(handover > 0, "never handed over");
        Assert.True(ai.Board.WaveActive, "the wave ended instead of going on");
        Assert.True(closest <= ai.Rules.RallyReach + 3f || ai.View.IsVisible(ai.Board.WaveTargetCell), "handed over far from the look point with the hall unseen");
        for (int k = 0; k < 3; k++) NextThink(sim);
        Vector2 centre = ai.BuildingCentre(w.Buildings.TypeId[hall.Index], w.Buildings.Cell[hall.Index]);
        Assert.Contains(log, c => c.Kind == CommandKind.AttackMove && c.Position == centre && c.Tick >= handover);
    }

    /// <summary>
    /// Every ramp top of the target's ground (and the ramp foot) covered by trees: the plateau can't be climbed. The wave must
    /// not stand waiting for the look point for good: it hands over (every member stuck) or ends, within the wave cooldown
    /// plus slack, and the AI does not freeze (it keeps sending orders or rallies home).
    /// </summary>
    [Fact]
    public void EveryRampTopInAForest_TheWaveHandsOverOrEnds_NotStuckLooking()
    {
        Simulation sim = LookScene(HighGroundLookTests.PlateauMap(), 31 * AiScenes.FlatSize + 46, out EntityHandle hall, out _, s =>
        {
            int tree = TestSim.Data.FindResource("tree");
            for (int y = 28; y <= 33; y++)
                for (int x = 40; x <= 41; x++)
                    Assert.True(s.World.Resources.Spawn(tree, y * AiScenes.FlatSize + x, TestSim.Data.Rules.TreeWood, out _), $"tree at ({x}, {y})");
        });
        AiPlayer ai = sim.AiOf(0)!;
        NextThink(sim);
        Assert.True(MilitaryManager.Looking(ai), "precondition: the wave looks first");
        int lookingSince = sim.TickNumber, longest = 0, current = 0;
        bool handed = false, ended = false;
        int limit = ai.Rules.WaveCooldownTicks * 3 + 4000;
        for (int t = 0; t < limit; t++)
        {
            sim.Tick();
            if (MilitaryManager.Looking(ai)) { current++; longest = Math.Max(longest, current); }
            else
            {
                if (ai.Board.WaveActive) handed = true; else ended = true;
                current = 0;
            }
        }
        _out.WriteLine($"looking from {lookingSince}: longest stretch {longest} ticks; handed {handed}, ended {ended}; waves {ai.Board.WavesStarted}");
        Assert.True(handed || ended, "the wave is still waiting for an unreachable look point");
        Assert.True(longest <= ai.Rules.WaveCooldownTicks + 2000, $"looked {longest} ticks in a row at an unreachable point");
    }

    /// <summary>
    /// The ramp packed with the enemy's own units (24 of player 1's Heavy Infantry on its cells and its foot; combat off, so
    /// nobody clears them): the wave must not wait for the look point for good. It reaches it through the crowd, hands
    /// over (every member stuck), or ends; no stretch on the look point outlasts the wave cooldown plus slack.
    /// </summary>
    [Fact]
    public void TheRampPackedWithEnemyUnits_TheWaveHandsOverOrEnds_NotStuckLooking()
    {
        Simulation sim = LookScene(HighGroundLookTests.PlateauMap(), 31 * AiScenes.FlatSize + 46, out _, out _, s =>
        {
            for (int y = 28; y <= 33; y++)
                for (int x = 35; x <= 38; x++)
                    CombatScenes.Place(s, 1, Heavy, s.World.NavGrid.CellCenter(x, y));
        });
        AiPlayer ai = sim.AiOf(0)!;
        NextThink(sim);
        Assert.True(MilitaryManager.Looking(ai), "precondition: the wave looks first");
        int longest = 0, current = 0;
        bool handed = false, ended = false;
        for (int t = 0; t < ai.Rules.WaveCooldownTicks * 3 + 4000; t++)
        {
            sim.Tick();
            if (MilitaryManager.Looking(ai)) { current++; longest = Math.Max(longest, current); }
            else
            {
                if (ai.Board.WaveActive) handed = true; else ended = true;
                current = 0;
            }
        }
        _out.WriteLine($"longest stretch looking {longest} ticks; handed {handed}, ended {ended}; waves {ai.Board.WavesStarted}");
        Assert.True(handed || ended, "the wave is still waiting behind the crowd");
        Assert.True(longest <= ai.Rules.WaveCooldownTicks + 2000, $"looked {longest} ticks in a row behind the crowd");
    }

    /// <summary>
    /// The target razed while the wave walks to the look point (out of the seat's sight: it holds a ghost). The wave must
    /// leave the dead hall within a bounded time: it goes on, sees the ghost gone, and hunts (player 1 has no other
    /// building) rather than looking at it for good.
    /// </summary>
    [Fact]
    public void TheTargetRazed_WhileLooking_TheWaveMovesOnToAHunt()
    {
        Simulation sim = LookScene(HighGroundLookTests.PlateauMap(), 31 * AiScenes.FlatSize + 46, out EntityHandle hall, out List<EntityHandle> army);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        NextThink(sim);
        Assert.True(MilitaryManager.Looking(ai));
        Vector2 look = ai.Board.WaveTargetPoint;
        // Raze when the first member is within twice the reach of the look point (it is at the look point).
        for (int t = 0; t < 4000; t++)
        {
            sim.Tick();
            if (army.Any(h => w.Units.Alive[h.Index] && Vector2.Distance(w.Units.Position[h.Index], look) <= 2 * ai.Rules.RallyReach)) break;
        }
        int razed = sim.TickNumber;
        Assert.True(w.Buildings.Free(hall));
        int leftAt = -1;
        for (int t = 0; t < 4000 && leftAt < 0; t++)
        {
            sim.Tick();
            if (!ai.Board.WaveActive || ai.Board.Hunting || ai.Board.WaveTarget != hall) leftAt = sim.TickNumber;
        }
        _out.WriteLine($"razed {razed}; left the hall at {leftAt}; hunting {ai.Board.Hunting}, wave {ai.Board.WaveActive}");
        Assert.True(leftAt > 0, "the wave kept the razed hall as its target");
        Assert.False(MilitaryManager.Looking(ai) && ai.Board.WaveTarget == hall);
    }

    /// <summary>A save at the knoll's look point (mid-look, the two-ramp map) loads hash-equal for 1,000 ticks and byte-identical on resave.</summary>
    [Fact]
    public void ASaveMidLook_OnTheKnoll_FollowsTheRun()
    {
        Simulation sim = LookScene(KnollMap(), 25 * AiScenes.FlatSize + 57, out _, out _);
        AiPlayer ai = sim.AiOf(0)!;
        NextThink(sim);
        for (int k = 0; k < 6; k++) NextThink(sim);
        Assert.True(MilitaryManager.Looking(ai), "precondition: still on the way to the look point");
        byte[] bytes = SaveTestRun.Save(sim);
        Simulation load = SaveTestRun.Load(s_look.Value, bytes);
        Assert.Equal(bytes, SaveTestRun.Save(load));
        Assert.True(MilitaryManager.Looking(load.AiOf(0)!));
        for (int t = 0; t < 1000; t++)
        {
            sim.Tick();
            load.Tick();
            Assert.True(sim.StateHash() == load.StateHash(), $"diverged at tick {sim.TickNumber}");
        }
        SaveTestRun.AssertSameState(sim, load, "knoll mid-look + 1,000");
    }
}
