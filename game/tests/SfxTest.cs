using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Game.Tests;

/// <summary>M2-6: the generated clips' shape, the Select / Command hooks through the real Match with injected input, the rate limit, and <c>--mute</c>.</summary>
/// <remarks>
/// Headless. Run: <c>&amp; $env:GODOT --headless --path game res://tests/SfxTest.tscn</c>; prints
/// "SFX TEST PASS" and exits 0, or prints each failure and exits 1.
/// </remarks>
public partial class SfxTest : Node
{
    private const int EdgeSamples = Sfx.MixRate * 2 / 1000; // 2 ms

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private Match _match = null!;
    private Sfx _sfx = null!;
    private SelectionController _sel = null!;
    private RtsCamera _camera = null!;
    private Simulation _sim = null!;

    public override async void _Ready()
    {
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            await Frame();
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            Clips();
            RateLimit();
            ParseMute();
            await StartMatch(mute: false);
            Check(!Sfx.Muted, "master bus muted without --mute");
            await Selecting();
            await Ordering();
            await StartMatch(mute: true);
            Check(Sfx.Muted, "master bus not muted with --mute");
            int before = _sfx.PlayCount(SfxEvent.Select);
            _sfx.Play(SfxEvent.Select);
            Check(_sfx.PlayCount(SfxEvent.Select) == before + 1, "counter did not run while muted");
            ExitAfterPlay();
            // BUG-0087: no wait here any more. The Select above is still sounding as the scene quits; Sfx._ExitTree
            // waits for the audio server's next mix, so the exit log has no ObjectDB leak warning.
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string a in new[] { "order_queue", "select_add", "select_type", "group_assign", "group_add" }) Input.ActionRelease(a);
        foreach (string f in _failures) GD.Print($"SFX TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("SFX TEST PASS");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    // Criterion 1: length, peak, quiet edges, no NaN, both as floats and as the 16-bit clip.
    private void Clips()
    {
        var sfx = new Sfx();
        AddChild(sfx);
        (SfxEvent e, float ms)[] rows = { (SfxEvent.Select, 70f), (SfxEvent.Command, 120f) };
        foreach ((SfxEvent e, float wantMs) in rows)
        {
            float[] s = Sfx.Synthesize(e);
            AudioStreamWav clip = sfx.Clip(e);
            byte[] data = clip.Data;
            Check(clip.Format == AudioStreamWav.FormatEnum.Format16Bits && clip.MixRate == Sfx.MixRate && !clip.Stereo, $"{e}: clip format {clip.Format} {clip.MixRate} Hz stereo {clip.Stereo}");
            Check(data.Length == s.Length * 2, $"{e}: {data.Length} bytes for {s.Length} samples");
            float ms = data.Length / 2 * 1000f / Sfx.MixRate;
            Check(Math.Abs(ms - wantMs) <= 5f, $"{e}: {ms:F1} ms, expected {wantMs} +- 5");
            float peak = 0f, head = 0f, tail = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float pcm = (short)(data[2 * i] | (data[2 * i + 1] << 8)) / 32768f;
                Check(float.IsFinite(s[i]), $"{e}: sample {i} is {s[i]}");
                float a = Math.Max(Math.Abs(s[i]), Math.Abs(pcm));
                peak = Math.Max(peak, a);
                if (i < EdgeSamples) head = Math.Max(head, a);
                if (i >= s.Length - EdgeSamples) tail = Math.Max(tail, a);
            }
            GD.Print($"clip {e}: {ms:F1} ms, peak {peak:F3}, first 2 ms {head:F4}, last 2 ms {tail:F4}");
            Check(peak <= 0.9f && peak > 0.2f, $"{e}: peak {peak}");
            Check(head < 0.05f && tail < 0.05f, $"{e}: edges {head} / {tail} not under 0.05");
        }
        Check(!sfx.Clip(SfxEvent.Select).Data.AsSpan().SequenceEqual(sfx.Clip(SfxEvent.Command).Data), "Select and Command clips are identical");
        sfx.QueueFree();
    }

    // Criterion 4 on a fresh pool with the clock driven by hand: 20 ms apart drops, 60 ms plays; one per frame.
    private void RateLimit()
    {
        var sfx = new Sfx();
        AddChild(sfx);
        const ulong t = 10_000_000;
        sfx.Play(SfxEvent.Select, 100, t);
        sfx.Play(SfxEvent.Select, 101, t + 20_000);
        Check(sfx.PlayCount(SfxEvent.Select) == 1, $"20 ms apart: {sfx.PlayCount(SfxEvent.Select)} plays, expected 1");
        sfx.Play(SfxEvent.Select, 102, t + 60_000);
        Check(sfx.PlayCount(SfxEvent.Select) == 2, $"60 ms apart: {sfx.PlayCount(SfxEvent.Select)} plays, expected 2");
        Check(sfx.LastPlayedFrame(SfxEvent.Select) == 102, $"last played frame {sfx.LastPlayedFrame(SfxEvent.Select)}");
        sfx.Play(SfxEvent.Select, 102, t + 500_000);
        Check(sfx.PlayCount(SfxEvent.Select) == 2, "a second play in the same frame was not dropped");
        // Events are limited separately.
        Check(sfx.LastPlayedFrame(SfxEvent.Command) == -1, "Command has a last frame before it ever played");
        sfx.Play(SfxEvent.Command, 102, t + 60_000);
        Check(sfx.PlayCount(SfxEvent.Command) == 1 && sfx.LastPlayedFrame(SfxEvent.Command) == 102, "Command was limited by Select");
        // Play allocates nothing once the clips exist (the pool's players are reused).
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (ulong k = 1; k <= 20; k++) sfx.Play(SfxEvent.Command, 102 + k, t + 60_000 + k * 100_000);
        bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        Check(sfx.PlayCount(SfxEvent.Command) == 21, $"20 spaced plays counted {sfx.PlayCount(SfxEvent.Command) - 1}");
        Check(bytes == 0, $"20 plays allocated {bytes} bytes");
        sfx.QueueFree();
    }

    // BUG-0087: a pool that played just now waits for one audio mix as it leaves the tree (well under the limit);
    // one that never played doesn't wait at all.
    private void ExitAfterPlay()
    {
        var quiet = new Sfx();
        AddChild(quiet);
        RemoveChild(quiet);
        Check(quiet.LastExitWaitMs == 0, $"a pool that never played waited {quiet.LastExitWaitMs} ms at exit");
        quiet.QueueFree();

        var sfx = new Sfx();
        AddChild(sfx);
        Check(sfx.Play(SfxEvent.Command), "Command did not play");
        RemoveChild(sfx);
        GD.Print($"exit right after a play: waited {sfx.LastExitWaitMs:F1} ms for an audio mix (limit {Sfx.ExitWaitLimitMs} ms)");
        Check(sfx.LastExitWaitMs > 0 && sfx.LastExitWaitMs < Sfx.ExitWaitLimitMs, $"exit after a play waited {sfx.LastExitWaitMs} ms");
        Check(sfx.StopAll() == 0, "a player still plays after the pool left the tree");
        sfx.QueueFree();
    }

    private void ParseMute()
    {
        Check(!LaunchOptions.Parse(Array.Empty<string>()).Mute, "Mute set without --mute");
        LaunchOptions o = LaunchOptions.Parse(new[] { "--mute", "--units", "5" });
        Check(o.Mute && o.UnitsPerPlayer == 5, "--mute swallowed the next flag");
        o = LaunchOptions.Parse(new[] { "--mute", "7", "--seed", "3" });
        Check(o.Mute && o.Seed == 3 && o.UnitsPerPlayer == LaunchOptions.DefaultUnitsPerPlayer, "--mute took a value");
    }

    private async Task StartMatch(bool mute)
    {
        if (IsInstanceValid(_match)) _match.QueueFree();
        await Frame();
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _match.Start(_data, LaunchOptions.Parse(mute ? new[] { "--units", "100", "--no-combat", "--mute" } : new[] { "--units", "100", "--no-combat" }));
        _sfx = _match.GetNode<Sfx>("Sfx");
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _sim = _match.GetNode<SimRunner>("SimRunner").Simulation!;
        while (_sim.TickNumber < 3) await Frame();
    }

    // Criterion 2.
    private async Task Selecting()
    {
        UnitStore u = _sim.World.Units;
        Vector2 screen = _camera.GetViewport().GetVisibleRect().Size;
        int a = -1;
        Vector2 atA = default;
        for (int i = 0; i < u.Capacity && a < 0; i++)
            if (u.Alive[i] && u.Owner[i] == SelectionController.LocalPlayer && _sel.TryScreenPosition(i, out atA) && atA.X > 50 && atA.Y > 50 && atA.X < screen.X - 50 && atA.Y < screen.Y - 50) a = i;
        Check(a >= 0, "no own unit on screen");
        Vector2 empty = FindEmpty(screen);
        await Expect("click own unit", SfxEvent.Select, 1, () => Click(atA));
        await Expect("box", SfxEvent.Select, 1, () => Box(new Vector2(1, 1), screen - new Vector2(1, 1)));
        GD.Print($"box selected {_sel.Selection.Count}");
        await Expect("click empty ground", SfxEvent.Select, 0, () => Click(empty));
        await Expect("double-click a type", SfxEvent.Select, 1, () => Click(atA, doubleClick: true));
        await Expect("Ctrl+1", SfxEvent.Select, 0, () => { Input.ActionPress("group_assign"); Press(Key.Key1); Input.ActionRelease("group_assign"); });
        await Expect("click own unit again", SfxEvent.Select, 1, () => Click(atA));
        await Expect("group recall", SfxEvent.Select, 1, () => Press(Key.Key1));
        await Expect("Tab", SfxEvent.Select, 0, () => Press(Key.Tab));
        await Expect("click own unit", SfxEvent.Select, 1, () => Click(atA));
        await Expect("re-select the same unit", SfxEvent.Select, 0, () => Click(atA));
        Check(_sfx.LastPlayedFrame(SfxEvent.Select) >= 0, "LastPlayedFrame never set");
    }

    // Criterion 3.
    private async Task Ordering()
    {
        Vector2 screen = _camera.GetViewport().GetVisibleRect().Size;
        Vector2 empty = FindEmpty(screen);
        Box(new Vector2(1, 1), screen - new Vector2(1, 1));
        int n = _sel.Selection.Count;
        await Expect("right-click", SfxEvent.Command, 1, () => RightClick(empty));
        await Expect("Shift+right-click", SfxEvent.Command, 1, () => { Input.ActionPress("order_queue"); RightClick(empty); Input.ActionRelease("order_queue"); });
        await Expect("S", SfxEvent.Command, 1, () => Press(Key.S));
        await Expect("H", SfxEvent.Command, 1, () => Press(Key.H));
        await Expect("A then click", SfxEvent.Command, 1, () => { Press(Key.A); Click(empty); });
        Rect2 mini = _match.GetNode<Minimap>("Hud/Minimap").GetGlobalRect();
        await Expect("minimap right-click", SfxEvent.Command, 1, () =>
        {
            Vector2 at = mini.GetCenter();
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Right });
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = at, GlobalPosition = at });
        });
        int dropped = _sel.DroppedOrders;
        await Expect("order dropped whole", SfxEvent.Command, 0, () =>
        {
            int fill = _sim.World.Config.CommandCapacity - _sim.PendingCommandCount - _sel.Selection.Count + 1;
            for (int i = 0; i < fill; i++) _sim.Enqueue(Command.Noop(0));
            RightClick(empty);
        });
        Check(_sel.DroppedOrders == dropped + 1, $"the overflow order was not dropped ({dropped} -> {_sel.DroppedOrders})");
        while (_sim.PendingCommandCount > 0) await Frame(); // let a tick drain the fill, or all 50 would be dropped too
        int moves = _sel.IssuedCount(CommandKind.Move);
        await Expect("50 right-clicks in one frame", SfxEvent.Command, 1, () => { for (int i = 0; i < 50; i++) RightClick(empty); });
        Check(_sel.IssuedCount(CommandKind.Move) > moves, "none of the 50 right-clicks enqueued anything");
        await Expect("right-click with nothing selected", SfxEvent.Command, 0, () => { _sel.Selection.Clear(); RightClick(empty); });
        GD.Print($"orders to {n} units: Command played {_sfx.PlayCount(SfxEvent.Command)} times, last frame {_sfx.LastPlayedFrame(SfxEvent.Command)}");
    }

    // Waits past the 50 ms gap and a frame, runs the action, and checks how many times each event played.
    private async Task Expect(string what, SfxEvent e, int want, Action action)
    {
        await ToSignal(GetTree().CreateTimer(0.06), SceneTreeTimer.SignalName.Timeout);
        await Frame();
        SfxEvent other = e == SfxEvent.Select ? SfxEvent.Command : SfxEvent.Select;
        int before = _sfx.PlayCount(e), otherBefore = _sfx.PlayCount(other);
        action();
        int got = _sfx.PlayCount(e) - before, otherGot = _sfx.PlayCount(other) - otherBefore;
        GD.Print($"{what}: {e} +{got}, {other} +{otherGot}");
        Check(got == want, $"{what}: {e} played {got} times, expected {want}");
        Check(otherGot == 0, $"{what}: {other} played {otherGot} times, expected 0");
    }

    // A screen point at least 30 px from every live unit, in the middle of the screen (so it is on the map).
    private Vector2 FindEmpty(Vector2 size)
    {
        UnitStore u = _sim.World.Units;
        for (float y = size.Y * 0.3f; y < size.Y * 0.8f; y += 10)
        {
            for (float x = size.X * 0.3f; x < size.X * 0.9f; x += 10)
            {
                bool clear = true;
                for (int i = 0; i < u.Capacity && clear; i++)
                    if (u.Alive[i] && _sel.TryScreenPosition(i, out Vector2 p) && p.DistanceTo(new Vector2(x, y)) <= 30f) clear = false;
                if (clear) return new Vector2(x, y);
            }
        }
        throw new InvalidOperationException("no empty ground on screen");
    }

    private void Press(Key key)
    {
        _sel._UnhandledInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        _sel._UnhandledInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    private void Click(Vector2 at, bool doubleClick = false)
    {
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, DoubleClick = doubleClick });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
    }

    private void Box(Vector2 from, Vector2 to)
    {
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = from });
        _sel._UnhandledInput(new InputEventMouseMotion { Position = (from + to) / 2 });
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
