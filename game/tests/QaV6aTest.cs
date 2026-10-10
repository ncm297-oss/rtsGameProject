using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.Replays;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// QA for M4-V6a (session 2026-10-09-1155) on the real Match scene: 500 targeting clicks (each armed click sends exactly one
/// <c>UseAbility</c> for a selected mage, the click path allocates 0 bytes after warm-up, then the hash twin); a mixed
/// selection (Heavy Infantry + two mages) never sends the order to the soldier; the circle on an unexplored cursor spot
/// (drawn, no snap to a hidden enemy); the only selected caster dying while targeting is armed (no exception, the range
/// ring never drawn round the dead slot, the click sends nothing).
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaV6aTest.tscn</c>; prints "QA V6A TEST PASS" and exits 0, or each failure and exits 1.</remarks>
public partial class QaV6aTest : Node
{
    private static readonly FieldInfo CommandsField = typeof(Simulation).GetField("_commands", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private Match _match = null!;
    private SimRunner _runner = null!;
    private Simulation _sim = null!;
    private SelectionController _sel = null!;
    private CommandCard _card = null!;
    private AbilityViews _views = null!;
    private RtsCamera _camera = null!;
    private Vector2 _screen;
    private int _telas, _mage, _raider, _infantry, _laborer;

    private World W => _sim.World;
    private UnitStore U => _sim.World.Units;
    private NavGrid G => _sim.World.NavGrid;

    public override async void _Ready()
    {
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            await Frame();
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            _telas = _data.FindAbility("telas_fire");
            _mage = _data.FindUnit("malazan_cadre_mage");
            _raider = _data.FindUnit("whirlwind_raider");
            _infantry = _data.FindUnit("malazan_heavy_infantry");
            _laborer = _data.FindUnit("malazan_laborer");
            await SpamAndMixed(1);
            await SpamAndMixed(6);
            await DyingCaster(1);
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures.Take(40)) GD.Print($"QA V6A TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA V6A TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private void StartMatch(ulong seed, params string[] extra)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _runner = _match.GetNode<SimRunner>("SimRunner");
        _runner.RecordCheckpointInterval = 1;
        var args = new List<string> { "--seed", seed.ToString(CultureInfo.InvariantCulture), "--mute", "--zoom", "30", "--units", "0", "--no-bases" };
        args.AddRange(extra);
        _match.Start(_data, LaunchOptions.Parse(args.ToArray()));
        _runner.ProcessMode = ProcessModeEnum.Disabled;
        _sim = _runner.Simulation!;
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _card = _match.GetNode<CommandCard>("Hud/CommandCard");
        _views = _match.GetNode<AbilityViews>("World3D/AbilityViews");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _screen = GetViewport().GetVisibleRect().Size;
    }

    private async Task EndMatch()
    {
        Input.ActionRelease("order_queue");
        RemoveChild(_match);
        _match.QueueFree();
        await Frame();
    }

    private System.Numerics.Vector2 Spot(FlowField field, HashSet<int> taken, int x, int y)
    {
        int best = -1;
        float bestD = float.MaxValue;
        for (int yy = Math.Max(0, y - 6); yy <= Math.Min(G.Height - 1, y + 6); yy++)
            for (int xx = Math.Max(0, x - 6); xx <= Math.Min(G.Width - 1, x + 6); xx++)
            {
                int c = yy * G.Width + xx;
                float d = (xx - x) * (xx - x) + (yy - y) * (yy - y);
                if (taken.Contains(c) || !(field.CostAt(c) <= 60f) || d >= bestD) continue;
                bestD = d;
                best = c;
            }
        if (best < 0) throw new InvalidOperationException($"no passable spot near ({x}, {y})");
        taken.Add(best);
        return G.CellCenter(best % G.Width, best / G.Width);
    }

    private List<EntityHandle> Live(int owner, int type)
    {
        var list = new List<EntityHandle>();
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && U.Owner[i] == owner && U.TypeId[i] == type) list.Add(new EntityHandle(i, U.Generation[i]));
        return list;
    }

    private async Task SpamAndMixed(ulong seed)
    {
        StartMatch(seed, "--no-combat");
        int center = FlowField.NearestPassable(G, G.Height / 2 * G.Width + G.Width / 2);
        int cx = center % G.Width, cy = center / G.Width;
        FlowField field = FlowField.Build(G, center);
        var taken = new HashSet<int>();
        System.Numerics.Vector2 knot = Spot(field, taken, cx + 6, cy);
        _sim.Enqueue(Command.SpawnUnit(1, _raider, knot));
        _sim.Enqueue(Command.SpawnUnit(1, _raider, Spot(field, taken, cx + 7, cy)));
        _sim.Enqueue(Command.SpawnUnit(0, _mage, Spot(field, taken, cx - 4, cy)));
        _sim.Enqueue(Command.SpawnUnit(0, _mage, Spot(field, taken, cx - 9, cy)));
        _sim.Enqueue(Command.SpawnUnit(0, _infantry, Spot(field, taken, cx + 2, cy)));
        _sim.Enqueue(Command.SpawnUnit(0, _laborer, Spot(field, taken, cx + 3, cy + 3)));
        for (int t = 0; t < 8; t++) _sim.Tick();
        List<EntityHandle> mages = Live(0, _mage), soldiers = Live(0, _infantry);
        if (!Check(mages.Count == 2 && soldiers.Count == 1, $"seed {seed}: staged {mages.Count} mages, {soldiers.Count} soldiers")) { await EndMatch(); return; }
        _camera.SetFocus(G.CellCenter(cx, cy).X, G.CellCenter(cx, cy).Y);
        await Frames();

        // ---- mixed selection: soldier first in the selection, two mages ----
        _sel.Selection.Clear();
        _sel.Selection.Add(soldiers[0]);
        foreach (EntityHandle m in mages) _sel.Selection.Add(m);
        _sel.Subgroups.Update(_sel.Selection.Items, U.TypeId, reset: true);
        await Frames();
        bool rowOnFirst = _card.ActionAt(0) == CardCommand.Ability;
        GD.Print($"seed {seed} mixed: active subgroup {_data.Units[_sel.Subgroups.ActiveType].Key}, cell 0 {_card.ActionAt(0)} (ability row on first show: {rowOnFirst})");
        bool armed = _sel.BeginAbility(_telas);
        Check(armed, $"seed {seed} mixed: BeginAbility refused with two ready mages selected");
        foreach (EntityHandle target in new[] { soldiers[0], mages[0], mages[1] })
        {
            System.Numerics.Vector2 p = U.Position[target.Index] + new System.Numerics.Vector2(0.5f, 0f);
            int from = Pending();
            _sel.AbilityOrder(_telas, p, false);
            List<Command> sent = PendingList(from);
            Check(sent.Count == 1 && sent[0].Kind == CommandKind.UseAbility && U.TypeId[sent[0].Unit.Index] == _mage,
                $"seed {seed} mixed: a point by unit {target.Index} sent {sent.Count} commands to type {(sent.Count > 0 ? U.TypeId[sent[0].Unit.Index] : -1)}");
        }
        Esc();
        _sim.Tick();

        // ---- 500 targeting clicks on ground pixels ----
        _sel.Selection.Clear();
        foreach (EntityHandle m in mages) _sel.Selection.Add(m);
        _sel.Subgroups.Update(_sel.Selection.Items, U.TypeId, reset: true);
        await Frames();
        var pixels = new List<Vector2>();
        for (int k = 0; k < 400 && pixels.Count < 12; k++)
        {
            var px = new Vector2(_screen.X * (0.2f + 0.6f * (k % 20) / 20f), _screen.Y * (0.15f + 0.4f * (k / 20) / 20f));
            if (_sel.AbilityPoint(px, out System.Numerics.Vector2 pt) && !_sel.EnemyAt(px, out _, out _)) pixels.Add(px);
        }
        if (!Check(pixels.Count >= 4, $"seed {seed} spam: only {pixels.Count} ground pixels")) { await EndMatch(); return; }
        int armedClicks = 0, sentTotal = 0, bad = 0;
        long bytes = 0;
        for (int c = 0; c < 520; c++)
        {
            if (c % 5 == 0) _sim.Tick();
            bool on = _sel.BeginAbility(_telas);
            int from = Pending();
            Vector2 px = pixels[c % pixels.Count];
            long b0 = GC.GetAllocatedBytesForCurrentThread();
            _sel.AttackMoveClick(px);
            long spent = GC.GetAllocatedBytesForCurrentThread() - b0;
            if (c >= 20) bytes += spent;
            int sentNow = Pending() - from;
            if (on) armedClicks++;
            sentTotal += sentNow;
            if (sentNow != (on ? 1 : 0)) { bad++; if (bad < 5) Check(false, $"seed {seed} spam click {c}: armed {on}, sent {sentNow}"); }
            if (sentNow == 1)
            {
                Command cmd = PendingList(from)[0];
                if (cmd.Kind != CommandKind.UseAbility || !mages.Contains(cmd.Unit)) Check(false, $"seed {seed} spam click {c}: sent {cmd.Kind} to {cmd.Unit}");
            }
            Check(!_sel.Targeting, $"seed {seed} spam click {c}: still armed after the click");
        }
        GD.Print($"seed {seed} spam: 520 clicks, {armedClicks} armed, {sentTotal} UseAbility, click path {bytes} bytes over the last 500");
        // Where the bytes go (one call each, after the warm-up above), and the A-move click path for comparison.
        {
            Vector2 px = pixels[0];
            long b0 = GC.GetAllocatedBytesForCurrentThread();
            bool q = Input.IsActionPressed("order_queue");
            long b1 = GC.GetAllocatedBytesForCurrentThread();
            _sel.AbilityPoint(px, out System.Numerics.Vector2 pt);
            long b2 = GC.GetAllocatedBytesForCurrentThread();
            _sel.PickCaster(_telas, pt, false, out _);
            long b3 = GC.GetAllocatedBytesForCurrentThread();
            _sel.AbilityOrder(_telas, pt, false);
            long b4 = GC.GetAllocatedBytesForCurrentThread();
            _sel.BeginAttackMove();
            long b5 = GC.GetAllocatedBytesForCurrentThread();
            _sel.AttackMoveClick(px);
            long b6 = GC.GetAllocatedBytesForCurrentThread();
            _sel.BeginAttackMove();
            long b7 = GC.GetAllocatedBytesForCurrentThread();
            _sel.AttackMoveClick(px);
            long b8 = GC.GetAllocatedBytesForCurrentThread();
            GD.Print($"seed {seed} bytes: IsActionPressed(string) {b1 - b0}, AbilityPoint {b2 - b1}, PickCaster {b3 - b2}, AbilityOrder {b4 - b3}, A-move click {b6 - b5} then {b8 - b7}");
            _sim.Tick();
        }
        // The real frame entry (_Process), not Sync: AbilityViewTest's 0 B steady row calls Sync directly.
        {
            for (int f = 0; f < 20; f++) _views._Process(0.016);
            long p0 = GC.GetAllocatedBytesForCurrentThread();
            for (int f = 0; f < 100; f++) _views._Process(0.016);
            long processBytes = GC.GetAllocatedBytesForCurrentThread() - p0;
            GD.Print($"seed {seed} AbilityViews._Process: {processBytes} bytes over 100 frames (not targeting)");
            Check(processBytes == 0, $"seed {seed}: AbilityViews._Process allocated {processBytes} bytes over 100 steady frames");
        }
        Check(bytes == 0, $"seed {seed} spam: the click path allocated {bytes} bytes over 500 clicks");
        Check(armedClicks > 0, $"seed {seed} spam: never armed");
        for (int t = 0; t < 560; t++) _sim.Tick(); // past the 25 s cooldown: a mage is ready for the next row

        // ---- the circle on an unexplored spot: a hidden Raider there is not snapped to ----
        int far = -1;
        for (int c = 0; c < G.Width * G.Height && far < 0; c++)
        {
            int x = c % G.Width, y = c / G.Width;
            if (Math.Abs(x - cx) < 30 || field.CostAt(c) > 400f || !(field.CostAt(c) >= 0f)) continue;
            if (!W.Fog.IsExplored(0, c)) far = c;
        }
        if (Check(far >= 0, $"seed {seed} unexplored: no unexplored passable cell"))
        {
            System.Numerics.Vector2 hiddenAt = G.CellCenter(far % G.Width, far / G.Width);
            _sim.Enqueue(Command.SpawnUnit(1, _raider, hiddenAt));
            _sim.Tick();
            _sim.Tick();
            EntityHandle hidden = Live(1, _raider).OrderBy(h => System.Numerics.Vector2.Distance(U.Position[h.Index], hiddenAt)).First();
            _camera.SetFocus(hiddenAt.X, hiddenAt.Y);
            await Frames();
            Vector3 body = new(U.Position[hidden.Index].X, TerrainHeight.At(W.Heightmap, U.Position[hidden.Index].X, U.Position[hidden.Index].Y) + 0.9f, U.Position[hidden.Index].Y);
            Vector2 px = _camera.UnprojectPosition(body);
            Check(_sel.BeginAbility(_telas) || _sel.SoonestReady(_telas) > 0, $"seed {seed} unexplored: arm failed with a ready mage");
            if (Check(_sel.TargetAbility == _telas, $"seed {seed} unexplored: not armed (cooldown {_sel.SoonestReady(_telas)} ticks)"))
            {
                bool enemy = _sel.EnemyAt(px, out _, out _);
                bool ok = _sel.AbilityPoint(px, out System.Numerics.Vector2 point);
                _views.Sync(W, 0.5f, px, false);
                Check(!enemy, $"seed {seed} unexplored: EnemyAt saw the hidden Raider");
                Check(ok && point != U.Position[hidden.Index], $"seed {seed} unexplored: the ability point snapped to the hidden Raider ({point})");
                Check(_views.TargetingShown && _views.RadiusRing.Visible, $"seed {seed} unexplored: no circle on the unexplored cursor spot");
                GD.Print($"seed {seed} unexplored: circle at {_views.RadiusCenter}, hidden Raider at {U.Position[hidden.Index]}, range caster {_views.RangeCaster}");
                Esc();
            }
        }
        Twin(seed);
        await EndMatch();
    }

    private async Task DyingCaster(ulong seed)
    {
        StartMatch(seed); // combat on
        int center = FlowField.NearestPassable(G, G.Height / 2 * G.Width + G.Width / 2);
        int cx = center % G.Width, cy = center / G.Width;
        FlowField field = FlowField.Build(G, center);
        var taken = new HashSet<int>();
        _sim.Enqueue(Command.SpawnUnit(0, _mage, Spot(field, taken, cx, cy)));
        for (int k = 0; k < 6; k++) _sim.Enqueue(Command.SpawnUnit(1, _raider, Spot(field, taken, cx + 1, cy)));
        _sim.Tick();
        _sim.Tick();
        EntityHandle mage = Live(0, _mage).Single();
        _camera.SetFocus(U.Position[mage.Index].X, U.Position[mage.Index].Y);
        _sel.Selection.Clear();
        _sel.Selection.Add(mage);
        _sel.Subgroups.Update(_sel.Selection.Items, U.TypeId, reset: true);
        await Frames();
        Vector2 px = new(_screen.X * 0.5f, _screen.Y * 0.3f);
        if (!Check(_sel.BeginAbility(_telas), $"seed {seed} dying: could not arm")) { await EndMatch(); return; }
        int ticks = 0;
        while (U.IsAlive(mage) && ticks < 2000)
        {
            _sim.Tick();
            ticks++;
            _views.Sync(W, 0.5f, px, false);
            if (!U.IsAlive(mage)) Check(_views.RangeCaster != mage.Index || U.IsAlive(new EntityHandle(mage.Index, U.Generation[mage.Index])),
                $"seed {seed} dying: range ring drawn round the dead mage's slot");
        }
        if (!Check(!U.IsAlive(mage), $"seed {seed} dying: the mage survived {ticks} ticks")) { await EndMatch(); return; }
        await Frames();
        _views.Sync(W, 0.5f, px, false);
        Check(_views.RangeCaster < 0, $"seed {seed} dying: range ring round slot {_views.RangeCaster} after the only caster died");
        GD.Print($"seed {seed} dying: mage died after {ticks} ticks; still armed {_sel.Targeting}, circle shown {_views.TargetingShown}, card cell 0 {_card.ActionAt(0)}");
        int from = Pending();
        _sel.AttackMoveClick(px);
        Check(Pending() == from, $"seed {seed} dying: a click after the caster died sent {Pending() - from} commands");
        Check(!_sel.Targeting, $"seed {seed} dying: still armed after the click");
        Twin(seed);
        await EndMatch();
    }

    private void Twin(ulong seed)
    {
        Replay replay = _runner.Recorder!.ToReplay();
        ReplayResult twin = ReplayPlayer.Run(replay, _data);
        Check(twin.Ok && replay.Checkpoints.Length == replay.TickCount, $"seed {seed}: twin {twin.Error} at tick {twin.Tick}");
        GD.Print($"seed {seed}: twin {replay.Commands.Length} commands ({replay.Commands.Count(c => c.Kind == CommandKind.UseAbility)} UseAbility), {replay.Checkpoints.Length} checkpoints equal: {twin.Ok}");
    }

    private void Esc()
    {
        _sel._UnhandledInput(new InputEventKey { Keycode = Godot.Key.Escape, PhysicalKeycode = Godot.Key.Escape, Pressed = true });
        _sel._UnhandledInput(new InputEventKey { Keycode = Godot.Key.Escape, PhysicalKeycode = Godot.Key.Escape, Pressed = false });
    }

    private int Pending() => ((CommandQueue)CommandsField.GetValue(_sim)!).Count;

    private List<Command> PendingList(int from)
    {
        var q = (CommandQueue)CommandsField.GetValue(_sim)!;
        var list = new List<Command>();
        for (int i = from; i < q.Count; i++) list.Add(q[i]);
        return list;
    }

    private async Task Frames()
    {
        await Frame();
        await Frame();
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
