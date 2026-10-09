using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// QA M4-V1 (2026-10-08-0313) on the real Match scene, the attacks the developer's <c>CombatViewTest</c> doesn't make:
/// (1) a unit selected and killed before the view saw a frame (selection, rings, panel, view, bar, flash, corpse, counts);
/// (2) its slot re-used by a new unit before the next frame (no inherited bar / flash / corpse move; view at the new spot),
/// and a re-used slot hurt before its first frame (bar shown); (3) a 60 v 60 brawl ticked by the runner itself at 8x, then
/// 1x, then stopped (pause): bars, counts and markers checked after every real frame, every death a marker, markers kept
/// while stopped, flashes fade in view time; (4) a Barracks with three Heavy Infantry queued killed by Raiders while the
/// runner ticks: on the death frame the rubble is drawn, the view gone, and the resource bar already shows the refund;
/// (5) a real 500 v 500 one-hit storm (the most deaths the sim gives in one tick), the combat and unit views' Sync time and
/// bytes per frame, and a 2,000-unit all-hurt frame time.
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaV5Test.tscn</c>; prints "QA M4-V1 TEST PASS".</remarks>
public partial class QaV5Test : Node
{
    private static readonly MethodInfo DamageMethod = typeof(BuildingStore).GetMethod("Damage", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private Match _match = null!;
    private SimRunner _runner = null!;
    private Simulation _sim = null!;
    private UnitViews _units = null!;
    private BuildingViews _buildings = null!;
    private CombatViews _combat = null!;
    private ResourceBar _bar = null!;
    private SelectionPanel _panel = null!;
    private SelectionController _sel = null!;
    private SelectionRings _rings = null!;
    private int _hi, _raider;

    private World W => _sim.World;
    private UnitStore U => _sim.World.Units;
    private BuildingStore B => _sim.World.Buildings;
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
            _hi = _data.FindUnit("malazan_heavy_infantry");
            _raider = _data.FindUnit("whirlwind_raider");
            await SelectedDiesAndSlotReuse();
            await RunnerBrawl();
            await BarracksWithQueueDies();
            await Storm();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures.Take(40)) GD.Print($"QA M4-V1 TEST FAIL: {f}");
        if (_failures.Count > 40) GD.Print($"QA M4-V1 TEST FAIL: ... {_failures.Count - 40} more");
        if (_failures.Count == 0) GD.Print("QA M4-V1 TEST PASS");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    private void StartMatch(bool runnerTicks, int units = 0, double speed = 1)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        var args = new List<string> { "--seed", "3", "--units", units.ToString(CultureInfo.InvariantCulture), "--no-bases", "--mute", "--zoom", "30",
            "--speed", speed.ToString(CultureInfo.InvariantCulture) };
        _match.Start(_data, LaunchOptions.Parse(args.ToArray()));
        _runner = _match.GetNode<SimRunner>("SimRunner");
        if (!runnerTicks) _runner.ProcessMode = ProcessModeEnum.Disabled;
        _sim = _runner.Simulation!;
        _units = _match.GetNode<UnitViews>("World3D/UnitViews");
        _buildings = _match.GetNode<BuildingViews>("World3D/BuildingViews");
        _combat = _match.GetNode<CombatViews>("World3D/CombatViews");
        _bar = _match.GetNode<ResourceBar>("Hud/ResourceBar");
        _panel = _match.GetNode<SelectionPanel>("Hud/SelectionPanel");
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _rings = _match.GetNode<SelectionRings>("World3D/SelectionRings");
        _match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
    }

    private async Task EndMatch()
    {
        RemoveChild(_match);
        _match.QueueFree();
        await Frame();
    }

    private System.Numerics.Vector2 Center(out int cx, out int cy)
    {
        int c = FlowField.NearestPassable(G, G.Height / 2 * G.Width + G.Width / 2);
        cx = c % G.Width;
        cy = c / G.Width;
        return G.CellCenter(cx, cy);
    }

    private EntityHandle NewestOf(int player, HashSet<long> before)
    {
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && U.Owner[i] == player && !before.Contains(Key(i, U.Generation[i]))) return new EntityHandle(i, U.Generation[i]);
        return default;
    }

    private static long Key(int i, int gen) => ((long)i << 32) | (uint)gen;

    private HashSet<long> LiveKeys()
    {
        var s = new HashSet<long>();
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i]) s.Add(Key(i, U.Generation[i]));
        return s;
    }

    private bool BarOn(int slot)
    {
        for (int k = 0; k < _combat.ShownBars; k++) if (_combat.BarSlot(k) == slot) return true;
        return false;
    }

    private int HurtAlive()
    {
        int n = 0;
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && U.Hp[i] < _data.Units[U.TypeId[i]].Hp) n++;
        return n;
    }

    // ---- (1) + (2) ----

    private async Task SelectedDiesAndSlotReuse()
    {
        StartMatch(runnerTicks: false);
        System.Numerics.Vector2 spot = Center(out _, out _);
        var before = LiveKeys();
        _sim.Enqueue(Command.SpawnUnit(0, _hi, spot));
        _sim.Tick();
        _sim.Tick();
        EntityHandle victim = NewestOf(0, before);
        Check(victim != default, "phase 1: no Heavy Infantry spawned");
        for (int k = 0; k < 4; k++)
            _sim.Enqueue(Command.SpawnUnit(1, _raider, spot + new System.Numerics.Vector2(k % 2 == 0 ? 1.6f : -1.6f, k < 2 ? 0.8f : -0.8f)));
        _sim.Tick();
        U.Hp[victim.Index] = 1; // staging: one hit kills it (test write between ticks)
        await Frame();
        await Frame();
        // Selected right before every tick, with no frame between the selection and the tick, until the tick that kills it.
        int ticks = 0;
        while (U.IsAlive(victim) && ticks < 400)
        {
            await Frame();
            _sel.SelectOnly(victim);
            Check(_sel.Selection.Count == 1, "phase 1: SelectOnly didn't select the victim");
            _sim.Tick();
            ticks++;
        }
        if (victim == default) { await EndMatch(); return; }
        if (!Check(!U.IsAlive(victim), $"phase 1: the 1-hp unit survived {ticks} ticks among 4 Raiders")) { await EndMatch(); return; }
        System.Numerics.Vector2 deathPos = W.Deaths.ToArray().First(d => d.Victim == victim).Position;
        await Frame();
        Check(_sel.Selection.Count == 0, $"phase 1: selection still counts {_sel.Selection.Count} after the selected unit died");
        Check(_rings.ShownCount == 0, $"phase 1: {_rings.ShownCount} selection rings drawn for a dead selection");
        Check(_panel.ShownKind == 0, $"phase 1: panel kind {_panel.ShownKind} for a dead selection (want none)");
        Check(!_units.ViewOf(victim.Index)!.Visible, "phase 1: dead unit's view still visible");
        Check(!BarOn(victim.Index), "phase 1: dead unit still has an hp bar");
        Check(!_units.Flash.IsLit(victim.Index) && !_units.ShownLit(victim.Index), "phase 1: dead unit's slot still lit");
        Check(_combat.Markers.Count == 1 && _combat.Markers.Position[0] == deathPos && _combat.MarkerShown(0) == 1, $"phase 1: markers {_combat.Markers.Count} at {_combat.Markers.Position[0]}, want 1 corpse at {deathPos}");
        Check(_bar.ShownLosses == 1 && W.Losses[0] == 1, $"phase 1: bar shows {_bar.ShownLosses} losses, sim {W.Losses[0]}");
        GD.Print($"phase 1: selected unit died on tick {W.TickNumber} ({ticks} ticks): selection 0, rings 0, panel none, view hidden, corpse at {deathPos}");

        // (2) The freed slot re-used by a spawn far away on the very next tick, before any frame in between.
        int farCell = FlowField.NearestPassable(G, (G.Height / 2) * G.Width + G.Width / 2 - 20);
        System.Numerics.Vector2 far = G.CellCenter(farCell % G.Width, farCell / G.Width);
        before = LiveKeys();
        _sim.Enqueue(Command.SpawnUnit(0, _hi, far));
        _sim.Tick();
        _sim.Tick();
        EntityHandle reborn = NewestOf(0, before);
        bool reused = reborn.Index == victim.Index;
        Check(reused, $"phase 2: the spawn took slot {reborn.Index}, not the freed {victim.Index} (LIFO free list expected; the row proves nothing)");
        await Frame();
        await Frame();
        MeshInstance3D view = _units.ViewOf(reborn.Index)!;
        Vector3 o = view.GlobalTransform.Origin;
        Check(view.Visible, "phase 2: re-used slot's view hidden");
        Check(System.Numerics.Vector2.Distance(new System.Numerics.Vector2(o.X, o.Z), far) < 1.5f, $"phase 2: re-used slot drawn at {o}, spawned at {far} (old unit died at {deathPos})");
        Check(!BarOn(reborn.Index) && U.Hp[reborn.Index] == _data.Units[_hi].Hp, $"phase 2: new full-hp unit has a bar ({U.Hp[reborn.Index]})");
        Check(!_units.Flash.IsLit(reborn.Index) && view.MaterialOverlay != _units.FlashMaterial, "phase 2: new unit inherited a flash");
        Check(_combat.Markers.Count == 1 && _combat.Markers.Position[0] == deathPos, "phase 2: the corpse moved or another appeared with the new unit");
        Check(_sel.Selection.Count == 0, "phase 2: the new unit in the dead selected unit's slot became selected");

        // A re-used slot hurt before its first frame: spawn into the fight, tick without frames until hurt, then one frame.
        U.Hp[reborn.Index] = 1;
        while (U.IsAlive(reborn) && W.TickNumber < 5000)
        {
            _sim.Enqueue(Command.Move(0, reborn, spot));
            for (int k = 0; k < 20 && U.IsAlive(reborn); k++) _sim.Tick();
            await Frame();
        }
        Check(!U.IsAlive(reborn), "phase 2: the second 1-hp unit never died");
        before = LiveKeys();
        _sim.Enqueue(Command.SpawnUnit(0, _hi, spot));
        int hurtTicks = 0;
        EntityHandle third = default;
        for (; hurtTicks < 400; hurtTicks++)
        {
            _sim.Tick();
            if (third == default) third = NewestOf(0, before);
            if (third != default && (!U.IsAlive(third) || U.Hp[third.Index] < _data.Units[_hi].Hp)) break;
        }
        if (third != default && U.IsAlive(third) && U.Hp[third.Index] < _data.Units[_hi].Hp)
        {
            await Frame();
            Check(BarOn(third.Index), $"phase 2: unit hurt before its first frame (hp {U.Hp[third.Index]}) has no bar");
            // BUG-0160 item 2 (M4-V2): a unit first seen below its type's hp flashes once.
            Check(_units.Flash.IsLit(third.Index) && _units.ShownLit(third.Index), $"phase 2: unit hurt before its first frame (hp {U.Hp[third.Index]}) did not flash");
            GD.Print($"phase 2: slot {victim.Index} re-used (reused {reused}); spawned-and-hurt unit slot {third.Index}: bar {BarOn(third.Index)}, flash lit {_units.Flash.IsLit(third.Index)} (first sight below max hp: one flash)");
        }
        else GD.Print($"phase 2: spawned-and-hurt row skipped (third {third}, ticks {hurtTicks})");
        await EndMatch();
    }

    // ---- (3) ----

    private void Stage(int perSide, out System.Numerics.Vector2 goal0, out System.Numerics.Vector2 goal1)
    {
        System.Numerics.Vector2 c = Center(out int cx, out int cy);
        FlowField field = FlowField.Build(G, cy * G.Width + cx);
        for (int p = 0; p < 2; p++)
        {
            int dir = p == 0 ? -1 : 1, placed = 0;
            for (int ring = 0; ring < 40 && placed < perSide; ring++)
                for (int cell = 0; cell < G.Width * G.Height && placed < perSide; cell++)
                {
                    int x = cell % G.Width, y = cell / G.Width, dx = (x - cx) * dir;
                    if (dx < 3 || dx > 8 || Math.Abs(y - cy) != ring || !(field.CostAt(cell) <= 40f)) continue;
                    _sim.Enqueue(Command.SpawnUnit(p, p == 0 ? _hi : _raider, G.CellCenter(x, y)));
                    placed++;
                }
            Check(placed == perSide, $"stage: {placed} of {perSide} for player {p}");
        }
        goal0 = c + new System.Numerics.Vector2(20f, 0f);
        goal1 = c - new System.Numerics.Vector2(20f, 0f);
    }

    private void OrderAll(System.Numerics.Vector2 goal0, System.Numerics.Vector2 goal1)
    {
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i]) _sim.Enqueue(Command.AttackMove(U.Owner[i], new EntityHandle(i, U.Generation[i]), U.Owner[i] == 0 ? goal0 : goal1));
    }

    private int _frameChecks, _frameFails;

    // After a real frame (runner first in the tree, so every view ran on this frame's ticks).
    private void CheckFrame(string what)
    {
        _frameChecks++;
        int k = 0;
        bool ok = true;
        for (int i = 0; i < U.Capacity && ok; i++)
        {
            if (!U.Alive[i] || U.Hp[i] >= _data.Units[U.TypeId[i]].Hp) continue;
            if (k >= _combat.ShownBars || _combat.BarSlot(k) != i) ok = Check(false, $"{what} tick {W.TickNumber}: hurt unit {i} has no bar");
            else if (Math.Abs(_combat.BarFill(k) - (float)U.Hp[i] / _data.Units[U.TypeId[i]].Hp) > 1e-5f) ok = Check(false, $"{what} tick {W.TickNumber}: unit {i} fill {_combat.BarFill(k)}");
            k++;
        }
        if (ok && k != _combat.ShownBars) ok = Check(false, $"{what} tick {W.TickNumber}: {_combat.ShownBars} bars for {k} hurt units");
        long deaths = W.Losses[0] + W.Losses[1];
        if (_combat.Markers.Added != deaths) ok = Check(false, $"{what} tick {W.TickNumber}: {deaths} deaths, {_combat.Markers.Added} markers added");
        if (_bar.ShownKills != W.Kills[0] || _bar.ShownLosses != W.Losses[0]) ok = Check(false, $"{what} tick {W.TickNumber}: bar {_bar.ShownKills}/{_bar.ShownLosses}, sim {W.Kills[0]}/{W.Losses[0]}");
        DeathMarkers m = _combat.Markers;
        for (int i = 0; i < m.Capacity && ok; i++)
        {
            if (!m.Active[i]) continue;
            long born = m.ExpiresAt[i] - DeathMarkers.LifetimeTicks(m.IsBuilding[i]);
            if (W.TickNumber >= m.ExpiresAt[i] || W.TickNumber < born) ok = Check(false, $"{what} tick {W.TickNumber}: marker {i} alive outside [{born}, {m.ExpiresAt[i]})");
        }
        for (int i = 0; i < U.Capacity && ok; i++)
            if (!U.Alive[i] && _units.Flash.IsLit(i)) ok = Check(false, $"{what} tick {W.TickNumber}: dead slot {i} lit");
        if (!ok) _frameFails++;
    }

    private async Task RunnerBrawl()
    {
        StartMatch(runnerTicks: true, speed: 8);
        Stage(60, out var goal0, out var goal1);
        for (int f = 0; f < 20 && U.Count < 120; f++) await Frame();
        Check(U.Count == 120, $"brawl: {U.Count} units after the spawns");
        OrderAll(goal0, goal1);
        int maxPerFrame = 0, frames8 = 0, frames1 = 0;
        var watch = Stopwatch.StartNew();
        // 8x until a third of them died.
        while (W.Losses[0] + W.Losses[1] < 40 && watch.Elapsed.TotalSeconds < 120)
        {
            int t0 = W.TickNumber;
            await Frame();
            frames8++;
            maxPerFrame = Math.Max(maxPerFrame, W.TickNumber - t0);
            CheckFrame("8x");
        }
        long at8 = W.Losses[0] + W.Losses[1];
        // 1x for 300 frames.
        _runner.GameSpeed = 1;
        for (int f = 0; f < 300; f++)
        {
            await Frame();
            frames1++;
            CheckFrame("1x");
        }
        long at1 = W.Losses[0] + W.Losses[1];
        // Stopped (pause): no ticks for 60 frames; markers stay, bars stay, flashes fade on view time.
        _runner.ProcessMode = ProcessModeEnum.Disabled;
        await Frame();
        int tick = W.TickNumber, markers = _combat.Markers.Count, bars = _combat.ShownBars;
        long added = _combat.Markers.Added;
        var wallStop = Stopwatch.StartNew();
        for (int f = 0; f < 60; f++) { await Frame(); CheckFrame("paused"); }
        double stoppedSec = wallStop.Elapsed.TotalSeconds;
        Check(W.TickNumber == tick && _combat.Markers.Count == markers && _combat.Markers.Added == added && _combat.ShownBars == bars,
            $"paused: tick {tick}->{W.TickNumber}, markers {markers}->{_combat.Markers.Count}, bars {bars}->{_combat.ShownBars}");
        if (stoppedSec > 0.3) Check(_units.Flash.LitCount == 0, $"paused: {_units.Flash.LitCount} units still lit after {stoppedSec:0.00} s of view time without a tick");
        GD.Print($"brawl: 8x {frames8} frames (up to {maxPerFrame} ticks a frame), {at8} deaths; 1x {frames1} frames, {at1} deaths; paused 60 frames ({stoppedSec:0.00} s): " +
            $"markers {markers} kept, lit {_units.Flash.LitCount}; {_frameChecks} frame checks, {_frameFails} failing frames");
        Check(maxPerFrame > 1, $"brawl: at 8x never more than {maxPerFrame} tick a frame");
        Check(at8 >= 40, $"brawl: only {at8} deaths at 8x in 120 s");
        await EndMatch();
    }

    // ---- (4) ----

    private async Task BarracksWithQueueDies()
    {
        StartMatch(runnerTicks: true, speed: 1);
        System.Numerics.Vector2 c = Center(out int cx, out int cy);
        int barracks = _data.FindBuilding("malazan_barracks");
        int anchor = -1;
        for (int r = 4; r < 30 && anchor < 0; r++)
            for (int dy = -r; dy <= r && anchor < 0; dy++)
            {
                int cell = (cy + dy) * G.Width + cx + r;
                if (B.Fits(barracks, cell)) anchor = cell;
            }
        if (!Check(anchor >= 0, "barracks: no spot")) { await EndMatch(); return; }
        System.Numerics.Vector2 at = G.CellCenter(anchor % G.Width, anchor / G.Width);
        _sim.Enqueue(Command.SpawnBuilding(0, barracks, at));
        int tb = W.TickNumber;
        while (W.TickNumber < tb + 3) await Frame();
        int slot = -1;
        for (int i = 0; i < B.Capacity; i++) if (B.Alive[i] && B.Owner[i] == 0 && B.TypeId[i] == barracks) slot = i;
        if (!Check(slot >= 0, "barracks: not spawned")) { await EndMatch(); return; }
        System.Numerics.Vector2 centre = StartBase.FootprintCenter(G, _data.Buildings[barracks], anchor);
        int gold0 = W.Gold[0], wood0 = W.Wood[0];
        for (int k = 0; k < 3; k++) _sim.Enqueue(Command.Train(0, centre, _hi));
        tb = W.TickNumber;
        while (W.TickNumber < tb + 3) await Frame();
        int queued = B.QueueCount[slot];
        int goldQueued = W.Gold[0];
        Check(queued == 3 && goldQueued == gold0 - 3 * _data.Units[_hi].CostGold, $"barracks: queue {queued}, gold {gold0}->{goldQueued}");
        DamageMethod.Invoke(B, new object[] { B.HandleOf(slot), B.Hp[slot] - 30 });
        for (int k = 0; k < 6; k++)
            _sim.Enqueue(Command.SpawnUnit(1, _raider, centre + new System.Numerics.Vector2(0f, (k - 2.5f) * 1.5f) + new System.Numerics.Vector2(_data.Buildings[barracks].FootprintWidth + 2f, 0f)));
        tb = W.TickNumber;
        while (W.TickNumber < tb + 3) await Frame();
        OrderAll(centre, centre);
        int frames = 0, goldPrev = W.Gold[0];
        bool died = false;
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed.TotalSeconds < 60)
        {
            goldPrev = W.Gold[0];
            int queuePrev = B.Alive[slot] ? B.QueueCount[slot] : 0;
            await Frame();
            frames++;
            if (B.Alive[slot]) continue;
            died = true;
            int marker = -1;
            for (int i = 0; i < _combat.Markers.Capacity; i++)
                if (_combat.Markers.Active[i] && _combat.Markers.IsBuilding[i]) marker = i;
            Check(marker >= 0 && _combat.MarkerShown(marker) == 2, $"barracks: no rubble drawn on the death frame (marker {marker})");
            if (marker >= 0) Check(System.Numerics.Vector2.Distance(_combat.Markers.Position[marker], centre) < 0.01f, $"barracks: rubble at {_combat.Markers.Position[marker]}, footprint centre {centre}");
            Check(!_buildings.IsShown(slot), "barracks: dead building's view still shown on the death frame");
            Check(_bar.ShownGold == W.Gold[0] && _bar.ShownWood == W.Wood[0], $"barracks: bar shows {_bar.ShownGold}/{_bar.ShownWood}, sim {W.Gold[0]}/{W.Wood[0]} on the death frame");
            Check(W.Gold[0] >= goldPrev + queuePrev * _data.Units[_hi].CostGold, $"barracks: gold {goldPrev}->{W.Gold[0]} after a queue of {queuePrev} was refunded");
            Check(_bar.ShownLosses == W.Losses[0] && W.Losses[0] >= 1, $"barracks: bar losses {_bar.ShownLosses}, sim {W.Losses[0]}");
            GD.Print($"barracks: died after {frames} frames with {queuePrev} queued; gold {goldPrev} -> {W.Gold[0]} shown {_bar.ShownGold} the same frame; rubble at {centre}");
            break;
        }
        Check(died, $"barracks: still alive after {frames} frames (hp {B.Hp[slot]})");
        await EndMatch();
    }

    // ---- (5) ----

    private async Task Storm()
    {
        StartMatch(runnerTicks: false, units: 0);
        _units.ProcessMode = ProcessModeEnum.Disabled;
        _combat.ProcessMode = ProcessModeEnum.Disabled; // the test calls the two Syncs itself, timed
        System.Numerics.Vector2 c = Center(out int cx, out int cy);
        // 500 HI and 500 Raiders on alternating cells of a block around the centre.
        int placed0 = 0, placed1 = 0;
        for (int r = 0; r < 40 && (placed0 < 500 || placed1 < 500); r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    int x = cx + dx, y = cy + dy;
                    if (x < 1 || y < 1 || x >= G.Width - 1 || y >= G.Height - 1 || !G.IsPassable(x, y)) continue;
                    int p = ((x + y) & 1) == 0 ? 0 : 1;
                    if (p == 0 && placed0 < 500) { _sim.Enqueue(Command.SpawnUnit(0, _hi, G.CellCenter(x, y))); placed0++; }
                    else if (p == 1 && placed1 < 500) { _sim.Enqueue(Command.SpawnUnit(1, _raider, G.CellCenter(x, y))); placed1++; }
                }
        _sim.Tick();
        _sim.Tick();
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && U.Owner[i] == 1) U.Hp[i] = 1;
        GD.Print($"storm: {U.Count} units spawned ({placed0} v {placed1})");
        float dt = 1f / 60f;
        _units.Sync(W, 1f, dt);
        _combat.Sync(W, 1f);
        int maxDeaths = 0, maxTick = 0;
        double worstMs = 0, totalMs = 0;
        var sw = new Stopwatch(); // made once: a Stopwatch is a 40 B object, not to be counted against the views
        long bytesAfterWarm = 0, combatBytes = 0;
        int frames = 0, allocLog = 0;
        for (int t = 0; t < 600 && W.Losses[1] < placed1; t++)
        {
            _sim.Tick();
            int d = W.Deaths.Length;
            if (d > maxDeaths) { maxDeaths = d; maxTick = W.TickNumber; }
            long before = GC.GetAllocatedBytesForCurrentThread();
            sw.Restart();
            _combat.Sync(W, 0f);
            long mid = GC.GetAllocatedBytesForCurrentThread();
            _units.Sync(W, 0f, dt);
            sw.Stop();
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            if (t >= 2) { bytesAfterWarm += bytes; combatBytes += mid - before; if (bytes > 0 && allocLog < 12) { allocLog++; GD.Print($"storm alloc tick {W.TickNumber}: combat {mid - before} B, units {bytes - (mid - before)} B, deaths {d}, hits {_units.Flash.HitCount}, lit {_units.Flash.LitCount}"); } }
            worstMs = Math.Max(worstMs, sw.Elapsed.TotalMilliseconds);
            totalMs += sw.Elapsed.TotalMilliseconds;
            frames++;
        }
        Check(_combat.Markers.Added == W.Losses[0] + W.Losses[1], $"storm: {W.Losses[0] + W.Losses[1]} deaths, {_combat.Markers.Added} markers");
        Check(bytesAfterWarm == 0, $"storm: combat + unit views allocated {bytesAfterWarm} bytes over {frames - 2} frames");
        GD.Print($"storm: most deaths in one tick {maxDeaths} (tick {maxTick}); {W.Losses[1]} raiders dead in {frames} ticks; combat+unit Sync worst {worstMs:0.000} ms, mean {totalMs / Math.Max(1, frames):0.000} ms; {bytesAfterWarm} B after warm-up ({combatBytes} B in CombatViews.Sync)");

        // Fill the pool to its cap with a synthetic 500-death tick on top, then time a frame with every live unit hurt.
        var storm = new Rts.Sim.Combat.DeathEvent[500];
        for (int round = 0; round < 5; round++)
        {
            for (int k = 0; k < storm.Length; k++)
            {
                int cell = (k * 53 + round * 17) % (G.Width * G.Height);
                storm[k] = new Rts.Sim.Combat.DeathEvent(new EntityHandle(k, 1), k % 20 == 0, k % 20 == 0 ? 0 : _hi, k % 2, 1 - k % 2, G.CellCenter(cell % G.Width, cell / G.Width));
            }
            sw.Restart();
            _combat.AddDeaths(W, storm, W.TickNumber);
            sw.Stop();
            GD.Print($"storm: synthetic 500 deaths added in {sw.Elapsed.TotalMilliseconds:0.000} ms (pool {_combat.Markers.Count}/{_combat.Markers.Capacity})");
        }
        Check(_combat.Markers.Count == _combat.Markers.Capacity, $"storm: pool {_combat.Markers.Count}, cap {_combat.Markers.Capacity}");
        await EndMatch();

        // 1,000 a side, every unit hurt and hit again each frame: bars and flashes for 2,000.
        StartMatch(runnerTicks: false, units: 1000);
        _units.ProcessMode = ProcessModeEnum.Disabled;
        _combat.ProcessMode = ProcessModeEnum.Disabled;
        _sim.Tick();
        _sim.Tick();
        int live = U.Count;
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i]) U.Hp[i] = _data.Units[U.TypeId[i]].Hp - 1;
        _units.Sync(W, 0f, dt);
        _combat.Sync(W, 0f);
        double worst = 0, sum = 0;
        long bytes2 = 0;
        for (int f = 0; f < 60; f++)
        {
            for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && U.Hp[i] > 1) U.Hp[i]--; // every unit "hit" each frame (test write, no tick)
            long b0 = GC.GetAllocatedBytesForCurrentThread();
            sw.Restart();
            _combat.Sync(W, 0.5f);
            long b1 = GC.GetAllocatedBytesForCurrentThread();
            _units.Sync(W, 0.5f, dt);
            sw.Stop();
            long fb = GC.GetAllocatedBytesForCurrentThread() - b0;
            bytes2 += fb;
            if (fb > 0 && f < 8) GD.Print($"2,000 hurt alloc frame {f}: combat {b1 - b0} B, units {fb - (b1 - b0)} B, lit {_units.Flash.LitCount}, hits {_units.Flash.HitCount}");
            if (f == 0) Check(_units.Flash.LitCount == live, $"2,000 hurt: first hit frame lit {_units.Flash.LitCount} of {live}");
            worst = Math.Max(worst, sw.Elapsed.TotalMilliseconds);
            sum += sw.Elapsed.TotalMilliseconds;
        }
        Check(_combat.ShownBars == live, $"2,000 hurt: {_combat.ShownBars} bars, {live} live");
        Check(bytes2 == 0, $"2,000 hurt: {bytes2} bytes over 60 frames");
        GD.Print($"2,000 hurt: {live} units, {_combat.ShownBars} bars, {_units.Flash.LitCount} lit; combat+unit Sync worst {worst:0.000} ms, mean {sum / 60:0.000} ms; {bytes2} B");
        await EndMatch();
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
