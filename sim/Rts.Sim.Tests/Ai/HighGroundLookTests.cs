using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Tests.Save;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-4d slice 2, criteria 3 and 5 (docs/03 "Implementation (M5-4d slice 2)"; docs/02 "High ground": the AI scouts ramps
/// before attacking up them). The scene is the flat AI scene's 64-cell map with player 1's base on a level-1 plateau
/// (x 40-62, y 14-46) reached by one ramp up from the west (x 36-39, y 28-33); player 0 (the Malazan AI) on level 0 knows
/// player 1's hall as a ghost (a billet of its own on the plateau saw it, then went). Combat off: the command log is checked.
/// </summary>
public class HighGroundLookTests
{
    private const int PlateauX0 = 40, PlateauY0 = 14, PlateauY1 = 46, RampX0 = 36, RampY0 = 28, RampY1 = 33;

    private static int Heavy => TestSim.Data.FindUnit("malazan_heavy_infantry");

    private static readonly Lazy<GameData> s_data = new(() => AiScenes.DataWithAi("malazan", root =>
    {
        AiScenes.Opening(root)["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1));
        AiScenes.Opening(root)["attack"] = new JsonObject { ["minArmyPop"] = 1, ["minArmyCost"] = 0 };
    }));

    /// <summary>The two-level map (see the type's summary): the ramp rises eastward a fifth of a level a cell.</summary>
    internal static Heightmap PlateauMap()
    {
        int n = AiScenes.FlatSize;
        var levels = new byte[n * n];
        var elevations = new float[n * n];
        for (int y = PlateauY0; y <= PlateauY1; y++)
            for (int x = PlateauX0; x < n - 1; x++)
            {
                levels[y * n + x] = 1;
                elevations[y * n + x] = MapConstants.LevelHeight;
            }
        for (int y = RampY0; y <= RampY1; y++)
            for (int x = RampX0; x < PlateauX0; x++)
                elevations[y * n + x] = MapConstants.LevelHeight * (x - RampX0 + 1) / (PlateauX0 - RampX0 + 1);
        return new Heightmap(n, n, levels, elevations);
    }

    /// <summary>The scene; <paramref name="armyUp"/> puts player 0's four Heavy Infantry on the plateau instead of at home.</summary>
    private static Simulation Scene(out EntityHandle hall, out List<EntityHandle> army, bool armyUp = false, PlayerViewOracle.Tally? oracle = null)
    {
        // With the oracle the map is left unexplored (its node memory follows the fog from tick 0; the billet still sees the hall).
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), s_data.Value, map: PlateauMap(),
            explored: oracle == null);
        World w = sim.World;
        if (oracle != null) sim.AiOf(0)!.ThoughtForTests = a => PlayerViewOracle.Check(w, a.View, 0, oracle, $"tick {a.Now}");
        hall = default;
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 1) hall = new EntityHandle(k, w.Buildings.Generation[k]);
        Assert.Equal(1, sim.World.NavGrid.LevelAt(AiScenes.HallAt(1).X, AiScenes.HallAt(1).Y));
        Assert.True(w.Buildings.Spawn(0, TestSim.Data.FindBuilding("malazan_billet"), 31 * AiScenes.FlatSize + 46, out EntityHandle billet));
        Tick(sim, oracle);
        Assert.True(w.Buildings.Free(billet));
        do Tick(sim, oracle); while (sim.TickNumber % 4 != 2); // through a fog update without the billet
        AiPlayer ai = sim.AiOf(0)!;
        Assert.True(ai.View.TryGetGhost(hall.Index, out _), "precondition: the hall is a ghost");
        army = new List<EntityHandle>();
        for (int k = 0; k < 4; k++)
            army.Add(CombatScenes.Place(sim, 0, Heavy, armyUp ? w.NavGrid.CellCenter(44 + k, 20) : w.NavGrid.CellCenter(18 + k, 28)));
        return sim;
    }

    /// <summary>One tick; with <paramref name="oracle"/>, QA's node oracle observes both players' fog update (it is stateful: every tick from the first).</summary>
    private static void Tick(Simulation sim, PlayerViewOracle.Tally? oracle)
    {
        sim.Tick();
        if (oracle == null) return;
        for (int p = 0; p < 2; p++) PlayerViewOracle.NodeOracle.For(sim.World).Observe(sim.World, p, oracle, $"tick {sim.TickNumber} player {p}");
    }

    private static void NextThink(Simulation sim)
    {
        int think = sim.AiOf(0)!.ThinkTicks;
        do sim.Tick(); while ((sim.TickNumber - 1) % think != 0);
    }

    private static Vector2 Centre(Simulation sim, EntityHandle building) =>
        sim.AiOf(0)!.BuildingCentre(sim.World.Buildings.TypeId[building.Index], sim.World.Buildings.Cell[building.Index]);

    /// <summary>The ramp top nearest the hall's centre: the plateau's west column beside the ramp, the nearer of rows 31 / 32 to the centre's row 32 (a tie at 0.5 cell: row order keeps 31).</summary>
    private static Vector2 ExpectedLook(Simulation sim) => sim.World.NavGrid.CellCenter(PlateauX0, 31);

    /// <summary>
    /// Criterion 3: the wave on the upper-level hall is first sent <c>AttackMove</c> to the ramp top, a level-1 cell beside a
    /// ramp cell, every member once; then, once a member is within <c>ai.rallyReach</c> of it or the hall's cell is in sight,
    /// to the hall's centre. No member is sent to the hall before that.
    /// </summary>
    [Fact]
    public void AWaveOnAnUpperLevelTarget_GoesToTheRampTopFirst_ThenToTheTarget()
    {
        Simulation sim = Scene(out EntityHandle hall, out List<EntityHandle> army);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        List<Command> log = AiScenes.Log(sim, 0);
        NextThink(sim);
        Vector2 look = ExpectedLook(sim);
        List<Command> first = log.Where(c => c.Kind == CommandKind.AttackMove).ToList();
        Assert.Equal(army.OrderBy(h => h.Index), first.Select(c => c.Unit));
        Assert.All(first, c => Assert.Equal(look, c.Position));
        Assert.True(w.NavGrid.WorldToCell(look, out int lx, out int ly));
        Assert.Equal(1, w.NavGrid.LevelAt(lx, ly));
        Assert.True((w.NavGrid.FlagsAt(lx - 1, ly) & NavFlags.Ramp) != 0, "the look point is not beside the ramp");
        Assert.True(MilitaryManager.Looking(ai));
        int handover = -1;
        for (int t = 0; t < 3000 && handover < 0; t++)
        {
            sim.Tick();
            if (!MilitaryManager.Looking(ai)) handover = sim.TickNumber;
        }
        Assert.True(handover > 0, "never left the look point");
        float reach = ai.Rules.RallyReach;
        bool near = army.Any(h => Vector2.Distance(w.Units.Position[h.Index], look) <= reach + 3f); // within reach at the handover think (a tick or so of walking since)
        bool seen = ai.View.IsVisible(ai.Board.WaveTargetCell);
        Assert.True(near || seen, "handed over neither at the look point nor with the hall in sight");
        Assert.Equal(Centre(sim, hall), ai.Board.WaveTargetPoint);
        NextThink(sim);
        Command firstToHall = log.First(c => c.Kind == CommandKind.AttackMove && c.Position == Centre(sim, hall));
        Assert.True(firstToHall.Tick >= handover);
        Assert.DoesNotContain(log, c => c.Kind == CommandKind.AttackMove && c.Position != look && c.Position != Centre(sim, hall));
        Assert.All(log.Where(c => c.Kind == CommandKind.AttackMove && c.Position == look), c => Assert.True(c.Tick <= handover));
        Assert.Equal(army.OrderBy(h => h.Index), log.Where(c => c.Kind == CommandKind.AttackMove && c.Position == Centre(sim, hall)).Take(4).Select(c => c.Unit).OrderBy(h => h.Index));
    }

    /// <summary>Criterion 3: an army already on the target's level gets no look point: its first <c>AttackMove</c> is the hall's centre.</summary>
    [Fact]
    public void AWaveAtTheTargetsLevel_GoesStraightToTheTarget()
    {
        Simulation sim = Scene(out EntityHandle hall, out List<EntityHandle> army, armyUp: true);
        AiPlayer ai = sim.AiOf(0)!;
        List<Command> log = AiScenes.Log(sim, 0);
        NextThink(sim);
        List<Command> first = log.Where(c => c.Kind == CommandKind.AttackMove).ToList();
        Assert.Equal(4, first.Count);
        Assert.All(first, c => Assert.Equal(Centre(sim, hall), c.Position));
        Assert.False(MilitaryManager.Looking(ai));
    }

    /// <summary>The rule's own cases: no look point from the same level or from above, for a level-0 target, nor anywhere on the flat scene.</summary>
    [Fact]
    public void NoLookPoint_ForASameOrLowerLevel_ASeenTarget_OrAFlatMap()
    {
        Simulation sim = Scene(out EntityHandle hall, out _);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        int type = w.Buildings.TypeId[hall.Index], cell = w.Buildings.Cell[hall.Index];
        Assert.True(MilitaryManager.LookPoint(ai, type, cell, 0, out Vector2 look));
        Assert.Equal(ExpectedLook(sim), look);
        Assert.False(MilitaryManager.LookPoint(ai, type, cell, 1, out _)); // from the same level
        Assert.False(MilitaryManager.LookPoint(ai, type, cell, 2, out _)); // from above
        (int hx, int hy) = AiScenes.HallAt(0);
        Assert.False(MilitaryManager.LookPoint(ai, TestSim.Data.FindBuilding("malazan_garrison_keep"), hy * AiScenes.FlatSize + hx, 0, out _)); // level 0
        Simulation flat = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), s_data.Value);
        (int ex, int ey) = AiScenes.HallAt(1);
        Assert.False(MilitaryManager.LookPoint(flat.AiOf(0)!, type, ey * AiScenes.FlatSize + ex, 0, out _));
    }

    /// <summary>
    /// The rule on the shipped maps (static terrain only): Vathar Crossing, a building on the south bank from troops down
    /// in the riverbed looks first at the top of a south-bank ford; Pale Hills, a building on a hill (level 1) or its knoll
    /// (level 2) from the camps' level looks at a ramp top on its own hill or knoll.
    /// </summary>
    [Theory]
    [InlineData("vathar_crossing", 195, 193, 0)]
    [InlineData("pale_hills", 80, 80, 0)]
    [InlineData("pale_hills", 91, 91, 0)]
    public void OnTheShippedMaps_TheLookPoint_IsARampTopOnTheTargetsOwnGround(string map, int x, int y, int fromLevel)
    {
        Simulation sim = MapFileTests.MapMatch(map, 1, false, out _);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        AiPlayer ai = sim.AiOf(0)!;
        int type = TestSim.Data.FindBuilding("whirlwind_tent"), level = g.LevelAt(x, y);
        Assert.True(level > fromLevel, $"({x}, {y}) is level {level}");
        int cell = y * g.Width + x;
        Assert.True(MilitaryManager.LookPoint(ai, type, cell, fromLevel, out Vector2 look));
        Assert.True(g.WorldToCell(look, out int lx, out int ly));
        Assert.Equal(level, g.LevelAt(lx, ly));
        Assert.Equal(w.Plateaus.At(cell), w.Plateaus.At(ly * g.Width + lx));
        bool beside = false;
        foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            beside |= (g.FlagsAt(lx + dx, ly + dy) & NavFlags.Ramp) != 0 && g.LevelAt(lx + dx, ly + dy) == level - 1;
        Assert.True(beside, $"look ({lx}, {ly}) is not a ramp top");
        if (map == "vathar_crossing") Assert.True(ly >= 144, $"look row {ly}: not the south bank");
    }

    /// <summary>
    /// Criterion 5: a save mid-look (the look point as <c>WaveTargetPoint</c>) loads into a sim that ticks hash-equal for 1,000
    /// ticks, the seats field for field equal; no new field (the look is read from the wave's target fields), format 6.
    /// </summary>
    [Fact]
    public void ASaveMidLook_FollowsTheRun_For1000Ticks()
    {
        Simulation sim = Scene(out _, out _);
        AiPlayer ai = sim.AiOf(0)!;
        NextThink(sim);
        NextThink(sim);
        Assert.True(MilitaryManager.Looking(ai));
        byte[] bytes = SaveTestRun.Save(sim);
        Assert.Equal(Rts.Sim.Save.SaveFormat.HuntMaplessVersion, BitConverter.ToInt32(bytes, Rts.Sim.Save.SaveFormat.MagicLength));
        Simulation load = SaveTestRun.Load(s_data.Value, bytes);
        Assert.Equal(bytes, SaveTestRun.Save(load));
        Assert.True(MilitaryManager.Looking(load.AiOf(0)!));
        SaveTestRun.AssertSameState(sim, load, "mid-look");
        for (int t = 0; t < 1000; t++)
        {
            sim.Tick();
            load.Tick();
            Assert.True(sim.StateHash() == load.StateHash(), $"diverged at tick {sim.TickNumber}");
        }
        SaveTestRun.AssertSameState(sim, load, "mid-look + 1,000");
        Assert.False(MilitaryManager.Looking(ai)); // it went on to the target within the run
    }

    /// <summary>
    /// Criterion 4 (scene row): at every think of the looking seat, from the wave's start through the look to the hand-over
    /// and on toward the hall, QA's <see cref="PlayerViewOracle"/> finds its view equal to the oracle's (no leak).
    /// </summary>
    [Fact]
    public void TheLookingSeat_SeesOnlyWhatItsFogGrants()
    {
        var tally = new PlayerViewOracle.Tally();
        Simulation sim = Scene(out _, out _, oracle: tally);
        AiPlayer ai = sim.AiOf(0)!;
        bool looked = false, handed = false;
        for (int t = 0; t < 2000 && !handed; t++)
        {
            Tick(sim, tally);
            looked |= MilitaryManager.Looking(ai);
            handed = looked && !MilitaryManager.Looking(ai);
        }
        Assert.True(looked && handed, $"looked {looked}, handed over {handed}");
        Assert.True(tally.Leaks == 0 && tally.First.Length == 0, tally.First + " | " + tally);
    }
}
