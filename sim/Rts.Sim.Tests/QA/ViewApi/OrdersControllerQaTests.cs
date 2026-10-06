using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>QA (M2-3): the keyboard order path modelled from the brief (A targeting, Esc, S / H, Shift-queue, groups, Tab) driving a sim that must hash like a bare twin; control-group fuzzing with deaths and recycled slots; double-tap timing; queue overflow.</summary>
/// <remarks>
/// <see cref="Controller"/> is QA's own re-statement of SelectionController's rules from the M2-3
/// brief, not a copy of the production class (which lives in Godot): an order is one command per
/// selected live unit or none (whole-order capacity check), A arms targeting only with a selection,
/// a left click while targeting is the order point and never selects, a right click while targeting
/// cancels and orders nothing, Ctrl + digit wins over Shift + digit, an empty selection never wipes
/// a group, a recall of an empty group changes nothing.
/// </remarks>
public class OrdersControllerQaTests
{
    private readonly ITestOutputHelper _out;

    public OrdersControllerQaTests(ITestOutputHelper output) => _out = output;

    private static SimConfig Cfg(ulong seed, int commandCapacity = 4096) =>
        TestSim.Config(seed, PlayerCount: 2, UnitCapacity: 2000, CommandCapacity: commandCapacity);

    private static List<Command> StartArmies(Simulation sim, int perPlayer)
    {
        var cmds = new List<Command>();
        GameData data = sim.World.Data;
        for (int p = 0; p < 2; p++)
        {
            FactionDef f = data.Factions[p];
            float maxR = 0f;
            foreach (int t in f.Units) maxR = Math.Max(maxR, data.Units[t].Radius);
            Vector2[] spots = StartLayout.Block(sim.World.NavGrid, perPlayer, p == 0, maxR);
            for (int k = 0; k < spots.Length; k++) cmds.Add(Command.SpawnUnit(p, f.Units[k % f.Units.Length], spots[k]));
        }
        return cmds;
    }

    /// <summary>The brief's controller rules over the pure ViewApi helpers; every command goes to both twins.</summary>
    private sealed class Controller
    {
        public readonly SelectionSet Selection;
        public readonly ControlGroups Groups;
        public readonly Subgroups Subgroups;
        public bool Targeting;
        public int Dropped, Issued;
        private readonly Simulation _a, _b;

        public Controller(Simulation a, Simulation b)
        {
            _a = a;
            _b = b;
            Selection = new SelectionSet(a.World.Units.Capacity);
            Groups = new ControlGroups(a.World.Units.Capacity);
            Subgroups = new Subgroups(a.World.Data.Units.Length);
        }

        private UnitStore U => _a.World.Units;

        public void Frame()
        {
            Selection.Prune(U.Alive, U.Generation);
            Groups.Prune(U.Alive, U.Generation);
            Subgroups.Update(Selection.Items, U.TypeId, reset: false);
        }

        public void Order(CommandKind kind, Vector2 target, bool queued)
        {
            Selection.Prune(U.Alive, U.Generation);
            if (Selection.Count == 0) return;
            if (_a.PendingCommandCount + Selection.Count > _a.World.Config.CommandCapacity)
            {
                Dropped++;
                return;
            }
            int before = _a.PendingCommandCount;
            foreach (EntityHandle h in Selection.Items)
            {
                Command c = kind switch
                {
                    CommandKind.Move => Command.Move(0, h, target, queued),
                    CommandKind.AttackMove => Command.AttackMove(0, h, target, queued),
                    CommandKind.Stop => Command.Stop(0, h, queued),
                    _ => Command.HoldPosition(0, h, queued),
                };
                _a.Enqueue(c);
                _b.Enqueue(c);
            }
            Assert.Equal(before + Selection.Count, _a.PendingCommandCount); // never half an order
            Issued += Selection.Count;
        }

        public void LeftClick(bool onMap, Vector2 ground, bool shift, Action select)
        {
            if (Targeting)
            {
                if (!onMap) return; // off the map: nothing, still targeting
                Order(CommandKind.AttackMove, ground, shift);
                Targeting = false;
                return;
            }
            select();
            Selection.Prune(U.Alive, U.Generation);
            Subgroups.Update(Selection.Items, U.TypeId, reset: true);
        }

        public void RightClick(bool onMap, Vector2 ground, bool shift)
        {
            if (Targeting) { Targeting = false; return; }
            if (onMap) Order(CommandKind.Move, ground, shift);
        }

        public void Key(char key, bool shift, bool ctrl)
        {
            switch (key)
            {
                case 'E': Targeting = false; break; // Esc
                case 'A': Targeting = Selection.Count > 0; break;
                case 'S': Targeting = false; Order(CommandKind.Stop, default, shift); break;
                case 'H': Targeting = false; Order(CommandKind.HoldPosition, default, shift); break;
                case 'T': Subgroups.Next(); break;
                default:
                    int g = key - '1';
                    Selection.Prune(U.Alive, U.Generation);
                    if (ctrl) { if (Selection.Count > 0) Groups.Assign(g, Selection.Items); }
                    else if (shift) Groups.Add(g, Selection.Items);
                    else if (Groups.Recall(g, Selection, U.Alive, U.Generation) > 0)
                        Subgroups.Update(Selection.Items, U.TypeId, reset: true);
                    break;
            }
        }
    }

    [Theory]
    [InlineData(3UL, 150)]
    [InlineData(19UL, 400)]
    [InlineData(4242UL, 1000)]
    public void ControllerModel_AllKindsQueuedTargetingGroups_HashEqualsBareTwin_EveryTick(ulong seed, int perPlayer)
    {
        var a = new Simulation(Cfg(seed));
        var b = new Simulation(Cfg(seed));
        World w = a.World;
        UnitStore u = w.Units;
        Heightmap map = w.Heightmap;
        var ctl = new Controller(a, b);
        var rng = new SimRng(seed ^ 0x5EED, RngStream.Combat);
        foreach (Command c in StartArmies(a, perPlayer)) { a.Enqueue(c); b.Enqueue(c); }
        a.Tick(); b.Tick();
        a.Tick(); b.Tick();

        var own = new List<int>();
        int events = 0, attackMoves = 0, deaths = 0, respawnsAsEnemy = 0, recalls = 0, maxQueue = 0;
        for (int tick = 0; tick < 800; tick++)
        {
            ctl.Frame();
            ulong before = a.StateHash();
            int pendingBefore = a.PendingCommandCount;
            int burst = rng.NextInt(0, 4);
            for (int e = 0; e < burst; e++)
            {
                events++;
                bool shift = rng.NextInt(0, 3) == 0, ctrl = rng.NextInt(0, 5) == 0;
                bool onMap = rng.NextInt(0, 8) != 0;
                var ground = new Vector2(rng.NextFloat() * map.Width * MapConstants.CellSize, rng.NextFloat() * map.Height * MapConstants.CellSize);
                switch (rng.NextInt(0, 12))
                {
                    case 0:
                    case 1:
                        bool wasTargeting = ctl.Targeting;
                        int pend = a.PendingCommandCount;
                        ctl.LeftClick(onMap, ground, shift, () =>
                        {
                            // box-select a random patch of own units (Shift adds)
                            own.Clear();
                            for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.Owner[i] == 0) own.Add(i);
                            if (!shift) ctl.Selection.Clear();
                            if (own.Count == 0) return;
                            int from = rng.NextInt(0, own.Count), n = rng.NextInt(1, Math.Min(own.Count, 300) + 1);
                            for (int k = 0; k < n; k++) { int s = own[(from + k) % own.Count]; ctl.Selection.Add(new EntityHandle(s, u.Generation[s])); }
                        });
                        if (wasTargeting && onMap && a.PendingCommandCount > pend) attackMoves++;
                        break;
                    case 2: ctl.RightClick(onMap, ground, shift); break;
                    case 3: ctl.Key('A', shift, ctrl); if (rng.NextInt(0, 3) == 0) ctl.Key('A', shift, ctrl); break;
                    case 4: ctl.Key('E', shift, ctrl); break;
                    case 5: ctl.Key('S', shift, false); break;
                    case 6: ctl.Key('H', shift, false); break;
                    case 7: ctl.Key('T', false, false); break;
                    case 8:
                        // Shift + right-click burst past the 8-order queue: the sim drops the rest silently.
                        for (int k = 0; k < 10; k++)
                            ctl.RightClick(true, new Vector2(rng.NextFloat() * map.Width * MapConstants.CellSize, rng.NextFloat() * map.Height * MapConstants.CellSize), true);
                        break;
                    default:
                        recalls++;
                        ctl.Key((char)('1' + rng.NextInt(0, ControlGroups.Count)), shift, ctrl);
                        break;
                }
            }
            // The hash covers pending commands, so it may move only if the controller enqueued something.
            if (a.PendingCommandCount == pendingBefore) Assert.Equal(before, a.StateHash());

            // Deaths and recycled slots, applied identically to both twins (no combat before M4).
            if (tick % 97 == 50)
            {
                for (int k = 0; k < 20; k++)
                {
                    int s = rng.NextInt(0, u.Capacity);
                    if (!u.Alive[s] || u.Owner[s] != 0) continue;
                    var h = new EntityHandle(s, u.Generation[s]);
                    Vector2 at = u.Position[s];
                    u.Free(h);
                    b.World.Units.Free(h);
                    deaths++;
                    Command spawn = Command.SpawnUnit(1, w.Data.Factions[1].Units[0], at);
                    if (a.PendingCommandCount < w.Config.CommandCapacity) { a.Enqueue(spawn); b.Enqueue(spawn); respawnsAsEnemy++; }
                }
            }

            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: controller-driven sim diverged from its bare twin at tick {a.TickNumber}");

            // Groups and the selection only ever hold the local player's live units after a prune.
            ctl.Frame();
            foreach (EntityHandle h in ctl.Selection.Items) Assert.True(u.Alive[h.Index] && u.Generation[h.Index] == h.Generation && u.Owner[h.Index] == 0);
            for (int g = 0; g < ControlGroups.Count; g++)
                foreach (EntityHandle h in ctl.Groups.Items(g)) Assert.True(u.Alive[h.Index] && u.Generation[h.Index] == h.Generation && u.Owner[h.Index] == 0, $"group {g} holds a non-own or dead unit at slot {h.Index}");
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                Assert.InRange(u.QueueCount[i], 0, OrderConstants.QueueCapacity);
                maxQueue = Math.Max(maxQueue, u.QueueCount[i]);
                Assert.True(float.IsFinite(u.Position[i].X) && float.IsFinite(u.Position[i].Y));
            }
        }
        _out.WriteLine($"seed {seed}, {perPlayer}/player: {events} events, {ctl.Issued} commands, {attackMoves} A-clicks, {ctl.Dropped} dropped orders, " +
            $"{recalls} group keys, {deaths} deaths, {respawnsAsEnemy} enemy respawns, max queue {maxQueue}, final hash {a.StateHash():X16}");
        Assert.True(ctl.Issued > 0 && attackMoves > 0 && deaths > 0);
        Assert.Equal(OrderConstants.QueueCapacity, maxQueue);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void NineGroupsOf1000_DeathsAndRecycledSlots_RecallOnlyTheOriginalLiveHandles(ulong seed)
    {
        const int cap = 2000;
        var store = new UnitStore(cap);
        for (int i = 0; i < cap; i++) store.Alloc();
        var groups = new ControlGroups(cap);
        var model = new List<EntityHandle>[ControlGroups.Count];
        var rng = new SimRng(seed, RngStream.Combat);
        var selection = new SelectionSet(cap);
        for (int g = 0; g < ControlGroups.Count; g++)
        {
            model[g] = new List<EntityHandle>();
            int from = rng.NextInt(0, cap);
            for (int k = 0; k < 1000; k++) { int s = (from + k) % cap; model[g].Add(new EntityHandle(s, store.Generation[s])); }
            groups.Assign(g, model[g].ToArray());
        }
        for (int round = 0; round < 60; round++)
        {
            // Kill some and recycle some (a recycled slot gets a new generation: a different unit).
            for (int k = 0; k < 50; k++)
            {
                int s = rng.NextInt(0, cap);
                if (store.Alive[s]) store.Free(new EntityHandle(s, store.Generation[s]));
            }
            int re = rng.NextInt(0, 40);
            for (int k = 0; k < re && store.FreeCount > 0; k++) store.Alloc();
            if (round % 2 == 0) groups.Prune(store.Alive, store.Generation); // sometimes recall without a frame prune in between

            int g = rng.NextInt(0, ControlGroups.Count);
            switch (rng.NextInt(0, 4))
            {
                case 0:
                    int n = groups.Recall(g, selection, store.Alive, store.Generation);
                    var expect = model[g].Where(h => store.Alive[h.Index] && store.Generation[h.Index] == h.Generation).ToArray();
                    Assert.Equal(expect.Length, n);
                    if (n > 0) Assert.Equal(expect, selection.Items.ToArray());
                    model[g] = expect.ToList();
                    break;
                case 1:
                    // Shift + digit with the current (pruned) selection
                    selection.Prune(store.Alive, store.Generation);
                    groups.Add(g, selection.Items);
                    model[g] = model[g].Where(h => store.Alive[h.Index] && store.Generation[h.Index] == h.Generation).ToList();
                    groups.Prune(store.Alive, store.Generation);
                    foreach (EntityHandle h in selection.Items) if (!model[g].Contains(h)) model[g].Add(h);
                    Assert.Equal(model[g].ToHashSet(), groups.Items(g).ToArray().ToHashSet());
                    break;
                case 2:
                    selection.Prune(store.Alive, store.Generation);
                    groups.Assign(g, selection.Items);
                    model[g] = selection.Items.ToArray().ToList();
                    break;
                default:
                    bool ok = groups.TryMean(g, store.Position, store.Alive, store.Generation, out Vector2 mean);
                    Assert.Equal(model[g].Any(h => store.Alive[h.Index] && store.Generation[h.Index] == h.Generation), ok);
                    Assert.True(float.IsFinite(mean.X) && float.IsFinite(mean.Y));
                    break;
            }
        }
        groups.Prune(store.Alive, store.Generation);
        for (int g = 0; g < ControlGroups.Count; g++)
            foreach (EntityHandle h in groups.Items(g)) Assert.True(store.Alive[h.Index] && store.Generation[h.Index] == h.Generation);
    }

    [Fact]
    public void DoubleTap_Window_Rules()
    {
        var groups = new ControlGroups(4);
        Assert.False(groups.Tap(0, 10.0));
        Assert.True(groups.Tap(0, 10.299));   // inside
        Assert.False(groups.Tap(0, 10.3));    // consumed: a third tap starts a new pair
        Assert.False(groups.Tap(0, 10.601 + 0.0)); // 301 ms later: outside
        Assert.False(groups.Tap(1, 10.7));    // another group never pairs
        Assert.False(groups.Tap(0, 10.8));    // the 1-tap in between broke the pair
        Assert.True(groups.Tap(0, 11.0));
        Assert.False(groups.Tap(2, 20.0));
        Assert.False(groups.Tap(2, 19.9));    // clock went backwards: never a double-tap
        Assert.Throws<ArgumentOutOfRangeException>(() => groups.Tap(9, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => groups.Tap(-1, 0));
    }

    [Fact]
    public void DoubleTap_Exactly300ms_GivesTheSameAnswerAtAnyClockValue()
    {
        // SelectionController passes Time.GetTicksMsec() / 1000.0, so gaps are whole milliseconds.
        int doubled = 0, single = 0;
        for (ulong t0 = 0; t0 < 20_000; t0 += 7)
        {
            var groups = new ControlGroups(1);
            groups.Tap(0, t0 / 1000.0);
            if (groups.Tap(0, (t0 + 300) / 1000.0)) doubled++;
            else single++;
        }
        _out.WriteLine($"exactly 300 ms: {doubled} double-taps, {single} singles");
        Assert.True(doubled == 0 || single == 0, $"exactly 300 ms is a double-tap {doubled} times and not {single} times");
    }

    [Fact]
    public void NinthQueuedOrder_1000Units_DroppedSilently_NothingCrashes_TwinsAgree()
    {
        var a = new Simulation(Cfg(5UL, 32768));
        var b = new Simulation(Cfg(5UL, 32768));
        foreach (Command c in StartArmies(a, 1000)) { a.Enqueue(c); b.Enqueue(c); }
        a.Tick(); b.Tick();
        a.Tick(); b.Tick(); // a command enqueued before tick N applies on the second Tick()
        UnitStore u = a.World.Units;
        var mine = new List<EntityHandle>();
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.Owner[i] == 0) mine.Add(new EntityHandle(i, u.Generation[i]));
        Assert.Equal(1000, mine.Count);
        Vector2 centre = new(a.World.Heightmap.Width * MapConstants.CellSize / 2f, a.World.Heightmap.Height * MapConstants.CellSize / 2f);
        // A plain move, then 12 queued orders of every kind per unit (the queue holds 8).
        for (int k = 0; k < 13; k++)
        {
            foreach (EntityHandle h in mine)
            {
                Vector2 p = centre + new Vector2(k * 3f - 18f, (h.Index % 10) - 5f);
                Command c = (k % 4) switch
                {
                    0 => Command.Move(0, h, p, k > 0),
                    1 => Command.AttackMove(0, h, p, true),
                    2 => Command.HoldPosition(0, h, true),
                    _ => Command.Stop(0, h, true),
                };
                a.Enqueue(c);
                b.Enqueue(c);
            }
        }
        a.Tick(); b.Tick();
        a.Tick(); b.Tick();
        foreach (EntityHandle h in mine) Assert.Equal(OrderConstants.QueueCapacity, u.QueueCount[h.Index]);
        for (int t = 0; t < 200; t++)
        {
            a.Tick();
            b.Tick();
            Assert.Equal(a.StateHash(), b.StateHash());
        }
        foreach (EntityHandle h in mine) Assert.InRange(u.QueueCount[h.Index], 0, OrderConstants.QueueCapacity);
    }

    [Fact]
    public void ControlGroupsAndSubgroups_OnALiveSim_NeverChangeTheHashOrTheTick()
    {
        var a = new Simulation(Cfg(8UL));
        var b = new Simulation(Cfg(8UL));
        foreach (Command c in StartArmies(a, 300)) { a.Enqueue(c); b.Enqueue(c); }
        UnitStore u = a.World.Units;
        var groups = new ControlGroups(u.Capacity);
        var sel = new SelectionSet(u.Capacity);
        var sub = new Subgroups(a.World.Data.Units.Length);
        var rng = new SimRng(8, RngStream.Combat);
        for (int t = 0; t < 300; t++)
        {
            a.Tick(); b.Tick();
            ulong h0 = a.StateHash();
            long tick = a.TickNumber;
            int pending = a.PendingCommandCount;
            int g = rng.NextInt(0, 9);
            for (int i = 0; i < u.Capacity; i += 1 + rng.NextInt(0, 5)) if (u.Alive[i]) sel.Add(new EntityHandle(i, u.Generation[i]));
            groups.Assign(g, sel.Items);
            groups.Add((g + 1) % 9, sel.Items);
            groups.Prune(u.Alive, u.Generation);
            groups.Recall(rng.NextInt(0, 9), sel, u.Alive, u.Generation);
            groups.TryMean(g, u.Position, u.Alive, u.Generation, out _);
            groups.Tap(g, t * 0.1);
            sub.Update(sel.Items, u.TypeId, reset: rng.NextInt(0, 2) == 0);
            sub.Next();
            Assert.Equal(h0, a.StateHash());
            Assert.Equal(tick, a.TickNumber);
            Assert.Equal(pending, a.PendingCommandCount);
            Assert.Equal(b.StateHash(), a.StateHash());
        }
    }

    /// <summary>Wall-clock tests; they run alone in <see cref="SerialCollection"/>.</summary>
    [Collection(SerialCollection.Name)]
    public class Serial
    {
        private readonly ITestOutputHelper _out;

        public Serial(ITestOutputHelper output) => _out = output;

        [Fact]
        [Trait("Category", "Perf")]
        public void FramePrune_NineGroupsOf1000_And1000Selected_IsCheap()
        {
            const int cap = 2000;
            var store = new UnitStore(cap);
            var all = new EntityHandle[cap];
            for (int i = 0; i < cap; i++) { all[i] = store.Alloc(); store.TypeId[i] = i % 7; }
            var groups = new ControlGroups(cap);
            var selection = new SelectionSet(cap);
            var sub = new Subgroups(7);
            for (int g = 0; g < ControlGroups.Count; g++) groups.Assign(g, all.AsSpan(g * 100, 1000));
            foreach (EntityHandle h in all.AsSpan(0, 1000)) selection.Add(h);
            for (int i = 0; i < 200; i++) groups.Prune(store.Alive, store.Generation); // warm up
            const int frames = 2000;
            var sw = Stopwatch.StartNew();
            for (int f = 0; f < frames; f++)
            {
                // what SelectionController._Process does every frame
                selection.Prune(store.Alive, store.Generation);
                groups.Prune(store.Alive, store.Generation);
                sub.Update(selection.Items, store.TypeId, reset: false);
            }
            sw.Stop();
            double usPerFrame = sw.Elapsed.TotalMilliseconds * 1000.0 / frames;
            _out.WriteLine($"per-frame prune (1,000 selected + 9 x 1,000 grouped) + subgroup refresh: {usPerFrame:F1} us");
            Assert.True(usPerFrame < 500, $"per-frame selection/group upkeep {usPerFrame:F1} us (budget 0.5 ms, 3% of a 60 fps frame)");
        }
    }
}
