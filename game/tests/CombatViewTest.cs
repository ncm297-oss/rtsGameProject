using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.Replays;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>M4-V1 on the real Match scene: hp bars, hit flashes, corpses, rubble, kill / loss counts and the live panel hp against the sim every frame of a scripted 20 v 20 brawl, plus <c>--no-combat</c>, a death storm and the hash twin.</summary>
/// <remarks>
/// Headless. Run: <c>&amp; $env:GODOT --headless --path game res://tests/CombatViewTest.tscn</c> (seeds 1 and 6; <c>-- --seed N</c>
/// for one); prints "COMBAT VIEW TEST PASS" and exits 0, or prints each failure and exits 1. The test ticks the sim itself
/// (SimRunner disabled) and drives the unit views' <c>Sync</c> three times a tick with a fixed 0.04 s step, so the flash
/// timer is exact; the other views run their own <c>_Process</c> in the two frames it awaits after each tick. One more run
/// lets the runner tick at 8x from frame time, where a frame runs several ticks, and checks no death was missed. Windowed
/// with <c>-- --shots &lt;dir&gt;</c> it saves the brawl with bars and corpses and the rubble.
/// </remarks>
public partial class CombatViewTest : Node
{
    private const int PerSide = 20, MaxTicks = 9000, FramesPerTick = 3;
    private const float Dt = 0.04f; // 0.15 s of flash = the hit frame and 3 more

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private string? _shots;

    private Match _match = null!;
    private SimRunner _runner = null!;
    private Simulation _sim = null!;
    private UnitViews _units = null!;
    private BuildingViews _buildings = null!;
    private CombatViews _combat = null!;
    private ResourceBar _bar = null!;
    private DebugOverlay _overlay = null!;
    private SelectionPanel _panel = null!;
    private SelectionController _sel = null!;
    private RtsCamera _camera = null!;
    private UiText _ui = null!;

    private World W => _sim.World;
    private UnitStore U => _sim.World.Units;

    // The test's own oracle, from the store alone.
    private int[] _lastHp = Array.Empty<int>(), _lastGen = Array.Empty<int>(), _sinceHit = Array.Empty<int>();
    private readonly List<(System.Numerics.Vector2 Pos, bool Building, long Expires)> _expected = new();

    public override async void _Ready()
    {
        try
        {
            await Run();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        int shown = _failures.Count;
        for (int i = 0; i < shown; i++) GD.Print($"COMBAT VIEW TEST FAIL: {_failures[i]}");
        if (_failures.Count > shown) GD.Print($"COMBAT VIEW TEST FAIL: ... and {_failures.Count - shown} more");
        if (_failures.Count == 0) GD.Print("COMBAT VIEW TEST PASS");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    private async Task Run()
    {
        string[] args = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(args, "--shots");
        if (at >= 0 && at + 1 < args.Length) _shots = args[at + 1];
        ulong[] seeds = { 1, 6 };
        at = Array.IndexOf(args, "--seed");
        if (at >= 0 && at + 1 < args.Length && ulong.TryParse(args[at + 1], out ulong one)) seeds = new[] { one };
        GetTree().Root.Size = new Vector2I(1152, 648); // headless windows are 64 x 64
        await Frame();
        DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
        if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
        _data = loaded.Data!;
        _ui = UiText.Shared ?? throw new InvalidOperationException("ui.json failed to load");

        await LaunchRows();
        foreach (ulong seed in seeds) await Brawl(seed);
        await RealFrames();
    }

    // --no-combat: parsed (takes no value, swallows nothing), off by default, and it reaches the match's sim. hud labels from ui.json.
    private async Task LaunchRows()
    {
        Check(LaunchOptions.Parse(new[] { "--no-combat" }).NoCombat, "--no-combat not parsed");
        Check(!LaunchOptions.Parse(Array.Empty<string>()).NoCombat, "combat off without the flag");
        LaunchOptions o = LaunchOptions.Parse(new[] { "--no-combat", "--seed", "3" });
        Check(o.NoCombat && o.Seed == 3, "--no-combat swallowed the next flag");
        Check(LaunchOptions.Parse(new[] { "--units", "--no-combat" }).NoCombat, "--units took --no-combat as its value");
        foreach (bool off in new[] { false, true })
        {
            var m = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
            AddChild(m);
            m.Start(_data, LaunchOptions.Parse(off ? new[] { "--units", "2", "--mute", "--no-combat" } : new[] { "--units", "2", "--mute" }));
            Simulation sim = m.GetNode<SimRunner>("SimRunner").Simulation!;
            Check(sim.World.CombatEnabled == !off, $"{(off ? "--no-combat" : "default")}: CombatEnabled {sim.World.CombatEnabled}");
            Check(sim.World.Config.Combat == !off, $"{(off ? "--no-combat" : "default")}: config Combat {sim.World.Config.Combat}");
            RemoveChild(m);
            m.QueueFree();
            await Frame();
        }
        Check(_ui.Hud(HudText.Kills).Length > 0 && _ui.Hud(HudText.Losses).Length > 0, "ui.json hud.kills / hud.losses missing");
    }

    private void StartMatch(ulong seed, bool runnerTicks)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _runner = _match.GetNode<SimRunner>("SimRunner");
        _runner.RecordCheckpointInterval = 1;
        var args = new List<string> { "--seed", seed.ToString(CultureInfo.InvariantCulture), "--units", "0", "--no-bases", "--mute", "--debug-overlay", "--zoom", "30", "--no-fog" }; // whole-map checks (M4-V4: every hurt unit has a bar, every death a marker)
        if (runnerTicks) args.AddRange(new[] { "--speed", "8" });
        _match.Start(_data, LaunchOptions.Parse(args.ToArray()));
        if (!runnerTicks) _runner.ProcessMode = ProcessModeEnum.Disabled; // the test ticks the sim itself
        _sim = _runner.Simulation!;
        _units = _match.GetNode<UnitViews>("World3D/UnitViews");
        if (!runnerTicks) _units.ProcessMode = ProcessModeEnum.Disabled; // the test drives its Sync with a fixed step
        _buildings = _match.GetNode<BuildingViews>("World3D/BuildingViews");
        _combat = _match.GetNode<CombatViews>("World3D/CombatViews");
        _bar = _match.GetNode<ResourceBar>("Hud/ResourceBar");
        _overlay = _match.GetNode<DebugOverlay>("DebugOverlay");
        _panel = _match.GetNode<SelectionPanel>("Hud/SelectionPanel");
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _lastHp = new int[U.Capacity];
        _lastGen = new int[U.Capacity];
        _sinceHit = new int[U.Capacity];
        Array.Fill(_lastGen, -1);
        Array.Fill(_sinceHit, 1000);
        _expected.Clear();
    }

    private async Task EndMatch()
    {
        RemoveChild(_match);
        _match.QueueFree();
        await Frame();
    }

    // Two armies of 20 west and east of the map's central cell, a Billet behind player 0, a Tent behind player 1; returns the attack-move goals.
    private (System.Numerics.Vector2 Goal0, System.Numerics.Vector2 Goal1) Stage()
    {
        NavGrid g = W.NavGrid;
        int center = FlowField.NearestPassable(g, g.Height / 2 * g.Width + g.Width / 2);
        int cx = center % g.Width, cy = center / g.Width;
        FlowField field = FlowField.Build(g, center);
        int hi = _data.FindUnit("malazan_heavy_infantry"), raider = _data.FindUnit("whirlwind_raider");
        int billet = _data.FindBuilding("malazan_billet"), tent = _data.FindBuilding("whirlwind_tent");
        var goals = new System.Numerics.Vector2[2];
        for (int p = 0; p < 2; p++)
        {
            int dir = p == 0 ? -1 : 1;
            int type = p == 0 ? billet : tent;
            int anchor = -1;
            float best = float.MaxValue;
            for (int c = 0; c < g.Width * g.Height; c++)
            {
                int x = c % g.Width, y = c / g.Width;
                if ((x - cx) * dir < 12 || (x - cx) * dir > 20 || !(field.CostAt(c) <= 30f) || !W.Buildings.Fits(type, c)) continue;
                float d = field.CostAt(c) + Math.Abs(y - cy);
                if (d < best) { best = d; anchor = c; }
            }
            if (anchor < 0) throw new InvalidOperationException($"no spot for player {p}'s building");
            _sim.Enqueue(Command.SpawnBuilding(p, type, g.CellCenter(anchor % g.Width, anchor / g.Width)));
            goals[1 - p] = StartBase.FootprintCenter(g, _data.Buildings[type], anchor);
            int placed = 0;
            for (int ring = 0; ring < 30 && placed < PerSide; ring++)
            {
                for (int c = 0; c < g.Width * g.Height && placed < PerSide; c++)
                {
                    int x = c % g.Width, y = c / g.Width;
                    int dx = (x - cx) * dir;
                    if (dx < 3 || dx > 7 || Math.Abs(y - cy) != ring || !(field.CostAt(c) <= 20f)) continue;
                    _sim.Enqueue(Command.SpawnUnit(p, p == 0 ? hi : raider, g.CellCenter(x, y)));
                    placed++;
                }
            }
            if (placed < PerSide) throw new InvalidOperationException($"only {placed} spots for player {p}");
        }
        return (goals[0], goals[1]);
    }

    private async Task Brawl(ulong seed)
    {
        StartMatch(seed, runnerTicks: false);
        Check(W.CombatEnabled, $"seed {seed}: combat off in a default match");
        CheckCorpseColours(seed);
        (System.Numerics.Vector2 goal0, System.Numerics.Vector2 goal1) = Stage();
        await TickAndCheck(seed);
        await TickAndCheck(seed);
        Check(U.Count == 2 * PerSide && W.Buildings.Count == 2, $"seed {seed}: {U.Count} units, {W.Buildings.Count} buildings after the spawns");
        EntityHandle watched = default;
        for (int i = 0; i < U.Capacity; i++)
        {
            if (!U.Alive[i]) continue;
            var h = new EntityHandle(i, U.Generation[i]);
            if (U.Owner[i] == 0 && watched == default) watched = h;
            _sim.Enqueue(Command.AttackMove(U.Owner[i], h, U.Owner[i] == 0 ? goal0 : goal1));
        }
        _sel.SelectOnly(watched);
        System.Numerics.Vector2 mid = (goal0 + goal1) / 2;
        _camera.SetFocus(mid.X, mid.Y);

        int unitDeaths = 0, buildingDeaths = 0, maxBars = 0, panelFrames = 0, lastDeathTick = -1;
        bool shotBrawl = false, shotRubble = false, shotCorpses = false, shotLate = false;
        long steadyBytes = 0;
        int steadyFrames = 0;
        for (int t = 0; t < MaxTicks; t++)
        {
            (int u, int b) = await TickAndCheck(seed);
            unitDeaths += u;
            buildingDeaths += b;
            if (u + b > 0) lastDeathTick = W.TickNumber;
            maxBars = Math.Max(maxBars, _combat.ShownBars);
            if (U.IsAlive(watched) && _sel.Selection.Count == 1)
            {
                panelFrames++;
                Check(_panel.StatValue(0).Text == U.Hp[watched.Index].ToString(CultureInfo.InvariantCulture),
                    $"seed {seed} tick {W.TickNumber}: panel hp '{_panel.StatValue(0).Text}', store {U.Hp[watched.Index]}");
            }
            // A steady brawl after warm-up: the per-frame view work allocates nothing (the bar's kill text and the F12
            // label are rebuilt only when a number changes, which is a change, not a steady frame).
            if (W.TickNumber >= 120 && steadyFrames < 300 && _combat.ShownBars > 0)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                _combat.Sync(W, 0.5f);
                _units.Sync(W, 0.5f, 0f);
                _panel.Sync();
                steadyBytes += GC.GetAllocatedBytesForCurrentThread() - before;
                steadyFrames++;
            }
            if (!shotBrawl && _combat.ShownBars >= 6 && _combat.Markers.Count >= 3)
            {
                shotBrawl = true;
                await Shot($"seed{seed}-brawl");
                await Shot($"seed{seed}-f12", overlay: true);
            }
            if (!shotCorpses && _combat.Markers.Count >= 8)
            {
                shotCorpses = true;
                await Shot($"seed{seed}-corpses");
            }
            // Most of the field dead: both teams' corpses side by side (M4-V2, BUG-0160 item 3).
            if (!shotLate && _combat.Markers.Count >= 25)
            {
                shotLate = true;
                await Shot($"seed{seed}-corpses-late");
            }
            if (!shotRubble && buildingDeaths > 0)
            {
                shotRubble = true;
                await Shot($"seed{seed}-rubble");
            }
            if (buildingDeaths > 0 && _combat.Markers.Count == 0 && W.TickNumber > lastDeathTick + DeathMarkers.BuildingLifetimeTicks) break;
        }
        GD.Print($"seed {seed}: {unitDeaths} unit deaths, {buildingDeaths} building deaths by tick {lastDeathTick}, markers gone by {W.TickNumber}; " +
            $"at most {maxBars} bars; kills {W.Kills[0]} / {W.Kills[1]}, losses {W.Losses[0]} / {W.Losses[1]}; panel live {panelFrames} frames; " +
            $"flash hits {_hits}, {_hitWhileLit} while lit; steady frames {steadyFrames}: {steadyBytes} bytes; corpses raised on a slope (BUG-0190) {_corpsesOnSlopes}");
        Check(unitDeaths >= PerSide, $"seed {seed}: only {unitDeaths} unit deaths");
        Check(buildingDeaths == 1, $"seed {seed}: {buildingDeaths} building deaths (want the losing side's one)");
        Check(_combat.Markers.Count == 0, $"seed {seed}: {_combat.Markers.Count} markers left after their time");
        Check(maxBars >= 6, $"seed {seed}: at most {maxBars} bars at once");
        Check(_hits > 50 && _hitWhileLit > 0, $"seed {seed}: {_hits} flash hits, {_hitWhileLit} while lit");
        Check(panelFrames > 50, $"seed {seed}: the selected unit's panel was checked {panelFrames} frames");
        Check(steadyFrames >= 100 && steadyBytes == 0, $"seed {seed}: {steadyFrames} steady brawl frames allocated {steadyBytes} bytes");
        _hits = _hitWhileLit = 0;

        await DeathStorm(seed);
        await CorpseCloseUp(seed);

        // The hash twin: the stream the scene sent, replayed bare, equals the views' sim every tick.
        Replay replay = _runner.Recorder!.ToReplay();
        ReplayResult twin = ReplayPlayer.Run(replay, _data);
        Check(twin.Ok && replay.Checkpoints.Length == replay.TickCount, $"seed {seed}: twin {twin.Error} at tick {twin.Tick} ({twin.ExpectedHash:x} vs {twin.ActualHash:x})");
        GD.Print($"seed {seed}: hash twin {replay.Commands.Length} commands, {replay.Checkpoints.Length} checkpoints equal");
        await EndMatch();
    }

    private int _hits, _hitWhileLit, _corpsesOnSlopes;

    // One tick, the unit views three times at the fixed step (flash checked each time), two frames for the other views, then every check. Returns this tick's unit and building deaths.
    private async Task<(int Units, int Buildings)> TickAndCheck(ulong seed)
    {
        long addedBefore = _combat.Markers.Added;
        _sim.Tick();
        DeathEvent[] deaths = W.Deaths.ToArray();
        int tick = W.TickNumber;
        for (int f = 0; f < FramesPerTick; f++)
        {
            _units.Sync(W, f / (float)FramesPerTick, Dt);
            CheckFlash(seed, tick, f);
        }
        await Frame();
        await Frame();
        int units = 0, buildings = 0;
        foreach (DeathEvent d in deaths)
        {
            if (d.IsBuilding)
            {
                buildings++;
                Check(!_buildings.IsShown(d.Victim.Index), $"seed {seed} tick {tick}: dead building {d.Victim.Index} still shown");
            }
            else
            {
                units++;
                MeshInstance3D? view = _units.ViewOf(d.Victim.Index);
                Check(view != null && !view.Visible, $"seed {seed} tick {tick}: dead unit {d.Victim.Index}'s view still visible");
            }
            _expected.Add((d.Position, d.IsBuilding, tick + DeathMarkers.LifetimeTicks(d.IsBuilding)));
            int slot = FindMarker(d.Position, d.IsBuilding, tick + DeathMarkers.LifetimeTicks(d.IsBuilding));
            if (!Check(slot >= 0, $"seed {seed} tick {tick}: no marker for the death at {d.Position}")) continue;
            Check(_combat.MarkerShown(slot) == (d.IsBuilding ? 2 : 1), $"seed {seed} tick {tick}: marker {slot} shows kind {_combat.MarkerShown(slot)}");
            Transform3D tr = _combat.MarkerTransform(slot);
            float ground = TerrainHeight.At(W.Heightmap, d.Position.X, d.Position.Y);
            Check(Mathf.Abs(tr.Origin.X - d.Position.X) < 1e-3f && Mathf.Abs(tr.Origin.Z - d.Position.Y) < 1e-3f && tr.Origin.Y > ground && tr.Origin.Y < ground + 1f && tr.Basis.Scale.X > 0.1f,
                $"seed {seed} tick {tick}: marker {slot} drawn at {tr.Origin}, death at {d.Position}");
            // BUG-0190 item 1: a corpse disc's underside is at the highest ground under its rim, so a ramp never buries half of it.
            if (!d.IsBuilding)
            {
                float top = TerrainHeight.MaxUnder(W.Heightmap, d.Position.X, d.Position.Y, tr.Basis.Scale.X * CombatViews.CorpseRimScale);
                Check(tr.Origin.Y - CombatViews.CorpseHeight / 2f >= top - 1e-3f, $"seed {seed} tick {tick}: corpse {slot} underside {tr.Origin.Y - CombatViews.CorpseHeight / 2f:F3} below the ground under its rim {top:F3}");
                if (top > ground + 0.05f) _corpsesOnSlopes++;
            }
        }
        Check(_combat.Markers.Added - addedBefore == deaths.Length, $"seed {seed} tick {tick}: {deaths.Length} deaths, {_combat.Markers.Added - addedBefore} markers added");
        CheckMarkers(seed, tick);
        CheckBars(seed, tick);
        CheckCounts(seed, tick);
        if (tick % 200 == 0) CheckOverlayLayout(seed, tick);
        return (units, buildings);
    }

    private int FindMarker(System.Numerics.Vector2 pos, bool building, long expires)
    {
        DeathMarkers m = _combat.Markers;
        for (int i = 0; i < m.Capacity; i++)
            if (m.Active[i] && m.Position[i] == pos && m.IsBuilding[i] == building && m.ExpiresAt[i] == expires) return i;
        return -1;
    }

    // The oracle's flash: lit exactly while fewer than 4 fixed steps passed since the unit's hp last fell.
    private void CheckFlash(ulong seed, int tick, int frame)
    {
        for (int i = 0; i < U.Capacity; i++)
        {
            bool same = U.Alive[i] && U.Generation[i] == _lastGen[i];
            // M4-V2 (BUG-0160 item 2): a unit first seen below its type's hp counts as hit once.
            bool first = U.Alive[i] && !same;
            bool hit = same ? U.Hp[i] < _lastHp[i] : first && U.Hp[i] < _data.Units[U.TypeId[i]].Hp;
            if (hit)
            {
                if (_sinceHit[i] <= 3) _hitWhileLit++;
                _sinceHit[i] = 0;
                _hits++;
            }
            else _sinceHit[i] = same ? _sinceHit[i] + 1 : 1000;
            bool want = U.Alive[i] && _sinceHit[i] <= 3;
            bool lit = _units.Flash.IsLit(i);
            if (lit != want || _units.Flash.WasHit(i) != hit)
                Check(false, $"seed {seed} tick {tick} frame {frame} slot {i}: lit {lit} (want {want}), hit {_units.Flash.WasHit(i)} (want {hit})");
            if (U.Alive[i])
            {
                MeshInstance3D? view = _units.ViewOf(i);
                bool drawn = view != null && view.MaterialOverlay == _units.FlashMaterial;
                if (drawn != want) Check(false, $"seed {seed} tick {tick} frame {frame} slot {i}: flash overlay drawn {drawn}, want {want}");
            }
            _lastHp[i] = U.Hp[i];
            _lastGen[i] = U.Alive[i] ? U.Generation[i] : -1;
        }
    }

    // Markers live exactly until their expiry tick; a slot not in use draws nothing.
    private void CheckMarkers(ulong seed, int tick)
    {
        int want = 0;
        foreach (var e in _expected) if (tick < e.Expires) want++;
        DeathMarkers m = _combat.Markers;
        Check(m.Count == want, $"seed {seed} tick {tick}: {m.Count} markers, want {want}");
        for (int i = 0; i < m.Capacity; i++)
        {
            if (m.Active[i])
            {
                if (tick >= m.ExpiresAt[i] || tick < m.ExpiresAt[i] - DeathMarkers.LifetimeTicks(m.IsBuilding[i]))
                    Check(false, $"seed {seed} tick {tick}: marker {i} alive outside its time (expires {m.ExpiresAt[i]})");
            }
            else if (_combat.MarkerShown(i) != 0 || _combat.MarkerTransform(i).Basis.Scale != Vector3.Zero)
                Check(false, $"seed {seed} tick {tick}: unused marker {i} still drawn");
        }
    }

    // A bar for exactly the live hurt units, in slot order, fill and colour from Hp / max.
    private void CheckBars(ulong seed, int tick)
    {
        int k = 0;
        for (int i = 0; i < U.Capacity; i++)
        {
            if (!U.Alive[i] || U.Hp[i] >= _data.Units[U.TypeId[i]].Hp) continue;
            if (k >= _combat.ShownBars || _combat.BarSlot(k) != i)
            {
                Check(false, $"seed {seed} tick {tick}: hurt unit {i} has no bar (bar {k} is {(k < _combat.ShownBars ? _combat.BarSlot(k) : -1)})");
                return;
            }
            float fill = Math.Clamp((float)U.Hp[i] / _data.Units[U.TypeId[i]].Hp, 0f, 1f);
            System.Numerics.Vector3 c = UnitHpBars.Color(fill);
            Color drawn = _combat.BarColor(k);
            if (Math.Abs(_combat.BarFill(k) - fill) > 1e-5f || Math.Abs(drawn.R - c.X) > 0.01f || Math.Abs(drawn.G - c.Y) > 0.01f)
                Check(false, $"seed {seed} tick {tick}: unit {i} bar fill {_combat.BarFill(k)} colour {drawn}, want {fill} {c}");
            k++;
        }
        Check(k == _combat.ShownBars, $"seed {seed} tick {tick}: {_combat.ShownBars} bars for {k} hurt units");
    }

    // Resource bar and F12 overlay equal World.Kills / Losses, labelled from ui.json.
    private void CheckCounts(ulong seed, int tick)
    {
        int k0 = W.Kills[0], l0 = W.Losses[0];
        string want = $"{_ui.Hud(HudText.Kills)} {k0} / {_ui.Hud(HudText.Losses)} {l0}";
        Check(_bar.ShownKills == k0 && _bar.ShownLosses == l0 && _bar.KillsLabel.Text == want,
            $"seed {seed} tick {tick}: bar '{_bar.KillsLabel.Text}' ({_bar.ShownKills} / {_bar.ShownLosses}), want '{want}'");
        Check(_overlay.Kills0 == k0 && _overlay.Losses0 == l0 && _overlay.Kills1 == W.Kills[1] && _overlay.Losses1 == W.Losses[1],
            $"seed {seed} tick {tick}: overlay {_overlay.Kills0}/{_overlay.Losses0} {_overlay.Kills1}/{_overlay.Losses1}, sim {k0}/{l0} {W.Kills[1]}/{W.Losses[1]}");
        string line = $"p1 {_ui.Hud(HudText.Kills)} {W.Kills[1]} / {_ui.Hud(HudText.Losses)} {W.Losses[1]}";
        Check(_overlay.Enabled && _match.GetNode<Label>("DebugOverlay/Label").Text.Contains(line), $"seed {seed} tick {tick}: overlay label lacks '{line}'");
    }

    // BUG-0160 item 1: every line of the F12 label ends left of the resource bar's labels (Pop, K / L) and the label ends
    // above the tick graph, measured with the label's own font at the 1152 x 648 window.
    private void CheckOverlayLayout(ulong seed, int tick)
    {
        Label label = _match.GetNode<Label>("DebugOverlay/Label");
        Font font = label.GetThemeFont("font");
        int size = label.GetThemeFontSize("font_size");
        float right = float.MaxValue;
        foreach (Control c in new Control[] { _bar, _bar.PopLabel, _bar.KillsLabel }) right = Math.Min(right, c.GetGlobalRect().Position.X);
        string[] lines = label.Text.Split('\n');
        Check(lines.Length == 5, $"seed {seed} tick {tick}: F12 label has {lines.Length} lines, want 5");
        float widest = 0f;
        foreach (string line in lines)
        {
            float w = font.GetStringSize(line, HorizontalAlignment.Left, -1, size).X;
            widest = Math.Max(widest, w);
            Check(label.GlobalPosition.X + w < right, $"seed {seed} tick {tick}: F12 line '{line}' is {w:0} px wide, runs past x {right:0} (resource bar)");
        }
        float bottom = label.GlobalPosition.Y + lines.Length * font.GetHeight(size);
        float graphTop = _overlay.Graph.GetGlobalRect().Position.Y;
        Check(bottom <= graphTop, $"seed {seed} tick {tick}: F12 label ends at y {bottom:0}, the graph starts at {graphTop:0}");
        if (!_layoutPrinted)
        {
            _layoutPrinted = true;
            GD.Print($"F12 layout: widest line {widest:0} px from x {label.GlobalPosition.X:0}, resource bar from x {right:0}; label ends y {bottom:0}, graph from y {graphTop:0}");
        }
    }

    private bool _layoutPrinted;

    // BUG-0160 item 3: corpses read by team: the fill is the owner colour at CorpseShade, the rim darker, and the two
    // players' fills are bright enough to read and far apart.
    private void CheckCorpseColours(ulong seed)
    {
        float[] lum = new float[2];
        for (int p = 0; p < 2; p++)
        {
            Color c = UnitViews.ColorFromRgb(_data.Factions[W.FactionOf(p)].PrimaryColor);
            Color fill = _combat.CorpseColor(p), rim = _combat.CorpseRimColor(p);
            Check(Math.Abs(fill.R - c.R * CombatViews.CorpseShade) < 1e-4f && Math.Abs(fill.G - c.G * CombatViews.CorpseShade) < 1e-4f && Math.Abs(fill.B - c.B * CombatViews.CorpseShade) < 1e-4f,
                $"seed {seed}: player {p} corpse fill {fill}, owner colour {c}");
            Check(rim.R < fill.R && rim.G < fill.G && rim.B < fill.B, $"seed {seed}: player {p} corpse rim {rim} not darker than {fill}");
            lum[p] = 0.2126f * fill.R + 0.7152f * fill.G + 0.0722f * fill.B;
            Check(lum[p] >= 0.2f, $"seed {seed}: player {p} corpse fill {fill} reads black (luminance {lum[p]:0.00})");
        }
        Color a = _combat.CorpseColor(0), b = _combat.CorpseColor(1);
        float d = MathF.Sqrt((a.R - b.R) * (a.R - b.R) + (a.G - b.G) * (a.G - b.G) + (a.B - b.B) * (a.B - b.B));
        Check(d >= 0.25f, $"seed {seed}: the two teams' corpses {a} / {b} are only {d:0.00} apart");
        Check(_combat.CorpseRimMesh.InstanceCount == _combat.CorpseMesh.InstanceCount, $"seed {seed}: rim pool {_combat.CorpseRimMesh.InstanceCount}, corpse pool {_combat.CorpseMesh.InstanceCount}");
        GD.Print($"seed {seed}: corpse fills {a} / {b} (luminance {lum[0]:0.00} / {lum[1]:0.00}, {d:0.00} apart), rims {_combat.CorpseRimColor(0)} / {_combat.CorpseRimColor(1)}");
    }

    // Windowed only (BUG-0160 item 3): a row of each team's corpses on open ground, zoomed in, through the real draw path
    // (AddDeaths), so the shot shows the two teams' discs side by side; then the camera goes back and they expire.
    private async Task CorpseCloseUp(ulong seed)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        NavGrid g = W.NavGrid;
        int center = FlowField.NearestPassable(g, g.Height / 2 * g.Width + g.Width / 2);
        System.Numerics.Vector2 c = g.CellCenter(center % g.Width, center / g.Width);
        int hi = _data.FindUnit("malazan_heavy_infantry"), raider = _data.FindUnit("whirlwind_raider");
        var deaths = new DeathEvent[12];
        for (int k = 0; k < deaths.Length; k++)
        {
            int owner = k / 6;
            var at = c + new System.Numerics.Vector2((k % 6 - 2.5f) * 1.6f, owner == 0 ? -1.2f : 1.2f);
            deaths[k] = new DeathEvent(new EntityHandle(k, 1), false, owner == 0 ? hi : raider, owner, 1 - owner, at);
        }
        _combat.AddDeaths(W, deaths, W.TickNumber);
        float zoom = _camera.Zoom;
        _camera.SetFocus(c.X, c.Y);
        _camera.SetZoom(14f);
        await Frame();
        await Shot($"seed{seed}-corpses-close");
        _camera.SetZoom(zoom);
        while (_combat.Markers.Count > 0) { _sim.Tick(); _combat.Sync(W, 1f); }
        await Frame();
    }

    // 500 deaths in one tick, five times: the pool stays at its cap, replaces the oldest, and the next frames allocate nothing.
    private async Task DeathStorm(ulong seed)
    {
        DeathMarkers m = _combat.Markers;
        var storm = new DeathEvent[500];
        NavGrid g = W.NavGrid;
        int unitType = _data.FindUnit("malazan_heavy_infantry"), tent = _data.FindBuilding("whirlwind_tent");
        long t0 = W.TickNumber;
        for (int round = 0; round < 5; round++)
        {
            for (int k = 0; k < storm.Length; k++)
            {
                int c = (k * 37 + round * 101) % (g.Width * g.Height);
                storm[k] = new DeathEvent(new EntityHandle(k, 1), k % 25 == 0, k % 25 == 0 ? tent : unitType, k % 2, 1 - k % 2, g.CellCenter(c % g.Width, c / g.Width));
            }
            _combat.AddDeaths(W, storm, W.TickNumber);
            Check(m.Count <= m.Capacity, $"seed {seed}: storm {round}: {m.Count} markers over the cap {m.Capacity}");
            _sim.Tick();
            await Frame();
        }
        Check(m.Count == m.Capacity && m.Replaced == 500, $"seed {seed}: storm left {m.Count} markers, {m.Replaced} replaced");
        int drawn = 0;
        for (int i = 0; i < m.Capacity; i++) if (_combat.MarkerShown(i) != 0) drawn++;
        Check(drawn == m.Capacity, $"seed {seed}: {drawn} storm markers drawn");
        _combat.Sync(W, 0.5f);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int f = 0; f < 120; f++)
        {
            if (f % 3 == 0) _sim.Tick();
            _combat.Sync(W, f % 3 / 3f);
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(bytes == 0, $"seed {seed}: 120 frames after the storm allocated {bytes} bytes");
        // Units' markers go at 10 s, the rubble at 20 s, all of it.
        while (W.TickNumber < t0 + 5 + DeathMarkers.BuildingLifetimeTicks) _sim.Tick();
        await Frame();
        await Frame();
        drawn = 0;
        for (int i = 0; i < m.Capacity; i++) if (_combat.MarkerShown(i) != 0) drawn++;
        Check(m.Count == 0 && drawn == 0, $"seed {seed}: {m.Count} storm markers, {drawn} drawn after 20 s");
        GD.Print($"seed {seed}: death storm 5 x 500 -> {m.Capacity} markers (cap), 0 bytes over 120 frames after, all gone after 20 s");
    }

    // The runner ticks at 8x from frame time, several ticks a frame: every death still leaves a marker (SimRunner.Ticked).
    private async Task RealFrames()
    {
        StartMatch(1, runnerTicks: true);
        (System.Numerics.Vector2 goal0, System.Numerics.Vector2 goal1) = Stage();
        for (int f = 0; f < 10 && U.Count < 2 * PerSide; f++) await Frame();
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i]) _sim.Enqueue(Command.AttackMove(U.Owner[i], new EntityHandle(i, U.Generation[i]), U.Owner[i] == 0 ? goal0 : goal1));
        int maxTicksPerFrame = 0, frames = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (W.Losses[0] + W.Losses[1] < 15 && watch.Elapsed.TotalSeconds < 120)
        {
            int before = W.TickNumber;
            await Frame();
            frames++;
            maxTicksPerFrame = Math.Max(maxTicksPerFrame, W.TickNumber - before);
        }
        int deaths = W.Losses[0] + W.Losses[1];
        GD.Print($"8x: {deaths} deaths over {W.TickNumber} ticks in {frames} frames (up to {maxTicksPerFrame} ticks a frame), {_combat.Markers.Added} markers");
        Check(deaths >= 15, $"8x: only {deaths} deaths in 120 s");
        Check(_combat.Markers.Added == deaths, $"8x: {deaths} deaths, {_combat.Markers.Added} markers (a death missed between frames?)");
        Check(maxTicksPerFrame > 1, $"8x: never more than {maxTicksPerFrame} tick a frame, the row proves nothing");
        await EndMatch();
    }

    private async Task Shot(string name, bool overlay = false)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        // The F12 layers (nav grid, arrows) off for the picture of the ground, back on after; the "f12" shot keeps them to
        // show the label's layout (M4-V2, BUG-0160).
        _overlay.SetEnabled(overlay);
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = $"{_shots}/combat-{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        _overlay.SetEnabled(true);
        await Frame();
        await Frame();
        GD.Print($"screenshot {path}");
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
