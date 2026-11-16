using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>
/// M6-V22: <see cref="OrderPath"/>, the order path the queued-orders drawing reads (current order's place, then each queue
/// entry), its first-Shift-building rule <see cref="OrderPath.QueuesBehind"/>, and the read-only proof (a twin never read
/// has the same state hash every tick).
/// </summary>
public class OrderPathTests
{
    private static int Billet => TestSim.Data.FindBuilding("malazan_billet");

    private static int Mage => TestSim.Data.FindUnit("malazan_cadre_mage");

    private static int Shaman => TestSim.Data.FindUnit("teblor_shaman");

    // An own Billet site anchored at cell (x, y) (straight into the store), so a Build there joins it without paying.
    private static int Site(Simulation sim, int x, int y)
    {
        int anchor = y * sim.World.NavGrid.Width + x;
        Assert.True(sim.World.Buildings.Spawn(0, Billet, anchor, out _, site: true));
        return anchor;
    }

    private static void Apply(Simulation sim)
    {
        sim.Tick();
        sim.Tick();
    }

    /// <summary>The reader over a world's own unit arrays, as the view builds it (<c>OrderPathView.NewReader</c>).</summary>
    internal static OrderPath Reader(World w)
    {
        UnitStore u = w.Units;
        return new OrderPath(w.Data, w.NavGrid, w.Buildings,
            u.Alive, u.Generation, u.TypeId, u.Position, u.State, u.Goal, u.Mode, u.AnchorPosition, u.Target, u.TargetIsBuilding,
            u.CastAbility, u.CastPoint, u.BuildTarget, u.GatherNode, u.GatherSite, u.QueueCount, u.QueueKind, u.QueuePosition, u.QueueTypeId);
    }

    private static OrderPoint[] Read(Simulation sim, EntityHandle unit, int room = OrderPath.MaxPoints)
    {
        var buffer = new OrderPoint[room];
        int n = Reader(sim.World).Fill(unit, buffer);
        return buffer.AsSpan(0, n).ToArray();
    }

    private static void Near(Vector2 want, Vector2 got, string what) =>
        Assert.True(Vector2.Distance(want, got) < 1e-3f, $"{what}: {got}, want {want}");

    // A worker walking a Move with eight queued entries: Move, AttackMove, Attack (unit), Attack (building), Gather,
    // Build, Repair, Stop. The path is the current Move's goal, then the eight in order with their places.
    [Fact]
    public void AWorkerWithACurrentMoveAndEightQueuedKinds_ReadsThemInOrder()
    {
        Simulation sim = Flat(units: 32);
        World w = sim.World;
        UnitStore u = w.Units;
        BuildingStore b = w.Buildings;
        EntityHandle worker = Place(sim, 0, Laborer, At(sim, 6, 24));
        EntityHandle enemy = Place(sim, 1, Laborer, At(sim, 14, 30));
        sim.Enqueue(Command.HoldPosition(1, enemy));
        Spot(sim, 0, enemy);
        Assert.True(b.Spawn(1, TestSim.Data.FindBuilding("whirlwind_tent"), 30 * w.NavGrid.Width + 30, out EntityHandle tent));
        Spotter(sim, 0, At(sim, 28, 28));
        Vector2 far = At(sim, 44, 24), move = At(sim, 10, 34), attackMove = At(sim, 12, 36), gather = At(sim, 8, 40), repair = At(sim, 18, 40);
        Vector2 build = Rts.Sim.ViewApi.PlacementGhost.AnchorPoint(w.NavGrid, 10 * w.NavGrid.Width + 20);
        sim.Enqueue(Command.Move(0, worker, far));
        Apply(sim);
        sim.Enqueue(Command.Move(0, worker, move, queued: true));
        sim.Enqueue(Command.AttackMove(0, worker, attackMove, queued: true));
        sim.Enqueue(Command.Attack(0, worker, enemy, isBuilding: false, queued: true));
        sim.Enqueue(Command.Attack(0, worker, tent, isBuilding: true, queued: true));
        sim.Enqueue(Command.Gather(0, worker, gather, queued: true));
        sim.Enqueue(Command.Build(0, worker, Billet, build, queued: true));
        sim.Enqueue(Command.Repair(0, worker, repair, queued: true));
        sim.Enqueue(Command.Stop(0, worker, queued: true));
        Apply(sim);
        Assert.Equal(8, u.QueueCount[worker.Index]);
        Assert.Equal(UnitState.Moving, u.State[worker.Index]);

        OrderPoint[] path = Read(sim, worker);
        CommandKind[] kinds =
        {
            CommandKind.Move, CommandKind.Move, CommandKind.AttackMove, CommandKind.Attack, CommandKind.Attack, CommandKind.Gather,
            CommandKind.Build, CommandKind.Repair, CommandKind.Stop,
        };
        Assert.Equal(kinds, path.Select(p => p.Kind).ToArray());
        Assert.True(path[0].IsCurrent && path[0].HasPlace);
        Assert.True(path.Skip(1).All(p => !p.IsCurrent));
        Near(u.Goal[worker.Index], path[0].Point, "current Move");
        Near(far, path[0].Point, "current Move's goal");
        Near(move, path[1].Point, "queued Move");
        Near(attackMove, path[2].Point, "queued AttackMove");
        Assert.True(path[3].Target == enemy && !path[3].TargetIsBuilding && path[3].TargetLive && path[3].HasPlace);
        // The reader decodes an Attack entry as the store does (UnitStore.QueuedTarget), both kinds of target.
        int q = worker.Index * Rts.Sim.Orders.OrderConstants.QueueCapacity;
        Assert.Equal(u.QueuedTarget(q + 2), path[3].Target);
        Assert.Equal(u.QueuedTarget(q + 3), path[4].Target);
        Near(u.Position[enemy.Index], path[3].Point, "queued Attack on a unit: where it stands");
        Assert.True(path[4].Target == tent && path[4].TargetIsBuilding && path[4].TargetLive);
        Near(StartBase.FootprintCenter(w.NavGrid, w.Data.Buildings[b.TypeId[tent.Index]], b.Cell[tent.Index]), path[4].Point, "queued Attack on a building: its footprint centre");
        Near(gather, path[5].Point, "queued Gather");
        Near(StartBase.FootprintCenter(w.NavGrid, w.Data.Buildings[Billet], 10 * w.NavGrid.Width + 20), path[6].Point, "queued Build: the footprint centre");
        Near(repair, path[7].Point, "queued Repair");
        Assert.False(path[8].HasPlace);
        Assert.True(path.Take(8).All(p => p.HasPlace));
    }

    // A caster: UseAbility at a point (Telas Fire) and HoldPosition; a Teblor Shaman's no-target Frenzy and a Stop: no place.
    [Fact]
    public void UseAbilityAtAPoint_HasItsPlace_ANoTargetCastAndHold_HaveNone()
    {
        Simulation sim = Flat(units: 32);
        UnitStore u = sim.World.Units;
        EntityHandle mage = Place(sim, 0, Mage, At(sim, 6, 20));
        EntityHandle shaman = Place(sim, 0, Shaman, At(sim, 6, 28));
        Vector2 cast = At(sim, 20, 30);
        sim.Enqueue(Command.Move(0, mage, At(sim, 44, 20)));
        sim.Enqueue(Command.Move(0, shaman, At(sim, 44, 28)));
        Apply(sim);
        sim.Enqueue(Command.UseAbility(0, mage, 0, cast, queued: true));
        sim.Enqueue(Command.HoldPosition(0, mage, queued: true));
        sim.Enqueue(Command.UseAbility(0, shaman, 0, u.Position[shaman.Index], queued: true));
        sim.Enqueue(Command.Stop(0, shaman, queued: true));
        Apply(sim);

        OrderPoint[] m = Read(sim, mage);
        Assert.Equal(new[] { CommandKind.Move, CommandKind.UseAbility, CommandKind.HoldPosition }, m.Select(p => p.Kind).ToArray());
        Assert.True(m[1].HasPlace);
        Near(cast, m[1].Point, "queued Telas Fire");
        Assert.False(m[2].HasPlace);

        OrderPoint[] s = Read(sim, shaman);
        Assert.Equal(new[] { CommandKind.Move, CommandKind.UseAbility, CommandKind.Stop }, s.Select(p => p.Kind).ToArray());
        Assert.False(s[1].HasPlace);
        Assert.False(s[2].HasPlace);
    }

    // An empty queue: only the current order's place (a walk), or nothing (standing Idle).
    [Fact]
    public void AnEmptyQueue_GivesOnlyTheCurrentOrdersPoint_OrNothing()
    {
        Simulation sim = Flat(units: 16);
        EntityHandle walker = Place(sim, 0, HeavyInfantry, At(sim, 6, 20));
        EntityHandle idle = Place(sim, 0, HeavyInfantry, At(sim, 6, 30));
        sim.Enqueue(Command.Move(0, walker, At(sim, 40, 20)));
        Apply(sim);
        OrderPoint[] p = Read(sim, walker);
        Assert.Single(p);
        Assert.True(p[0].IsCurrent && p[0].Kind == CommandKind.Move);
        Near(At(sim, 40, 20), p[0].Point, "the walk's goal");
        Assert.Empty(Read(sim, idle));
        // Arrived: Idle, nothing.
        RunUntil(sim, () => sim.World.Units.State[walker.Index] == UnitState.Idle, 1200);
        Assert.Empty(Read(sim, walker));
    }

    // The current order's kinds beyond a walk: an attack-move leg (its end), an explicit Attack (the target), a gather loop
    // (the node), a build (the site's centre).
    [Fact]
    public void TheCurrentOrder_AttackMoveAttackGatherAndBuild_HaveTheirPlaces()
    {
        Simulation sim = Flat(units: 32);
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle mover = Place(sim, 0, HeavyInfantry, At(sim, 6, 10));
        EntityHandle attacker = Place(sim, 0, HeavyInfantry, At(sim, 6, 16));
        EntityHandle enemy = Place(sim, 1, Laborer, At(sim, 30, 16));
        sim.Enqueue(Command.HoldPosition(1, enemy));
        Spot(sim, 0, enemy);
        EntityHandle tree = ResourceMaps.Spawn(w, ResourceMaps.Tree, 10, 30, ResourceMaps.TreeWood);
        EntityHandle gatherer = Place(sim, 0, Laborer, At(sim, 10, 27));
        EntityHandle builder = Place(sim, 0, Laborer, At(sim, 30, 36));
        int anchor = Site(sim, 36, 40);
        sim.Enqueue(Command.AttackMove(0, mover, At(sim, 44, 10)));
        sim.Enqueue(Command.Attack(0, attacker, enemy, isBuilding: false));
        sim.Enqueue(Command.Gather(0, gatherer, At(sim, 10, 30)));
        sim.Enqueue(Command.Build(0, builder, Billet, PlacementGhost.AnchorPoint(w.NavGrid, anchor)));
        Apply(sim);

        OrderPoint am = Read(sim, mover).Single();
        Assert.True(am.IsCurrent && am.Kind == CommandKind.AttackMove && am.HasPlace);
        Near(u.AnchorPosition[mover.Index], am.Point, "the leg's end");
        OrderPoint at = Read(sim, attacker).Single();
        Assert.True(at.Kind == CommandKind.Attack && at.Target == enemy && at.TargetLive);
        Near(u.Position[enemy.Index], at.Point, "the target");
        Assert.True(EconomySystem.OnLoop(u, gatherer.Index));
        OrderPoint g = Read(sim, gatherer).Single();
        Assert.True(g.Kind == CommandKind.Gather);
        Near(u.GatherSite[gatherer.Index], g.Point, "the node");
        OrderPoint bd = Read(sim, builder).Single();
        Assert.Equal(CommandKind.Build, bd.Kind);
        Near(StartBase.FootprintCenter(w.NavGrid, w.Data.Buildings[Billet], anchor), bd.Point, "the site's centre");
        Assert.True(w.Resources.IsAlive(tree));
    }

    // A queued Attack whose target died is still an entry (the sim drops it when it starts), marked not live, no place read.
    [Fact]
    public void AQueuedAttackOnADeadTarget_IsNotLive_AndCarriesNoPoint()
    {
        Simulation sim = Flat(units: 16);
        UnitStore u = sim.World.Units;
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 6, 20));
        EntityHandle enemy = Place(sim, 1, Laborer, At(sim, 20, 30));
        sim.Enqueue(Command.HoldPosition(1, enemy));
        Spot(sim, 0, enemy);
        sim.Enqueue(Command.Move(0, a, At(sim, 44, 20)));
        Apply(sim);
        sim.Enqueue(Command.Attack(0, a, enemy, isBuilding: false, queued: true));
        Apply(sim);
        u.Free(enemy);
        OrderPoint[] p = Read(sim, a);
        Assert.Equal(2, p.Length);
        Assert.True(p[1].Kind == CommandKind.Attack && p[1].Target == enemy && !p[1].TargetLive && p[1].Point == Vector2.Zero);
    }

    // A dead slot, a stale handle and a slot recycled for a new unit all read nothing.
    [Fact]
    public void ADeadOrStaleSlot_GivesNothing()
    {
        Simulation sim = Flat(units: 4);
        UnitStore u = sim.World.Units;
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 6, 20));
        sim.Enqueue(Command.Move(0, a, At(sim, 40, 20)));
        Apply(sim);
        sim.Enqueue(Command.Move(0, a, At(sim, 40, 30), queued: true));
        Apply(sim);
        Assert.Equal(2, Read(sim, a).Length);
        Assert.Empty(Read(sim, new EntityHandle(a.Index, a.Generation + 1)));
        u.Free(a);
        Assert.Empty(Read(sim, a));
        EntityHandle b = Place(sim, 0, HeavyInfantry, At(sim, 6, 20));
        Assert.Equal(a.Index, b.Index);
        sim.Enqueue(Command.Move(0, b, At(sim, 40, 20)));
        Apply(sim);
        Assert.Empty(Read(sim, a));
        Assert.Single(Read(sim, b));
        Assert.Empty(Read(sim, new EntityHandle(-1, 1)));
        Assert.Empty(Read(sim, new EntityHandle(99, 1)));
    }

    // A buffer shorter than the path is filled to its length, no further.
    [Fact]
    public void AShortBuffer_IsFilledToItsLength()
    {
        Simulation sim = Flat(units: 4);
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 6, 20));
        sim.Enqueue(Command.Move(0, a, At(sim, 40, 20)));
        Apply(sim);
        for (int k = 0; k < 5; k++) sim.Enqueue(Command.Move(0, a, At(sim, 10 + 4 * k, 30), queued: true));
        Apply(sim);
        Assert.Equal(6, Read(sim, a).Length);
        Assert.Equal(3, Read(sim, a, room: 3).Length);
        Assert.Empty(Read(sim, a, room: 0));
    }

    // The first Shift-placed building's rule: replace idleness, queue behind an order that ends or a gather loop (M6-V23: a
    // loop with a queue ends after the trip in hand, M6-S11).
    [Fact]
    public void QueuesBehind_ReplacesIdle_QueuesBehindAGatherLoopOrAnOrderThatEnds()
    {
        Simulation sim = Flat(units: 32);
        World w = sim.World;
        UnitStore u = w.Units;
        ResourceMaps.Spawn(w, ResourceMaps.Tree, 10, 30, ResourceMaps.TreeWood);
        EntityHandle idle = Place(sim, 0, Laborer, At(sim, 6, 6));
        EntityHandle walker = Place(sim, 0, Laborer, At(sim, 6, 10));
        EntityHandle gatherer = Place(sim, 0, Laborer, At(sim, 10, 27));
        // M6-S11: in reach of the tree from the start, with a drop-off standing (a Keep in the far corner), so its queued Move
        // waits for its trip (walking out empty, or with no drop-off, it would start the Move at once).
        Assert.True(w.Buildings.Spawn(0, GatherMaps.Keep, 42 * w.NavGrid.Width + 42, out _));
        EntityHandle gathererQueued = Place(sim, 0, Laborer, At(sim, 11, 30));
        EntityHandle builder = Place(sim, 0, Laborer, At(sim, 30, 36));
        EntityHandle idleQueued = Place(sim, 0, Laborer, At(sim, 40, 6));
        EntityHandle attacker = Place(sim, 0, Laborer, At(sim, 20, 16));
        EntityHandle enemy = Place(sim, 1, Laborer, At(sim, 36, 16));
        sim.Enqueue(Command.HoldPosition(1, enemy));
        Spot(sim, 0, enemy);
        sim.Enqueue(Command.Move(0, walker, At(sim, 44, 10)));
        sim.Enqueue(Command.Gather(0, gatherer, At(sim, 10, 30)));
        sim.Enqueue(Command.Gather(0, gathererQueued, At(sim, 10, 30)));
        sim.Enqueue(Command.Build(0, builder, Billet, PlacementGhost.AnchorPoint(w.NavGrid, Site(sim, 36, 40))));
        sim.Enqueue(Command.Attack(0, attacker, enemy, isBuilding: false));
        Apply(sim);
        sim.Enqueue(Command.Move(0, gathererQueued, At(sim, 20, 20), queued: true));
        Apply(sim);
        Assert.True(EconomySystem.OnLoop(u, gatherer.Index) && EconomySystem.OnLoop(u, gathererQueued.Index));
        Assert.False(EconomySystem.OnLoop(u, walker.Index) || EconomySystem.OnLoop(u, builder.Index));
        Assert.Equal(1, u.QueueCount[gathererQueued.Index]);

        OrderPath reader = Reader(w);
        Assert.False(reader.QueuesBehind(idle.Index), "idle: replace");
        Assert.True(reader.QueuesBehind(walker.Index), "walking a Move: queue");
        Assert.True(reader.QueuesBehind(gatherer.Index), "gather loop: queue (M6-V23)");
        Assert.True(reader.QueuesBehind(gathererQueued.Index), "gather loop with a queue (it finishes its trip first, M6-S11): queue (M6-V23)");
        Assert.True(reader.QueuesBehind(builder.Index), "building: queue");
        Assert.True(reader.QueuesBehind(attacker.Index), "attacking: queue");
        Assert.Equal(CombatMode.Ordered, u.Mode[attacker.Index]);
        // An Idle unit with orders queued (a test seam: its queue would pop next tick): queue behind them.
        u.QueueKind[idleQueued.Index * Rts.Sim.Orders.OrderConstants.QueueCapacity] = CommandKind.Move;
        u.QueueCount[idleQueued.Index] = 1;
        Assert.True(reader.QueuesBehind(idleQueued.Index), "idle with a queue: queue");
        u.Free(walker);
        Assert.False(reader.QueuesBehind(walker.Index), "dead slot");
        Assert.False(reader.QueuesBehind(-1));
        Assert.False(reader.QueuesBehind(u.Capacity));
    }

    // Read-only: twin sims, one read every tick (every unit's path and the building rule), one never read, hash alike every
    // tick for 600 ticks, while units walk, fight, gather, build and pop queued orders.
    [Fact]
    public void ReadingEveryTick_HashEqualsATwinNeverRead_600Ticks()
    {
        Simulation a = Scene(), b = Scene();
        Assert.Equal(b.StateHash(), a.StateHash());
        var buffer = new OrderPoint[OrderPath.MaxPoints];
        OrderPath reader = Reader(a.World);
        long sink = 0;
        int read = 0;
        for (int tick = 0; tick < 600; tick++)
        {
            if (tick % 150 == 0) Orders(a, tick);
            if (tick % 150 == 0) Orders(b, tick);
            ulong before = a.StateHash();
            UnitStore u = a.World.Units;
            for (int i = 0; i < u.Capacity; i++)
            {
                int n = reader.Fill(new EntityHandle(i, u.Generation[i]), buffer);
                read += n;
                for (int k = 0; k < n; k++) sink += (long)buffer[k].Point.X + (int)buffer[k].Kind;
                if (reader.QueuesBehind(i)) sink++;
            }
            Assert.Equal(before, a.StateHash());
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"tick {a.TickNumber}: the read sim diverged from its twin");
        }
        Assert.True(read > 600 && sink != 0, $"read {read} points");
    }

    private static Simulation Scene()
    {
        Simulation sim = Flat(units: 48);
        World w = sim.World;
        ResourceMaps.Spawn(w, ResourceMaps.Tree, 10, 30, ResourceMaps.TreeWood);
        for (int k = 0; k < 6; k++) Place(sim, 0, HeavyInfantry, At(sim, 6, 6 + 2 * k));
        for (int k = 0; k < 3; k++) Place(sim, 0, Laborer, At(sim, 12 + 2 * k, 27));
        Place(sim, 0, Mage, At(sim, 6, 20));
        for (int k = 0; k < 4; k++) Place(sim, 1, HeavyInfantry, At(sim, 40, 6 + 3 * k));
        return sim;
    }

    // The same orders for both twins: walks with queues of every kind, a fight, gathering and a building.
    private static void Orders(Simulation sim, int tick)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        int s = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.Owner[i] != 0) continue;
            var h = new EntityHandle(i, u.Generation[i]);
            int type = u.TypeId[i];
            if (type == Laborer)
            {
                if (s % 3 == 0) sim.Enqueue(Command.Gather(0, h, At(sim, 10, 30)));
                else if (s % 3 == 1) sim.Enqueue(Command.Build(0, h, Billet, PlacementGhost.AnchorPoint(w.NavGrid, (20 + tick / 50) * w.NavGrid.Width + 30)));
                else
                {
                    sim.Enqueue(Command.Move(0, h, At(sim, 30, 40)));
                    sim.Enqueue(Command.Gather(0, h, At(sim, 10, 30), queued: true));
                    sim.Enqueue(Command.Repair(0, h, At(sim, 30, 21), queued: true));
                }
            }
            else
            {
                sim.Enqueue(Command.Move(0, h, At(sim, 20 + s, 10 + (tick / 30))));
                sim.Enqueue(Command.Move(0, h, At(sim, 24, 30), queued: true));
                sim.Enqueue(Command.AttackMove(0, h, At(sim, 38, 10), queued: true));
                if (type == Mage) sim.Enqueue(Command.UseAbility(0, h, 0, At(sim, 30, 12), queued: true));
                sim.Enqueue(s % 2 == 0 ? Command.Stop(0, h, queued: true) : Command.HoldPosition(0, h, queued: true));
            }
            s++;
        }
    }
}
