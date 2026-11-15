using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Orders;
using Rts.Sim.ViewApi;
using Rts.Sim.Tests.ViewApi;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M6-V22): attacks on <see cref="OrderPath"/>, the read-only order reader behind the queued-orders drawing and the
/// first-Shift-building rule: a worker walking its cargo home, a builder whose site is cancelled, a queued Attack whose
/// target's slot is reused (unit and building), and a seeded command fuzz that reads every slot every tick and checks the
/// path's shape against the store, with a never-read twin's hash.
/// </summary>
public class OrderPathQaTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static int Billet => TestSim.Data.FindBuilding("malazan_billet");

    private static int Depot => TestSim.Data.FindBuilding("malazan_depot");

    private static int Mage => TestSim.Data.FindUnit("malazan_cadre_mage");

    private static void Apply(Simulation sim)
    {
        sim.Tick();
        sim.Tick();
    }

    private static OrderPoint[] Read(Simulation sim, EntityHandle unit)
    {
        var buffer = new OrderPoint[OrderPath.MaxPoints];
        int n = OrderPathTests.Reader(sim.World).Fill(unit, buffer);
        return buffer.AsSpan(0, n).ToArray();
    }

    // A worker walking a full load to the drop-off (Moving, GatherNode still set) is on its gather loop: a first Shift
    // placement replaces (a Build queued behind it would never start). Same while it stands Returning or Gathering.
    [Fact]
    public void AWorkerCarryingCargoHome_IsStillOnItsLoop_ReplacesNotQueues()
    {
        Simulation sim = Flat(units: 16);
        World w = sim.World;
        UnitStore u = w.Units;
        ResourceMaps.Spawn(w, ResourceMaps.Tree, 10, 30, ResourceMaps.TreeWood);
        Assert.True(w.Buildings.Spawn(0, Depot, 10 * w.NavGrid.Width + 34, out _));
        EntityHandle worker = Place(sim, 0, Laborer, At(sim, 10, 27));
        sim.Enqueue(Command.Gather(0, worker, At(sim, 10, 30)));
        Apply(sim);
        OrderPath reader = OrderPathTests.Reader(w);
        bool sawGathering = false, sawWalkingHome = false;
        for (int t = 0; t < 2000 && !sawWalkingHome; t++)
        {
            sim.Tick();
            if (!EconomySystem.OnLoop(u, worker.Index)) continue;
            if (u.State[worker.Index] == UnitState.Gathering) sawGathering = true;
            if (u.Cargo[worker.Index] > 0 && u.State[worker.Index] == UnitState.Moving) sawWalkingHome = true;
            Assert.False(reader.QueuesBehind(worker.Index), $"tick {t}: on its loop ({u.State[worker.Index]}, cargo {u.Cargo[worker.Index]}) the rule said queue");
            OrderPoint[] p = Read(sim, worker);
            Assert.True(p.Length == 1 && p[0].IsCurrent && p[0].Kind == CommandKind.Gather, $"tick {t}: path {p.Length} [{(p.Length > 0 ? p[0].Kind : CommandKind.Noop)}]");
        }
        Assert.True(sawGathering && sawWalkingHome, $"gathering seen {sawGathering}, walking home with cargo seen {sawWalkingHome}");
    }

    // A builder whose site is cancelled: once the cancel applies, the builder stands Idle with no build target, reads no
    // path, and the rule replaces. Never a current Build point on a dead site.
    [Fact]
    public void ABuilderWhoseSiteIsCancelled_ReadsNothing_AndTheRuleReplaces()
    {
        Simulation sim = Flat(units: 16);
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle builder = Place(sim, 0, Laborer, At(sim, 30, 36));
        int anchor = 40 * w.NavGrid.Width + 36;
        Vector2 point = PlacementGhost.AnchorPoint(w.NavGrid, anchor);
        Assert.True(w.Buildings.Spawn(0, Billet, anchor, out EntityHandle site, site: true));
        sim.Enqueue(Command.Build(0, builder, Billet, point));
        Apply(sim);
        Assert.Equal(site, u.BuildTarget[builder.Index]);
        OrderPath reader = OrderPathTests.Reader(w);
        Assert.True(reader.QueuesBehind(builder.Index));
        Assert.Equal(CommandKind.Build, Read(sim, builder).Single().Kind);
        // Cancel and look at every tick on the way: a dead site never yields a point.
        sim.Enqueue(Command.Cancel(0, point));
        for (int t = 0; t < 4; t++)
        {
            sim.Tick();
            OrderPoint[] p = Read(sim, builder);
            if (!w.Buildings.IsAlive(site))
                Assert.True(p.Length == 0 || p[0].Kind != CommandKind.Build, $"tick {t}: a Build point on a cancelled site");
        }
        Assert.False(w.Buildings.IsAlive(site));
        Assert.Empty(Read(sim, builder));
        Assert.False(reader.QueuesBehind(builder.Index), $"cancelled site: state {u.State[builder.Index]}, build {u.BuildTarget[builder.Index]}");
    }

    // A queued Attack whose target died and whose slot was reused by a new unit (another owner, a new generation) is not
    // live and carries no place: the drawing must never follow the newcomer.
    [Fact]
    public void AQueuedAttack_WhoseTargetSlotIsReusedByANewUnit_IsNotLive()
    {
        Simulation sim = Flat(units: 8);
        UnitStore u = sim.World.Units;
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 6, 20));
        EntityHandle enemy = Place(sim, 1, Laborer, At(sim, 20, 30));
        sim.Enqueue(Command.HoldPosition(1, enemy));
        Spot(sim, 0, enemy);
        sim.Enqueue(Command.Move(0, a, At(sim, 44, 20)));
        Apply(sim);
        sim.Enqueue(Command.Attack(0, a, enemy, isBuilding: false, queued: true));
        Apply(sim);
        Assert.Equal(1, u.QueueCount[a.Index]);
        u.Free(enemy);
        EntityHandle newcomer = Place(sim, 1, HeavyInfantry, At(sim, 30, 10));
        Assert.Equal(enemy.Index, newcomer.Index);
        Assert.NotEqual(enemy.Generation, newcomer.Generation);
        OrderPoint q = Read(sim, a)[1];
        Assert.True(q.Kind == CommandKind.Attack && q.Target == enemy && !q.TargetLive && q.Point == Vector2.Zero,
            $"live {q.TargetLive}, point {q.Point}, newcomer at {u.Position[newcomer.Index]}");
    }

    // The same for a building target destroyed and its slot reused by a new building.
    [Fact]
    public void AQueuedAttack_WhoseBuildingSlotIsReused_IsNotLive()
    {
        Simulation sim = Flat(units: 8);
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 6, 20));
        int tentType = TestSim.Data.FindBuilding("whirlwind_tent");
        Assert.True(w.Buildings.Spawn(1, tentType, 30 * w.NavGrid.Width + 30, out EntityHandle tent));
        Spotter(sim, 0, At(sim, 28, 28));
        // Player 1 keeps a unit, so the defeat rule doesn't clear its buildings.
        EntityHandle keeper = Place(sim, 1, Laborer, At(sim, 44, 4));
        sim.Enqueue(Command.HoldPosition(1, keeper));
        sim.Enqueue(Command.Move(0, a, At(sim, 6, 44)));
        Apply(sim);
        sim.Enqueue(Command.Attack(0, a, tent, isBuilding: true, queued: true));
        Apply(sim);
        Assert.True(1 == u.QueueCount[a.Index], $"queue {u.QueueCount[a.Index]}, ghost {w.Fog.HasGhost(0, tent)}, state {u.State[a.Index]}, alive {w.Buildings.IsAlive(tent)}");
        Assert.True(Read(sim, a)[1].TargetLive);
        Assert.True(w.Buildings.Free(tent));
        Assert.True(w.Buildings.Spawn(1, tentType, 10 * w.NavGrid.Width + 40, out EntityHandle again));
        Assert.Equal(tent.Index, again.Index);
        OrderPoint q = Read(sim, a)[1];
        Assert.True(q.TargetIsBuilding && !q.TargetLive && q.Point == Vector2.Zero, $"live {q.TargetLive}, point {q.Point}");
    }

    // Seeded command fuzz (every order kind, queued or not, to live, dead and stale handles), the reader read on every slot
    // every tick: the path's shape matches the store, every point is finite and on the map, a live Attack point is the
    // target's position, the building rule never queues behind a loop, and the read sim hashes as a never-read twin.
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(7UL)]
    [InlineData(11UL)]
    [InlineData(42UL)]
    public void Fuzz_EveryKindQueuedOrNot_PathMatchesTheStore_AndTheTwinHashes(ulong seed)
    {
        Simulation a = FuzzScene(seed), b = FuzzScene(seed);
        World w = a.World;
        UnitStore u = w.Units;
        OrderPath reader = OrderPathTests.Reader(w);
        var buffer = new OrderPoint[OrderPath.MaxPoints];
        float size = w.NavGrid.Width * Rts.Sim.Map.MapConstants.CellSize;
        var rng = new Random((int)seed * 7919);
        var rngTwin = new Random((int)seed * 7919);
        int points = 0, queuedSeen = 0, attackLive = 0, attackDead = 0;
        for (int tick = 0; tick < 1500; tick++)
        {
            FuzzOrders(a, rng);
            FuzzOrders(b, rngTwin);
            for (int i = 0; i < u.Capacity; i++)
            {
                var h = new EntityHandle(i, u.Generation[i]);
                int n = reader.Fill(h, buffer);
                if (!u.Alive[i])
                {
                    Assert.Equal(0, n);
                    continue;
                }
                Assert.InRange(n, 0, OrderPath.MaxPoints);
                int current = n > 0 && buffer[0].IsCurrent ? 1 : 0;
                Assert.Equal(u.QueueCount[i], n - current);
                for (int k = 0; k < n; k++)
                {
                    OrderPoint p = buffer[k];
                    Assert.True(k == 0 || !p.IsCurrent, $"seed {seed} tick {tick} slot {i}: a current point at {k}");
                    if (!p.IsCurrent)
                        Assert.Equal(u.QueueKind[i * OrderConstants.QueueCapacity + k - current], p.Kind);
                    Assert.True(float.IsFinite(p.Point.X) && float.IsFinite(p.Point.Y), $"seed {seed} tick {tick} slot {i} point {k}: {p.Point}");
                    if (p.Kind == CommandKind.Attack)
                    {
                        if (p.TargetLive)
                        {
                            attackLive++;
                            if (!p.TargetIsBuilding) Assert.Equal(u.Position[p.Target.Index], p.Point);
                        }
                        else
                        {
                            attackDead++;
                            Assert.Equal(Vector2.Zero, p.Point);
                        }
                    }
                    else if (p.HasPlace)
                    {
                        Assert.True(p.Point.X >= 0 && p.Point.Y >= 0 && p.Point.X <= size && p.Point.Y <= size,
                            $"seed {seed} tick {tick} slot {i} point {k} ({p.Kind}): {p.Point} off the {size} m map");
                    }
                    if (!p.IsCurrent) queuedSeen++;
                    points++;
                }
                if (EconomySystem.OnLoop(u, i)) Assert.False(reader.QueuesBehind(i));
            }
            ulong before = a.StateHash();
            Assert.Equal(before, a.StateHash());
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed} tick {a.TickNumber}: read sim diverged from its twin");
        }
        output.WriteLine($"seed {seed}: {points} points read, {queuedSeen} queued, Attack live {attackLive} / dead {attackDead}");
        Assert.True(points > 2000 && queuedSeen > 500 && attackLive > 0, $"points {points}, queued {queuedSeen}, attack live {attackLive} dead {attackDead}");
    }

    private static Simulation FuzzScene(ulong seed)
    {
        Simulation sim = Flat(units: 40, seed: seed);
        World w = sim.World;
        ResourceMaps.Spawn(w, ResourceMaps.Tree, 10, 30, ResourceMaps.TreeWood);
        ResourceMaps.Spawn(w, ResourceMaps.Tree, 12, 30, ResourceMaps.TreeWood);
        Assert.True(w.Buildings.Spawn(0, Depot, 10 * w.NavGrid.Width + 34, out _));
        Assert.True(w.Buildings.Spawn(1, TestSim.Data.FindBuilding("whirlwind_tent"), 36 * w.NavGrid.Width + 36, out _));
        for (int k = 0; k < 6; k++) Place(sim, 0, HeavyInfantry, At(sim, 6, 6 + 2 * k));
        for (int k = 0; k < 5; k++) Place(sim, 0, Laborer, At(sim, 12 + 2 * k, 27));
        Place(sim, 0, Mage, At(sim, 6, 20));
        for (int k = 0; k < 6; k++) Place(sim, 1, HeavyInfantry, At(sim, 40, 6 + 3 * k));
        for (int k = 0; k < 2; k++) Place(sim, 1, Laborer, At(sim, 38, 30 + 2 * k));
        return sim;
    }

    // Up to three random orders a tick from either seat, to a random slot with its current, an older or a newer generation.
    private static void FuzzOrders(Simulation sim, Random rng)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        int orders = rng.Next(4);
        for (int o = 0; o < orders; o++)
        {
            int slot = rng.Next(u.Capacity);
            int gen = u.Generation[slot] + (rng.Next(10) == 0 ? rng.Next(3) - 1 : 0);
            var h = new EntityHandle(slot, gen);
            int player = u.Alive[slot] ? u.Owner[slot] : rng.Next(2);
            if (rng.Next(20) == 0) player = 1 - player;
            bool queued = rng.Next(3) != 0;
            Vector2 p = At(sim, 2 + rng.Next(44), 2 + rng.Next(44), (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f);
            int other = rng.Next(u.Capacity);
            var target = new EntityHandle(other, u.Generation[other]);
            Command c = rng.Next(10) switch
            {
                0 => Command.Move(player, h, p, queued),
                1 => Command.AttackMove(player, h, p, queued),
                2 => Command.Attack(player, h, target, isBuilding: false, queued: queued),
                3 => Command.Gather(player, h, At(sim, 10 + 2 * rng.Next(2), 30), queued),
                4 => Command.Build(player, h, Billet, PlacementGhost.AnchorPoint(w.NavGrid, (8 + rng.Next(30)) * w.NavGrid.Width + 8 + rng.Next(30)), queued),
                5 => Command.Repair(player, h, At(sim, 34, 10), queued),
                6 => Command.Stop(player, h, queued),
                7 => Command.HoldPosition(player, h, queued),
                8 => Command.UseAbility(player, h, 0, p, queued),
                _ => Command.Attack(player, h, w.Buildings.HandleOf(rng.Next(2)), isBuilding: true, queued: queued),
            };
            sim.Enqueue(c);
        }
    }
}
