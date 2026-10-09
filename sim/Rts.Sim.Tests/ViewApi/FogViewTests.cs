using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Rts.Sim.Vision;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>
/// M4-V4: the view's fog (<see cref="FogView"/>): the texture packer against the fog's bytes, the hide rule against
/// <c>CanSeeUnit</c> / <c>CanSeeBuilding</c> and an independent oracle, the shot rule's cell, the minimap's fog layer and
/// enemy-dot pick, the building pick's filter, 0 bytes, and the hash twin.
/// </summary>
[Collection(SerialCollection.Name)]
public class FogViewTests
{
    private readonly ITestOutputHelper _out;

    public FogViewTests(ITestOutputHelper output) => _out = output;

    // ---- Texture packer ----

    [Fact]
    public void Texture_EqualsTheFogBytes_AfterEveryUpdate_AndPacksOnlyWhenTheVersionMoves()
    {
        Simulation sim = CombatViewScene.Create();
        World w = sim.World;
        var view = new FogView(w.Fog.Width, w.Fog.Height, w.Units.Capacity, w.Buildings.Capacity, 0);
        Assert.True(view.PackTexture(w.Fog)); // the first call always packs
        Assert.Equal(1, view.Uploads);
        CombatViewScene.Start(sim);
        int ticks = 2, versions = 1, lastVersion = w.Fog.Version(0);
        Assert.True(view.PackTexture(w.Fog));
        versions++;
        int before = view.Uploads;
        for (int t = 0; t < 1200; t++)
        {
            sim.Tick();
            ticks++;
            int v = w.Fog.Version(0);
            bool packed = view.PackTexture(w.Fog);
            Assert.Equal(v != lastVersion, packed);
            if (packed) versions++;
            lastVersion = v;
            Assert.Equal(v, view.TextureVersion);
            Assert.True(w.Fog.Visibility(0).SequenceEqual(view.Texture), $"tick {sim.TickNumber}: texture differs from the fog bytes");
            Assert.False(view.PackTexture(w.Fog)); // a second call in the same frame copies nothing
        }
        Assert.Equal(versions, view.Uploads);
        // Over the 1,200 ticks of the loop: one upload per fog update, at most one per 4 ticks (+1 for where the span starts).
        Assert.True(view.Uploads - before <= 1200 / VisionConstants.UpdateInterval + 1, $"{view.Uploads - before} uploads over 1200 ticks");
        _out.WriteLine($"{ticks} ticks, {view.Uploads} uploads");
    }

    [Fact]
    public void Texture_ShowsAllThreeStates_OnTheBrawl()
    {
        Simulation sim = CombatViewScene.Create();
        CombatViewScene.Start(sim);
        for (int t = 0; t < 600; t++) sim.Tick();
        World w = sim.World;
        var view = new FogView(w.Fog.Width, w.Fog.Height, w.Units.Capacity, w.Buildings.Capacity, 0);
        view.PackTexture(w.Fog);
        var seen = new int[3];
        foreach (byte b in view.Texture) seen[b]++;
        Assert.True(seen[0] > 0 && seen[1] > 0 && seen[2] > 0, $"states {seen[0]} / {seen[1]} / {seen[2]}");
    }

    [Fact]
    public void Disabled_TextureAllVisible_UploadedOnce_EveryLiveSlotShown()
    {
        Simulation sim = CombatViewScene.Create();
        World w = sim.World;
        var view = new FogView(w.Fog.Width, w.Fog.Height, w.Units.Capacity, w.Buildings.Capacity, 0, enabled: false);
        CombatViewScene.Start(sim);
        Assert.True(view.PackTexture(w.Fog));
        for (int t = 0; t < 40; t++)
        {
            sim.Tick();
            Assert.False(view.PackTexture(w.Fog));
            view.Refresh(w.Fog, w.TickNumber, w.Units.Alive, w.Buildings.Alive);
            for (int i = 0; i < w.Units.Capacity; i++) Assert.Equal(w.Units.Alive[i], view.ShowsUnit(i));
            for (int i = 0; i < w.Buildings.Capacity; i++) Assert.Equal(w.Buildings.Alive[i], view.ShowsBuilding(i));
            Assert.True(view.ShowsPoint(w.Fog, new Vector2(1000f, -5f)));
        }
        Assert.Equal(1, view.Uploads);
        Assert.All(view.Texture, b => Assert.Equal(VisionConstants.Visible, b));
    }

    // ---- The hide rule ----

    [Fact]
    public void HideRule_EqualsCanSee_AndTheOracle_EveryTickOfTheBrawl_ForBothPlayers()
    {
        Simulation sim = CombatViewScene.Create();
        World w = sim.World;
        var views = new[]
        {
            new FogView(w.Fog.Width, w.Fog.Height, w.Units.Capacity, w.Buildings.Capacity, 0),
            new FogView(w.Fog.Width, w.Fog.Height, w.Units.Capacity, w.Buildings.Capacity, 1),
        };
        CombatViewScene.Start(sim);
        int hiddenEnemy = 0, shownEnemy = 0, flips = 0, deaths = 0;
        var last = new bool[w.Units.Capacity];
        for (int t = 0; t < 1500; t++)
        {
            sim.Tick();
            deaths += w.Deaths.Length;
            foreach (FogView view in views)
            {
                int p = view.Player;
                Assert.True(view.Refresh(w.Fog, w.TickNumber, w.Units.Alive, w.Buildings.Alive));
                Assert.False(view.Refresh(w.Fog, w.TickNumber, w.Units.Alive, w.Buildings.Alive)); // same tick: nothing to do
                for (int i = 0; i < w.Units.Capacity; i++)
                {
                    bool oracle = w.Units.Alive[i] && (w.Units.Owner[i] == p || w.Fog.IsVisible(p, w.Fog.CellOf(w.Units.Position[i])) || w.Fog.Revealed(p, i));
                    Assert.Equal(w.Fog.CanSeeUnit(p, i), view.ShowsUnit(i));
                    Assert.Equal(oracle, view.ShowsUnit(i));
                    if (p != 0 || !w.Units.Alive[i] || w.Units.Owner[i] == 0) continue;
                    if (view.ShowsUnit(i)) shownEnemy++; else hiddenEnemy++;
                    if (view.ShowsUnit(i) != last[i]) flips++;
                    last[i] = view.ShowsUnit(i);
                }
                for (int i = 0; i < w.Buildings.Capacity; i++)
                    Assert.Equal(w.Fog.CanSeeBuilding(p, i), view.ShowsBuilding(i));
            }
        }
        _out.WriteLine($"enemy unit-ticks shown {shownEnemy}, hidden {hiddenEnemy}, {flips} flips, {deaths} deaths");
        Assert.True(hiddenEnemy > 0 && shownEnemy > 0 && flips > 0, "the brawl never hid or showed an enemy");
        Assert.True(deaths > 0);
    }

    [Fact]
    public void HideRule_AHighGroundShooter_IsShownExactlyWhileRevealed()
    {
        Simulation sim = FogMaps.Sim(FogMaps.TwoLevel());
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle victim = Place(sim, 0, HeavyInfantry, FogMaps.Cell(16, 30));
        EntityHandle shooter = Place(sim, 1, Crossbowman, FogMaps.Cell(21, 30));
        sim.Enqueue(Command.HoldPosition(0, victim));
        var view = new FogView(w.Fog.Width, w.Fog.Height, u.Capacity, w.Buildings.Capacity, 0);
        int shownTicks = 0, revealedTicks = 0;
        for (int t = 0; t < 300; t++)
        {
            sim.Tick();
            view.Refresh(w.Fog, w.TickNumber, u.Alive, w.Buildings.Alive);
            bool revealed = w.Fog.RevealEnd(0, shooter.Index) > w.TickNumber;
            Assert.False(w.Fog.IsVisible(0, w.Fog.CellOf(u.Position[shooter.Index]))); // never seen through the fog
            Assert.Equal(u.Alive[shooter.Index] && revealed, view.ShowsUnit(shooter.Index));
            if (revealed) revealedTicks++;
            if (view.ShowsUnit(shooter.Index)) shownTicks++;
        }
        _out.WriteLine($"shooter shown {shownTicks} ticks (revealed {revealedTicks})");
        Assert.True(shownTicks > 0, "the shooter was never revealed");
    }

    [Fact]
    public void ShotRule_CellOf_MatchesTheFogsCell_ForAnyPoint()
    {
        Simulation sim = CombatViewScene.Create();
        FogStore fog = sim.World.Fog;
        var rng = new System.Random(5); // test-side randomness only
        Vector2[] edge =
        {
            new(float.NaN, 3f), new(3f, float.NaN), new(float.PositiveInfinity, 1f), new(float.NegativeInfinity, 1f), new(-0.01f, -7f),
            new(1e30f, 1e30f), new(-1e30f, 5f), new(fog.Width * MapConstants.CellSize, fog.Height * MapConstants.CellSize), Vector2.Zero,
            new(MapConstants.CellSize - 1e-6f, MapConstants.CellSize),
        };
        foreach (Vector2 p in edge) Assert.Equal(fog.CellOf(p), FogView.CellOf(fog.Width, fog.Height, p));
        for (int k = 0; k < 20000; k++)
        {
            var p = new Vector2((float)(rng.NextDouble() * 120 - 12), (float)(rng.NextDouble() * 120 - 12));
            Assert.Equal(fog.CellOf(p), FogView.CellOf(fog.Width, fog.Height, p));
        }
    }

    [Fact]
    public void ShotRule_ShowsPoint_IsTheVisibleBitOfItsCell()
    {
        Simulation sim = CombatViewScene.Create();
        CombatViewScene.Start(sim);
        for (int t = 0; t < 300; t++) sim.Tick();
        World w = sim.World;
        var view = new FogView(w.Fog.Width, w.Fog.Height, w.Units.Capacity, w.Buildings.Capacity, 0);
        int visible = 0, hidden = 0;
        for (int y = 0; y < w.Fog.Height; y++)
            for (int x = 0; x < w.Fog.Width; x++)
            {
                Vector2 p = FogMaps.Cell(x, y, 0.3f);
                bool shows = view.ShowsPoint(w.Fog, p);
                Assert.Equal(w.Fog.Visibility(0)[y * w.Fog.Width + x] == VisionConstants.Visible, shows);
                if (shows) visible++; else hidden++;
            }
        Assert.True(visible > 0 && hidden > 0);
    }

    [Fact]
    public void Brightness_AndMinimapAlpha_OfTheThreeStates()
    {
        Assert.Equal(0f, FogView.Brightness(VisionConstants.Unexplored));
        Assert.Equal(FogView.ExploredBrightness, FogView.Brightness(VisionConstants.Explored));
        Assert.Equal(1f, FogView.Brightness(VisionConstants.Visible));
        Assert.Equal(255, FogView.MinimapAlpha(VisionConstants.Unexplored));
        Assert.Equal(153, FogView.MinimapAlpha(VisionConstants.Explored));
        Assert.Equal(0, FogView.MinimapAlpha(VisionConstants.Visible));
        Assert.Equal(0, FogView.MinimapAlpha(200)); // a stray value draws as visible, never as a hole of black
    }

    [Fact]
    public void Ghosts_AreTheM43bHook_AndCollectNothingYet()
    {
        Simulation sim = CombatViewScene.Create();
        World w = sim.World;
        var view = new FogView(w.Fog.Width, w.Fog.Height, w.Units.Capacity, w.Buildings.Capacity, 0);
        var a = new int[8];
        Assert.Equal(0, view.CollectGhosts(w.Fog, a, a, a));
    }

    [Fact]
    public void BadArguments_AnswerFalse_OrThrowOnConstruction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FogView(0, 4, 1, 1, 0));
        var view = new FogView(4, 4, 2, 2, 0);
        Assert.False(view.ShowsUnit(-1));
        Assert.False(view.ShowsUnit(2));
        Assert.False(view.ShowsBuilding(5));
        Simulation sim = CombatViewScene.Create();
        World w = sim.World;
        var other = new FogView(w.Fog.Width, w.Fog.Height, w.Units.Capacity, w.Buildings.Capacity, 9); // no such player
        Assert.True(other.PackTexture(w.Fog));
        Assert.All(other.Texture, b => Assert.Equal(0, b));
        CombatViewScene.Start(sim);
        other.Refresh(w.Fog, w.TickNumber, w.Units.Alive, w.Buildings.Alive);
        for (int i = 0; i < w.Units.Capacity; i++) Assert.False(other.ShowsUnit(i));
    }

    // ---- Minimap ----

    [Fact]
    public void MinimapFog_AlphaFollowsEachCell_StartsOpaque()
    {
        Simulation sim = CombatViewScene.Create();
        World w = sim.World;
        var raster = new MinimapRaster(w.Heightmap, w.NavGrid, new uint[] { 0x4B4F55, 0xC8892E }, w.Units.Capacity);
        for (int c = 0; c < w.Fog.Width * w.Fog.Height; c++) Assert.Equal(255, raster.Fog[c * 4 + 3]);
        CombatViewScene.Start(sim);
        for (int t = 0; t < 400; t++) sim.Tick();
        raster.DrawFog(w.Fog.Visibility(0));
        ReadOnlySpan<byte> bytes = w.Fog.Visibility(0);
        for (int c = 0; c < bytes.Length; c++)
        {
            Assert.Equal(FogView.MinimapAlpha(bytes[c]), raster.Fog[c * 4 + 3]);
            Assert.Equal(0, raster.Fog[c * 4] | raster.Fog[c * 4 + 1] | raster.Fog[c * 4 + 2]);
        }
        raster.DrawFog(bytes.Slice(0, 10)); // short: the rest unexplored
        Assert.Equal(255, raster.Fog[11 * 4 + 3]);
        Assert.Equal(2, raster.FogDraws);
    }

    [Fact]
    public void MinimapDotPick_ShownEnemiesOnly_TheDotBlock_NearestWins()
    {
        Heightmap map = LocalMovementTests.Flat(48);
        var raster = new MinimapRaster(map, new NavGrid(map), new uint[] { 0x4B4F55, 0xC8892E }, 8);
        Vector2[] pos = { new(21f, 21f), new(41f, 41f), new(44.5f, 41f), new(61f, 61f), new(81f, 81f) };
        int[] owner = { 0, 1, 1, 1, 1 };
        bool[] shown = { true, true, true, false, true };
        Assert.Equal(-1, raster.EnemyDotAt(new Vector2(21f, 21f), shown, pos, owner, 0)); // own dot
        Assert.Equal(1, raster.EnemyDotAt(new Vector2(41.2f, 41f), shown, pos, owner, 0));
        Assert.Equal(2, raster.EnemyDotAt(new Vector2(44f, 41f), shown, pos, owner, 0)); // both blocks hold it: the nearer
        Assert.Equal(-1, raster.EnemyDotAt(new Vector2(61f, 61f), shown, pos, owner, 0)); // hidden: no dot
        Assert.Equal(4, raster.EnemyDotAt(new Vector2(81f + 2.9f, 81f - 1.9f), shown, pos, owner, 0)); // the rim's far corner
        Assert.Equal(-1, raster.EnemyDotAt(new Vector2(81f + 5.1f, 81f), shown, pos, owner, 0)); // one cell past the rim
        Assert.Equal(-1, raster.EnemyDotAt(new Vector2(float.NaN, 3f), shown, pos, owner, 0));
        Assert.Equal(0, raster.EnemyDotAt(new Vector2(21f, 21f), shown, pos, owner, 1)); // roles swap for player 1
        // Every pixel of a lone dot's drawn block picks it, and no pixel outside.
        raster.DrawDots(shown, pos, owner);
        int cells = map.Width * map.Height, hits = 0;
        for (int c = 0; c < cells; c++)
        {
            int x = c % map.Width, y = c / map.Width;
            int got = raster.EnemyDotAt(FogMaps.Cell(x, y), new[] { false, false, false, false, true }, pos, owner, 0);
            bool drawn = raster.Dots[c * 4 + 3] != 0 && System.Math.Abs(x - 40) <= 3 && System.Math.Abs(y - 40) <= 3;
            Assert.Equal(drawn ? 4 : -1, got);
            if (got == 4) hits++;
        }
        Assert.Equal(MinimapRaster.DotCells, hits);
    }

    [Fact]
    public void BuildingPick_SkipsSlotsTheViewDoesNotShow()
    {
        Simulation sim = CombatViewScene.Create();
        CombatViewScene.Start(sim);
        World w = sim.World;
        Vector2 at = CombatViewScene.BuildingAt(1);
        var origin = new Vector3(at.X, 40f, at.Y + 20f);
        var dir = new Vector3(0f, -40f, -20f);
        int slot = BuildingPicker.PickRay(w.Buildings, w.Data.Buildings, w.NavGrid, w.Heightmap, -1, origin, dir, 3f, 0.15f, out float t0);
        Assert.True(slot >= 0);
        var shown = new bool[w.Buildings.Capacity];
        Assert.Equal(-1, BuildingPicker.PickRay(w.Buildings, w.Data.Buildings, w.NavGrid, w.Heightmap, -1, origin, dir, 3f, 0.15f, shown, out float t1));
        Assert.True(float.IsPositiveInfinity(t1));
        shown[slot] = true;
        Assert.Equal(slot, BuildingPicker.PickRay(w.Buildings, w.Data.Buildings, w.NavGrid, w.Heightmap, -1, origin, dir, 3f, 0.15f, shown, out float t2));
        Assert.Equal(t0, t2);
        Assert.Equal(-1, BuildingPicker.PickRay(w.Buildings, w.Data.Buildings, w.NavGrid, w.Heightmap, -1, origin, dir, 3f, 0.15f, new bool[slot], out _)); // past the end: not shown
    }

    // ---- Cost and the twin ----

    [Fact]
    public void FogSync_2000Units_AllocatesZeroBytes()
    {
        const int perSide = 1000;
        Simulation sim = new(TestSim.Config(Seed: 3, PlayerCount: 2, UnitCapacity: 2 * perSide, CommandCapacity: 4 * perSide), LocalMovementTests.Flat(128));
        World w = sim.World;
        int hi = w.Data.FindUnit("malazan_heavy_infantry");
        for (int k = 0; k < perSide; k++)
        {
            sim.Enqueue(Command.SpawnUnit(0, hi, new Vector2(20f + k % 25 * 1.5f, 40f + k / 25 * 1.5f)));
            sim.Enqueue(Command.SpawnUnit(1, hi, new Vector2(180f + k % 25 * 1.5f, 40f + k / 25 * 1.5f)));
        }
        sim.Tick();
        sim.Tick();
        var view = new FogView(w.Fog.Width, w.Fog.Height, w.Units.Capacity, w.Buildings.Capacity, 0);
        var raster = new MinimapRaster(w.Heightmap, w.NavGrid, new uint[] { 0x4B4F55, 0xC8892E }, w.Units.Capacity);
        int sum = 0;
        Action block = () =>
        {
            for (int f = 0; f < 8; f++)
            {
                sim.Tick(); // outside the view's cost, but on the measured thread: it allocates nothing either
                view.Refresh(w.Fog, w.TickNumber, w.Units.Alive, w.Buildings.Alive);
                if (view.PackTexture(w.Fog)) raster.DrawFog(view.Texture);
                raster.DrawDots(view.UnitShown, w.Units.Position, w.Units.Owner);
                sum += raster.EnemyDotAt(new Vector2(200f, 60f), view.UnitShown, w.Units.Position, w.Units.Owner, 0);
                for (int i = 0; i < 200; i++) sum += view.ShowsPoint(w.Fog, w.Units.Position[i * 10]) ? 1 : 0;
            }
        };
        block();
        AllocationProbe.AssertZero(block, _out);
        _out.WriteLine($"fog sync at {2 * perSide} units: 0 bytes, checksum {sum}");
    }

    [Fact]
    public void HashTwin_TheFogViewReadsOnly()
    {
        Simulation viewed = CombatViewScene.Create(), bare = CombatViewScene.Create();
        CombatViewScene.Start(viewed, bare);
        World w = viewed.World;
        var view = new FogView(w.Fog.Width, w.Fog.Height, w.Units.Capacity, w.Buildings.Capacity, 0);
        var raster = new MinimapRaster(w.Heightmap, w.NavGrid, new uint[] { 0x4B4F55, 0xC8892E }, w.Units.Capacity);
        var a = new int[4];
        for (int t = 0; t < 2000; t++)
        {
            viewed.Tick();
            bare.Tick();
            view.Refresh(w.Fog, w.TickNumber, w.Units.Alive, w.Buildings.Alive);
            if (view.PackTexture(w.Fog)) raster.DrawFog(view.Texture);
            raster.EnemyDotAt(new Vector2(50f, 48f), view.UnitShown, w.Units.Position, w.Units.Owner, 0);
            view.ShowsPoint(w.Fog, new Vector2(50f, 48f));
            view.CollectGhosts(w.Fog, a, a, a);
            Assert.Equal(bare.StateHash(), viewed.StateHash());
        }
    }
}
