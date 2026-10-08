using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// QA M3-V1 (2026-10-07-0925): attacks on the economy view in the real Match scene. Right-click Gather with 0 / 200
/// workers and mixed selections (one sound, N commands, overflow dropped whole), a node under the minimap (stays a Move),
/// a Shift-queued Gather behind a Move, felled nodes, a full building store (256 views, hit-point bars via reflection,
/// destroyed and recycled slots), a site cancelled before any frame sees it, a slot recycled between two frames, and
/// view allocation over 300 hauling frames.
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaV1Test.tscn</c>; prints "QA M3-V1 TEST PASS" and exits 0, else each failure and exit 1.</remarks>
public partial class QaV1Test : Node
{
    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private Match _match = null!;
    private Simulation _sim = null!;
    private SelectionController _sel = null!;
    private RtsCamera _camera = null!;
    private UnitViews _units = null!;
    private BuildingViews _buildings = null!;
    private ResourceBar _bar = null!;
    private Sfx _sfx = null!;

    private string? _shots;

    private World W => _sim.World;

    // Windowed only (-- --shots <dir> [--size WxH|max]): workers hauling at 60 m and 30 m zoom with the resource bar.
    private async Task Shots(string size)
    {
        await StartMatch(1, 100, 30);
        _match.GetNode<SimRunner>("SimRunner").ProcessMode = ProcessModeEnum.Inherit;
        List<EntityHandle> workers = BaseWorkers();
        int mine = NearestNode(HallCenter(0), ResourceKind.Gold), tree = NearestNode(HallCenter(0), ResourceKind.Wood);
        for (int k = 0; k < workers.Count; k++)
            _sim.Enqueue(Command.Gather(0, workers[k], k % 2 == 0 ? NodeCenter(mine) : NodeCenter(tree)));
        _match.GetNode<SimRunner>("SimRunner").GameSpeed = 4.0;
        await ToSignal(GetTree().CreateTimer(6.0), SceneTreeTimer.SignalName.Timeout);
        _match.GetNode<SimRunner>("SimRunner").GameSpeed = 0.0001;
        foreach (float zoom in new[] { 60f, 30f })
        {
            _camera.SetZoom(zoom);
            System.Numerics.Vector2 c = HallCenter(0);
            _camera.SetFocus(c.X, c.Y);
            for (int i = 0; i < 10; i++) await Frame();
            for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            int carrying = Enumerable.Range(0, U.Capacity).Count(i => U.Alive[i] && U.Cargo[i] > 0);
            string path = $"{_shots}/qa-v1-{size}-zoom{zoom}.png";
            GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print($"screenshot {path} ({GetViewport().GetVisibleRect().Size}, {carrying} carrying, bar '{_bar.Text}', bar rect {_bar.GetGlobalRect()})");
        }
    }
    private UnitStore U => _sim.World.Units;

    public override async void _Ready()
    {
        try
        {
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            string[] args = OS.GetCmdlineUserArgs();
            int at = Array.IndexOf(args, "--shots");
            if (at >= 0 && at + 1 < args.Length) _shots = args[at + 1];
            int sz = Array.IndexOf(args, "--size");
            string size = sz >= 0 && sz + 1 < args.Length ? args[sz + 1] : "1152x648";
            if (size == "max") DisplayServer.WindowSetMode(DisplayServer.WindowMode.Maximized);
            else
            {
                string[] wh = size.Split('x');
                GetTree().Root.Size = new Vector2I(int.Parse(wh[0]), int.Parse(wh[1]));
            }
            for (int i = 0; i < 5; i++) await Frame();
            if (_shots != null && DisplayServer.GetName() != "headless")
            {
                await Shots(size);
                GetTree().Quit(0);
                return;
            }
            await FreshTreeAndMine();
            await WorkerFloods();
            await MinimapOverNode();
            await QueuedGatherAfterMove();
            await FelledNodes();
            await StoreFull();
            await SiteCancelledBeforeAnyFrame();
            await HaulingAllocation();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        Input.ActionRelease("order_queue");
        foreach (string f in _failures) GD.Print($"QA M3-V1 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M3-V1 TEST PASS");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    // 200 workers + 100 soldiers: one Gather per worker-type unit, one Move per other, one sound; 0 workers: Moves;
    // empty selection: nothing; a nearly full queue: the whole order dropped, then the exact fit goes out whole.
    private async Task WorkerFloods()
    {
        await StartMatch(1, 100, 200);
        Check(U.Count == 600, $"floods: {U.Count} units, want 600");
        int mine = NearestNode(HallCenter(0), ResourceKind.Gold);
        Vector2 px = await OnScreen(NodeCenter(mine));
        Check(SelectionController.NodeAt(W, Pick(px)) == mine, "floods: pixel does not pick the mine");

        List<EntityHandle> workerType = Own(h => IsWorker(h));
        List<EntityHandle> others = Own(h => !IsWorker(h));
        Check(workerType.Count >= 200 && others.Count > 0, $"floods: {workerType.Count} worker-type, {others.Count} others");

        // All worker-type units.
        Select(workerType);
        (int g0, int m0, int p0) = Counts();
        await SoundGap();
        int s0 = _sfx.PlayCount(SfxEvent.Command);
        RightClick(px);
        (int g1, int m1, int p1) = Counts();
        Check(g1 - g0 == workerType.Count && m1 == m0 && p1 - p0 == workerType.Count, $"floods: {workerType.Count} workers -> {g1 - g0} Gathers, {m1 - m0} Moves, {p1 - p0} enqueued");
        Check(_sfx.PlayCount(SfxEvent.Command) == s0 + 1, $"floods: {_sfx.PlayCount(SfxEvent.Command) - s0} Command sounds for one order");
        Tick(1);

        // Everyone.
        var all = workerType.Concat(others).ToList();
        Select(all);
        (g0, m0, p0) = Counts();
        RightClick(px);
        (g1, m1, p1) = Counts();
        Check(g1 - g0 == workerType.Count && m1 - m0 == others.Count && p1 - p0 == all.Count, $"floods mixed: {g1 - g0} Gathers / {m1 - m0} Moves / {p1 - p0} enqueued for {workerType.Count} + {others.Count}");
        Tick(1);

        // Only soldiers (0 workers): Moves.
        Select(others);
        (g0, m0, _) = Counts();
        RightClick(px);
        (g1, m1, _) = Counts();
        Check(g1 == g0 && m1 - m0 == others.Count, $"floods 0 workers: {g1 - g0} Gathers / {m1 - m0} Moves");
        Tick(1);

        // Empty selection: nothing.
        _sel.Selection.Clear();
        (g0, m0, p0) = Counts();
        RightClick(px);
        (g1, m1, p1) = Counts();
        Check(g1 == g0 && m1 == m0 && p1 == p0, "floods: empty selection enqueued something");

        // A worker of the ENEMY can never be in the selection; a dead worker is pruned: kill-free proxy, stale handle.
        var stale = new EntityHandle(workerType[0].Index, workerType[0].Generation + 1);
        _sel.Selection.Clear();
        _sel.Selection.Add(stale);
        (g0, m0, p0) = Counts();
        RightClick(px);
        (g1, m1, p1) = Counts();
        Check(p1 == p0 && g1 == g0, $"floods: a stale worker handle enqueued {p1 - p0} commands");

        // Overflow: one too many dropped whole (one warning, DroppedOrders + 1, no sound), the exact fit goes whole.
        int cap = W.Config.CommandCapacity;
        Select(all);
        int fill = cap - all.Count + 1 - _sim.PendingCommandCount;
        for (int i = 0; i < fill; i++) _sim.Enqueue(Command.Noop(0));
        int pending = _sim.PendingCommandCount, dropped = _sel.DroppedOrders;
        (g0, m0, _) = Counts();
        await SoundGap();
        s0 = _sfx.PlayCount(SfxEvent.Command);
        RightClick(px);
        (g1, m1, _) = Counts();
        Check(_sim.PendingCommandCount == pending && _sel.DroppedOrders == dropped + 1 && g1 == g0 && m1 == m0,
            $"floods overflow: enqueued {_sim.PendingCommandCount - pending}, dropped {_sel.DroppedOrders - dropped}, gathers {g1 - g0}");
        Check(_sfx.PlayCount(SfxEvent.Command) == s0, "floods overflow: a dropped order played the Command sound");
        Tick(1);
        fill = cap - all.Count - _sim.PendingCommandCount;
        for (int i = 0; i < fill; i++) _sim.Enqueue(Command.Noop(0));
        pending = _sim.PendingCommandCount;
        RightClick(px);
        Check(_sim.PendingCommandCount == cap && _sim.PendingCommandCount - pending == all.Count, $"floods exact fit: {_sim.PendingCommandCount - pending} of {all.Count} enqueued, queue {_sim.PendingCommandCount}/{cap}");
        Tick(1);
        GD.Print($"floods: {workerType.Count} worker-type + {others.Count} others; overflow at {cap} dropped whole");
        await EndMatch();
    }

    // Criterion 3 from a fresh start on seeds 1 / 31: five base workers right-clicked onto the nearest tree, then (new
    // match) the nearest mine: ticks until all are Gathering, and until the total rises (report; bound 60 / 1,200 on the
    // brief's seed-1 mine, distances logged).
    private async Task FreshTreeAndMine()
    {
        foreach (ulong seed in new ulong[] { 1, 31 })
        {
            foreach (ResourceKind kind in new[] { ResourceKind.Wood, ResourceKind.Gold })
            {
                await StartMatch(seed, 100, null);
                List<EntityHandle> workers = BaseWorkers();
                int node = NearestNode(HallCenter(0), kind);
                Vector2 px = await OnScreen(NodeCenter(node));
                Select(workers);
                RightClick(px);
                int start = kind == ResourceKind.Gold ? W.Gold[0] : W.Wood[0];
                int all = -1, rose = -1;
                for (int t = 1; t <= 1200 && (all < 0 || rose < 0); t++)
                {
                    Tick(1);
                    if (all < 0 && workers.All(h => U.State[h.Index] == UnitState.Gathering)) all = t;
                    if (rose < 0 && (kind == ResourceKind.Gold ? W.Gold[0] : W.Wood[0]) > start) rose = t;
                }
                float far = workers.Max(h => System.Numerics.Vector2.Distance(_match.Bases!.Workers[0][0], NodeCenter(node)));
                GD.Print($"fresh seed {seed} {kind}: node {System.Numerics.Vector2.Distance(HallCenter(0), NodeCenter(node)):0.0} m from the hall; all Gathering at tick {all}, total rose at tick {rose}");
                Check(all > 0 && rose > 0 && rose <= 1200, $"fresh seed {seed} {kind}: all Gathering at {all}, rose at {rose}");
                if (seed == 1 && kind == ResourceKind.Gold) Check(all <= 60, $"fresh seed 1 mine: all Gathering at tick {all}, want <= 60");
                await EndMatch();
            }
        }
    }

    // A node under the minimap: the minimap takes the click (a Move for everyone); the same pixel sent to the 3D view
    // would be a Gather, which proves a node really was under it.
    private async Task MinimapOverNode()
    {
        await StartMatch(1, 100, null);
        var mini = _match.GetNode<Minimap>("Hud/Minimap");
        Rect2 rect = mini.GetGlobalRect();
        List<EntityHandle> workers = BaseWorkers();
        Vector2 hit = new(-1, -1);
        for (int node = 0; node < W.Resources.Capacity && hit.X < 0; node++)
        {
            if (!W.Resources.Alive[node]) continue;
            System.Numerics.Vector2 c = NodeCenter(node);
            FocusOn(c);
            await Frame();
            if (!TryPick(rect.GetCenter(), out System.Numerics.Vector2 under)) continue;
            System.Numerics.Vector2 focus = 2 * c - under;
            _camera.SetFocus(focus.X, focus.Y);
            await Frame();
            for (float dy = 4; dy < rect.Size.Y - 4 && hit.X < 0; dy += 6)
                for (float dx = 4; dx < rect.Size.X - 4 && hit.X < 0; dx += 6)
                {
                    Vector2 px = rect.Position + new Vector2(dx, dy);
                    if (TryPick(px, out System.Numerics.Vector2 gp) && SelectionController.NodeAt(W, gp) >= 0) hit = px;
                }
        }
        if (!Check(hit.X >= 0, "minimap: could not put a node under the minimap")) { await EndMatch(); return; }
        Select(workers);
        (int g0, int m0, _) = Counts();
        GetViewport().PushInput(Button(MouseButton.Right, true, hit));
        GetViewport().PushInput(Button(MouseButton.Right, false, hit));
        (int g1, int m1, _) = Counts();
        Check(g1 == g0 && m1 - m0 == workers.Count, $"minimap over a node: {g1 - g0} Gathers, {m1 - m0} Moves (want 0 / {workers.Count})");
        Tick(1);
        Select(workers);
        (g0, _, _) = Counts();
        _sel.CommandAt(hit, false);
        (g1, _, _) = Counts();
        Check(g1 - g0 == workers.Count, $"minimap control: the 3D pick under pixel {hit} gave {g1 - g0} Gathers (test setup)");
        GD.Print($"minimap: node under pixel {hit} of {rect}: minimap Move, 3D pick Gather");
        await EndMatch();
    }

    // Shift + right-click on a mine after a plain Move: the Move runs first, then each worker gathers.
    private async Task QueuedGatherAfterMove()
    {
        await StartMatch(1, 100, null);
        List<EntityHandle> workers = BaseWorkers();
        System.Numerics.Vector2 hall = HallCenter(0);
        int mine = NearestNode(hall, ResourceKind.Gold);
        System.Numerics.Vector2 mineC = NodeCenter(mine);
        // Ground 16 m from the hall on the side away from the mine.
        System.Numerics.Vector2 away = hall + System.Numerics.Vector2.Normalize(hall - mineC) * 16f;
        System.Numerics.Vector2 ground = OpenNear(away);
        Select(workers);
        RightClick(await OnScreen(ground));
        Vector2 minePx = await OnScreen(mineC);
        Select(workers);
        Input.ActionPress("order_queue");
        RightClick(minePx);
        Input.ActionRelease("order_queue");
        Tick(2);
        int cap = OrderConstants.QueueCapacity;
        foreach (EntityHandle h in workers)
            Check(U.QueueCount[h.Index] == 1 && U.QueueKind[h.Index * cap] == CommandKind.Gather && U.State[h.Index] == UnitState.Moving,
                $"queued: worker {h.Index} state {U.State[h.Index]} queue {U.QueueCount[h.Index]}");
        var nearGround = new float[workers.Count];
        Array.Fill(nearGround, float.MaxValue);
        var gatherAt = new int[workers.Count];
        Array.Fill(gatherAt, -1);
        for (int t = 0; t < 1500 && gatherAt.Any(x => x < 0); t++)
        {
            Tick(1);
            for (int k = 0; k < workers.Count; k++)
            {
                int i = workers[k].Index;
                if (gatherAt[k] < 0) nearGround[k] = Math.Min(nearGround[k], System.Numerics.Vector2.Distance(U.Position[i], ground));
                if (gatherAt[k] < 0 && U.State[i] == UnitState.Gathering) gatherAt[k] = t;
            }
        }
        Check(gatherAt.All(x => x >= 0), $"queued: gathering at [{string.Join(", ", gatherAt)}]");
        Check(nearGround.All(d => d < 8f), $"queued: closest approach to the Move point before gathering [{string.Join(", ", nearGround.Select(d => d.ToString("0.0")))}] m");
        GD.Print($"queued: Move then Gather; nearest to move point {nearGround.Max():0.0} m, gathering by tick {gatherAt.Max()}");
        await EndMatch();
    }

    // A felled tree and an emptied mine: a right-click on their old cells is a Move. M3-V3b: the right click ray-picks the
    // drawn props, so the node is one with open ground in front of it (south, toward the camera): another tree's canopy
    // in front would rightly take the click. Before felling, the click's context target is the node itself.
    private async Task FelledNodes()
    {
        await StartMatch(1, 20, null);
        List<EntityHandle> workers = BaseWorkers();
        MethodInfo take = typeof(ResourceStore).GetMethod("Take", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (ResourceKind kind in new[] { ResourceKind.Wood, ResourceKind.Gold })
        {
            int node = NearestOpenNode(HallCenter(0), kind);
            if (!Check(node >= 0, $"felled {kind}: no node with open ground in front")) continue;
            System.Numerics.Vector2 c = NodeCenter(node);
            Vector2 px = await OnScreen(c);
            Check(SelectionController.NodeAt(W, Pick(px)) == node, $"felled {kind}: pixel misses the node before felling");
            Check(_sel.ContextTarget(px, out System.Numerics.Vector2 before, out _) && SelectionController.NodeAt(W, before) == node,
                $"felled {kind}: the click's target before felling is {before}, not the node");
            take.Invoke(W.Resources, new object[] { W.Resources.HandleOf(node), W.Resources.Remaining[node] });
            Check(!W.Resources.Alive[node], $"felled {kind}: still alive");
            Select(workers);
            (int g0, int m0, _) = Counts();
            RightClick(px);
            (int g1, int m1, _) = Counts();
            Check(g1 == g0 && m1 - m0 == workers.Count, $"felled {kind}: {g1 - g0} Gathers, {m1 - m0} Moves");
            Tick(2);
        }
        await EndMatch();
    }

    // The nearest live node of a kind with no other node in the four cell rows south of its footprint (one cell wider each side).
    private int NearestOpenNode(System.Numerics.Vector2 from, ResourceKind kind)
    {
        NavGrid g = W.NavGrid;
        ResourceStore r = W.Resources;
        int best = -1;
        float bestD = float.PositiveInfinity;
        for (int i = 0; i < r.Capacity; i++)
        {
            if (!r.Alive[i]) continue;
            ResourceDef def = _data.Resources[r.TypeId[i]];
            if (def.Resource != kind) continue;
            int x = r.Cell[i] % g.Width, y = r.Cell[i] / g.Width;
            bool open = y + def.FootprintHeight + 4 < g.Height;
            for (int dy = 0; dy < 4 && open; dy++)
                for (int dx = -1; dx <= def.FootprintWidth && open; dx++)
                {
                    int cx = x + dx, cy = y + def.FootprintHeight + dy;
                    open = cx >= 0 && cx < g.Width && (g.FlagsAt(cx, cy) & NavFlags.Resource) == 0;
                }
            float d = System.Numerics.Vector2.Distance(NodeCenter(i), from);
            if (open && d < bestD) (best, bestD) = (i, d);
        }
        return best;
    }

    // 256 buildings: every one shown in its owner's colour; 300 Syncs allocate nothing; damage shows a hit-point bar;
    // destroyed buildings vanish within a frame; freed slots refilled with another type show the new box, colour, no bar.
    private async Task StoreFull()
    {
        await StartMatch(3, 0, 0);
        BuildingStore b = W.Buildings;
        NavGrid g = W.NavGrid;
        int house0 = StartBase.BuildingOfSlot(_data, W.FactionOf(0), BuildingSlot.House);
        int house1 = StartBase.BuildingOfSlot(_data, W.FactionOf(1), BuildingSlot.House);
        int barracks = StartBase.BuildingOfSlot(_data, W.FactionOf(0), BuildingSlot.InfantryHall);
        int n = 0;
        for (int y = 1; y < g.Height - 3 && b.Count < b.Capacity; y += 3)
        {
            for (int x = 1; x < g.Width - 3; x += 3)
            {
                _sim.Enqueue(Command.SpawnBuilding(n % 2, n % 2 == 0 ? house0 : house1, g.CellCenter(x, y)));
                n++;
            }
            Tick(1);
        }
        Tick(1);
        Check(b.Count == b.Capacity, $"store full: {b.Count} / {b.Capacity} buildings");
        // One more is refused quietly.
        _sim.Enqueue(Command.SpawnBuilding(0, house0, g.CellCenter(g.Width / 2, g.Height / 2)));
        Tick(1);
        await Frames();
        int shown = 0, wrong = 0;
        for (int k = 0; k < b.Capacity; k++)
        {
            if (!b.Alive[k]) continue;
            if (_buildings.IsShown(k) && _buildings.ViewOf(k)!.Visible) shown++;
            if (_buildings.BoxOf(k).MaterialOverride != _buildings.PlayerMaterial(b.Owner[k])) wrong++;
        }
        Check(shown == b.Count && wrong == 0 && _buildings.NodeCount == b.Capacity, $"store full: {shown} shown, {wrong} wrong colour, {_buildings.NodeCount} nodes");
        _buildings.Sync(W);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int f = 0; f < 300; f++) _buildings.Sync(W);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(bytes == 0, $"store full: 300 Syncs of 256 buildings allocated {bytes} bytes");

        // Damage via reflection (no damage command yet).
        MethodInfo damage = typeof(BuildingStore).GetMethod("Damage", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var hurt = new List<int>();
        var killed = new List<int>();
        for (int k = 0; k < b.Capacity; k++)
        {
            if (!b.Alive[k]) continue;
            int max = _data.Buildings[b.TypeId[k]].Hp;
            if (k % 5 == 0) { damage.Invoke(b, new object[] { b.HandleOf(k), max / 2 }); hurt.Add(k); }
            else if (k % 7 == 0) { damage.Invoke(b, new object[] { b.HandleOf(k), max }); killed.Add(k); }
        }
        await Frames();
        foreach (int k in hurt)
        {
            BuildingBarKind bar = _buildings.ShownBar(k, out float fill);
            float want = (float)b.Hp[k] / _data.Buildings[b.TypeId[k]].Hp;
            if (!Check(bar == BuildingBarKind.HitPoints && Mathf.IsEqualApprox(fill, want) && _buildings.BarFillOf(k).Visible, $"hp bar slot {k}: {bar} {fill}, want {want}")) break;
        }
        foreach (int k in killed)
            if (!Check(!b.Alive[k] && !_buildings.IsShown(k) && !_buildings.ViewOf(k)!.Visible, $"destroyed slot {k} still shown")) break;
        // Refill the freed slots with barracks (another type and footprint) for the other player.
        int free = b.FreeCount;
        for (int y = 1; y < g.Height - 5 && b.FreeCount > 0; y += 2)
            for (int x = 1; x < g.Width - 5 && b.FreeCount > 0; x += 2)
                if (b.Fits(barracks, y * g.Width + x)) { _sim.Enqueue(Command.SpawnBuilding(1, barracks, g.CellCenter(x, y))); Tick(1); }
        await Frames();
        int refilled = 0;
        foreach (int k in killed)
        {
            if (!b.Alive[k]) continue;
            refilled++;
            Aabb box = _buildings.BoxOf(k).GetAabb();
            BuildingDef def = _data.Buildings[b.TypeId[k]];
            Check(_buildings.IsShown(k) && Mathf.IsEqualApprox(box.Size.X, def.FootprintWidth * MapConstants.CellSize)
                && Mathf.IsEqualApprox(box.Size.Y, BuildingViews.BoxHeight)
                && _buildings.BoxOf(k).MaterialOverride == _buildings.PlayerMaterial(b.Owner[k])
                && _buildings.ShownBar(k, out _) == BuildingBarKind.None && !_buildings.BarFillOf(k).Visible,
                $"recycled slot {k} ({def.Key}, owner {b.Owner[k]}): box {box.Size}, bar {_buildings.ShownBar(k, out _)}");
        }
        Check(_buildings.NodeCount == b.Capacity, $"recycling made new nodes: {_buildings.NodeCount}");
        GD.Print($"store full: {b.Capacity} shown, 0 bytes over 300 Syncs ({bytes}), {hurt.Count} hp bars, {killed.Count} destroyed, {refilled} of {free} freed slots refilled with another type");
        await EndMatch();
    }

    // A site cancelled before any frame ran shows nothing; a site replaced by a finished building in the same slot
    // between two frames is drawn as the finished building (colour, full height, no progress bar).
    private async Task SiteCancelledBeforeAnyFrame()
    {
        await StartMatch(1, 20, null);
        BuildingStore b = W.Buildings;
        NavGrid g = W.NavGrid;
        List<EntityHandle> workers = BaseWorkers();
        int house = StartBase.BuildingOfSlot(_data, W.FactionOf(0), BuildingSlot.House);
        int anchor = -1;
        System.Numerics.Vector2 hall = HallCenter(0);
        float best = float.MaxValue;
        for (int cell = 0; cell < g.Width * g.Height; cell++)
        {
            if (!W.CanPlace(0, house, cell, out _)) continue;
            float d = System.Numerics.Vector2.Distance(g.CellCenter(cell % g.Width, cell / g.Width), hall);
            if (d < best) (best, anchor) = (d, cell);
        }
        if (!Check(anchor >= 0, "site: no house spot")) { await EndMatch(); return; }
        System.Numerics.Vector2 at = g.CellCenter(anchor % g.Width, anchor / g.Width);
        int nodes = _buildings.NodeCount;
        _sim.Enqueue(Command.Build(0, workers[0], house, at));
        int slot = -1;
        for (int t = 0; t < 200 && slot < 0; t++)
        {
            Tick(1);
            slot = b.SlotAt(anchor % g.Width, anchor / g.Width);
        }
        if (!Check(slot >= 0 && b.UnderConstruction[slot], "site: no site appeared")) { await EndMatch(); return; }
        _sim.Enqueue(Command.Cancel(0, at));
        Tick(2); // a command applies on the second Tick after Enqueue
        Check(!b.Alive[slot], "site: cancel did not free the site");
        await Frames();
        Check(!_buildings.IsShown(slot) && (_buildings.ViewOf(slot) == null || !_buildings.ViewOf(slot)!.Visible), "site cancelled before any frame is shown");
        Check(_buildings.NodeCount == nodes, $"site cancelled before any frame made {_buildings.NodeCount - nodes} nodes");

        // Now a site that is shown, then replaced (cancel + finished spawn in the same slot) between two frames.
        _sim.Enqueue(Command.Build(0, workers[0], house, at));
        slot = -1;
        for (int t = 0; t < 200 && slot < 0; t++)
        {
            Tick(1);
            slot = b.SlotAt(anchor % g.Width, anchor / g.Width);
        }
        for (int t = 0; t < 100; t++) Tick(1); // some progress
        await Frames();
        Check(slot >= 0 && _buildings.ShownAsSite(slot) && _buildings.ShownBar(slot, out float f0) == BuildingBarKind.Progress, "site 2 not shown as a site");
        int gen = b.Generation[slot];
        _sim.Enqueue(Command.Cancel(0, at));
        Tick(2);
        _sim.Enqueue(Command.SpawnBuilding(1, house, at));
        Tick(2);
        int now = b.SlotAt(anchor % g.Width, anchor / g.Width);
        Check(now == slot && b.Generation[now] != gen && !b.UnderConstruction[now], $"site 2: replacement in slot {now} (was {slot})");
        await Frames();
        Transform3D tr = _buildings.BoxOf(slot).Transform;
        Check(!_buildings.ShownAsSite(slot) && _buildings.BoxOf(slot).MaterialOverride == _buildings.PlayerMaterial(1)
            && _buildings.ShownBar(slot, out _) == BuildingBarKind.None && !_buildings.BarFillOf(slot).Visible && !_buildings.ViewOf(slot)!.GetNode<MeshInstance3D>("BarBack").Visible
            && Mathf.IsEqualApprox(tr.Basis.Scale.Y, 1f),
            $"replaced slot: site {_buildings.ShownAsSite(slot)}, bar {_buildings.ShownBar(slot, out _)}, scale {tr.Basis.Scale}");
        await EndMatch();
    }

    // 300 frames of hauling (sim ticking, cargo appearing and vanishing, tints changing): the unit and building views
    // allocate nothing once every worker has its marker; the bar allocates only on rebuilds.
    private async Task HaulingAllocation()
    {
        await StartMatch(1, 100, 30);
        List<EntityHandle> workers = BaseWorkers();
        int mine = NearestNode(HallCenter(0), ResourceKind.Gold), tree = NearestNode(HallCenter(0), ResourceKind.Wood);
        for (int k = 0; k < workers.Count; k++)
            _sim.Enqueue(Command.Gather(0, workers[k], k % 2 == 0 ? NodeCenter(mine) : NodeCenter(tree)));
        for (int t = 0; t < 900; t++)
        {
            Tick(1);
            _units.Sync(W, 0.5f);
            _buildings.Sync(W);
            _bar.Sync(W);
        }
        int markers = _units.MarkerCount, builds = _bar.Builds, changes = 0, deposits = 0;
        long viewBytes = 0, barBytes = 0;
        int lastGold = W.Gold[0] + W.Wood[0];
        var cargo = new bool[U.Capacity];
        for (int f = 0; f < 300; f++)
        {
            Tick(1);
            for (int i = 0; i < U.Capacity; i++)
            {
                bool has = U.Alive[i] && U.Cargo[i] > 0;
                if (has != cargo[i]) changes++;
                cargo[i] = has;
            }
            if (W.Gold[0] + W.Wood[0] != lastGold) deposits++;
            lastGold = W.Gold[0] + W.Wood[0];
            long a = GC.GetAllocatedBytesForCurrentThread();
            _units.Sync(W, f / 300f);
            _buildings.Sync(W);
            long b = GC.GetAllocatedBytesForCurrentThread();
            _bar.Sync(W);
            long c = GC.GetAllocatedBytesForCurrentThread();
            viewBytes += b - a;
            barBytes += c - b;
        }
        int rebuilds = _bar.Builds - builds;
        GD.Print($"hauling: 300 frames, {changes} cargo changes, {deposits} deposit frames, new markers {_units.MarkerCount - markers}; views {viewBytes} bytes; bar {barBytes} bytes over {rebuilds} rebuilds");
        Check(changes > 10 && deposits > 0, $"hauling: too little traffic ({changes} cargo changes, {deposits} deposits)");
        Check(_units.MarkerCount == markers && viewBytes == 0, $"hauling: unit + building views allocated {viewBytes} bytes over 300 frames ({_units.MarkerCount - markers} new markers)");
        Check(rebuilds > 0 || barBytes == 0, $"hauling: bar allocated {barBytes} bytes with no rebuild");
        await EndMatch();
    }

    // ---- helpers ----

    private async Task StartMatch(ulong seed, int units, int? workers)
    {
        var args = new List<string> { "--seed", seed.ToString(), "--units", units.ToString(), "--mute" };
        if (workers is int n) args.AddRange(new[] { "--workers", n.ToString() });
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _match.Start(_data, LaunchOptions.Parse(args.ToArray()));
        var runner = _match.GetNode<SimRunner>("SimRunner");
        runner.ProcessMode = ProcessModeEnum.Disabled;
        _sim = runner.Simulation!;
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _units = _match.GetNode<UnitViews>("World3D/UnitViews");
        _buildings = _match.GetNode<BuildingViews>("World3D/BuildingViews");
        _bar = _match.GetNode<ResourceBar>("Hud/ResourceBar");
        _sfx = _match.GetNode<Sfx>("Sfx");
        Tick(2);
        await Frames();
    }

    private async Task EndMatch()
    {
        RemoveChild(_match);
        _match.QueueFree();
        await Frame();
    }

    private (int Gathers, int Moves, int Pending) Counts() =>
        (_sel.IssuedCount(CommandKind.Gather), _sel.IssuedCount(CommandKind.Move), _sim.PendingCommandCount);

    private bool IsWorker(EntityHandle h) => _data.Units[U.TypeId[h.Index]].Slot == UnitSlot.Worker;

    private List<EntityHandle> Own(Func<EntityHandle, bool> pred)
    {
        var list = new List<EntityHandle>();
        for (int i = 0; i < U.Capacity; i++)
        {
            if (!U.Alive[i] || U.Owner[i] != 0) continue;
            var h = new EntityHandle(i, U.Generation[i]);
            if (pred(h)) list.Add(h);
        }
        return list;
    }

    private List<EntityHandle> BaseWorkers()
    {
        StartBasePlan plan = _match.Bases!;
        return Own(h => U.TypeId[h.Index] == plan.WorkerType[0] && plan.Workers[0].Contains(U.Position[h.Index]));
    }

    private System.Numerics.Vector2 HallCenter(int p)
    {
        StartBasePlan plan = _match.Bases!;
        return StartBase.FootprintCenter(W.NavGrid, _data.Buildings[plan.HallType[p]], plan.HallAnchor[p]);
    }

    private int NearestNode(System.Numerics.Vector2 from, ResourceKind kind)
    {
        int best = -1;
        float bestD = float.PositiveInfinity;
        for (int i = 0; i < W.Resources.Capacity; i++)
        {
            if (!W.Resources.Alive[i] || _data.Resources[W.Resources.TypeId[i]].Resource != kind) continue;
            float d = System.Numerics.Vector2.Distance(NodeCenter(i), from);
            if (d < bestD) (best, bestD) = (i, d);
        }
        return best;
    }

    private System.Numerics.Vector2 NodeCenter(int node)
    {
        ResourceDef d = _data.Resources[W.Resources.TypeId[node]];
        int c = W.Resources.Cell[node], w = W.NavGrid.Width;
        return new((c % w + d.FootprintWidth * 0.5f) * MapConstants.CellSize, (c / w + d.FootprintHeight * 0.5f) * MapConstants.CellSize);
    }

    private System.Numerics.Vector2 OpenNear(System.Numerics.Vector2 p)
    {
        NavGrid g = W.NavGrid;
        g.WorldToCell(p, out int px, out int py);
        for (int r = 0; r < 20; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                    if (StartLayout.IsOpen(g, px + dx, py + dy)) return g.CellCenter(px + dx, py + dy);
        throw new InvalidOperationException("no open ground");
    }

    private void Select(List<EntityHandle> units)
    {
        _sel.Selection.Clear();
        foreach (EntityHandle h in units) _sel.Selection.Add(h);
    }

    private void RightClick(Vector2 at)
    {
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = at });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = at });
    }

    private static InputEventMouseButton Button(MouseButton b, bool pressed, Vector2 at) =>
        new() { ButtonIndex = b, Pressed = pressed, Position = at, GlobalPosition = at, ButtonMask = pressed ? MouseButtonMask.Right : 0 };

    private async Task<Vector2> OnScreen(System.Numerics.Vector2 ground)
    {
        FocusOn(ground);
        await Frame();
        return _camera.UnprojectPosition(new Vector3(ground.X, TerrainHeight.At(W.Heightmap, ground.X, ground.Y), ground.Y));
    }

    private void FocusOn(System.Numerics.Vector2 ground)
    {
        _camera.SetZoom(30f);
        _camera.SetFocus(ground.X, ground.Y);
    }

    private bool TryPick(Vector2 px, out System.Numerics.Vector2 ground)
    {
        Vector3 o = _camera.ProjectRayOrigin(px), d = _camera.ProjectRayNormal(px);
        bool ok = GroundPicker.TryPick(W.Heightmap, new(o.X, o.Y, o.Z), new(d.X, d.Y, d.Z), out System.Numerics.Vector3 hit);
        ground = new(hit.X, hit.Z);
        return ok;
    }

    private System.Numerics.Vector2 Pick(Vector2 px)
    {
        Check(TryPick(px, out System.Numerics.Vector2 g), $"pixel {px} missed the map");
        return g;
    }

    private async Task SoundGap() => await ToSignal(GetTree().CreateTimer(Sfx.MinGapMs / 1000.0 + 0.02), SceneTreeTimer.SignalName.Timeout);

    private void Tick(int n)
    {
        for (int i = 0; i < n; i++) _sim.Tick();
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private async Task Frames()
    {
        await Frame();
        await Frame();
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
