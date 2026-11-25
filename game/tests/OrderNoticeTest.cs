using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.Replays;
using Rts.Sim.ViewApi;
using NVector2 = System.Numerics.Vector2;

namespace Rts.Game.Tests;

/// <summary>
/// M6-V27 (Request 58's view half): the local player is told when a queued order of theirs is dropped, on the real Match and
/// the shipped data.
/// </summary>
/// <remarks>
/// Headless, in the scene loop: <c>&amp; $env:GODOT --headless --path game res://tests/OrderNoticeTest.tscn</c>; prints
/// "ORDER NOTICE TEST PASS" or FAIL lines. Three seats (you, an enemy on team 2, an ally on team 1), no AI, the standard
/// 200 / 200 start. Rows:
/// (A) a Laborer walks, then its Shift-queued Garrison Keep (275 / 275) cannot be paid at its turn: in the frame after that
/// tick the Refused sound has played once and the line over the Laborer reads <c>placement.cannot_afford</c>, following it;
/// (B) a queued House onto the Keep's own footprint: the line reads the error's text (<c>placement.blocked</c>);
/// (C) a ninth queued order sent straight to the sim (past the view's own pre-check): one sound, <c>orders.queue_full</c>;
/// (D) twelve Laborers refused in one tick: one sound, twelve lines, each gone after <see cref="NoticeLines.LifetimeSeconds"/>;
/// the bytes of a frame with twelve lines and with none (0), and of a tick read with no entries (0);
/// (E) entries whose unit is dead or whose slot holds a newer unit (forged on the real store): no line, no sound, no error;
/// (F) the enemy's and the ally's refused Keeps and an own queued Gather on bare ground (the sim lists all three): nothing
/// shown, nothing played;
/// (G) the line on screen at zoom 20 and 60 (its size follows the zoom);
/// (H) <c>ui.json</c> without <c>orders.queue_full</c> (a forged copy, the key looked up first) fails naming the key; without
/// the section, naming it; the shipped file loads; no C# literal of any placement or orders text under <c>scripts/</c>;
/// (R) the live match's recording played back (Load Game -> Watch): the sim lists the same own refusals, nothing is shown or played.
/// Windowed with <c>-- --shots &lt;dir&gt;</c> (1600 x 900) it saves <c>m6v27-cant-afford-40m.png</c> and <c>-20m.png</c>.
/// </remarks>
public partial class OrderNoticeTest : Node
{
    private const string Tag = "ORDER NOTICE TEST";

    private readonly List<string> _failures = new();
    private readonly List<(int Tick, DroppedOrder Entry)> _log = new();
    private GameData _data = null!;
    private UiText _ui = null!;
    private string? _shots;

    private Match _match = null!;
    private SimRunner _runner = null!;
    private Simulation _sim = null!;
    private RtsCamera _camera = null!;
    private OrderNoticeView _notices = null!;
    private Sfx _sfx = null!;
    private int _keep, _house, _worker;

    private World W => _sim.World;

    public override async void _Ready()
    {
        try
        {
            string[] args = OS.GetCmdlineUserArgs();
            int at = Array.IndexOf(args, "--shots");
            if (at >= 0 && at + 1 < args.Length) _shots = args[at + 1];
            GetTree().Root.Size = new Vector2I(1600, 900);
            await Frame();
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            _ui = UiText.Shared ?? throw new InvalidOperationException("ui.json failed to load");
            _keep = _data.FindBuilding("malazan_garrison_keep");
            _house = _data.FindBuilding("malazan_billet");
            TextRows();
            Replay replay = await Live();
            await Replayed(replay);
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures) GD.Print($"{Tag} FAIL: {f}");
        if (_failures.Count == 0) GD.Print($"{Tag} PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- (H) the text ----

    private void TextRows()
    {
        string json = File.ReadAllText(UiText.DefaultPath);
        var shipped = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        string? queueFull = shipped["orders"]?["queue_full"]?.GetValue<string>();
        Check(queueFull != null && queueFull.Length > 0 && _ui.Orders(OrdersText.QueueFull) == queueFull,
            $"(H) the shipped ui.json's orders.queue_full '{queueFull}' is not what UiText reads ('{_ui.Orders(OrdersText.QueueFull)}')");
        // Rule 58: the key is looked up in the shipped file before the forged copy removes it.
        var noKey = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        if (Check(noKey["orders"] is System.Text.Json.Nodes.JsonObject o && o.ContainsKey("queue_full"), "(H) the shipped ui.json has no orders.queue_full to remove"))
        {
            noKey["orders"]!.AsObject().Remove("queue_full");
            var errors = new List<string>();
            UiText? forged = UiText.Parse(noKey.ToJsonString(), errors);
            Check(forged == null && errors.Count == 1 && errors[0].Contains("orders.queue_full"), $"(H) ui.json without orders.queue_full: [{string.Join("; ", errors)}]");
            GD.Print($"  (H) without orders.queue_full: {(forged == null ? "refused" : "loaded")}, [{string.Join("; ", errors)}]");
        }
        var noSection = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        noSection.Remove("orders");
        var sectionErrors = new List<string>();
        Check(UiText.Parse(noSection.ToJsonString(), sectionErrors) == null && sectionErrors.Count == 1 && sectionErrors[0].Contains("orders"),
            $"(H) ui.json without orders: [{string.Join("; ", sectionErrors)}]");
        Check(UiText.Parse(json, new List<string>()) != null, "(H) the shipped ui.json does not load");

        // No C# literal of a text the notices show: every placement text and the orders text, quoted, in no script.
        var texts = new List<string> { _ui.Orders(OrdersText.QueueFull) };
        foreach (PlacementError e in Enum.GetValues<PlacementError>())
            if (e != PlacementError.None && _ui.PlacementText(e).Length > 0) texts.Add(_ui.PlacementText(e));
        int files = 0;
        foreach (string file in Directory.GetFiles(ProjectSettings.GlobalizePath("res://scripts"), "*.cs", SearchOption.AllDirectories))
        {
            files++;
            // Code lines only: a comment may quote a text it explains (CommandCard's "Locked"); a literal in code is what rule 8 forbids.
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimStart();
                if (line.StartsWith("//")) continue;
                foreach (string t in texts)
                    Check(!line.Contains($"\"{t}\""), $"(H) {Path.GetFileName(file)}:{i + 1} has the literal \"{t}\"");
            }
        }
        GD.Print($"  (H) {texts.Count} texts, none a literal in the code of {files} scripts");
    }

    // ---- the live match ----

    private async Task<Replay> Live()
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _match.GetNode<SimRunner>("SimRunner").RecordCheckpointInterval = 20;
        _match.Start(_data, LaunchOptions.Parse(new[] { "--units", "0", "--seed", "1", "--players", "3", "--team", "1", "1", "--team", "2", "2",
            "--team", "3", "1", "--mute" }));
        Open(_match);
        Check(_notices.Enabled, "the notices are off in a live match");
        while (_sim.TickNumber < 4) await Frame();
        List<EntityHandle> mine = Workers(0);
        Check(mine.Count >= 3, $"only {mine.Count} workers for player 0");
        _worker = W.Units.TypeId[mine[0].Index];
        Check(W.Gold[0] < _data.Buildings[_keep].CostGold, $"player 0 has {W.Gold[0]} gold: the Keep ({_data.Buildings[_keep].CostGold}) is affordable");

        await CannotAffordRow(mine[0]);
        await BlockedRow(mine[1]);
        await QueueFullRow(mine[2]);
        await ForeignAndOtherKindsRow();
        DeadRow(mine[0]);
        await ZoomRows(mine[0]);
        await TwelveRow();
        await PickRows();
        if (_shots != null) await Shots(mine[0]);

        _runner.Ticked -= OnTicked;
        Replay replay = _runner.Recorder!.ToReplay();
        RemoveChild(_match);
        _match.QueueFree();
        await Frame();
        return replay;
    }

    private void Open(Match m)
    {
        _runner = m.GetNode<SimRunner>("SimRunner");
        _sim = _runner.Simulation!;
        _camera = m.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _notices = m.GetNode<OrderNoticeView>("World3D/OrderNotices");
        _sfx = m.GetNode<Sfx>("Sfx");
        _log.Clear();
        _runner.Ticked += OnTicked; // after the view's own handler (Match.Start subscribed it first)
    }

    private void OnTicked(Simulation sim)
    {
        foreach (DroppedOrder e in sim.World.DroppedOrders) _log.Add((sim.TickNumber, e));
    }

    // (A) A walk, then a Shift-queued Keep it cannot pay for at its turn.
    private async Task CannotAffordRow(EntityHandle w)
    {
        Focus(w);
        int anchor = AnchorWith(PlacementError.CannotAfford, _keep, w, 6);
        NVector2 pos = W.Units.Position[w.Index];
        (int sounds, int cued, int logged) = Marks();
        _sim.Enqueue(Command.Move(0, w, pos + new NVector2(6f, 0f)));
        _sim.Enqueue(Command.Build(0, w, _keep, PlacementGhost.AnchorPoint(W.NavGrid, anchor), queued: true));
        int at = await DropOf(w, logged, 20);
        if (!Check(at >= 0, "(A) the queued Keep was never dropped")) return;
        int dropTick = _log[at].Tick;
        Check(_log[at].Entry.Placement == PlacementError.CannotAfford && _log[at].Entry.Kind == CommandKind.Build, $"(A) dropped {_log[at].Entry.Kind} with {_log[at].Entry.Placement}");
        await Frame();
        int slot = LineOf(w);
        string want = _ui.PlacementText(PlacementError.CannotAfford);
        Check(_sfx.PlayCount(SfxEvent.Refused) - sounds == 1 && _notices.SoundsCued - cued == 1,
            $"(A) Refused sound played {_sfx.PlayCount(SfxEvent.Refused) - sounds} times, cued {_notices.SoundsCued - cued}");
        if (!Check(slot >= 0, "(A) no line over the worker")) return;
        Label3D label = _notices.Label(slot);
        Check(label.Visible && label.Text == want, $"(A) the line reads '{label.Text}' (visible {label.Visible}), want '{want}'");
        GD.Print($"  (A) dropped at tick {dropTick} (the walk first); next frame: {_sfx.PlayCount(SfxEvent.Refused) - sounds} Refused, line '{label.Text}'");
        // It follows the unit: a walk, and the line's ground point stays over it.
        _sim.Enqueue(Command.Move(0, w, W.Units.Position[w.Index] + new NVector2(-8f, 0f)));
        for (int f = 0; f < 15; f++) await Frame();
        Vector3 ground = UnitViews.GroundPoint(W, w.Index, (float)_runner.Alpha);
        float off = new Vector2(label.Position.X - ground.X, label.Position.Z - ground.Z).Length();
        Check(_notices.Lines.Active[slot] ? off < 0.01f && label.Position.Y > ground.Y + 1.5f : true,
            $"(A) the line is {off:0.000} m beside the walking unit, {label.Position.Y - ground.Y:0.00} m over it");
        GD.Print($"  (A) walking: line {off:0.000} m beside the unit, {label.Position.Y - ground.Y:0.00} m over its ground");
    }

    // (B) A queued House onto the start Keep's own footprint: blocked at its turn.
    private async Task BlockedRow(EntityHandle w)
    {
        int hallAnchor = _match.Bases!.HallAnchor[0];
        (int sounds, _, int logged) = Marks();
        _sim.Enqueue(Command.Stop(0, w));
        _sim.Enqueue(Command.Build(0, w, _house, PlacementGhost.AnchorPoint(W.NavGrid, hallAnchor), queued: true));
        int at = await DropOf(w, logged, 10);
        if (!Check(at >= 0, "(B) the queued House was never dropped")) return;
        int dropTick = _log[at].Tick;
        PlacementError error = _log[at].Entry.Placement;
        Check(error == PlacementError.Blocked, $"(B) dropped with {error}, want Blocked");
        await Frame();
        int slot = LineOf(w);
        string want = _ui.PlacementText(error);
        Check(slot >= 0 && _notices.Label(slot).Visible && _notices.Label(slot).Text == want && _notices.TextOf(slot) == want,
            $"(B) the line reads '{(slot >= 0 ? _notices.Label(slot).Text : "none")}', want '{want}'");
        Check(_sfx.PlayCount(SfxEvent.Refused) - sounds == 1, $"(B) {_sfx.PlayCount(SfxEvent.Refused) - sounds} Refused sounds");
        GD.Print($"  (B) dropped at tick {dropTick} with {error}: line '{(slot >= 0 ? _notices.Label(slot).Text : "none")}'");
    }

    // (C) Eight queued Moves behind a long walk, then a ninth queued order sent to the sim directly.
    private async Task QueueFullRow(EntityHandle w)
    {
        NVector2 pos = W.Units.Position[w.Index];
        _sim.Enqueue(Command.Move(0, w, pos + new NVector2(0f, 30f)));
        for (int k = 0; k < OrderConstants.QueueCapacity; k++) _sim.Enqueue(Command.Move(0, w, pos + new NVector2(k, 25f), queued: true));
        await Ticks(3);
        Check(W.Units.QueueCount[w.Index] == OrderConstants.QueueCapacity, $"(C) the queue holds {W.Units.QueueCount[w.Index]}");
        (int sounds, int cued, int logged) = Marks();
        _sim.Enqueue(Command.AttackMove(0, w, pos + new NVector2(4f, 4f), queued: true));
        int at = await DropOf(w, logged, 10);
        if (!Check(at >= 0, "(C) the ninth order was not listed")) return;
        int dropTick = _log[at].Tick;
        Check(_log[at].Entry.QueueFull && _log[at].Entry.Kind == CommandKind.AttackMove, $"(C) the entry is {_log[at].Entry}");
        await Frame();
        int slot = LineOf(w);
        string want = _ui.Orders(OrdersText.QueueFull);
        Check(slot >= 0 && _notices.Label(slot).Visible && _notices.Label(slot).Text == want, $"(C) the line reads '{(slot >= 0 ? _notices.Label(slot).Text : "none")}', want '{want}'");
        Check(_sfx.PlayCount(SfxEvent.Refused) - sounds == 1 && _notices.SoundsCued - cued == 1, $"(C) {_sfx.PlayCount(SfxEvent.Refused) - sounds} Refused sounds");
        GD.Print($"  (C) ninth order refused at tick {dropTick}: line '{(slot >= 0 ? _notices.Label(slot).Text : "none")}'");
        _sim.Enqueue(Command.Stop(0, w));
        await Ticks(3);
    }

    // (F) The enemy's and the ally's refused Keeps and an own queued Gather on bare ground: listed by the sim, nothing shown.
    private async Task ForeignAndOtherKindsRow()
    {
        await Wait(NoticeLines.LifetimeSeconds + 0.3);
        List<EntityHandle> enemy = Workers(1), ally = Workers(2), mine = Workers(0);
        if (!Check(enemy.Count > 0 && ally.Count > 0, "(F) no enemy or ally worker")) return;
        (int sounds, int cued, int logged) = Marks();
        long shown = _notices.NoticesShown;
        foreach ((int player, EntityHandle w) in new[] { (1, enemy[0]), (2, ally[0]) })
        {
            _sim.Enqueue(Command.Stop(player, w));
            _sim.Enqueue(Command.Build(player, w, StartBase.BuildingOfSlot(_data, W.FactionOf(player), BuildingSlot.TownHall),
                PlacementGhost.AnchorPoint(W.NavGrid, AnchorWith(PlacementError.CannotAfford, StartBase.BuildingOfSlot(_data, W.FactionOf(player), BuildingSlot.TownHall), w, 6, player)), queued: true));
        }
        EntityHandle own = mine[^1];
        _sim.Enqueue(Command.Stop(0, own));
        _sim.Enqueue(Command.Gather(0, own, BareGround(own), queued: true));
        await Ticks(4);
        await Frame();
        int enemyEntries = 0, allyEntries = 0, gather = 0;
        for (int k = logged; k < _log.Count; k++)
        {
            DroppedOrder e = _log[k].Entry;
            if (e.Owner == 1 && e.Kind == CommandKind.Build) enemyEntries++;
            if (e.Owner == 2 && e.Kind == CommandKind.Build) allyEntries++;
            if (e.Owner == 0 && e.Kind == CommandKind.Gather) gather++;
        }
        Check(enemyEntries == 1 && allyEntries == 1 && gather == 1, $"(F) the sim listed enemy {enemyEntries}, ally {allyEntries}, own Gather {gather}");
        Check(_sfx.PlayCount(SfxEvent.Refused) == sounds && _notices.SoundsCued == cued && _notices.NoticesShown == shown && _notices.ShownLines == 0,
            $"(F) shown {_notices.NoticesShown - shown}, lines {_notices.ShownLines}, sounds {_sfx.PlayCount(SfxEvent.Refused) - sounds}");
        GD.Print($"  (F) enemy {enemyEntries}, ally {allyEntries}, own Gather {gather} listed: {_notices.NoticesShown - shown} shown, {_sfx.PlayCount(SfxEvent.Refused) - sounds} sounds");
    }

    // (E) Forged entries on the real store: a dead slot and a live slot with an older generation give no line, no sound.
    private void DeadRow(EntityHandle live)
    {
        UnitStore u = W.Units;
        int dead = -1;
        for (int i = 0; i < u.Capacity && dead < 0; i++) if (!u.Alive[i]) dead = i;
        (int sounds, int cued, _) = Marks();
        long shown = _notices.NoticesShown, lost = _notices.NoticesDead;
        var forged = new[]
        {
            new DroppedOrder(new EntityHandle(dead, u.Generation[dead]), 0, CommandKind.Build, PlacementError.CannotAfford, false),
            new DroppedOrder(new EntityHandle(live.Index, live.Generation - 1), 0, CommandKind.Build, PlacementError.Blocked, false),
            new DroppedOrder(new EntityHandle(live.Index, live.Generation + 1), 0, CommandKind.Move, PlacementError.None, true),
        };
        int n = _notices.Collect(forged, u.Alive, u.Generation);
        _notices.Sync(W, (float)_runner.Alpha, 0f);
        Check(n == 0 && _notices.NoticesShown == shown && _notices.NoticesDead - lost == 3 && _notices.SoundsCued == cued && _sfx.PlayCount(SfxEvent.Refused) == sounds,
            $"(E) dead / reused entries: {n} lines, {_notices.NoticesDead - lost} counted dead, {_notices.SoundsCued - cued} sounds");
        // A line whose slot another unit has taken draws nothing (the line kept an older handle).
        int slot = _notices.Lines.Show(new EntityHandle(live.Index, live.Generation - 1), PlacementError.CannotAfford, false);
        _notices.Sync(W, (float)_runner.Alpha, 0f);
        Check(!_notices.Label(slot).Visible, "(E) a line over a reused slot is drawn");
        _notices.Lines.Clear();
        _notices.Sync(W, (float)_runner.Alpha, 0f);
        GD.Print($"  (E) 3 forged dead / reused entries: 0 lines, 0 sounds; a reused slot's line hidden");
    }

    // (G) The line on screen and sized by the zoom at the closest and farthest zoom.
    private async Task ZoomRows(EntityHandle w)
    {
        foreach (float zoom in new[] { CameraLimits.MinZoom, CameraLimits.MaxZoom })
        {
            _camera.SetZoom(zoom);
            Focus(w);
            int slot = _notices.Lines.Show(w, PlacementError.CannotAfford, false);
            await Frame();
            await Frame();
            Label3D label = _notices.Label(slot);
            Camera3D cam = GetViewport().GetCamera3D();
            Vector2 at = cam.UnprojectPosition(label.GlobalPosition);
            Rect2 screen = GetViewport().GetVisibleRect();
            // The em's height on screen: the label's font size in world units, stood up along the camera's own up.
            float em = label.PixelSize * label.FontSize;
            float px = (cam.UnprojectPosition(label.GlobalPosition + cam.GlobalTransform.Basis.Y * em) - at).Length();
            Check(label.Visible && screen.HasPoint(at) && Mathf.IsEqualApprox(label.PixelSize, BuildGhost.ReasonPixelPerZoom * zoom) && px >= 20f,
                $"(G) zoom {zoom}: line at {at} on {screen.Size}, visible {label.Visible}, pixel size {label.PixelSize}, em {px:0.0} px");
            GD.Print($"  (G) zoom {zoom}: line at screen {at:0}, pixel size {label.PixelSize:0.0000}, em {px:0.0} px of {screen.Size.Y}");
            _notices.Lines.Clear();
        }
        _camera.SetZoom(CameraLimits.DefaultZoom);
        await Frame();
    }

    // (D) Twelve Laborers refused in one tick: one sound, twelve lines, all gone after the lifetime; the frame's bytes.
    private async Task TwelveRow()
    {
        List<EntityHandle> mine = Workers(0);
        NVector2 centre = W.Units.Position[mine[0].Index];
        for (int k = mine.Count; k < 12; k++) _sim.Enqueue(Command.SpawnUnit(0, _worker, centre + new NVector2(-6f + 1.5f * (k % 5), -6f - 1.5f * (k / 5))));
        await Ticks(3);
        mine = Workers(0);
        if (!Check(mine.Count >= 12, $"(D) only {mine.Count} workers")) return;
        mine = mine.GetRange(0, 12);
        await Wait(NoticeLines.LifetimeSeconds + 0.3);
        _notices.Lines.Clear();

        // A frame and a tick read with no entries: 0 bytes.
        float alpha = (float)_runner.Alpha;
        for (int k = 0; k < 10; k++) { _notices.Sync(W, alpha, 0f); _notices.CollectTick(W); }
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int k = 0; k < 100; k++) { _notices.Sync(W, alpha, 0f); _notices.CollectTick(W); }
        long idleBytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
        Check(idleBytes == 0, $"(D) 100 frames and tick reads with no notices allocated {idleBytes} bytes");

        int anchor = AnchorWith(PlacementError.CannotAfford, _keep, mine[0], 8);
        (int sounds, int cued, int logged) = Marks();
        foreach (EntityHandle w in mine)
        {
            _sim.Enqueue(Command.Stop(0, w));
            _sim.Enqueue(Command.Build(0, w, _keep, PlacementGhost.AnchorPoint(W.NavGrid, anchor), queued: true));
        }
        await Until(() => _log.Count - logged >= 12, 5);
        await Frame();
        var ticks = new HashSet<int>();
        for (int k = logged; k < _log.Count; k++) ticks.Add(_log[k].Tick);
        Check(_log.Count - logged == 12 && ticks.Count == 1, $"(D) {_log.Count - logged} entries over {ticks.Count} ticks");
        Check(_notices.Lines.Count == 12 && _notices.ShownLines == 12 && _notices.SoundsCued - cued == 1 && _sfx.PlayCount(SfxEvent.Refused) - sounds == 1,
            $"(D) {_notices.Lines.Count} lines, {_notices.ShownLines} drawn, {_notices.SoundsCued - cued} sounds cued, {_sfx.PlayCount(SfxEvent.Refused) - sounds} played");
        string want = _ui.PlacementText(PlacementError.CannotAfford);
        int right = 0;
        foreach (EntityHandle w in mine) { int s = LineOf(w); if (s >= 0 && _notices.Label(s).Visible && _notices.Label(s).Text == want) right++; }
        Check(right == 12, $"(D) {right} of 12 lines read '{want}'");

        alpha = (float)_runner.Alpha;
        before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int k = 0; k < 100; k++) _notices.Sync(W, alpha, 0f);
        long twelveBytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
        GD.Print($"  (D) twelve refused on tick {string.Join(",", ticks)}: {_notices.Lines.Count} lines, {right} reading '{want}', 1 sound; 100 frames with 12 lines: {twelveBytes} bytes; with none: {idleBytes} bytes");

        ulong start = Time.GetTicksUsec();
        await Wait(NoticeLines.LifetimeSeconds - 0.6);
        int stillAt = _notices.ShownLines;
        await Until(() => _notices.Lines.Count == 0, 3);
        double gone = (Time.GetTicksUsec() - start) / 1e6;
        await Frame();
        int visible = 0;
        for (int i = 0; i < _notices.Lines.Capacity; i++) if (_notices.Label(i).Visible) visible++;
        Check(stillAt == 12 && _notices.Lines.Count == 0 && visible == 0 && gone < NoticeLines.LifetimeSeconds + 0.5,
            $"(D) {stillAt} lines at {NoticeLines.LifetimeSeconds - 0.6:0.0} s, all gone after {gone:0.00} s ({visible} labels still visible)");
        GD.Print($"  (D) 12 lines at {NoticeLines.LifetimeSeconds - 0.6:0.0} s, all gone {gone:0.00} s after the refusal frame (lifetime {NoticeLines.LifetimeSeconds} s)");
    }

    // (P) A left click walks the ray for resource nodes once (FinishSelect asks PickResource, then the own-building pick
    // without asking again): clicks over the start Keep and a grid of the screen, counted on the controller.
    private async Task PickRows()
    {
        SelectionController sel = _match.GetNode<SelectionController>("SelectionController");
        NavGrid g = W.NavGrid;
        NVector2 hall = StartBase.FootprintCenter(g, _data.Buildings[_match.Bases!.HallType[0]], _match.Bases.HallAnchor[0]);
        _camera.SetFocus(hall.X, hall.Y);
        await Frame();
        await Frame();
        Camera3D cam = GetViewport().GetCamera3D();
        Rect2 screen = GetViewport().GetVisibleRect();
        var pixels = new List<Vector2> { cam.UnprojectPosition(new Vector3(hall.X, TerrainHeight.At(W.Heightmap, hall.X, hall.Y) + 3f, hall.Y)) };
        for (int gy = 1; gy <= 5; gy++)
            for (int gx = 1; gx <= 9; gx++) pixels.Add(new Vector2(screen.Size.X * gx / 10f, screen.Size.Y * gy / 6f));
        int bad = 0, onBuilding = 0, onGround = 0, onUnit = 0;
        foreach (Vector2 p in pixels)
        {
            sel.ClearBuilding();
            sel.ClearResource();
            sel.Selection.Clear();
            int before = sel.ResourcePicks;
            sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = p });
            sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = p });
            int calls = sel.ResourcePicks - before;
            if (sel.Selection.Count > 0) { onUnit++; if (calls != 0) bad++; }
            else
            {
                if (sel.SelectedBuilding >= 0) onBuilding++;
                else if (sel.SelectedResource < 0) onGround++;
                if (calls != 1) bad++;
            }
        }
        sel.ClearBuilding();
        sel.Selection.Clear();
        Check(bad == 0 && onBuilding >= 1 && onGround >= 1, $"(P) {bad} of {pixels.Count} clicks walked the node ray other than once (building {onBuilding}, ground {onGround}, unit {onUnit})");
        GD.Print($"  (P) {pixels.Count} left clicks (own building {onBuilding}, open ground {onGround}, unit {onUnit}): PickResource once each where no unit is picked, {bad} wrong");
    }

    private async Task Shots(EntityHandle w)
    {
        foreach (float zoom in new[] { CameraLimits.DefaultZoom, CameraLimits.MinZoom })
        {
            await Wait(NoticeLines.LifetimeSeconds + 0.3);
            _camera.SetZoom(zoom);
            Focus(w);
            int logged = _log.Count;
            _sim.Enqueue(Command.Stop(0, w));
            _sim.Enqueue(Command.Build(0, w, _keep, PlacementGhost.AnchorPoint(W.NavGrid, AnchorWith(PlacementError.CannotAfford, _keep, w, 6)), queued: true));
            await DropOf(w, logged, 10);
            Focus(w);
            for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            string path = $"{_shots}/m6v27-cant-afford-{zoom:0}m.png";
            GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print($"screenshot {path}");
        }
        _camera.SetZoom(CameraLimits.DefaultZoom);
    }

    // ---- (R) the replay ----

    private async Task Replayed(Replay replay)
    {
        var m = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(m);
        ReplayError refused = m.StartReplay(_data, replay, LaunchOptions.Parse(new[] { "--speed", "8", "--mute" }), "order-notice-test");
        if (!Check(refused == ReplayError.None, $"(R) replay refused: {refused}")) return;
        _match = m;
        Open(m);
        int sounds = _sfx.PlayCount(SfxEvent.Refused);
        int maxShown = 0;
        ulong start = Time.GetTicksUsec();
        while (_runner.PlaybackState == PlaybackState.Playing && Time.GetTicksUsec() - start < 90_000_000UL)
        {
            await Frame();
            maxShown = Math.Max(maxShown, _notices.ShownLines);
        }
        int own = 0;
        foreach ((_, DroppedOrder e) in _log) if (e.Owner == 0 && OrderNotices.IsNotice(in e)) own++;
        Check(_runner.PlaybackState == PlaybackState.Ended, $"(R) playback {_runner.PlaybackState} ({_runner.PlaybackError}) at tick {_sim.TickNumber}");
        Check(!_notices.Enabled && own >= 15, $"(R) notices enabled {_notices.Enabled}; the replayed sim listed {own} own refusals");
        Check(_notices.NoticesShown == 0 && _notices.SoundsCued == 0 && maxShown == 0 && _sfx.PlayCount(SfxEvent.Refused) == sounds,
            $"(R) in playback: {_notices.NoticesShown} shown, {maxShown} lines at most, {_notices.SoundsCued} sounds cued, {_sfx.PlayCount(SfxEvent.Refused) - sounds} played");
        GD.Print($"  (R) playback to tick {_sim.TickNumber}: {own} own refusals listed by the sim, 0 shown, 0 played");
        _runner.Ticked -= OnTicked;
        RemoveChild(m);
        m.QueueFree();
        await Frame();
    }

    // ---- helpers ----

    private List<EntityHandle> Workers(int player)
    {
        var list = new List<EntityHandle>();
        UnitStore u = W.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == player && _data.Units[u.TypeId[i]].IsWorker) list.Add(new EntityHandle(i, u.Generation[i]));
        return list;
    }

    // The nearest anchor at least `away` cells from the unit where CanPlace refuses exactly `want` (so the order fails for that reason only).
    private int AnchorWith(PlacementError want, int type, EntityHandle near, int away, int player = 0)
    {
        NavGrid g = W.NavGrid;
        NVector2 p = W.Units.Position[near.Index];
        int x0 = (int)(p.X / MapConstants.CellSize), y0 = (int)(p.Y / MapConstants.CellSize);
        for (int r = away; r < 40; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r || !g.InBounds(x0 + dx, y0 + dy)) continue;
                    int a = (y0 + dy) * g.Width + x0 + dx;
                    if (!W.CanPlace(player, type, a, out PlacementError e) && e == want) return a;
                }
        throw new InvalidOperationException($"no anchor refused {want} near {p}");
    }

    // The nearest cell centre to the unit with no resource node within the gather search radius (plus a cell), so a Gather there has nothing to work.
    private NVector2 BareGround(EntityHandle near)
    {
        NavGrid g = W.NavGrid;
        ResourceStore r = W.Resources;
        float clear = W.Data.Rules.NodeSearchRadius + 2f * MapConstants.CellSize;
        NVector2 p = W.Units.Position[near.Index];
        int x0 = (int)(p.X / MapConstants.CellSize), y0 = (int)(p.Y / MapConstants.CellSize);
        for (int rad = 0; rad < 60; rad++)
            for (int dy = -rad; dy <= rad; dy++)
                for (int dx = -rad; dx <= rad; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != rad || !g.InBounds(x0 + dx, y0 + dy)) continue;
                    NVector2 c = g.CellCenter(x0 + dx, y0 + dy);
                    bool bare = true;
                    for (int n = 0; n < r.Capacity && bare; n++)
                        if (r.Alive[n] && NVector2.Distance(c, g.CellCenter(r.Cell[n] % g.Width, r.Cell[n] / g.Width)) < clear) bare = false;
                    if (bare) return c;
                }
        throw new InvalidOperationException($"no bare ground near {p}");
    }

    private int LineOf(EntityHandle unit)
    {
        for (int i = 0; i < _notices.Lines.Capacity; i++)
            if (_notices.Lines.Active[i] && _notices.Lines.Unit[i] == unit) return i;
        return -1;
    }

    private (int Sounds, int Cued, int Logged) Marks() => (_sfx.PlayCount(SfxEvent.Refused), _notices.SoundsCued, _log.Count);

    // The log index of the first entry for `unit` logged at or after index `from`, waiting up to `seconds`; -1 for none.
    private async Task<int> DropOf(EntityHandle unit, int from, double seconds)
    {
        ulong start = Time.GetTicksUsec();
        while (Time.GetTicksUsec() - start < (ulong)(seconds * 1_000_000))
        {
            for (int k = from; k < _log.Count; k++) if (_log[k].Entry.Unit == unit) return k;
            await Frame();
        }
        return -1;
    }

    private void Focus(EntityHandle w)
    {
        NVector2 p = W.Units.Position[w.Index];
        _camera.SetFocus(p.X, p.Y);
    }

    private async Task Ticks(int n)
    {
        int until = _sim.TickNumber + n;
        while (_sim.TickNumber < until) await Frame();
    }

    private async Task Wait(double seconds)
    {
        ulong start = Time.GetTicksUsec();
        while (Time.GetTicksUsec() - start < (ulong)(seconds * 1_000_000)) await Frame();
    }

    private async Task Until(Func<bool> done, double seconds)
    {
        ulong start = Time.GetTicksUsec();
        while (!done() && Time.GetTicksUsec() - start < (ulong)(seconds * 1_000_000)) await Frame();
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
