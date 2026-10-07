using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Game.Tests;

/// <summary>QA (M2-6): an independent audit of the generated clips (DC offset, clicks, pitch), box and right-click spam at 2,000 units, rapid S / H / A / Esc, a selection that dies in the frame it is ordered, minimap edges, and <c>--mute</c> beside every other flag.</summary>
/// <remarks>
/// Headless. Run: <c>&amp; $env:GODOT --headless --path game res://tests/QaM26Test.tscn</c>; prints
/// "QA M2-6 TEST PASS" and exits 0, or prints each failure and exits 1. Scan the log for ERROR too.
/// </remarks>
public partial class QaM26Test : Node
{
    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private Match _match = null!;
    private Sfx _sfx = null!;
    private SelectionController _sel = null!;
    private RtsCamera _camera = null!;
    private Simulation _sim = null!;
    private Vector2 _screen;

    public override async void _Ready()
    {
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            await Frame();
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            ClipAudit();
            MuteWithEveryFlag();
            await StartMatch();
            await BoxSpam();
            await RightClickSpam();
            await NoAllocationAfterWarmUp();
            await RapidKeys();
            await SelectionDiesMidFrame();
            await MinimapEdges();
            Check(_sfx.GetChildCount() == Sfx.PoolSize, $"Sfx has {_sfx.GetChildCount()} children, expected the {Sfx.PoolSize}-player pool");
            await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string a in new[] { "order_queue", "select_add", "select_type", "group_assign", "group_add" }) Input.ActionRelease(a);
        foreach (string f in _failures) GD.Print($"QA M2-6 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M2-6 TEST PASS");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    // Reads the 16-bit clip bytes (what plays), not Synthesize's floats.
    private void ClipAudit()
    {
        var sfx = new Sfx();
        AddChild(sfx);
        (SfxEvent e, (float hz, float ms)[] notes)[] rows =
        {
            (SfxEvent.Select, new[] { (1320f, 70f) }),
            (SfxEvent.Command, new[] { (660f, 60f), (990f, 60f) }),
        };
        foreach ((SfxEvent e, (float hz, float ms)[] notes) in rows)
        {
            byte[] data = sfx.Clip(e).Data;
            int n = data.Length / 2;
            var s = new float[n];
            for (int i = 0; i < n; i++) s[i] = (short)(data[2 * i] | (data[2 * i + 1] << 8)) / 32768f;
            double sum = 0, peak = 0;
            foreach (float v in s) { sum += v; peak = Math.Max(peak, Math.Abs(v)); }
            double dc = sum / n;
            Check(Math.Abs(dc) < 0.01, $"{e}: DC offset {dc:F5}");
            Check(Math.Abs(s[0]) <= 2f / 32768f && Math.Abs(s[n - 1]) <= 2f / 32768f, $"{e}: first / last sample {s[0]} / {s[n - 1]} not silent");
            int at = 0;
            foreach ((float hz, float ms) in notes)
            {
                int count = (int)MathF.Round(ms * Sfx.MixRate / 1000f);
                // A click is a step bigger than the steepest a sine of this pitch and the clip's peak can take.
                double maxStep = 2 * Math.PI * hz / Sfx.MixRate * peak * 1.15 + 2.0 / 32768;
                double worst = 0;
                int crossings = 0;
                for (int i = at + 1; i < at + count; i++)
                {
                    worst = Math.Max(worst, Math.Abs(s[i] - s[i - 1]));
                    if ((s[i - 1] < 0) != (s[i] < 0) && s[i] != 0 && s[i - 1] != 0) crossings++;
                }
                double want = 2 * hz * ms / 1000.0;
                GD.Print($"clip {e} note {hz} Hz: dc {dc:F5}, worst step {worst:F4} (limit {maxStep:F4}), crossings {crossings} (~{want:F0})");
                Check(worst <= maxStep, $"{e} {hz} Hz: step {worst:F4} > {maxStep:F4} (click)");
                Check(Math.Abs(crossings - want) <= want * 0.1 + 4, $"{e} {hz} Hz: {crossings} zero crossings, expected ~{want:F0} (wrong pitch)");
                // Boundary between notes: both sides near silence.
                Check(Math.Abs(s[at]) < 0.01f && Math.Abs(s[at + count - 1]) < 0.01f, $"{e} {hz} Hz: note edges {s[at]} / {s[at + count - 1]}");
                at += count;
            }
            Check(at == n, $"{e}: notes cover {at} of {n} samples");
        }
        sfx.QueueFree();
    }

    private void MuteWithEveryFlag()
    {
        string[][] others =
        {
            new[] { "--seed", "3" }, new[] { "--speed", "2" }, new[] { "--screenshot", "C:/x.png" },
            new[] { "--screenshot-after", "1" }, new[] { "--units", "5" }, new[] { "--zoom", "50" },
            new[] { "--no-hud" }, new[] { "--debug-overlay" }, new[] { "--forests", "2" }, new[] { "--mines", "1" },
        };
        foreach (string[] f in others)
        {
            var before = new List<string> { "--mute" };
            before.AddRange(f);
            var after = new List<string>(f) { "--mute" };
            foreach (List<string> args in new[] { before, after })
            {
                LaunchOptions o = LaunchOptions.Parse(args.ToArray());
                Check(o.Mute && OtherApplied(o, f[0]), $"[{string.Join(' ', args)}]: mute {o.Mute}, {f[0]} applied {OtherApplied(o, f[0])}");
            }
            if (f.Length == 2)
            {
                // A value-taking flag with its value missing must not eat --mute.
                LaunchOptions o = LaunchOptions.Parse(new[] { f[0], "--mute" });
                Check(o.Mute, $"[{f[0]} --mute]: --mute was swallowed as {f[0]}'s value");
            }
        }
        Check(LaunchOptions.Parse(new[] { "--mute", "--mute" }).Mute, "--mute --mute");
        Check(!LaunchOptions.Parse(new[] { "--MUTE" }).Mute && !LaunchOptions.Parse(new[] { "--mute=0" }).Mute && !LaunchOptions.Parse(new[] { "mute" }).Mute, "a misspelt --mute muted");
        LaunchOptions all = LaunchOptions.Parse(new[] { "--seed", "3", "--mute", "true", "--units", "5", "--no-hud", "--debug-overlay" });
        Check(all.Mute && all.Seed == 3 && all.UnitsPerPlayer == 5 && all.NoHud && all.DebugOverlay, "--mute true in the middle broke the other flags");
    }

    private static bool OtherApplied(LaunchOptions o, string flag) => flag switch
    {
        "--seed" => o.Seed == 3,
        "--speed" => o.Speed == 2,
        "--screenshot" => o.ScreenshotPath == "C:/x.png",
        "--screenshot-after" => o.ScreenshotAfter == 1,
        "--units" => o.UnitsPerPlayer == 5,
        "--zoom" => o.Zoom != null,
        "--no-hud" => o.NoHud,
        "--debug-overlay" => o.DebugOverlay,
        "--forests" => o.Forests == 2,
        "--mines" => o.Mines == 1,
        _ => false,
    };

    private async Task StartMatch()
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _match.Start(_data, LaunchOptions.Parse(new[] { "--units", "1000", "--zoom", "60" }));
        _sfx = _match.GetNode<Sfx>("Sfx");
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _sim = _match.GetNode<SimRunner>("SimRunner").Simulation!;
        while (_sim.TickNumber < 3) await Frame();
        _screen = _camera.GetViewport().GetVisibleRect().Size;
    }

    // 300 box selects, one per frame, alternating whole screen / left half so each one changes the selection.
    private async Task BoxSpam()
    {
        await Gap();
        int p0 = _sfx.PlayCount(SfxEvent.Select), c0 = _sfx.PlayCount(SfxEvent.Command);
        ulong t0 = Time.GetTicksUsec();
        int maxSel = 0;
        const int frames = 300;
        for (int k = 0; k < frames; k++)
        {
            Vector2 to = k % 2 == 0 ? _screen - new Vector2(1, 1) : new Vector2(_screen.X / 2, _screen.Y - 1);
            Box(new Vector2(1, 1), to);
            maxSel = Math.Max(maxSel, _sel.Selection.Count);
            await Frame();
        }
        double ms = (Time.GetTicksUsec() - t0) / 1000.0;
        int plays = _sfx.PlayCount(SfxEvent.Select) - p0;
        GD.Print($"box spam: {frames} boxes over {ms:F0} ms, up to {maxSel} selected, Select played {plays}");
        Check(maxSel > 100, $"box spam selected at most {maxSel}");
        Check(plays >= 1 && plays <= frames && plays <= ms / Sfx.MinGapMs + 1, $"box spam: {plays} Select plays in {ms:F0} ms / {frames} frames");
        Check(_sfx.PlayCount(SfxEvent.Command) == c0, "box spam played Command");
    }

    // 1,000 right-clicks over 200 frames (5 per frame) with ~1,000 selected: drops are fine, crashes and buzz are not.
    private async Task RightClickSpam()
    {
        Box(new Vector2(1, 1), _screen - new Vector2(1, 1));
        Vector2 ground = new(_screen.X / 2, _screen.Y / 2);
        await Gap();
        int p0 = _sfx.PlayCount(SfxEvent.Command), s0 = _sfx.PlayCount(SfxEvent.Select);
        int moves0 = _sel.IssuedCount(CommandKind.Move), drop0 = _sel.DroppedOrders;
        ulong t0 = Time.GetTicksUsec();
        int framesWithOrder = 0;
        for (int k = 0; k < 200; k++)
        {
            int before = _sel.IssuedCount(CommandKind.Move);
            for (int j = 0; j < 5; j++) RightClick(ground + new Vector2(j * 7, k % 13));
            if (_sel.IssuedCount(CommandKind.Move) > before) framesWithOrder++;
            await Frame();
        }
        double ms = (Time.GetTicksUsec() - t0) / 1000.0;
        int plays = _sfx.PlayCount(SfxEvent.Command) - p0;
        GD.Print($"right-click spam: 1000 clicks, {_sel.Selection.Count} selected, {(_sel.IssuedCount(CommandKind.Move) - moves0) / Math.Max(1, _sel.Selection.Count)} orders sent, {_sel.DroppedOrders - drop0} dropped, {framesWithOrder} frames ordered, Command played {plays} in {ms:F0} ms");
        Check(plays >= 1 && plays <= framesWithOrder && plays <= ms / Sfx.MinGapMs + 1, $"right-click spam: {plays} Command plays, {framesWithOrder} frames with an order, {ms:F0} ms");
        Check(_sfx.PlayCount(SfxEvent.Select) == s0, "right-click spam played Select");
    }

    // The order path (with its sound) allocates nothing once warm; measured on Order directly so picking is out of it.
    private async Task NoAllocationAfterWarmUp()
    {
        SelectOwn(10);
        for (int k = 0; k < 5; k++) { _sel.Order(CommandKind.Stop, null, false); _sfx.Play(SfxEvent.Select); await Frame(); }
        long total = 0;
        int p0 = _sfx.PlayCount(SfxEvent.Command);
        for (int k = 0; k < 100; k++)
        {
            await ToSignal(GetTree().CreateTimer(0.0), SceneTreeTimer.SignalName.Timeout);
            while (_sim.PendingCommandCount > 0) await Frame();
            long b = GC.GetAllocatedBytesForCurrentThread();
            _sel.Order(CommandKind.HoldPosition, null, false);
            _sfx.Play(SfxEvent.Select);
            total += GC.GetAllocatedBytesForCurrentThread() - b;
        }
        GD.Print($"100 warm orders + Select plays: {total} bytes allocated, Command played {_sfx.PlayCount(SfxEvent.Command) - p0}");
        Check(total == 0, $"100 warm Order + Play calls allocated {total} bytes");
    }

    // S, H, A, Esc, A, Esc, S in one frame: one Command, every order counted, A disarmed.
    private async Task RapidKeys()
    {
        SelectOwn(20);
        int n = _sel.Selection.Count;
        while (_sim.PendingCommandCount > 0) await Frame();
        await Gap();
        int c0 = _sfx.PlayCount(SfxEvent.Command), s0 = _sfx.PlayCount(SfxEvent.Select);
        int stop0 = _sel.IssuedCount(CommandKind.Stop), hold0 = _sel.IssuedCount(CommandKind.HoldPosition);
        foreach (Key k in new[] { Key.S, Key.H, Key.A, Key.Escape, Key.A, Key.Escape, Key.S }) Press(k);
        Check(_sfx.PlayCount(SfxEvent.Command) - c0 == 1, $"S/H/A/Esc burst: Command played {_sfx.PlayCount(SfxEvent.Command) - c0}");
        Check(_sel.IssuedCount(CommandKind.Stop) - stop0 == 2 * n && _sel.IssuedCount(CommandKind.HoldPosition) - hold0 == n, "S/H/A/Esc burst: not every order went out");
        Check(!_sel.Targeting, "S/H/A/Esc burst left A armed");
        Check(_sfx.PlayCount(SfxEvent.Select) == s0, "S/H/A/Esc burst played Select");
        // A then Esc then a click on ground: a selection click on empty ground, no order, no sound.
        await Gap();
        c0 = _sfx.PlayCount(SfxEvent.Command);
        Press(Key.A);
        Press(Key.Escape);
        int sel0 = _sfx.PlayCount(SfxEvent.Select);
        int am0 = _sel.IssuedCount(CommandKind.AttackMove);
        Click(EmptyGround());
        Check(_sfx.PlayCount(SfxEvent.Command) == c0 && _sel.IssuedCount(CommandKind.AttackMove) == am0, "A, Esc, click ordered an attack-move");
        Check(_sfx.PlayCount(SfxEvent.Select) == sel0, "A, Esc, click on ground played Select");
        // A with nothing selected arms nothing; the click after it is a plain click.
        _sel.Selection.Clear();
        await Gap();
        Press(Key.A);
        Check(!_sel.Targeting, "A with nothing selected armed targeting");
        Check(_sfx.PlayCount(SfxEvent.Command) == c0, "A with nothing selected played Command");
    }

    private async Task SelectionDiesMidFrame()
    {
        UnitStore u = _sim.World.Units;
        while (_sim.PendingCommandCount > 0) await Frame();
        // Whole selection freed between input events in one frame.
        SelectOwn(5);
        await Gap();
        int c0 = _sfx.PlayCount(SfxEvent.Command);
        foreach (EntityHandle h in _sel.Selection.Items.ToArray()) u.Free(h);
        RightClick(EmptyGround());
        Press(Key.S);
        Press(Key.A);
        Check(_sfx.PlayCount(SfxEvent.Command) == c0, "orders to a dead selection played Command");
        Check(!_sel.Targeting, "A armed on a dead selection");
        // Part of it freed: the order goes to the living and plays once.
        SelectOwn(5);
        await Gap();
        c0 = _sfx.PlayCount(SfxEvent.Command);
        int m0 = _sel.IssuedCount(CommandKind.Move);
        EntityHandle[] five = _sel.Selection.Items.ToArray();
        u.Free(five[0]);
        u.Free(five[1]);
        RightClick(EmptyGround());
        Check(_sfx.PlayCount(SfxEvent.Command) - c0 == 1 && _sel.IssuedCount(CommandKind.Move) - m0 == 3, $"part-dead order: Command +{_sfx.PlayCount(SfxEvent.Command) - c0}, Move +{_sel.IssuedCount(CommandKind.Move) - m0}");
        // A group whose units all died: recall does nothing and plays nothing.
        SelectOwn(3);
        Input.ActionPress("group_assign");
        Press(Key.Key4);
        Input.ActionRelease("group_assign");
        foreach (EntityHandle h in _sel.Selection.Items.ToArray()) u.Free(h);
        SelectOwn(2);
        await Gap();
        int s0 = _sfx.PlayCount(SfxEvent.Select);
        int count = _sel.Selection.Count;
        Press(Key.Key4);
        Check(_sfx.PlayCount(SfxEvent.Select) == s0 && _sel.Selection.Count == count, "recall of a dead group played Select or changed the selection");
        // Selection {a, b, c} with c dying, group {a, b}: the recall leaves the same live set, no sound.
        SelectOwn(2);
        Input.ActionPress("group_assign");
        Press(Key.Key5);
        Input.ActionRelease("group_assign");
        SelectOwn(3);
        EntityHandle third = _sel.Selection.Items[2];
        u.Free(third);
        await Gap();
        s0 = _sfx.PlayCount(SfxEvent.Select);
        Press(Key.Key5);
        Check(_sfx.PlayCount(SfxEvent.Select) == s0 && _sel.Selection.Count == 2, $"recall equal to the live selection: Select +{_sfx.PlayCount(SfxEvent.Select) - s0}, selected {_sel.Selection.Count}");
    }

    private async Task MinimapEdges()
    {
        Rect2 mini = _match.GetNode<Minimap>("Hud/Minimap").GetGlobalRect();
        Vector2 at = mini.GetCenter();
        await Gap();
        _sel.Selection.Clear();
        int c0 = _sfx.PlayCount(SfxEvent.Command), s0 = _sfx.PlayCount(SfxEvent.Select);
        MiniClick(MouseButton.Right, at);
        Check(_sfx.PlayCount(SfxEvent.Command) == c0, "minimap right-click with nothing selected played Command");
        MiniClick(MouseButton.Left, at);
        await Frame();
        Check(_sfx.PlayCount(SfxEvent.Select) == s0, "minimap left-click (camera jump) played Select");
        // A armed, minimap right-click cancels it and orders nothing.
        SelectOwn(5);
        while (_sim.PendingCommandCount > 0) await Frame();
        await Gap();
        Press(Key.A);
        MiniClick(MouseButton.Right, at);
        Check(!_sel.Targeting && _sfx.PlayCount(SfxEvent.Command) == c0, "minimap right-click while A armed ordered or kept A armed");
        // And a real minimap order plays once even with 5 in the same frame.
        await Gap();
        for (int k = 0; k < 5; k++) MiniClick(MouseButton.Right, at + new Vector2(k, 0));
        Check(_sfx.PlayCount(SfxEvent.Command) - c0 == 1, $"5 minimap orders in one frame played {_sfx.PlayCount(SfxEvent.Command) - c0}");
    }

    private void MiniClick(MouseButton b, Vector2 at)
    {
        MouseButtonMask mask = b == MouseButton.Right ? MouseButtonMask.Right : MouseButtonMask.Left;
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = b, Pressed = true, Position = at, GlobalPosition = at, ButtonMask = mask });
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = b, Pressed = false, Position = at, GlobalPosition = at });
    }

    private void SelectOwn(int n)
    {
        UnitStore u = _sim.World.Units;
        _sel.Selection.Clear();
        for (int i = 0; i < u.Capacity && _sel.Selection.Count < n; i++)
            if (u.Alive[i] && u.Owner[i] == SelectionController.LocalPlayer) _sel.Selection.Add(new EntityHandle(i, u.Generation[i]));
    }

    private Vector2 EmptyGround()
    {
        UnitStore u = _sim.World.Units;
        for (float y = _screen.Y * 0.2f; y < _screen.Y * 0.8f; y += 10)
        {
            for (float x = _screen.X * 0.2f; x < _screen.X * 0.8f; x += 10)
            {
                bool clear = true;
                for (int i = 0; i < u.Capacity && clear; i++)
                    if (u.Alive[i] && _sel.TryScreenPosition(i, out Vector2 p) && p.DistanceTo(new Vector2(x, y)) <= 30f) clear = false;
                if (clear) return new Vector2(x, y);
            }
        }
        throw new InvalidOperationException("no empty ground on screen");
    }

    private async Task Gap()
    {
        await ToSignal(GetTree().CreateTimer(0.06), SceneTreeTimer.SignalName.Timeout);
        await Frame();
    }

    private void Press(Key key)
    {
        _sel._UnhandledInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        _sel._UnhandledInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    private void Click(Vector2 at)
    {
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
    }

    private void Box(Vector2 from, Vector2 to)
    {
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = from });
        _sel._UnhandledInput(new InputEventMouseMotion { Position = to });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = to });
    }

    private void RightClick(Vector2 at)
    {
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = at });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = at });
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private void Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
    }
}
