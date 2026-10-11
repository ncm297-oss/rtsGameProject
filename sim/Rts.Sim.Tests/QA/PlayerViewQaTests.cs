using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Vision;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// QA (M5-1, session 2026-10-11-0314): <see cref="PlayerView"/> attacked past the developer's four-player scene. Ten players
/// (indices 8 and 9 use the second byte of the per-unit detection bits), stealth cast at random ticks, Sandstorms, towers,
/// buildings and units destroyed through the test seams at random (slots recycled by training), Laborers sent toward the
/// map's edges; every answer of every player's view, every tick, against <see cref="PlayerViewOracle"/>; the hash twin over
/// the same scene; and single-rule rows for the QA focus.
/// </summary>
public class PlayerViewQaTests
{
    private const int Players = 10;
    private readonly ITestOutputHelper _out;

    public PlayerViewQaTests(ITestOutputHelper output) => _out = output;

    private static GameData Data => StealthTests.Data;

    private static Simulation Scene(ulong seed)
    {
        var map = MapGenParams.Default with { Forests = 6, GoldMines = 4 };
        var sim = new Simulation(new SimConfig(seed, Players, Players * 24 + 32, 4096) { Data = Data, Map = map });
        NavGrid g = sim.World.NavGrid;
        var open = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
            if (g.IsPassable(c % g.Width, c / g.Width)) open.Add(c);
        var rng = new SimRng(seed, 901);
        int heavy = Data.FindUnit("malazan_heavy_infantry"), laborer = Data.FindUnit("malazan_laborer"),
            bow = Data.FindUnit("malazan_crossbowman"), priest = Data.FindUnit("whirlwind_priest");
        for (int p = 0; p < Players; p++)
        {
            // Bases on a ring round the map's centre, close enough that neighbours meet.
            float angle = p * 2f * MathF.PI / Players;
            int px = (int)(g.Width / 2 + 28 * MathF.Cos(angle)), py = (int)(g.Height / 2 + 28 * MathF.Sin(angle));
            FactionDef f = Data.Factions[p % Data.Factions.Length];
            sim.Enqueue(Command.SpawnBuilding(p, OfSlot(f.Id, BuildingSlot.TownHall), Centre(g, Near(g, open, px, py, ref rng))));
            sim.Enqueue(Command.SpawnBuilding(p, OfSlot(f.Id, BuildingSlot.WatchTower), Centre(g, Near(g, open, px, py, ref rng))));
            sim.Enqueue(Command.SpawnBuilding(p, OfSlot(f.Id, BuildingSlot.House), Centre(g, Near(g, open, px + 6, py, ref rng))));
            int[] types = { heavy, heavy, heavy, heavy, laborer, laborer, bow, bow, bow, priest };
            foreach (int type in types) sim.Enqueue(Command.SpawnUnit(p, type, Centre(g, Near(g, open, px, py, ref rng))));
        }
        return sim;
    }

    private static int OfSlot(int faction, BuildingSlot slot)
    {
        foreach (BuildingDef b in Data.Buildings)
            if (b.Faction == faction && b.Slot == slot) return b.Id;
        throw new InvalidOperationException($"no {slot}");
    }

    private static Vector2 Centre(NavGrid g, int c) => g.CellCenter(c % g.Width, c / g.Width);

    private static int Near(NavGrid g, List<int> open, int x, int y, ref SimRng rng)
    {
        for (int tries = 0; tries < 60; tries++)
        {
            int px = x + rng.NextInt(-5, 6), py = y + rng.NextInt(-5, 6);
            if (px >= 0 && py >= 0 && px < g.Width && py < g.Height && g.IsPassable(px, py)) return py * g.Width + px;
        }
        return open[rng.NextInt(0, open.Count)];
    }

    /// <summary>Random orders: stealth at random ticks, storms, attack-moves, Laborers to the map's corners and edges, random kills through the seams, workers trained into freed slots.</summary>
    private static void Orders(Simulation sim, int t, ref SimRng rng)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        BuildingStore b = w.Buildings;
        int heavy = Data.FindUnit("malazan_heavy_infantry"), laborer = Data.FindUnit("malazan_laborer"), priest = Data.FindUnit("whirlwind_priest");
        int width = w.NavGrid.Width, height = w.NavGrid.Height;
        if (t > 100 && t % 41 == 0)
        {
            // A random building dies through the seam, wherever it stands (in some players' sight, out of others').
            for (int tries = 0; tries < 8; tries++)
            {
                int k = rng.NextInt(0, b.Capacity);
                if (b.Alive[k] && Data.Buildings[b.TypeId[k]].Slot != BuildingSlot.TownHall)
                {
                    Assert.True(b.Free(b.HandleOf(k)));
                    break;
                }
            }
        }
        if (t > 50 && t % 31 == 0)
        {
            for (int tries = 0; tries < 8; tries++)
            {
                int k = rng.NextInt(0, u.Capacity);
                if (!u.Alive[k]) continue;
                u.Free(new EntityHandle(k, u.Generation[k]));
                break;
            }
        }
        if (t % 60 == 7)
        {
            for (int k = 0; k < b.Capacity; k++)
            {
                if (!b.Alive[k] || b.UnderConstruction[k] || Data.Buildings[b.TypeId[k]].Slot != BuildingSlot.TownHall) continue;
                int faction = Data.Buildings[b.TypeId[k]].Faction;
                foreach (int ut in Data.Factions[faction].Units)
                    if (Data.Units[ut].Slot == UnitSlot.Worker)
                    {
                        BuildingDef def = Data.Buildings[b.TypeId[k]];
                        int anchor = b.Cell[k];
                        var at = new Vector2((anchor % width + def.FootprintWidth / 2f) * MapConstants.CellSize, (anchor / width + def.FootprintHeight / 2f) * MapConstants.CellSize);
                        sim.Enqueue(Command.Train(b.Owner[k], at, ut));
                    }
            }
        }
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            var h = new EntityHandle(i, u.Generation[i]);
            int owner = u.Owner[i];
            if (u.TypeId[i] == heavy && rng.NextInt(0, 60) == 0)
            {
                sim.Enqueue(Command.UseAbility(owner, h, 0, u.Position[i]));
                continue;
            }
            if (rng.NextInt(0, u.TypeId[i] == laborer ? 40 : 120) != 0) continue;
            if (u.TypeId[i] == laborer)
            {
                // Corners and edges as often as the middle.
                int x = rng.NextInt(0, 3) switch { 0 => 0, 1 => width - 1, _ => rng.NextInt(0, width) };
                int y = rng.NextInt(0, 3) switch { 0 => 0, 1 => height - 1, _ => rng.NextInt(0, height) };
                sim.Enqueue(Command.Move(owner, h, new Vector2((x + 0.5f) * MapConstants.CellSize, (y + 0.5f) * MapConstants.CellSize)));
                continue;
            }
            Vector2 enemy = u.Position[i];
            for (int tries = 0; tries < 8; tries++)
            {
                int j = rng.NextInt(0, u.Capacity);
                if (u.Alive[j] && u.Owner[j] != owner) { enemy = u.Position[j]; break; }
            }
            if (u.TypeId[i] == priest) sim.Enqueue(Command.UseAbility(owner, h, 0, enemy));
            else sim.Enqueue(Command.AttackMove(owner, h, enemy));
        }
    }

    [Theory]
    [InlineData(3UL)]
    [InlineData(11UL)]
    public void TenPlayers_EveryAnswerEveryTick_EqualsTheOracle(ulong seed)
    {
        const int ticks = 800;
        Simulation sim = Scene(seed);
        World w = sim.World;
        var kept = new PlayerView[Players];
        for (int p = 0; p < Players; p++) kept[p] = new PlayerView(w, p);
        var rng = new SimRng(seed, 902);
        var tally = new PlayerViewOracle.Tally();
        long towerReveals = 0, highPlayerStealthHidden = 0;
        for (int t = 0; t < ticks; t++)
        {
            Orders(sim, t, ref rng);
            sim.Tick();
            for (int p = 0; p < Players; p++)
            {
                PlayerView view = (t + p) % 7 == 0 ? new PlayerView(w, p) : kept[p];
                PlayerViewOracle.Check(w, view, p, tally, $"seed {seed} tick {t} player {p}");
                for (int k = 0; k < w.Buildings.Capacity; k++)
                    if (w.Buildings.Alive[k] && w.Buildings.Owner[k] != p && w.Fog.BuildingRevealed(p, k) && view.TryGetBuilding(k, out _)) towerReveals++;
                if (p >= 8)
                    for (int j = 0; j < w.Units.Capacity; j++)
                        if (w.Units.Alive[j] && w.Units.Owner[j] != p && w.IsStealthed(j) && !w.IsDetectedBy(p, j) && w.Fog.SeesUnitCell(p, j)) highPlayerStealthHidden++;
            }
            Assert.True(tally.First.Length == 0, tally.First + "\n" + tally);
        }
        _out.WriteLine($"seed {seed}: {tally}; tower reveals answered {towerReveals}; players 8-9 stealth-hidden in sight {highPlayerStealthHidden}");
        Assert.Equal(0, tally.Leaks);
        Assert.Equal(0, tally.Misses);
        Assert.Equal(0, tally.FieldMismatches);
        Assert.Equal(0, tally.OracleDisagreements);
        Assert.True(tally.StealthHidden > 0 && tally.InStorm > 0 && tally.GhostsOfDeadBuildings > 0, tally.ToString());
    }

    /// <summary>Ten players' views read whole every tick (a kept one and a fresh one): the hash equals a bare twin's after every tick and doesn't move across the reads.</summary>
    [Fact]
    public void TenPlayers_ReadingEveryView_LeavesTheHashEqualToABareTwin()
    {
        const ulong seed = 5;
        Simulation a = Scene(seed), b = Scene(seed);
        var ra = new SimRng(seed, 902);
        var rb = new SimRng(seed, 902);
        var kept = new PlayerView[Players];
        for (int p = 0; p < Players; p++) kept[p] = new PlayerView(a.World, p);
        for (int t = 0; t < 600; t++)
        {
            Orders(a, t, ref ra);
            Orders(b, t, ref rb);
            a.Tick();
            b.Tick();
            ulong before = a.StateHash();
            for (int p = 0; p < Players; p++)
            {
                long fresh = PlayerViewReader.ReadAll(new PlayerView(a.World, p), cells: true);
                Assert.Equal(fresh, PlayerViewReader.ReadAll(kept[p], cells: true));
                // Random access too: every slot of every kind, in and out of range.
                for (int s = -2; s <= a.World.Units.Capacity + 1; s++) kept[p].TryGetUnit(s, out _);
                for (int s = -2; s <= a.World.Buildings.Capacity + 1; s++)
                {
                    kept[p].TryGetBuilding(s, out _);
                    kept[p].TryGetGhost(s, out _);
                    kept[p].QueueCount(s);
                    kept[p].QueueProgress(s);
                    kept[p].TryGetQueueItem(s, 0, out _, out _);
                }
            }
            Assert.Equal(before, a.StateHash());
            Assert.True(before == b.StateHash(), $"the twins differ after tick {t}");
        }
    }

    /// <summary>A view of player 9 of 10 and a cursor started anywhere (negative, past the end, int.MinValue) answer only in range.</summary>
    [Fact]
    public void CursorsStartedAnywhere_NeverAnswerOutOfRange()
    {
        Simulation sim = Scene(1);
        for (int t = 0; t < 10; t++) sim.Tick();
        var v = new PlayerView(sim.World, Players - 1);
        foreach (int start in new[] { int.MinValue, -5, -1, sim.World.Units.Capacity - 1, sim.World.Units.Capacity, int.MaxValue - 1, int.MaxValue })
        {
            int s = start;
            while (v.NextUnit(ref s, out SeenUnit u))
            {
                Assert.InRange(s, 0, sim.World.Units.Capacity - 1);
                Assert.Equal(s, u.Handle.Index);
            }
            Assert.Equal(sim.World.Units.Capacity, s);
            s = start;
            while (v.NextBuilding(ref s, out _)) Assert.InRange(s, 0, sim.World.Buildings.Capacity - 1);
            s = start;
            while (v.NextGhost(ref s, out _)) Assert.InRange(s, 0, sim.World.Buildings.Capacity - 1);
            s = start;
            while (v.NextResource(ref s, out _)) Assert.InRange(s, 0, sim.World.Resources.Capacity - 1);
            Assert.Equal(sim.World.Resources.Capacity, s);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerView(sim.World, Players));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerView(sim.World, int.MinValue));
    }

    /// <summary>
    /// A unit that becomes stealthed by its own cast inside a tick (the cast resolves in the ability phase) is hidden from a
    /// non-detecting enemy's view read right after that tick: no tick of lag (QA focus: "a unit stealthed in phase 5 of the
    /// same tick the view is read").
    /// </summary>
    [Fact]
    public void AUnitStealthedByItsOwnCast_IsHidden_RightAfterTheTickItCast()
    {
        Simulation sim = StealthTests.Scene(players: 2, combat: false);
        World w = sim.World;
        int heavy = Data.FindUnit("malazan_heavy_infantry");
        EntityHandle caster = Place(sim, 1, heavy, At(sim, 20, 20));
        Place(sim, 0, Data.FindUnit("malazan_crossbowman"), At(sim, 25, 20)); // 5 cells off: sees it, outside the 3 m proximity
        FogMaps.RunThroughNextUpdate(sim);
        var v0 = new PlayerView(w, 0);
        Assert.True(v0.TryGetUnit(caster.Index, out _));
        sim.Enqueue(Command.UseAbility(1, caster, 0, w.Units.Position[caster.Index]));
        int ticks = 0;
        while (!w.IsStealthed(caster.Index) && ticks < 40)
        {
            Assert.True(v0.TryGetUnit(caster.Index, out _), $"hidden at tick {sim.TickNumber} before its stealth started");
            sim.Tick();
            ticks++;
        }
        Assert.True(w.IsStealthed(caster.Index), "the stealth cast never resolved");
        Assert.False(v0.TryGetUnit(caster.Index, out _));
        int s = -1;
        while (v0.NextUnit(ref s, out SeenUnit u)) Assert.NotEqual(caster.Index, u.Handle.Index);
    }

    /// <summary>
    /// A high-ground reveal belongs to the unit it was set on, not its slot: a unit revealed to player 0 dies, a new enemy unit
    /// takes the slot out of player 0's sight while the reveal would still run; both the random access and the cursor hide
    /// it. (A mutant of <c>FogStore.NextSeenUnit</c> without the generation test survived both fuzzes; this row kills it.)
    /// </summary>
    [Fact]
    public void ARevealOnADeadUnit_IsNotInheritedByTheUnitThatTakesItsSlot()
    {
        Simulation sim = Flat(size: 64, players: 3);
        World w = sim.World;
        Place(sim, 0, Laborer, At(sim, 5, 5)); // player 0 sees its corner only
        EntityHandle first = Place(sim, 1, HeavyInfantry, At(sim, 50, 50));
        FogMaps.RunThroughNextUpdate(sim);
        w.Fog.Reveal(first, 0, sim.TickNumber + 1000);
        var v0 = new PlayerView(w, 0);
        Assert.True(v0.TryGetUnit(first.Index, out _), "the revealed unit is not answered");
        w.Units.Free(first);
        EntityHandle second = Place(sim, 2, HeavyInfantry, At(sim, 52, 50));
        Assert.Equal(first.Index, second.Index);
        Assert.NotEqual(first.Generation, second.Generation);
        Assert.False(w.Fog.CanSeeUnit(0, second.Index));
        Assert.False(v0.TryGetUnit(second.Index, out _));
        int s = -1;
        while (v0.NextUnit(ref s, out SeenUnit u)) Assert.NotEqual(second.Index, u.Handle.Index);
        sim.Tick();
        Assert.False(v0.TryGetUnit(second.Index, out _));
        s = -1;
        while (v0.NextUnit(ref s, out SeenUnit u)) Assert.NotEqual(second.Index, u.Handle.Index);
    }

    /// <summary>
    /// docs/02 "AI opponent": "The AI sees only its own fog-of-war view. It never reads hidden enemy state." A tree the player
    /// explored and no longer sees, felled out of its sight, is the world changing where the player can't look: the view
    /// should keep answering it (as the ghost list does for a building that died unseen) until the player sees its ground.
    /// M5-1 dropped it at once (BUG-0540); M5-2 keeps it in the fog's node memory (docs/03 "Implementation (M5-1)", Lifetime).
    /// </summary>
    [Fact]
    public void ANodeDepletedOutOfSight_IsStillAnswered_UntilItsGroundIsSeen()
    {
        Simulation sim = ResourceScene(out EntityHandle node);
        World w = sim.World;
        var v0 = new PlayerView(w, 0);
        Assert.True(v0.TryGetResource(node.Index, out SeenResource known));
        Assert.Equal(-1, known.Remaining); // out of sight: amount unknown
        w.Resources.Take(node, int.MaxValue); // felled by someone else, out of player 0's sight
        Assert.False(w.Resources.Alive[node.Index]);
        sim.Tick();
        Assert.True(v0.TryGetResource(node.Index, out _), "player 0 learned a node it can't see was felled");
    }

    /// <summary>A flat map with player 0 having explored a node and walked away (the node explored, not visible).</summary>
    private static Simulation ResourceScene(out EntityHandle node)
    {
        Simulation sim = Flat(size: 64);
        World w = sim.World;
        int tree = -1;
        for (int r = 0; r < w.Data.Resources.Length && tree < 0; r++)
            if (w.Data.Resources[r].FootprintWidth == 1 && w.Data.Resources[r].FootprintHeight == 1) tree = r;
        Assert.True(tree >= 0, "no 1 x 1 resource type");
        Assert.True(w.Resources.Spawn(tree, 40 * w.NavGrid.Width + 40, 100, out node));
        EntityHandle scout = Place(sim, 0, Laborer, At(sim, 38, 40));
        FogMaps.RunThroughNextUpdate(sim);
        Assert.True(w.Fog.IsVisible(0, 40 * w.NavGrid.Width + 40));
        w.Units.Free(scout);
        Place(sim, 0, Laborer, At(sim, 5, 5));
        FogMaps.RunThroughNextUpdate(sim);
        Assert.False(w.Fog.IsVisible(0, 40 * w.NavGrid.Width + 40));
        Assert.True(w.Fog.IsExplored(0, 40 * w.NavGrid.Width + 40));
        return sim;
    }
}
