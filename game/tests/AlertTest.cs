using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>M6-V12: attack alerts on the real Match: an off-screen raid on the local base (text, ping, one Alert sound per 10 s / 30 m), Space to the alert, an on-screen raid (ping and sound, no text), and the same alerts in the replay.</summary>
/// <remarks>
/// Headless. Run: <c>&amp; $env:GODOT --headless --path game res://tests/AlertTest.tscn</c>; prints "ALERT TEST PASS" and exits 0,
/// or prints each failure and exits 1. The alerts' own frame step is disabled and driven from the runner's every tick with a
/// fixed <see cref="StepSeconds"/> of view time, so the live match and its replay see the same view time at every tick (the
/// 10 s suppression is 100 ticks). Windowed with <c>-- --shots &lt;dir&gt;</c> it saves the alert text and the minimap ping.
/// </remarks>
public partial class AlertTest : Node
{
    private const float StepSeconds = 0.1f;
    private const int Raiders = 4;

    private readonly List<string> _failures = new();
    private readonly List<(int Tick, AttackAlertKind Kind, System.Numerics.Vector2 At)> _live = new(), _replayed = new();
    private GameData _data = null!;
    private UiText _ui = null!;
    private string? _shots;

    private Match _match = null!;
    private SimRunner _runner = null!;
    private Simulation _sim = null!;
    private RtsCamera _camera = null!;
    private AlertHud _alerts = null!;
    private Minimap _minimap = null!;
    private Sfx _sfx = null!;
    private List<(int Tick, AttackAlertKind Kind, System.Numerics.Vector2 At)> _log = null!;

    private World W => _sim.World;

    public override async void _Ready()
    {
        try
        {
            string[] args = OS.GetCmdlineUserArgs();
            int at = Array.IndexOf(args, "--shots");
            if (at >= 0 && at + 1 < args.Length) _shots = args[at + 1];
            GetTree().Root.Size = new Vector2I(1152, 648);
            await Frame();
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            _ui = UiText.Shared ?? throw new InvalidOperationException("ui.json failed to load");
            KeyRow();
            Replay replay = await Live();
            await Replayed(replay);
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures) GD.Print($"ALERT TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("ALERT TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // Space is a fixed key: no action may take it.
    private void KeyRow()
    {
        Check(Array.IndexOf(KeyMap.Reserved, Key.Space) >= 0 && AlertHud.JumpKey == Key.Space, "Space is not among the reserved keys");
        Check(new KeyMap().TrySet(Array.IndexOf(KeyMap.Actions, "order_stop"), Key.Space) == KeyMap.IsReserved, "order_stop could take Space");
    }

    private void Open(Match m)
    {
        _match = m;
        _runner = m.GetNode<SimRunner>("SimRunner");
        _sim = _runner.Simulation!;
        _camera = m.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _alerts = m.GetNode<AlertHud>("Alerts");
        _alerts.SetProcess(false); // stepped from the runner's ticks below (input stays on: Space)
        _minimap = m.GetNode<Minimap>("Hud/Minimap");
        _sfx = m.GetNode<Sfx>("Sfx");
        _runner.Ticked += OnTicked;
    }

    private void OnTicked(Simulation sim)
    {
        int before = (int)_alerts.Alerts.Raised;
        _alerts.Step(sim.World, StepSeconds);
        for (int k = 0; k < _alerts.Alerts.NewCount; k++)
        {
            int a = _alerts.Alerts.New(k);
            _log.Add((sim.TickNumber, _alerts.Alerts.Kind(a), _alerts.Alerts.Position(a)));
        }
        if (_alerts.Alerts.Raised > before) GD.Print($"tick {sim.TickNumber}: alert {_alerts.Alerts.Kind(_alerts.Alerts.LastIndex)} at {_alerts.Alerts.Position(_alerts.Alerts.LastIndex)}, text shown {_alerts.TextShown} '{_alerts.TextLabel?.Text}'");
    }

    private async Task<Replay> Live()
    {
        _log = _live;
        var m = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(m);
        m.GetNode<SimRunner>("SimRunner").RecordCheckpointInterval = 20;
        m.Start(_data, LaunchOptions.Parse(new[] { "--units", "0", "--seed", "1", "--speed", "4", "--mute" }));
        Open(m);
        while (_sim.TickNumber < 3) await Frame();
        StartBasePlan bases = _match.Bases!;
        NavGrid g = W.NavGrid;
        System.Numerics.Vector2 hall = StartBase.FootprintCenter(g, _data.Buildings[bases.HallType[0]], bases.HallAnchor[0]);
        EntityHandle hallHandle = DefeatStaging.BuildingAt(W, 0, bases.HallAnchor[0]);
        Check(hallHandle != default, "no Town Hall for player 0");

        // The workers walk 60 m off toward the map centre first, so the raid's first blows land on the hall (a Base alert).
        System.Numerics.Vector2 centre = new System.Numerics.Vector2(g.Width, g.Height) * MapConstants.CellSize / 2;
        System.Numerics.Vector2 aside = hall + System.Numerics.Vector2.Normalize(centre - hall) * 60f;
        foreach (EntityHandle h in DefeatStaging.UnitsOf(W, 0)) _sim.Enqueue(Command.Move(0, h, aside));
        await Until(() => FarFrom(hall, 40f), 20);
        Check(FarFrom(hall, 40f), "the workers did not walk 40 m from the hall");

        // (a) Off screen: the camera on the workers 60 m off (explored ground, so a windowed shot shows the land), a raid of four on the hall.
        System.Numerics.Vector2 away = aside;
        _camera.SetFocus(away.X, away.Y);
        await Frame();
        Check(!ScreenCheck.OnScreen(_camera, W.Heightmap, hall), "the hall is on screen with the camera 60 m off");
        int texts0 = _alerts.TextsShown, sounds0 = _sfx.PlayCount(SfxEvent.Alert), pings0 = _minimap.PingsAdded;
        int raidTick = W.TickNumber;
        Raid(hallHandle, hall);
        await Until(() => _alerts.Alerts.Raised >= 1, 15);
        if (!Check(_alerts.Alerts.Raised == 1, $"off-screen raid: {_alerts.Alerts.Raised} alerts by tick {W.TickNumber} (raid sent at {raidTick})")) return _runner.Recorder!.ToReplay();
        int first = _alerts.Alerts.LastIndex;
        System.Numerics.Vector2 alertAt = _alerts.Alerts.Position(first);
        Check(_alerts.Alerts.Kind(first) == AttackAlertKind.Base && System.Numerics.Vector2.Distance(alertAt, hall) < 0.01f,
            $"off-screen raid: alert {_alerts.Alerts.Kind(first)} at {alertAt}, hall at {hall}");
        Check(_alerts.TextShown && _alerts.TextLabel!.Text == _ui.Alert(AlertText.BaseUnderAttack) && _alerts.TextsShown == texts0 + 1,
            $"off-screen raid: text shown {_alerts.TextShown} '{_alerts.TextLabel?.Text}', {_alerts.TextsShown - texts0} times");
        Rect2 label = _alerts.TextLabel!.GetGlobalRect();
        Check(Math.Abs(label.GetCenter().X - 1152 / 2f) < 2f && label.Position.Y < 100f, $"alert text at {label}, want top centre");
        Check(_sfx.PlayCount(SfxEvent.Alert) == sounds0 + 1, $"off-screen raid: Alert played {_sfx.PlayCount(SfxEvent.Alert) - sounds0} times");
        int ping = -1;
        for (int k = 0; k < Minimap.MaxPings; k++) if (_minimap.PingActive(k)) ping = k;
        System.Numerics.Vector2 hallPx = _minimap.Fit.ToPixel(hall);
        Check(_minimap.PingsAdded == pings0 + 1 && ping >= 0 && _minimap.PingPixel(ping).DistanceTo(new Vector2(hallPx.X, hallPx.Y)) < 1f,
            $"off-screen raid: {_minimap.PingsAdded - pings0} pings, active {ping}, at {(ping >= 0 ? _minimap.PingPixel(ping) : default)} (hall {hallPx})");
        await Shot("alert-base");

        // Hits go on for the next 10 s of view time (100 ticks): no second alert, no second sound; the text goes after 4 s.
        int firstTick = _live[0].Tick;
        await Until(() => W.TickNumber >= firstTick + 95, 30);
        Check(_alerts.Alerts.Raised == 1 && _sfx.PlayCount(SfxEvent.Alert) == sounds0 + 1 && _alerts.Alerts.HitsOn(first) > 1,
            $"9.5 s of raid: {_alerts.Alerts.Raised} alerts, {_sfx.PlayCount(SfxEvent.Alert) - sounds0} sounds, {_alerts.Alerts.HitsOn(first)} hits on the first");
        Check(!_alerts.TextShown, "the alert text is still up after 9.5 s (want 4 s)");
        await Until(() => _alerts.Alerts.Raised >= 2, 15);
        Check(_alerts.Alerts.Raised == 2 && _sfx.PlayCount(SfxEvent.Alert) == sounds0 + 2 && W.TickNumber - firstTick >= 100,
            $"after 10 s: {_alerts.Alerts.Raised} alerts, {_sfx.PlayCount(SfxEvent.Alert) - sounds0} sounds, {W.TickNumber - firstTick} ticks after the first");

        // (b) Space: the camera jumps to the last alert.
        _camera.SetFocus(away.X, away.Y);
        int jumps = _alerts.Jumps;
        GetViewport().PushInput(new InputEventKey { Keycode = Key.Space, PhysicalKeycode = Key.Space, Pressed = true });
        GetViewport().PushInput(new InputEventKey { Keycode = Key.Space, PhysicalKeycode = Key.Space, Pressed = false });
        await Frame();
        System.Numerics.Vector2 last = _alerts.Alerts.Position(_alerts.Alerts.LastIndex);
        float off = System.Numerics.Vector2.Distance(_camera.Focus, last);
        GD.Print($"Space: camera focus {_camera.Focus}, last alert {last}: {off:F2} m");
        Check(_alerts.Jumps == jumps + 1 && off <= 5f, $"Space: {_alerts.Jumps - jumps} jumps, focus {off:F2} m from the alert");

        // (c) On screen: the camera on the hall, the next alert (10 s on) pings and sounds, no text.
        Check(ScreenCheck.OnScreen(_camera, W.Heightmap, hall), "after Space the hall is not on screen");
        int onScreen0 = _alerts.OnScreenAlerts, texts1 = _alerts.TextsShown, sounds1 = _sfx.PlayCount(SfxEvent.Alert), pings1 = _minimap.PingsAdded;
        Raid(hallHandle, hall); // fresh raiders, in case the first ones fell to the workers
        await Until(() => _alerts.Alerts.Raised >= 3, 15);
        Check(_alerts.Alerts.Raised == 3 && _alerts.OnScreenAlerts == onScreen0 + 1 && _alerts.TextsShown == texts1 && !_alerts.TextShown,
            $"on-screen raid: {_alerts.Alerts.Raised} alerts, {_alerts.OnScreenAlerts - onScreen0} on screen, text shown {_alerts.TextsShown - texts1} times");
        Check(_sfx.PlayCount(SfxEvent.Alert) == sounds1 + 1 && _minimap.PingsAdded == pings1 + 1,
            $"on-screen raid: {_sfx.PlayCount(SfxEvent.Alert) - sounds1} sounds, {_minimap.PingsAdded - pings1} pings");
        await Shot("alert-on-screen");
        // The newest ping pulses 3 s of the minimap's own frame time (the wall clock here), then goes (the raid goes on: more
        // alerts may come meanwhile, each 10 s of view time on; the replay must show the same ones).
        int newest = -1;
        for (int k = 0; k < Minimap.MaxPings; k++) if (_minimap.PingActive(k) && (newest < 0 || _minimap.PingAge(k) < _minimap.PingAge(newest))) newest = k;
        if (Check(newest >= 0, "no ping pulsing after the on-screen alert"))
        {
            ulong pinged = Time.GetTicksUsec();
            float age0 = _minimap.PingAge(newest);
            await Until(() => !_minimap.PingActive(newest), 6);
            double pulsed = (Time.GetTicksUsec() - pinged) / 1e6 + age0;
            Check(!_minimap.PingActive(newest) && pulsed >= Minimap.PingSeconds - 0.1 && pulsed < Minimap.PingSeconds + 0.5, $"the ping pulsed {pulsed:F2} s (want {Minimap.PingSeconds})");
        }
        int endTick = W.TickNumber;
        _runner.Ticked -= OnTicked;
        Replay replay = _runner.Recorder!.ToReplay();
        GD.Print($"live: {_live.Count} alerts by tick {endTick}, replay of {replay.TickCount} ticks, {replay.Commands.Length} commands");
        RemoveChild(_match);
        _match.QueueFree();
        await Frame();
        return replay;
    }

    // Raiders spawned beside the hall's footprint, each ordered onto it the tick after it appears.
    private void Raid(EntityHandle hallHandle, System.Numerics.Vector2 hall)
    {
        int raider = _data.FindUnit("whirlwind_raider");
        BuildingDef def = _data.Buildings[W.Buildings.TypeId[hallHandle.Index]];
        float reach = Math.Max(def.FootprintWidth, def.FootprintHeight) * MapConstants.CellSize / 2f + 1.5f;
        for (int k = 0; k < Raiders; k++)
        {
            float a = k * MathF.Tau / Raiders + 0.4f;
            _sim.Enqueue(Command.SpawnUnit(1, raider, hall + new System.Numerics.Vector2(MathF.Cos(a), MathF.Sin(a)) * reach));
        }
        _pendingRaid = hallHandle;
        _runner.Ticked += OrderRaid;
    }

    private EntityHandle _pendingRaid;

    // Every live unit of player 0 at least `meters` from `at`.
    private bool FarFrom(System.Numerics.Vector2 at, float meters)
    {
        foreach (EntityHandle h in DefeatStaging.UnitsOf(W, 0))
            if (System.Numerics.Vector2.Distance(W.Units.Position[h.Index], at) < meters) return false;
        return true;
    }

    private void OrderRaid(Simulation sim)
    {
        _runner.Ticked -= OrderRaid;
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == 1) sim.Enqueue(Command.Attack(1, new EntityHandle(i, u.Generation[i]), _pendingRaid, isBuilding: true));
    }

    // The recording played back: the same alerts at the same ticks for player 1's seat (the local player, slot 0).
    private async Task Replayed(Replay replay)
    {
        _log = _replayed;
        var m = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(m);
        ReplayError refused = m.StartReplay(_data, replay, LaunchOptions.Parse(new[] { "--speed", "8", "--mute" }), "alert-test");
        if (!Check(refused == ReplayError.None, $"replay refused: {refused}")) return;
        Open(m);
        await Until(() => _runner.PlaybackState != PlaybackState.Playing, 60);
        _runner.Ticked -= OnTicked;
        string Show(List<(int Tick, AttackAlertKind Kind, System.Numerics.Vector2 At)> l) => string.Join("; ", l.ConvertAll(x => $"{x.Tick} {x.Kind} {x.At}"));
        GD.Print($"replay: {_runner.PlaybackState} at tick {_sim.TickNumber}; alerts live [{Show(_live)}], replayed [{Show(_replayed)}]");
        Check(_live.Count >= 3 && _replayed.Count == _live.Count, $"replay: {_replayed.Count} alerts, live {_live.Count}");
        for (int k = 0; k < Math.Min(_live.Count, _replayed.Count); k++)
            Check(_live[k] == _replayed[k], $"replay alert {k}: {_replayed[k]}, live {_live[k]}");
        RemoveChild(m);
        m.QueueFree();
        await Frame();
    }

    private async Task Until(Func<bool> done, double seconds)
    {
        ulong start = Time.GetTicksUsec();
        while (!done() && Time.GetTicksUsec() - start < (ulong)(seconds * 1_000_000)) await Frame();
    }

    private async Task Shot(string name)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = $"{_shots}/{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"screenshot {path}");
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
