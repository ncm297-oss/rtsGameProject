using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Abilities;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.Replays;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// QA for M4-V6c (session 2026-10-10-1015) on the real Match scene. (1) Leaks: an enemy Sandstorm on unexplored ground
/// and one on explored-only ground are never drawn while an own storm on unexplored ground always is; the multimesh's
/// visible count always equals the shown discs. (2) A seen enemy storm and an own one expiring: each disc gone the frame
/// its slot frees (not at the next fog update); the fade's first frame 0.35 x 19/20 and last frame 0. (3) Scale: 64 live
/// zones (16 blocking) with 2,000 units at 8x speed through the runner: 0 B in <c>AbilityViews._Process</c> and its frame
/// cost. (4) Blinded + Slowed + Burning on one Raider and one Heavy Infantry: three distinct colours, side by side;
/// windowed with <c>-- --shots &lt;dir&gt;</c> it saves <c>qa-v6c-markers-zoomN.png</c> at zoom 20, 30 and 60.
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaV6cTest.tscn</c>; prints "QA V6C TEST PASS" and exits 0, or each failure and exits 1.</remarks>
public partial class QaV6cTest : Node
{
    private static readonly MethodInfo CreateZone = typeof(ZoneSystem).GetMethod("Create", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo ApplyStatus = typeof(StatusStore).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private Match _match = null!;
    private SimRunner _runner = null!;
    private Simulation _sim = null!;
    private AbilityViews _views = null!;
    private RtsCamera _camera = null!;
    private string? _shots;
    private AbilityDef _storm = null!;

    private World W => _sim.World;
    private UnitStore U => _sim.World.Units;
    private NavGrid G => _sim.World.NavGrid;

    public override async void _Ready()
    {
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            string[] args = OS.GetCmdlineUserArgs();
            int at = Array.IndexOf(args, "--shots");
            if (at >= 0 && at + 1 < args.Length) _shots = args[at + 1];
            await Frame();
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            _storm = _data.Abilities[_data.FindAbility("sandstorm")];
            await Leaks(1);
            await Leaks(6);
            await FreedAndFade(1);
            await Scale(6);
            await Markers(1);
            await CliffShot(1);
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures.Take(40)) GD.Print($"QA V6C TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA V6C TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private void StartMatch(ulong seed, params string[] extra)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _runner = _match.GetNode<SimRunner>("SimRunner");
        _runner.RecordCheckpointInterval = 1;
        var args = new List<string> { "--seed", seed.ToString(CultureInfo.InvariantCulture), "--mute", "--zoom", "30" };
        args.AddRange(extra);
        _match.Start(_data, LaunchOptions.Parse(args.ToArray()));
        _runner.ProcessMode = ProcessModeEnum.Disabled;
        _sim = _runner.Simulation!;
        _views = _match.GetNode<AbilityViews>("World3D/AbilityViews");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
    }

    private async Task EndMatch()
    {
        RemoveChild(_match);
        _match.QueueFree();
        await Frame();
    }

    private void Zone(int owner, AbilityDef def, System.Numerics.Vector2 at) => CreateZone.Invoke(null, new object[] { W, owner, def, at });

    // A Sandstorm copy: blocking or not; `quiet` drops its statuses (so a scale row measures the discs, not the markers).
    private static AbilityDef Dust(AbilityDef s, bool blocks = false, bool quiet = false)
    {
        var copy = (AbilityDef)typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(s, null)!;
        AbilityEffect fx = s.Effects[s.ZoneEffect] with { BlocksVision = blocks };
        if (quiet) fx = fx with { ZoneStatuses = System.Collections.Immutable.ImmutableArray<ZoneStatus>.Empty };
        typeof(AbilityDef).GetProperty(nameof(AbilityDef.Effects))!.SetValue(copy, s.Effects.SetItem(s.ZoneEffect, fx));
        return copy;
    }

    private bool AnyVisibleWithin(int slot)
    {
        ReadOnlySpan<byte> vis = W.Fog.Visibility(0);
        System.Numerics.Vector2 c = W.Zones.Center[slot];
        float reach = W.Zones.Radius(slot) + 2f;
        for (int cell = 0; cell < vis.Length; cell++)
            if (vis[cell] == 2 && System.Numerics.Vector2.Distance(G.CellCenter(cell % G.Width, cell / G.Width), c) <= reach + 1e-4f) return true;
        return false;
    }

    private bool Drawn(int slot)
    {
        for (int k = 0; k < _views.ShownZones; k++) if (_views.ZoneDiscAt(k).Slot == slot) return true;
        return false;
    }

    private int SlotAt(System.Numerics.Vector2 p, int owner)
    {
        for (int k = 0; k < W.Zones.Capacity; k++) if (W.Zones.Alive[k] && W.Zones.Owner[k] == owner && W.Zones.Center[k] == p) return k;
        return -1;
    }

    // The first passable cell (scanning a coarse grid) whose own fog byte is `state` and whose whole radius + 4 m box is
    // in that state or darker for player 0 (so neither a rim nor an inside cell is visible).
    private int CellWhere(byte state)
    {
        ReadOnlySpan<byte> vis = W.Fog.Visibility(0);
        int reach = (int)MathF.Ceiling((_storm.Radius + 4f) / 2f);
        for (int y = reach + 4; y < G.Height - reach - 4; y += 3)
            for (int x = reach + 4; x < G.Width - reach - 4; x += 3)
            {
                if (!G.IsPassable(x, y) || vis[y * G.Width + x] != state) continue;
                bool clean = true;
                for (int dy = -reach; dy <= reach && clean; dy++)
                    for (int dx = -reach; dx <= reach && clean; dx++)
                        clean = vis[(y + dy) * G.Width + x + dx] < 2 && vis[(y + dy) * G.Width + x + dx] <= Math.Max(state, (byte)0);
                if (clean) return y * G.Width + x;
            }
        return -1;
    }

    // ---- (1) leaks ----

    private async Task Leaks(ulong seed)
    {
        StartMatch(seed, "--units", "30", "--no-combat", "--no-bases");
        for (int t = 0; t < 8; t++) _sim.Tick();
        Twin(seed, "leaks");
        int hiddenCell = CellWhere(0);
        if (!Check(hiddenCell >= 0, $"leaks seed {seed}: no unexplored area")) { await EndMatch(); return; }
        System.Numerics.Vector2 hidden = G.CellCenter(hiddenCell % G.Width, hiddenCell / G.Width);
        Zone(1, _storm, hidden);
        System.Numerics.Vector2 ownFar = hidden + new System.Numerics.Vector2(0f, 2f);
        Zone(0, _storm, ownFar);
        int enemySlot = SlotAt(hidden, 1), ownSlot = SlotAt(ownFar, 0);

        // An explored-only patch: an own Lancer sees ground then is removed; a storm cast there after the next update.
        int lancer = _data.FindUnit("malazan_wickan_lancer");
        int seenCell = -1;
        for (int i = 0; i < U.Capacity && seenCell < 0; i++)
            if (U.Alive[i] && U.Owner[i] == 0)
            {
                // 30 m off the army's first unit toward the map's centre.
                System.Numerics.Vector2 c = new(G.Width, G.Height);
                System.Numerics.Vector2 p = U.Position[i] + System.Numerics.Vector2.Normalize(c - U.Position[i]) * 34f;
                int cell = FlowField.NearestPassable(G, (int)(p.Y / 2f) * G.Width + (int)(p.X / 2f));
                if (cell >= 0 && W.Fog.Visibility(0)[cell] == 0) seenCell = cell;
            }
        int exploredSlot = -1;
        if (seenCell >= 0)
        {
            System.Numerics.Vector2 sp = G.CellCenter(seenCell % G.Width, seenCell / G.Width);
            if (SpawnOne(0, lancer, sp) is EntityHandle scout)
            {
                for (int t = 0; t < 6; t++) _sim.Tick();
                Check(W.Fog.Visibility(0)[seenCell] == 2, $"leaks seed {seed}: the scout's cell is not visible");
                U.Free(scout);
                for (int t = 0; t < 6; t++) _sim.Tick();
                Zone(1, _storm, sp);
                exploredSlot = SlotAt(sp, 1);
                Check(W.Fog.Visibility(0)[seenCell] == 1, $"leaks seed {seed}: the scouted cell is {W.Fog.Visibility(0)[seenCell]}, want explored");
            }
        }
        Check(exploredSlot >= 0, $"leaks seed {seed}: no explored-only storm staged");
        int leaked = 0, ownMissing = 0, meshMismatch = 0, exploredDark = 0;
        for (int t = 0; t < 120; t++)
        {
            _sim.Tick();
            _views.Sync(W, 0.5f, Vector2.Zero, false);
            // Independent of ZoneDiscs: a disc is a leak when no cell centre within radius + 2 m is visible to player 0.
            if (Drawn(enemySlot) && !AnyVisibleWithin(enemySlot)) leaked++;
            if (exploredSlot >= 0 && W.Zones.Alive[exploredSlot] && !AnyVisibleWithin(exploredSlot))
            {
                exploredDark++;
                if (Drawn(exploredSlot)) leaked++;
            }
            if (!Drawn(ownSlot)) ownMissing++;
            if (_views.ZoneNodesVisible != _views.ShownZones) meshMismatch++;
        }
        Check(_views.ZoneNodes == W.Zones.Capacity, $"leaks: zone nodes {_views.ZoneNodes}, store {W.Zones.Capacity}");
        GD.Print($"leaks seed {seed}: unexplored enemy storm slot {enemySlot}, explored-only slot {exploredSlot}, own slot {ownSlot}: explored-only dark {exploredDark} frames, leaked {leaked} frames, own missing {ownMissing}, mesh mismatch {meshMismatch}");
        Check(exploredDark >= 100, $"leaks seed {seed}: the explored-only storm was dark only {exploredDark} of 120 frames (staging failed)");
        Check(leaked == 0 && ownMissing == 0 && meshMismatch == 0, $"leaks seed {seed}: leaked {leaked}, own missing {ownMissing}, mesh mismatch {meshMismatch} of 120 frames");
        await EndMatch();
    }

    // ---- (2) freed between updates; the fade's first and last frame ----

    private async Task FreedAndFade(ulong seed)
    {
        StartMatch(seed, "--units", "30", "--no-combat", "--no-bases");
        for (int t = 0; t < 8; t++) _sim.Tick();
        System.Numerics.Vector2 army = Centroid(0);
        Zone(1, _storm, army);
        Zone(0, _storm, army + new System.Numerics.Vector2(10f, 0f));
        int e = SlotAt(army, 1), o = SlotAt(army + new System.Numerics.Vector2(10f, 0f), 0);
        int ticks = 0, lateEnemy = -1, lateOwn = -1, enemyDrawnFrames = 0;
        float firstFade = -1f, lastFrame = -1f;
        bool enemyWasAlive = true, ownWasAlive = true;
        while ((W.Zones.Alive[e] || W.Zones.Alive[o]) && ticks < 400)
        {
            _sim.Tick();
            ticks++;
            _views.Sync(W, 0.5f, Vector2.Zero, false);
            if (W.Zones.Alive[e] && Drawn(e)) enemyDrawnFrames++;
            if (W.Zones.Alive[o])
            {
                for (int k = 0; k < _views.ShownZones; k++)
                {
                    if (_views.ZoneDiscAt(k).Slot != o) continue;
                    int left = W.Zones.TicksRemaining[o];
                    if (left == ZoneDiscs.FadeTicks) firstFade = _views.ZoneDiscFill(k).A;
                    if (left == 1) lastFrame = _views.ZoneDiscFill(k).A;
                }
            }
            if (enemyWasAlive && !W.Zones.Alive[e]) { enemyWasAlive = false; if (Drawn(e)) lateEnemy = ticks; }
            if (ownWasAlive && !W.Zones.Alive[o]) { ownWasAlive = false; if (Drawn(o)) lateOwn = ticks; }
            if (_views.ZoneNodesVisible != _views.ShownZones || _views.ShownZones > W.Zones.Count)
                Check(false, $"freed: tick {ticks} mesh {_views.ZoneNodesVisible}, shown {_views.ShownZones}, live {W.Zones.Count}");
        }
        bool offUpdate = !Rts.Sim.Vision.VisionSystem.IsUpdateTick(_sim.TickNumber - 1);
        GD.Print($"freed seed {seed}: both storms over after {ticks} ticks (expiry {(offUpdate ? "off" : "on")} an update tick); enemy drawn {enemyDrawnFrames} frames; late frames enemy {lateEnemy}, own {lateOwn}; fade first {firstFade}, last {lastFrame}; shown now {_views.ShownZones}");
        Check(enemyDrawnFrames > 200, $"freed: the seen enemy storm drawn only {enemyDrawnFrames} frames");
        Check(lateEnemy < 0 && lateOwn < 0 && _views.ShownZones == 0 && _views.ZoneNodesVisible == 0, $"freed: drawn after its slot freed (enemy {lateEnemy}, own {lateOwn}), {_views.ShownZones} left");
        Check(Math.Abs(firstFade - 0.35f * 19f / 20f) < 1e-6f && lastFrame == 0f, $"fade: first {firstFade}, last {lastFrame}");
        await EndMatch();
    }

    // ---- (3) 64 zones, 2,000 units, 8x speed ----

    private async Task Scale(ulong seed)
    {
        StartMatch(seed, "--units", "1000", "--speed", "8");
        for (int t = 0; t < 4; t++) _sim.Tick();
        _runner.GameSpeed = 8.0;
        // The same frames with no zone first: the discs' share of the frame cost.
        _runner.ProcessMode = ProcessModeEnum.Inherit;
        var bare = new System.Diagnostics.Stopwatch();
        for (int f = 0; f < 10; f++) { _runner._Process(1.0 / 60.0); _views._Process(1.0 / 60.0); }
        for (int f = 0; f < 60; f++)
        {
            _runner._Process(1.0 / 60.0);
            bare.Start();
            _views._Process(1.0 / 60.0);
            bare.Stop();
        }
        AbilityDef dust = Dust(_storm, quiet: true), still = Dust(_storm, blocks: true, quiet: true);
        System.Numerics.Vector2 west = Centroid(0), east = Centroid(1);
        for (int k = 0; k < 64; k++)
        {
            System.Numerics.Vector2 baseAt = (k & 1) == 0 ? west : east;
            // Half over each army, spread on a grid; every fourth blocks vision.
            System.Numerics.Vector2 at = baseAt + new System.Numerics.Vector2((k / 2 % 8 - 4) * 9f, (k / 16 - 2) * 9f);
            Zone(k & 1, k % 4 == 0 ? still : dust, at); // no statuses: the cost measured is the discs'
        }
        int live = W.Zones.Count, blocking = W.Zones.BlockerCount;
        _runner.ProcessMode = ProcessModeEnum.Inherit;
        for (int f = 0; f < 10; f++) { _runner._Process(1.0 / 60.0); _views._Process(1.0 / 60.0); }
        var watch = new System.Diagnostics.Stopwatch();
        double worst = 0;
        long bytes = 0;
        int frames = 0, minShown = int.MaxValue, startTick = W.TickNumber;
        while (frames < 60)
        {
            _runner._Process(1.0 / 60.0);
            // The fog refresh first (cached per tick, so _Process below reuses it), then Collect alone: the split.
            _split.Restart();
            _views.Fog?.Refreshed(W);
            _split.Stop();
            _refreshMs += _split.Elapsed.TotalMilliseconds;
            _split.Restart();
            int nShown = ZoneDiscs.Collect(W.Zones, W.Fog, 0, true, _probe);
            _split.Stop();
            _collectMs += _split.Elapsed.TotalMilliseconds;
            _shownSum += nShown;
            long b0 = GC.GetAllocatedBytesForCurrentThread();
            watch.Restart();
            _views._Process(1.0 / 60.0);
            watch.Stop();
            bytes += GC.GetAllocatedBytesForCurrentThread() - b0;
            worst = Math.Max(worst, watch.Elapsed.TotalMilliseconds);
            minShown = Math.Min(minShown, _views.ShownZones);
            frames++;
            _total += watch.Elapsed.TotalMilliseconds;
        }
        int ticks = W.TickNumber - startTick;
        GD.Print($"scale seed {seed}: {U.Count} units, {live} zones ({blocking} blocking), still {W.Zones.Count}; {frames} frames at 8x = {ticks} ticks; AbilityViews._Process {_total / frames:0.000} ms avg (no zones: {bare.Elapsed.TotalMilliseconds / 60:0.000}), {worst:0.000} ms worst, {bytes} B; discs shown >= {minShown} (avg {_shownSum / (double)frames:0.0}); split per frame: fog refresh {_refreshMs / frames:0.000} ms, ZoneDiscs.Collect {_collectMs / frames:0.000} ms");
        Check(U.Count >= 1900 && live == 64 && W.Zones.Count == 64, $"scale: {U.Count} units, {live} staged, {W.Zones.Count} live at the end");
        Check(ticks >= 100, $"scale: only {ticks} ticks in {frames} frames at 8x");
        Check(bytes == 0, $"scale: AbilityViews._Process allocated {bytes} B over {frames} frames with 64 zones");
        Check(minShown >= 32, $"scale: only {minShown} discs shown (32 own)");
        _runner.ProcessMode = ProcessModeEnum.Disabled;
        await EndMatch();
    }

    private double _total, _refreshMs, _collectMs;
    private int _shownSum;
    private readonly System.Diagnostics.Stopwatch _split = new();
    private readonly ZoneDisc[] _probe = new ZoneDisc[64];

    // ---- (4) Blinded + Slowed + Burning on one Raider and one Heavy Infantry ----

    private async Task Markers(ulong seed)
    {
        StartMatch(seed, "--units", "20", "--no-combat", "--no-fog");
        for (int t = 0; t < 4; t++) _sim.Tick();
        int blinded = _data.FindStatus("blinded"), slowed = _data.FindStatus("slowed"), burning = _data.FindStatus("burning");
        int raider = _data.FindUnit("whirlwind_raider"), heavy = _data.FindUnit("malazan_heavy_infantry");
        System.Numerics.Vector2 c = Centroid(0) + new System.Numerics.Vector2(14f, 0f);
        EntityHandle r = SpawnOne(1, raider, c) ?? default, h = SpawnOne(0, heavy, c + new System.Numerics.Vector2(2.5f, 0f)) ?? default;
        if (!Check(r.Generation != 0 && h.Generation != 0, $"markers: spawn failed {r} {h}")) { await EndMatch(); return; }
        foreach (EntityHandle u in new[] { r, h })
        {
            ApplyStatus.Invoke(U.Statuses, new object[] { u.Index, blinded, 2f, 2400, 1 });
            ApplyStatus.Invoke(U.Statuses, new object[] { u.Index, slowed, 0.3f, 2400, 1 });
            ApplyStatus.Invoke(U.Statuses, new object[] { u.Index, burning, 1f, 2400, 0 });
        }
        _views.Sync(W, 0.5f, Vector2.Zero, false);
        Color cb = _views.StatusColor(blinded), cs = _views.StatusColor(slowed), cd = _views.StatusColor(burning);
        Check(cb == AbilityViews.BlindColor && cb != cs && cb != cd && cb != AbilityViews.OtherStatusColor && cs != cd, $"markers: blind {cb}, slow {cs}, burn {cd}");
        int perUnit = 0;
        for (int k = 0; k < _views.ShownMarkers; k++) if (_views.MarkerAt(k).Unit == r.Index) perUnit++;
        Check(perUnit == 3, $"markers: the Raider shows {perUnit} markers, want 3");
        // Distinct enough to tell apart: every pair differs by at least 0.25 in some channel.
        static float Gap(Color a, Color b) => Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
        GD.Print($"markers: blind-slow gap {Gap(cb, cs):0.00}, blind-burn {Gap(cb, cd):0.00}, slow-burn {Gap(cs, cd):0.00}; Raider shows {perUnit}");
        Check(Gap(cb, cs) >= 0.25f && Gap(cb, cd) >= 0.25f, $"markers: colour gaps blind-slow {Gap(cb, cs)}, blind-burn {Gap(cb, cd)}");
        foreach (float zoom in new[] { 20f, 30f, 60f })
        {
            _camera.SetZoom(zoom);
            _camera.SetFocus(c.X + 1f, c.Y);
            await Frames();
            await Shot($"qa-v6c-markers-zoom{zoom:0}");
        }
        await EndMatch();
    }

    // ---- (5) windowed only: an own storm on a plateau's edge, its disc reaching over the cliff ----

    private async Task CliffShot(ulong seed)
    {
        StartMatch(seed, "--units", "10", "--no-combat", "--no-fog", "--no-bases");
        for (int t = 0; t < 4; t++) _sim.Tick();
        Rts.Sim.Map.Heightmap hm = W.Heightmap;
        int found = -1;
        for (int y = 10; y < hm.Height - 10 && found < 0; y++)
            for (int x = 10; x < hm.Width - 10 && found < 0; x++)
                if (hm.LevelAt(x, y) >= 1 && hm.LevelAt(x - 1, y) >= 1 && hm.LevelAt(x - 2, y) >= 1 && hm.LevelAt(x + 2, y) == hm.LevelAt(x, y) - 1 && !hm.IsRamp(x + 2, y))
                    found = y * hm.Width + x;
        if (!Check(found >= 0, "cliff: no plateau edge found")) { await EndMatch(); return; }
        int fx = found % hm.Width, fy = found / hm.Width;
        System.Numerics.Vector2 at = G.CellCenter(fx, fy);
        // A: centred on the plateau's edge (its east part over the low ground); B: 8 cells south, centred on the low ground
        // 3 cells off the cliff (its west part under the plateau). A Laborer on the low ground at each storm's east edge.
        System.Numerics.Vector2 low = G.CellCenter(fx + 3, fy + 8);
        Zone(0, _storm, at);
        Zone(0, _storm, low);
        int laborer = _data.FindUnit("malazan_laborer");
        SpawnOne(0, laborer, at + new System.Numerics.Vector2(5.5f, 0f));
        SpawnOne(0, laborer, low + new System.Numerics.Vector2(5.5f, 0f));
        _views.Sync(W, 0.5f, Vector2.Zero, false);
        Check(_views.ShownZones == 2, $"cliff: {_views.ShownZones} discs");
        GD.Print($"cliff: storm A at cell ({fx}, {fy}) level {hm.LevelAt(fx, fy)}, disc y {(_views.ShownZones > 0 ? _views.ZoneDiscPosition(0).Y : -1f):0.00}; storm B at ({fx + 3}, {fy + 8}) level {hm.LevelAt(fx + 3, fy + 8)}, disc y {(_views.ShownZones > 1 ? _views.ZoneDiscPosition(1).Y : -1f):0.00}");
        foreach (float zoom in new[] { 30f, 50f })
        {
            _camera.SetZoom(zoom);
            _camera.SetFocus(at.X, at.Y + 8f);
            await Frames();
            await Shot($"qa-v6c-cliff-zoom{zoom:0}");
        }
        await EndMatch();
    }

    // Spawns one unit through the command queue (two ticks) and returns it, or null.
    private EntityHandle? SpawnOne(int owner, int type, System.Numerics.Vector2 at)
    {
        var alive = new bool[U.Capacity];
        for (int i = 0; i < U.Capacity; i++) alive[i] = U.Alive[i];
        _sim.Enqueue(Rts.Sim.Commands.Command.SpawnUnit(owner, type, at));
        _sim.Tick();
        _sim.Tick();
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && !alive[i] && U.TypeId[i] == type) return new EntityHandle(i, U.Generation[i]);
        return null;
    }

    private System.Numerics.Vector2 Centroid(int player)
    {
        var sum = System.Numerics.Vector2.Zero;
        int n = 0;
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && U.Owner[i] == player && _data.Units[U.TypeId[i]].Slot != UnitSlot.Worker) { sum += U.Position[i]; n++; }
        return sum / Math.Max(1, n);
    }

    private void Twin(ulong seed, string row)
    {
        Replay replay = _runner.Recorder!.ToReplay();
        ReplayResult twin = ReplayPlayer.Run(replay, _data);
        Check(twin.Ok, $"{row} seed {seed}: twin {twin.Error} at tick {twin.Tick}");
    }

    private async Task Shot(string name)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = $"{_shots}/{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"screenshot {path}");
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
