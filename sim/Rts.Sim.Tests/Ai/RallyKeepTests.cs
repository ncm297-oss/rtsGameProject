using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-S14 item E (BUG-1558 items 2 and 3; docs/03 "Implementation (M6-S14)"): the rally point in use is kept while it is
/// still acceptable and within <c>ai.rallyReach</c> of the nominal point (a flickering target no longer moves it), and with no
/// point of the line acceptable the last acceptable one is kept instead of the nominal one. <see cref="RallyPassageTests"/>'
/// flat AI map: player 0's Keep centred at (20, 64) m, the nominal point due east at (32, 64) m; with the wall (columns
/// 17-19, open only at row 32) the nominal point is a passage's mouth and the point moves back to (28, 64) m.
/// </summary>
public class RallyKeepTests(ITestOutputHelper output)
{
    internal static readonly Lazy<GameData> s_quiet = new(() => AiScenes.DataWithAi("malazan", root =>
        AiScenes.SetBuildOrder(AiScenes.Opening(root), AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1)))));

    private static Heightmap Map(bool wall)
    {
        var rows = new string[AiScenes.FlatSize];
        for (int y = 0; y < rows.Length; y++)
        {
            var row = new char[AiScenes.FlatSize];
            for (int x = 0; x < row.Length; x++) row[x] = wall && x >= 17 && x <= 19 && y != 32 ? '1' : '0';
            rows[y] = new string(row);
        }
        return FromRows(rows);
    }

    private static Simulation Scene(bool wall) =>
        AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), s_quiet.Value, map: Map(wall));

    /// <summary>Ticks through player 0's next think (its rally is set by then).</summary>
    internal static void NextThink(Simulation sim)
    {
        int think = sim.AiOf(0)!.ThinkTicks;
        do sim.Tick(); while ((sim.TickNumber - 1) % think != 0);
    }

    private static int Tent => s_quiet.Value.FindBuilding("whirlwind_tent");

    /// <summary>
    /// Criterion 7, the flicker (failed on the base: the point moved every think, 20 versions in 20 thinks): a tent of player 1
    /// north-east of the base, in the sight of a Billet of player 0, stands on one think and is gone the next (freed in
    /// sight: no ghost), for 20 thinks. It is the nearest known enemy building whenever it stands, so the nominal point flips
    /// between (32, 64) m and a point 3 m from it; the point in use stays acceptable and in reach of both, so it never moves.
    /// </summary>
    [Fact]
    public void AFlickeringTarget_DoesNotMoveThePoint_WhileItIsAcceptable()
    {
        Simulation sim = Scene(wall: false);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        // The eye: a Billet of player 0 beside the tent's spot (a building's sight; a soldier would walk to the rally point).
        Assert.True(w.Buildings.Spawn(0, w.Data.FindBuilding("malazan_billet"), 25 * AiScenes.FlatSize + 26, out _));
        NextThink(sim);
        NextThink(sim);
        Vector2 start = ai.Board.Rally;
        int version = ai.Board.RallyVersion;
        Assert.Equal(new Vector2(32f, 64f), start);
        const int anchor = 22 * AiScenes.FlatSize + 30;
        Vector2 towardTent = ai.HallPoint + Vector2.Normalize(ai.BuildingCentre(Tent, anchor) - ai.HallPoint) * ai.Rules.RallyDistance;
        float shift = Vector2.Distance(towardTent, start);
        Assert.InRange(shift, 1f, ai.Rules.RallyReach - 0.5f); // the scene: the flicker moves the nominal point inside the ring
        int stood = 0;
        EntityHandle tent = default;
        for (int k = 0; k < 20; k++)
        {
            if (k % 2 == 0) Assert.True(w.Buildings.Spawn(1, Tent, anchor, out tent));
            else Assert.True(w.Buildings.Free(tent));
            NextThink(sim);
            if (ai.Board.KnownEnemyBuildings > 0) stood++;
        }
        output.WriteLine($"nominal {start} / {towardTent} ({shift:F2} m apart); the tent known on {stood} of 20 thinks; rally {ai.Board.Rally} v{ai.Board.RallyVersion} (was v{version})");
        Assert.True(stood >= 8, "the scene: the tent was not seen on the thinks it stood");
        Assert.Equal(version, ai.Board.RallyVersion);
        Assert.Equal(start, ai.Board.Rally);
        Assert.True(ai.Board.HasRallyKept);
        Assert.Equal(start, ai.Board.RallyKept);
    }

    /// <summary>
    /// Criterion 7's control (green on the base and after): a target that moves the nominal point farther than
    /// <c>ai.rallyReach</c> (a tent due north of the Keep) still moves the point, to that nominal point's line (within reach of it).
    /// </summary>
    [Fact]
    public void ATargetFarOffTheLine_StillMovesThePoint()
    {
        Simulation sim = Scene(wall: false);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        Assert.True(w.Buildings.Spawn(0, w.Data.FindBuilding("malazan_billet"), 13 * AiScenes.FlatSize + 9, out _)); // the eye
        NextThink(sim);
        NextThink(sim);
        int version = ai.Board.RallyVersion;
        const int anchor = 10 * AiScenes.FlatSize + 9;
        Assert.True(w.Buildings.Spawn(1, Tent, anchor, out _));
        NextThink(sim);
        NextThink(sim);
        Vector2 want = ai.HallPoint + Vector2.Normalize(ai.BuildingCentre(Tent, anchor) - ai.HallPoint) * ai.Rules.RallyDistance;
        Assert.True(Vector2.Distance(want, new Vector2(32f, 64f)) > ai.Rules.RallyReach);
        Assert.Equal(version + 1, ai.Board.RallyVersion);
        // To that nominal point, or (its cell a mine's, a passage there) the nearest acceptable point of its line within reach.
        Assert.True(Vector2.Distance(want, ai.Board.Rally) <= ai.Rules.RallyReach, $"rally {ai.Board.Rally}, want {want}");
        Assert.True(Vector2.Distance(new Vector2(32f, 64f), ai.Board.Rally) > ai.Rules.RallyReach);
    }

    /// <summary>
    /// Criterion 7, the fall-back (failed on the base: the point went back to the nominal one, in the passage's mouth): the
    /// point stands at (28, 64) m, back from the mouth; a building of player 0 goes up over it and the one other acceptable
    /// point of the line, (26, 64) m. No point is acceptable now, and the point stays (28, 64) m, the last acceptable one.
    /// </summary>
    [Fact]
    public void WithNoPointAcceptable_TheLastAcceptableOneIsKept()
    {
        (Simulation sim, int version) = ToTheFallBack();
        AiPlayer ai = sim.AiOf(0)!;
        output.WriteLine($"rally {ai.Board.Rally} v{ai.Board.RallyVersion} (was v{version}); kept {ai.Board.RallyKept}");
        Assert.Equal(new Vector2(28f, 64f), ai.Board.Rally);
        Assert.Equal(version, ai.Board.RallyVersion);
        Assert.Equal(new Vector2(28f, 64f), ai.Board.RallyKept);
    }

    /// <summary>The wall scene to the point at (28, 64) m, then a Billet of player 0 over cells 13-14 x 31-32 and two thinks; returns the sim and the version before the Billet.</summary>
    internal static (Simulation Sim, int Version) ToTheFallBack()
    {
        Simulation sim = Scene(wall: true);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        NextThink(sim);
        NextThink(sim);
        Assert.Equal(new Vector2(28f, 64f), ai.Board.Rally);
        int version = ai.Board.RallyVersion;
        int billet = w.Data.FindBuilding("malazan_billet");
        Assert.Equal(2, w.Data.Buildings[billet].FootprintWidth);
        Assert.True(w.Buildings.Spawn(0, billet, 31 * AiScenes.FlatSize + 13, out _));
        NextThink(sim);
        NextThink(sim);
        Assert.True(ai.View.IsKnownClosed(14, 32) && ai.View.IsKnownClosed(13, 32), "the scene: the Billet closes both points");
        return (sim, version);
    }

    /// <summary>
    /// Criterion 8 (twins, saves): the fall-back scene twice is hash-equal every tick for 1,000 ticks; a save taken with the
    /// kept point in force (the point not the nominal one, no point acceptable) loads equal field for field, saves again to the
    /// same bytes and follows the run hash for hash for 1,000 ticks, its rally unchanged.
    /// </summary>
    [Fact]
    public void TheKeptPoint_Twins_AndSurvivesASave()
    {
        (Simulation a, _) = ToTheFallBack();
        (Simulation b, _) = ToTheFallBack();
        byte[] bytes = SaveTestRun.Save(a);
        Simulation load = SaveTestRun.Load(s_quiet.Value, bytes);
        Assert.Equal(bytes, SaveTestRun.Save(load));
        SaveTestRun.AssertSameState(a, load, "the kept point");
        Assert.True(load.AiOf(0)!.Board.HasRallyKept);
        for (int t = 0; t < 1000; t++)
        {
            a.Tick();
            b.Tick();
            load.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"twins diverged at tick {a.TickNumber}");
            Assert.True(a.StateHash() == load.StateHash(), $"the load diverged at tick {a.TickNumber}");
        }
        SaveTestRun.AssertSameState(a, load, "the kept point + 1,000");
        Assert.Equal(new Vector2(28f, 64f), load.AiOf(0)!.Board.Rally);
    }
}
