using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Entities;

namespace Rts.Game.Tests;

/// <summary>
/// QA attacks on M6-V1 (session 2026-10-11-0634): the game shell through <c>Main.tscn</c> with no user args.
/// Junk typed as the seed (Start stays disabled, a forced Start press starts nothing; the 2^64 - 1 edge starts a match on
/// that seed); Esc on the setup and the menu; Esc with a build menu, a placement ghost, a targeted ability and M armed
/// (each cancels, none pauses); pause during a tick burst (8x speed, a 300 ms stall right after Esc: no tick while paused,
/// tick numbers contiguous, no catch-up burst after Resume); no input leak while paused (right-click, S, H, A, wheel,
/// grid keys: no command enqueued, camera and selection unchanged); two Esc in one frame; and quit to menu 10 times in a
/// row on 128 and 256 maps (node count, object count, orphan nodes and memory flat).
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaShellTest.tscn</c>; prints "QA SHELL TEST PASS" or "QA SHELL TEST FAIL: ..." lines.</remarks>
public partial class QaShellTest : Node
{
    private readonly List<string> _failures = new();
    private Main _main = null!;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            await Frames(1);
            _main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
            _main.UserArgs = Array.Empty<string>();
            AddChild(_main);
            await Frames(2);
            Check(_main.Menu is { Visible: true } && _main.CurrentMatch == null, "no-arg boot is not the menu");
            await EscOnMenus();
            await JunkSeeds();
            await ArmedEsc();
            await PauseBurstAndLeaks();
            await QuitCycles();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        foreach (string f in _failures.Take(60)) GD.Print($"QA SHELL TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA SHELL TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- menus ----

    private async Task EscOnMenus()
    {
        await Key(Godot.Key.Escape);
        Check(_main.Menu!.Visible && !_main.Setup!.Visible && _main.CurrentMatch == null && !GetTree().Paused, "Esc on the main menu changed something");
        _main.Menu.SkirmishButton.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(1);
        Check(_main.Setup.Visible, "Skirmish did not open the setup");
        await Key(Godot.Key.Escape);
        Check(_main.Menu.Visible && !_main.Setup.Visible, "Esc on the setup did not go back to the menu");
        // An echo Esc does nothing.
        _main.Menu.SkirmishButton.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(1);
        GetViewport().PushInput(new InputEventKey { Keycode = Godot.Key.Escape, PhysicalKeycode = Godot.Key.Escape, Pressed = true, Echo = true });
        await Frames(1);
        Check(_main.Setup.Visible, "an echo Esc left the setup");
    }

    private async Task JunkSeeds()
    {
        SkirmishSetup setup = _main.Setup!;
        Check(setup.Visible, "setup not up for the seed rows");
        string[] junk =
        {
            "", " ", "-1", "+5", "1e5", "0x10", "1.5", "1,000", "abc", "7a", "1 2", "٣", "１２", "18446744073709551616",
            "99999999999999999999", "--seed", " ", "\t",
        };
        foreach (string text in junk)
        {
            await TypeInto(setup.SeedEdit, text);
            Check(setup.Seed == null, $"seed '{Esc(text)}' (field '{Esc(setup.SeedEdit.Text)}') parsed as {setup.Seed}");
            Check(setup.StartButton.Disabled || setup.Seed != null, $"seed '{Esc(text)}': Start enabled with field '{Esc(setup.SeedEdit.Text)}'");
            // Start forced anyway (a stale enabled state, a keyboard activation): no match.
            setup.StartButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(1);
            Check(_main.CurrentMatch == null, $"a match started on junk seed '{Esc(text)}'");
            if (_main.CurrentMatch != null) { _main.QuitToMenu(); _main.Menu!.SkirmishButton.EmitSignal(BaseButton.SignalName.Pressed); await Frames(1); }
        }
        // Over-long input stops at MaxLength.
        await TypeInto(setup.SeedEdit, new string('9', 30));
        Check(setup.SeedEdit.Text.Length <= 20 && setup.Seed == null, $"30 nines: field length {setup.SeedEdit.Text.Length}, seed {setup.Seed}");
        // Accepted edges.
        foreach ((string text, ulong want) in new[] { ("0", 0UL), (" 7 ", 7UL), ("007", 7UL), ("18446744073709551615", ulong.MaxValue) })
        {
            await TypeInto(setup.SeedEdit, text);
            Check(setup.Seed == want && !setup.StartButton.Disabled, $"seed '{text}': {setup.Seed}, Start disabled {setup.StartButton.Disabled}");
        }
        // Programmatic text (no text_changed): Start must not start a junk seed even with a stale enabled button.
        setup.SeedEdit.Text = "junk";
        setup.StartButton.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(1);
        Check(_main.CurrentMatch == null, "Start with programmatic junk text started a match");
        if (!setup.StartButton.Disabled) GD.Print("  note: Start stays enabled after a programmatic junk Text (text_changed not emitted); Start is still guarded");
        // The max seed reaches the sim.
        await TypeInto(setup.SeedEdit, "18446744073709551615");
        setup.MapSizeOption.Select(2);
        await ClickControl(setup.StartButton);
        await Frames(2);
        Check(_main.CurrentMatch != null, "no match on the max seed");
        if (_main.CurrentMatch != null)
        {
            Simulation sim = Runner().Simulation!;
            Check(sim.World.Config.Seed == ulong.MaxValue && sim.World.NavGrid.Width == 256, $"max seed match: seed {sim.World.Config.Seed}, map {sim.World.NavGrid.Width}");
            int t0 = sim.TickNumber;
            await Seconds(0.3);
            Check(sim.TickNumber > t0, "the 256 / max-seed match does not tick");
            await Key(Godot.Key.Escape);
            Check(_main.Paused, "Esc did not pause the 256 match");
            _main.Pause!.QuitToMenuButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);
            Check(_main.CurrentMatch == null && !GetTree().Paused, "quit from the 256 match");
        }
    }

    // ---- armed Esc ----

    private async Task ArmedEsc()
    {
        await StartFromSetup("11", 0);
        Match match = _main.CurrentMatch!;
        SimRunner runner = Runner();
        var sel = match.GetNode<SelectionController>("SelectionController");
        var card = match.GetNode<CommandCard>("Hud/CommandCard");
        Simulation sim = runner.Simulation!;

        // A worker: the build menu, then a placement ghost.
        EntityHandle worker = FindOwn(sim, h => sim.World.Data.Units[sim.World.Units.TypeId[h.Index]].Slot == Rts.Sim.Data.UnitSlot.Worker);
        Check(worker.Generation != 0, "no own worker");
        if (worker.Generation != 0)
        {
            sel.SelectOnly(worker);
            await Frames(2);
            Check(sel.ActiveSubgroupIsWorker, "the selected worker's subgroup is not a worker one");
            Check(card.OpenMenu(false) && card.MenuOpen, "the basic build menu did not open");
            await Key(Godot.Key.Escape);
            Check(!card.MenuOpen && !_main.Paused && !GetTree().Paused, $"Esc with the build menu open: menu {card.MenuOpen}, paused {_main.Paused}");

            card.OpenMenu(false);
            await Frames(1);
            int place = -1;
            for (int i = 0; i < 16 && place < 0; i++) if (card.ActionAt(i) == CardCommand.Place) place = i;
            Check(place >= 0, "no Place cell in the basic menu");
            if (place >= 0)
            {
                card.Press(place);
                await Frames(1);
                Check(card.GhostActive, "the placement ghost is not up");
                await Key(Godot.Key.Escape);
                Check(!card.GhostActive && !card.MenuOpen && !_main.Paused && !GetTree().Paused,
                    $"Esc with the ghost up: ghost {card.GhostActive}, menu {card.MenuOpen}, paused {_main.Paused}");
            }
        }

        // M armed.
        if (worker.Generation != 0)
        {
            sel.SelectOnly(worker);
            sel.BeginMove();
            Check(sel.Targeting, "M did not arm");
            await Key(Godot.Key.Escape);
            Check(!sel.Targeting && !_main.Paused, $"Esc with M armed: targeting {sel.Targeting}, paused {_main.Paused}");
        }

        // A targeted ability armed (the first own unit with an ability that takes a point or a target).
        int abilityId = -1;
        EntityHandle caster = FindOwn(sim, h =>
        {
            var def = sim.World.Data.Units[sim.World.Units.TypeId[h.Index]];
            foreach (int a in def.Abilities)
                if (!Rts.Sim.ViewApi.AbilityCaster.IsNoTarget(sim.World.Data.Abilities[a].Kind))
                {
                    abilityId = a;
                    return true;
                }
            return false;
        });
        if (caster.Generation == 0) GD.Print("  note: no own unit with a targeted ability in the default skirmish; ability Esc row skipped");
        else
        {
            sel.SelectOnly(caster);
            await Frames(2);
            Check(sel.BeginAbility(abilityId), $"ability {abilityId} would not arm on unit {caster.Index}");
            GD.Print($"  ability row: unit {caster.Index} ({sim.World.Data.Units[sim.World.Units.TypeId[caster.Index]].Key}), ability {sim.World.Data.Abilities[abilityId].Key}");
            Check(sel.Targeting && sel.TargetKind == Rts.Sim.Commands.CommandKind.UseAbility, $"ability {abilityId} did not arm");
            await Key(Godot.Key.Escape);
            Check(!sel.Targeting && !_main.Paused && !GetTree().Paused, $"Esc with ability {abilityId} armed: targeting {sel.Targeting}, paused {_main.Paused}");
        }

        // Now nothing is armed: Esc pauses; two Esc in one frame net to running.
        await Key(Godot.Key.Escape);
        Check(_main.Paused && GetTree().Paused, "Esc with nothing armed did not pause");
        await Key(Godot.Key.Escape);
        Check(!_main.Paused && !GetTree().Paused, "Esc on the pause menu did not resume");
        PushKey(Godot.Key.Escape);
        PushKey(Godot.Key.Escape);
        await Frames(2);
        Check(!_main.Paused && !GetTree().Paused && !_main.Pause!.Visible, $"two Esc in one frame: paused {_main.Paused}, tree {GetTree().Paused}");
    }

    // ---- pause during a burst, input leaks ----

    private async Task PauseBurstAndLeaks()
    {
        Match match = _main.CurrentMatch!;
        SimRunner runner = Runner();
        Simulation sim = runner.Simulation!;
        var sel = match.GetNode<SelectionController>("SelectionController");
        RtsCamera cam = Find<RtsCamera>(match)!;
        var ticks = new List<(int Tick, bool Paused)>();
        Action<Simulation> onTick = s => ticks.Add((s.TickNumber, GetTree().Paused));
        runner.Ticked += onTick;
        runner.GameSpeed = 8.0;
        await Seconds(0.3);
        Check(ticks.Count > 0, "no tick at 8x");

        // Pause, then stall the main thread 300 ms in the same frame: that frame time must not become ticks.
        EntityHandle unit = FindOwn(sim, h => sel.SelectOnly(h));
        await Frames(1);
        PushKey(Godot.Key.Escape);
        Check(_main.Paused, "Esc did not pause at 8x");
        System.Threading.Thread.Sleep(300);
        int pausedAt = sim.TickNumber;
        int pending = sim.PendingCommandCount;
        int selected = sel.Selection.Count;
        Vector3 camPos = cam.GlobalPosition;
        float zoom = cam.Zoom;

        // Leaks: right-click on the ground, S, H, A then a click, a grid key, the wheel.
        Vector2 mid = GetViewport().GetVisibleRect().Size / 2f;
        PushMouse(MouseButton.Right, mid + new Vector2(120f, 60f));
        PushKey(Godot.Key.S);
        PushKey(Godot.Key.H);
        PushKey(Godot.Key.A);
        PushMouse(MouseButton.Left, mid + new Vector2(-150f, 40f));
        PushKey(Godot.Key.Q);
        PushKey(Godot.Key.B);
        PushMouse(MouseButton.WheelUp, mid);
        PushMouse(MouseButton.WheelUp, mid);
        PushKey(Godot.Key.Tab);
        int frames = await Seconds(0.6);
        Check(sim.TickNumber == pausedAt, $"ticked while paused: {pausedAt} -> {sim.TickNumber} over {frames} frames");
        Check(ticks.All(t => !t.Paused), $"a tick ran with the tree paused: {string.Join(",", ticks.Where(t => t.Paused).Select(t => t.Tick))}");
        Check(sim.PendingCommandCount == pending, $"input leaked into the sim while paused: pending {pending} -> {sim.PendingCommandCount}");
        Check(!sel.Targeting && sel.Selection.Count == selected, $"paused: targeting {sel.Targeting}, selection {selected} -> {sel.Selection.Count}");
        Check(cam.GlobalPosition.IsEqualApprox(camPos) && Math.Abs(cam.Zoom - zoom) < 1e-4f, $"camera moved while paused: {camPos} -> {cam.GlobalPosition}, zoom {zoom} -> {cam.Zoom}");

        // Resume: no catch-up burst (at most MaxTicksPerFrame on the next frame, and tick numbers contiguous throughout).
        int before = sim.TickNumber;
        PushKey(Godot.Key.Escape);
        Check(!_main.Paused, "Esc did not resume at 8x");
        await Frames(1);
        int burst = sim.TickNumber - before;
        Check(burst <= Rts.Sim.ViewApi.FixedStepClock.MaxTicksPerFrame, $"{burst} ticks on the first frame after resume");
        await Seconds(0.3);
        Check(sim.PendingCommandCount == 0 || sim.PendingCommandCount <= pending, $"pending after resume {sim.PendingCommandCount}");
        for (int i = 1; i < ticks.Count; i++)
            if (ticks[i].Tick != ticks[i - 1].Tick + 1) { Check(false, $"tick numbers jump {ticks[i - 1].Tick} -> {ticks[i].Tick}"); break; }
        runner.Ticked -= onTick;
        runner.GameSpeed = 1.0;
        GD.Print($"  burst: {ticks.Count} ticks recorded at 8x, {burst} on the first frame after resume, unit {unit.Index}");

        // Pause while a box-drag is half done (press, Esc, release while paused, resume, move): probe the stuck-drag case.
        Vector2 p0 = mid + new Vector2(-200f, -100f);
        GetViewport().PushInput(new InputEventMouseMotion { Position = p0, GlobalPosition = p0 });
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = p0, GlobalPosition = p0, ButtonMask = MouseButtonMask.Left });
        await Frames(1);
        PushKey(Godot.Key.Escape);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = p0, GlobalPosition = p0 });
        await Frames(2);
        PushKey(Godot.Key.Escape);
        await Frames(1);
        Vector2 p1 = p0 + new Vector2(150f, 90f);
        GetViewport().PushInput(new InputEventMouseMotion { Position = p1, GlobalPosition = p1 });
        await Frames(2);
        Control? box = Find<Control>(sel, c => c is ColorRect or Panel or ReferenceRect && c.Visible);
        // BUG-0580 (open, S3): a release swallowed by the pause leaves the drag live. Printed, not failed, until the fix;
        // flip to Check(box == null, ...) when BUG-0580 is fixed.
        GD.Print($"  KNOWN BUG-0580 probe (box-select): a visible selection box after resume with no button held: {(box != null ? box.Name + " " + box.Size : "none")}");
        if (box != null)
        {
            // Clean it up for the next steps.
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = p1, GlobalPosition = p1, ButtonMask = MouseButtonMask.Left });
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = p1, GlobalPosition = p1 });
            await Frames(1);
        }

        // The same for the middle-button camera drag: press, Esc, release while paused, Esc, move with no button held.
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = true, Position = mid, GlobalPosition = mid, ButtonMask = MouseButtonMask.Middle });
        await Frames(1);
        PushKey(Godot.Key.Escape);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = false, Position = mid, GlobalPosition = mid });
        await Frames(2);
        PushKey(Godot.Key.Escape);
        await Frames(1);
        Vector3 camBefore = cam.GlobalPosition;
        Vector2 p2 = mid + new Vector2(-120f, 0f);
        GetViewport().PushInput(new InputEventMouseMotion { Position = p2, GlobalPosition = p2, Relative = new Vector2(-120f, 0f) });
        await Frames(2);
        float moved = cam.GlobalPosition.DistanceTo(camBefore);
        GD.Print($"  KNOWN BUG-0580 probe (camera drag): the camera moved {moved:0.00} m on a motion with no button held after resume");
        // Clean up: a full middle click ends any drag.
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = true, Position = p2, GlobalPosition = p2, ButtonMask = MouseButtonMask.Middle });
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = false, Position = p2, GlobalPosition = p2 });
        await Frames(1);
    }

    // ---- quit cycles ----

    private async Task QuitCycles()
    {
        // Leave the current match through the pause menu first.
        if (_main.CurrentMatch != null)
        {
            await Key(Godot.Key.Escape);
            _main.Pause!.QuitToMenuButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);
        }
        var rows = new List<(int Nodes, double Objects, double Orphans, double Resources, double Static, long Managed)>();
        for (int cycle = 0; cycle < 10; cycle++)
        {
            await StartFromSetup((100 + cycle).ToString(), cycle % 2 == 0 ? 0 : 2);
            Check(_main.CurrentMatch != null, $"cycle {cycle}: no match");
            await Seconds(0.25);
            await Key(Godot.Key.Escape);
            Check(_main.Paused, $"cycle {cycle}: not paused");
            await ClickControl(_main.Pause!.QuitToMenuButton);
            await Frames(3);
            Check(_main.CurrentMatch == null && !GetTree().Paused && _main.Menu!.Visible, $"cycle {cycle}: quit to menu state");
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Frames(2);
            rows.Add((GetTree().GetNodeCount(), Performance.GetMonitor(Performance.Monitor.ObjectCount),
                Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount), Performance.GetMonitor(Performance.Monitor.ObjectResourceCount),
                Performance.GetMonitor(Performance.Monitor.MemoryStatic), GC.GetTotalMemory(true)));
        }
        for (int i = 0; i < rows.Count; i++)
            GD.Print($"  cycle {i}: nodes {rows[i].Nodes}, objects {rows[i].Objects}, orphans {rows[i].Orphans}, resources {rows[i].Resources}, static {rows[i].Static / 1048576.0:0.0} MB, managed {rows[i].Managed / 1048576.0:0.0} MB");
        Check(rows.All(r => r.Nodes == rows[0].Nodes), $"node count not flat: {string.Join(",", rows.Select(r => r.Nodes))}");
        Check(rows.All(r => r.Orphans == 0), $"orphan nodes: {string.Join(",", rows.Select(r => r.Orphans))}");
        // Same-size cycles compared (even = 128, odd = 256), from the second of each so first-use caches are in.
        double objGrowth = Math.Max(rows[8].Objects - rows[2].Objects, rows[9].Objects - rows[3].Objects);
        double resGrowth = Math.Max(rows[8].Resources - rows[2].Resources, rows[9].Resources - rows[3].Resources);
        double staticGrowthMb = Math.Max(rows[8].Static - rows[2].Static, rows[9].Static - rows[3].Static) / 1048576.0;
        double managedGrowthMb = Math.Max(rows[8].Managed - rows[2].Managed, rows[9].Managed - rows[3].Managed) / 1048576.0;
        GD.Print($"  growth over 3 cycles of each size: objects {objGrowth}, resources {resGrowth}, static {staticGrowthMb:0.00} MB, managed {managedGrowthMb:0.00} MB");
        Check(objGrowth <= 50, $"objects grow {objGrowth} over 3 same-size cycles");
        Check(resGrowth <= 10, $"resources grow {resGrowth} over 3 same-size cycles");
        Check(staticGrowthMb < 8.0, $"static memory grows {staticGrowthMb:0.00} MB over 3 same-size cycles");
        Check(managedGrowthMb < 8.0, $"managed memory grows {managedGrowthMb:0.00} MB over 3 same-size cycles");
    }

    // ---- helpers ----

    private SimRunner Runner() => _main.CurrentMatch!.GetNode<SimRunner>("SimRunner");

    private async Task StartFromSetup(string seed, int sizeIndex)
    {
        if (!_main.Setup!.Visible)
        {
            await ClickControl(_main.Menu!.SkirmishButton);
        }
        SkirmishSetup setup = _main.Setup;
        Check(setup.Visible, "setup not shown");
        await TypeInto(setup.SeedEdit, seed);
        setup.MapSizeOption.Select(sizeIndex);
        await ClickControl(setup.StartButton);
        await Frames(2);
        Check(_main.CurrentMatch != null && Runner().Simulation!.World.Config.Seed == ulong.Parse(seed.Trim()), $"no match on seed {seed}");
    }

    private EntityHandle FindOwn(Simulation sim, Func<EntityHandle, bool> pick)
    {
        var u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.Owner[i] != SelectionController.LocalPlayer) continue;
            var h = new EntityHandle(i, u.Generation[i]);
            if (pick(h)) return h;
        }
        return default;
    }

    private static T? Find<T>(Node root, Func<T, bool>? where = null) where T : class
    {
        if (root is T t && (where == null || where(t))) return t;
        foreach (Node c in root.GetChildren())
            if (Find(c, where) is T found) return found;
        return null;
    }

    private async Task TypeInto(LineEdit edit, string text)
    {
        edit.GrabFocus();
        edit.SelectAll();
        await Frames(1);
        PushKey(Godot.Key.Backspace);
        if (edit.Text.Length > 0) { edit.Clear(); }
        foreach (char ch in text)
        {
            GetViewport().PushInput(new InputEventKey { Keycode = Godot.Key.None, Unicode = ch, Pressed = true });
            GetViewport().PushInput(new InputEventKey { Keycode = Godot.Key.None, Unicode = ch, Pressed = false });
        }
        await Frames(1);
    }

    private void PushKey(Key key)
    {
        GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    private async Task Key(Key key)
    {
        PushKey(key);
        await Frames(2);
    }

    private void PushMouse(MouseButton button, Vector2 at)
    {
        GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = button, Pressed = true, Position = at, GlobalPosition = at });
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = button, Pressed = false, Position = at, GlobalPosition = at });
    }

    private async Task ClickControl(Control c)
    {
        Check(c.IsVisibleInTree(), $"{c.Name} is not visible");
        Vector2 at = c.GetGlobalRect().GetCenter();
        GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        await Frames(1);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Left });
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
        await Frames(2);
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task<int> Seconds(double seconds)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        int frames = 0;
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            frames++;
        }
        return frames;
    }

    private static string Esc(string s) => string.Concat(s.Select(c => c < 32 || c > 126 ? $"\\u{(int)c:X4}" : c.ToString()));

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
