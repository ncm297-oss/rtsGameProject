using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Rts.Sim.Vision;
using Rts.Sim.Tests.ViewApi;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>
/// QA attacks on M4-V4 (session 2026-10-09-0125): <see cref="FogView"/> and the minimap fog layer against the sim's fog
/// on generated multi-level maps under seeded random orders (three seeds), every tick; a unit pacing across a fog-cell
/// boundary (no flicker beyond what its cell and the fog version explain); a high-ground shooter shown for exactly the
/// reveal after its last hit; a minimap Attack on a dot whose unit died this tick; and the read-only proof.
/// </summary>
public class FogViewQaTests
{
    private readonly ITestOutputHelper _out;

    public FogViewQaTests(ITestOutputHelper output) => _out = output;

    private static readonly uint[] Rgb = { 0x4B4F55, 0xC8892E };

    // A generated 128 x 128 map (levels 0-2) with 30 units a side on passable cells near opposite corners.
    private static Simulation Generated(ulong seed, out int spawned)
    {
        var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: 128, CommandCapacity: 1024));
        World w = sim.World;
        NavGrid g = w.NavGrid;
        int[] types = { w.Data.FindUnit("malazan_heavy_infantry"), w.Data.FindUnit("whirlwind_raider"), w.Data.FindUnit("whirlwind_desert_archer") };
        spawned = 0;
        for (int p = 0; p < 2; p++)
        {
            int placed = 0;
            for (int y = 10; y < g.Height - 10 && placed < 30; y += 2)
                for (int x = 10; x < g.Width - 10 && placed < 30; x += 3)
                {
                    int cx = p == 0 ? x : g.Width - 1 - x, cy = p == 0 ? y : g.Height - 1 - y;
                    if (!g.IsPassable(cx, cy)) continue;
                    sim.Enqueue(Command.SpawnUnit(p, types[placed % types.Length], g.CellCenter(cx, cy)));
                    placed++;
                    spawned++;
                }
        }
        sim.Tick();
        sim.Tick();
        return sim;
    }

    // Every 150 ticks each live unit gets a random Move / AttackMove: one in four anywhere on the map (a few just off it), the rest near the centre, so the armies meet and part again.
    private static void RandomOrders(Simulation sim, ref SimRng rng)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        float size = w.NavGrid.Width * MapConstants.CellSize;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            var h = new EntityHandle(i, u.Generation[i]);
            var goal = rng.NextInt(0, 4) == 0
                ? new Vector2(rng.NextInt(0, (int)size + 20) - 10, rng.NextInt(0, (int)size + 20) - 10)
                : new Vector2(size / 2 + rng.NextInt(-40, 41), size / 2 + rng.NextInt(-40, 41));
            int k = rng.NextInt(0, 3);
            sim.Enqueue(k == 0 ? Command.Move(u.Owner[i], h, goal) : Command.AttackMove(u.Owner[i], h, goal));
        }
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(6UL)]
    [InlineData(11UL)]
    public void TextureHideRuleShotRuleAndMinimapLayer_MatchTheFog_EveryTick_GeneratedMapRandomOrders(ulong seed)
    {
        Simulation sim = Generated(seed, out int spawned);
        World w = sim.World;
        var view = new FogView(w.Fog.Width, w.Fog.Height, w.Units.Capacity, w.Buildings.Capacity, 0);
        var raster = new MinimapRaster(w.Heightmap, w.NavGrid, Rgb, w.Units.Capacity);
        var rng = new SimRng(seed ^ 0xF06, RngStream.MapGen);
        int updates = 0, packs = 0, shownEnemy = 0, hiddenEnemy = 0, startTick = w.TickNumber;
        var seen = new int[3];
        for (int t = 0; t < 2400; t++)
        {
            if (t % 150 == 0) RandomOrders(sim, ref rng);
            int before = w.Fog.Version(0);
            sim.Tick();
            if (w.Fog.Version(0) != before) updates++;
            view.Refresh(w.Fog, w.TickNumber, w.Units.Alive, w.Buildings.Alive);
            bool packed = view.PackTexture(w.Fog);
            if (packed)
            {
                packs++;
                raster.DrawFog(view.Texture);
            }
            Assert.Equal(w.Fog.Version(0), view.TextureVersion);
            ReadOnlySpan<byte> fog = w.Fog.Visibility(0);
            Assert.True(fog.SequenceEqual(view.Texture), $"seed {seed} tick {w.TickNumber}: texture differs from the fog bytes");
            for (int c = 0; c < fog.Length; c++)
            {
                seen[fog[c]]++;
                Assert.Equal(FogView.MinimapAlpha(fog[c]), raster.Fog[c * 4 + 3]);
                // The texture's "visible" byte is the visible bit the shot rule reads.
                Assert.Equal(fog[c] == VisionConstants.Visible, w.Fog.IsVisible(0, c));
            }
            UnitStore u = w.Units;
            for (int i = 0; i < u.Capacity; i++)
            {
                Assert.Equal(w.Fog.CanSeeUnit(0, i), view.ShowsUnit(i));
                if (!u.Alive[i]) continue;
                Assert.Equal(w.Fog.IsVisible(0, w.Fog.CellOf(u.Position[i])), view.ShowsPoint(w.Fog, u.Position[i]));
                if (u.Owner[i] != 0) { if (view.ShowsUnit(i)) shownEnemy++; else hiddenEnemy++; }
            }
            for (int i = 0; i < w.Buildings.Capacity; i++) Assert.Equal(w.Fog.CanSeeBuilding(0, i), view.ShowsBuilding(i));
            ProjectileStore s = w.Projectiles;
            for (int i = 0; i < s.Capacity; i++)
                if (s.Alive[i]) Assert.Equal(w.Fog.IsVisible(0, w.Fog.CellOf(s.Position[i])), view.ShowsPoint(w.Fog, s.Position[i]));
        }
        int ticks = w.TickNumber - startTick;
        _out.WriteLine($"seed {seed}: {spawned} spawned, {ticks} ticks, {updates} fog updates, {packs} packs; states {seen[0]}/{seen[1]}/{seen[2]}; enemy unit-ticks shown {shownEnemy} hidden {hiddenEnemy}");
        Assert.Equal(updates, packs - (packs > updates ? 1 : 0)); // one pack per update (+ the first)
        Assert.True(packs <= ticks / VisionConstants.UpdateInterval + 1, $"{packs} packs over {ticks} ticks");
        Assert.True(seen[0] > 0 && seen[1] > 0 && seen[2] > 0, "a state never appeared");
        Assert.True(shownEnemy > 0 && hiddenEnemy > 0, "the fog never hid or showed an enemy");
    }

    /// <summary>
    /// A raider pacing east-west across the edge of a held infantry's sight circle: every tick the view equals
    /// <c>CanSeeUnit</c>, and its shown state changes only on a tick where its cell changed or the fog updated (no flicker
    /// from anything else, e.g. a repeated <c>Refresh</c> or a stale cache).
    /// </summary>
    [Fact]
    public void BoundaryPacer_ShownChangesOnlyWithItsCellOrAFogUpdate()
    {
        Simulation sim = FogMaps.Sim(LocalMovementTests.Flat(48), combat: false);
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle watcher = Place(sim, 0, HeavyInfantry, FogMaps.Cell(10, 24));
        EntityHandle pacer = Place(sim, 1, Raider, FogMaps.Cell(16, 24));
        sim.Enqueue(Command.HoldPosition(0, watcher));
        var view = new FogView(w.Fog.Width, w.Fog.Height, u.Capacity, w.Buildings.Capacity, 0);
        view.Refresh(w.Fog, w.TickNumber, u.Alive, w.Buildings.Alive);
        bool last = view.ShowsUnit(pacer.Index);
        int lastCell = w.Fog.CellOf(u.Position[pacer.Index]), flips = 0, shown = 0, hidden = 0;
        for (int t = 0; t < 1600; t++)
        {
            if (t % 80 == 0) sim.Enqueue(Command.Move(1, pacer, FogMaps.Cell(t % 160 == 0 ? 22 : 12, 24)));
            int version = w.Fog.Version(0);
            sim.Tick();
            for (int frame = 0; frame < 3; frame++) view.Refresh(w.Fog, w.TickNumber, u.Alive, w.Buildings.Alive);
            bool now = view.ShowsUnit(pacer.Index);
            Assert.Equal(w.Fog.CanSeeUnit(0, pacer.Index), now);
            int cell = w.Fog.CellOf(u.Position[pacer.Index]);
            if (now != last)
            {
                flips++;
                Assert.True(cell != lastCell || w.Fog.Version(0) != version, $"tick {w.TickNumber}: shown flipped with neither its cell nor the fog changing");
            }
            if (now) shown++; else hidden++;
            last = now;
            lastCell = cell;
        }
        _out.WriteLine($"pacer: shown {shown}, hidden {hidden}, {flips} flips");
        Assert.True(flips >= 10 && shown > 0 && hidden > 0, $"the pacer never crossed the edge ({flips} flips)");
    }

    /// <summary>
    /// docs/02 "High ground": an attack from high ground reveals the attacker for 2 s. On screen: each run of ticks the
    /// hidden shooter is shown ends the same number of ticks after the last hit that landed in it, within one tick of
    /// <see cref="VisionConstants.HighGroundRevealTicks"/> (measured: 39), and never starts without a hit.
    /// </summary>
    [Fact]
    public void HighGroundShooter_EachShownRun_EndsTheSameRevealAfterItsLastHit()
    {
        Simulation sim = FogMaps.Sim(FogMaps.TwoLevel());
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle victim = Place(sim, 0, HeavyInfantry, FogMaps.Cell(16, 30));
        EntityHandle shooter = Place(sim, 1, Crossbowman, FogMaps.Cell(21, 30));
        sim.Enqueue(Command.HoldPosition(0, victim));
        var view = new FogView(w.Fog.Width, w.Fog.Height, u.Capacity, w.Buildings.Capacity, 0);
        int lastHitTick = int.MinValue, runs = 0, hits = 0, gap = -1;
        bool was = false;
        int hp = u.Hp[victim.Index];
        for (int t = 0; t < 600 && u.Alive[victim.Index]; t++)
        {
            sim.Tick();
            int after = w.TickNumber; // the tick that just ran is after - 1
            if (u.Hp[victim.Index] < hp) { lastHitTick = after; hits++; }
            hp = u.Hp[victim.Index];
            view.Refresh(w.Fog, w.TickNumber, u.Alive, w.Buildings.Alive);
            bool now = view.ShowsUnit(shooter.Index);
            if (now && !was) Assert.True(after == lastHitTick, $"tick {after}: shown without a hit this tick (last hit {lastHitTick})");
            if (!now && was)
            {
                runs++;
                // The screen mirrors CanSeeUnit, which shows a reveal while the next tick still counts it (FogStore.Revealed):
                // a hit in tick t (until t + 40) is drawn after ticks t .. t + 38, 39 tick intervals. Every run must end the
                // same way, within a tick of the 2 s.
                int g = after - lastHitTick;
                Assert.True(g == VisionConstants.HighGroundRevealTicks || g == VisionConstants.HighGroundRevealTicks - 1, $"run ended {g} ticks after its last hit");
                if (gap >= 0) Assert.Equal(gap, g);
                gap = g;
            }
            was = now;
        }
        _out.WriteLine($"{hits} hits, {runs} complete shown runs, each ending {gap} ticks after its last hit");
        Assert.True(hits > 0 && runs > 0, $"no complete reveal run ({hits} hits)");
    }

    /// <summary>
    /// A minimap right-click on a dot whose unit died in the tick that just ran: the dot pick (over the fog's shown units,
    /// refreshed for this tick) never returns the dead slot, so the click is a Move, never an Attack on a dead handle.
    /// </summary>
    [Fact]
    public void MinimapPick_OnTheDotOfAUnitThatDiedThisTick_IsNotAnAttack()
    {
        Simulation sim = CombatViewScene.Create();
        CombatViewScene.Start(sim);
        World w = sim.World;
        UnitStore u = w.Units;
        var view = new FogView(w.Fog.Width, w.Fog.Height, u.Capacity, w.Buildings.Capacity, 0);
        var raster = new MinimapRaster(w.Heightmap, w.NavGrid, Rgb, u.Capacity);
        var lastPos = new Vector2[u.Capacity];
        var lastShown = new bool[u.Capacity];
        int checkedDeaths = 0;
        for (int t = 0; t < 1500; t++)
        {
            sim.Tick();
            view.Refresh(w.Fog, w.TickNumber, u.Alive, w.Buildings.Alive);
            foreach (DeathEvent d in w.Deaths)
            {
                int slot = d.Victim.Index;
                if (d.IsBuilding || d.VictimOwner == 0 || !lastShown[slot]) continue;
                int got = raster.EnemyDotAt(lastPos[slot], view.UnitShown, u.Position, u.Owner, 0);
                Assert.True(got != slot || u.Alive[slot], $"tick {w.TickNumber}: the dot pick returned dead slot {slot}");
                if (got >= 0) Assert.True(u.Alive[got] && view.ShowsUnit(got));
                checkedDeaths++;
            }
            for (int i = 0; i < u.Capacity; i++)
            {
                lastPos[i] = u.Position[i];
                lastShown[i] = view.ShowsUnit(i);
            }
        }
        _out.WriteLine($"{checkedDeaths} enemy deaths on a shown dot checked");
        Assert.True(checkedDeaths > 0, "no enemy died on a shown dot");
    }

    /// <summary>The dot pick and the fog layer with short, empty or junk spans: no throw, the documented answers.</summary>
    [Fact]
    public void MinimapFogAndPick_ShortAndJunkSpans()
    {
        Heightmap map = LocalMovementTests.Flat(16);
        var raster = new MinimapRaster(map, new NavGrid(map), Rgb, 4);
        raster.DrawFog(ReadOnlySpan<byte>.Empty);
        for (int c = 0; c < 16 * 16; c++) Assert.Equal(255, raster.Fog[c * 4 + 3]);
        raster.DrawFog(new byte[] { 2, 1, 0, 7, 255 });
        Assert.Equal(0, raster.Fog[3]);
        Assert.Equal(153, raster.Fog[7]);
        Assert.Equal(255, raster.Fog[11]);
        Assert.Equal(0, raster.Fog[15]);  // above visible counts as visible
        Assert.Equal(0, raster.Fog[19]);
        Assert.Equal(255, raster.Fog[23]); // past the span: unexplored
        Vector2[] pos = { new(5f, 5f), new(float.NaN, 5f), new(float.PositiveInfinity, 1f) };
        int[] owner = { 1, 1, 1 };
        Assert.Equal(-1, raster.EnemyDotAt(new Vector2(5f, 5f), new[] { true }, pos, new[] { 0 }, 0)); // own
        Assert.Equal(0, raster.EnemyDotAt(new Vector2(5f, 5f), new[] { true, true, true }, pos, owner, 0));
        Assert.Equal(-1, raster.EnemyDotAt(new Vector2(5f, 5f), ReadOnlySpan<bool>.Empty, pos, owner, 0));
        Assert.Equal(-1, raster.EnemyDotAt(new Vector2(5f, 5f), new[] { true }, pos, new[] { 9 }, 0)); // no such player
        Assert.Equal(-1, raster.EnemyDotAt(new Vector2(-50f, 5f), new[] { true }, pos, owner, 0));     // off the map
    }

    /// <summary>The view never changes the sim: a viewed run on a generated map under random orders hashes as a bare one, every tick.</summary>
    [Fact]
    public void ReadOnly_GeneratedMapRandomOrders_HashTwinEveryTick()
    {
        Simulation viewed = Generated(6, out _), bare = Generated(6, out _);
        World w = viewed.World;
        var view = new FogView(w.Fog.Width, w.Fog.Height, w.Units.Capacity, w.Buildings.Capacity, 0);
        var raster = new MinimapRaster(w.Heightmap, w.NavGrid, Rgb, w.Units.Capacity);
        var r1 = new SimRng(99, RngStream.MapGen);
        var r2 = new SimRng(99, RngStream.MapGen);
        var a = new int[8];
        for (int t = 0; t < 800; t++)
        {
            if (t % 25 == 0) { RandomOrders(viewed, ref r1); RandomOrders(bare, ref r2); }
            viewed.Tick();
            bare.Tick();
            view.Refresh(w.Fog, w.TickNumber, w.Units.Alive, w.Buildings.Alive);
            if (view.PackTexture(w.Fog)) raster.DrawFog(view.Texture);
            raster.DrawDots(view.UnitShown, w.Units.Position, w.Units.Owner);
            raster.EnemyDotAt(new Vector2(128f, 128f), view.UnitShown, w.Units.Position, w.Units.Owner, 0);
            view.ShowsPoint(w.Fog, new Vector2(128f, 128f));
            view.CollectGhosts(w.Fog, w.Buildings.Generation);
            Assert.Equal(bare.StateHash(), viewed.StateHash());
        }
    }
}
