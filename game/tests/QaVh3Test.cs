using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// QA for M4-VH3 (session 2026-10-10-2334) on the real Match scene. (1) Cliff: Sandstorm discs and Telas Fire's rings
/// centred exactly on a cliff lip, one cell onto the plateau, one cell off on the low ground and on a ramp: every point
/// on 9 circles drawn on the terrain + lift, every mesh triangle inside one cell (none spans the cliff wall) and on its
/// cell's surface. (2) Fog oracle: 32 zones of both players under the fog with fighting armies for 600 ticks, slots freed
/// and reused at new centres: every frame each slot's node is visible exactly when the M4-V6c rule says (own, or a cell
/// within radius + 2 m visible) and sits at its zone's current centre and radius. (3) Scale: 32 live zones and about 2,000
/// units at 8x: AbilityViews._Process 0 B and its frame cost; the targeting rings armed with the cursor moving every frame
/// (a rebuild a frame): 0 B and the cost per frame. (4) One max-hp fill a frame: per-frame fill counts over 300 frames
/// at 1x and at 8x with UnitViews and CombatViews both processing. (5) Windowed only (<c>-- --shots &lt;dir&gt;</c>): the
/// lip disc and rings at zoom 20 / 30 / 50 / 60, and the own and the enemy (Whirlwind) storm side by side, their rim
/// colours measured from the still's pixels.
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaVh3Test.tscn</c>; prints "QA VH3 TEST PASS" and exits 0, or each failure and exits 1.</remarks>
public partial class QaVh3Test : Node
{
    private static readonly MethodInfo CreateZone = typeof(ZoneSystem).GetMethod("Create", BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private Match _match = null!;
    private SimRunner _runner = null!;
    private Simulation _sim = null!;
    private AbilityViews _views = null!;
    private SelectionController _sel = null!;
    private RtsCamera _camera = null!;
    private string? _shots;
    private AbilityDef _storm = null!;
    private int _telas;

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
            _telas = _data.FindAbility("telas_fire");
            await Cliff(1);
            await Cliff(6);
            await FogOracle(1);
            await FogOracle(6);
            await Scale(6);
            await MaxHpFills(1, 1.0);
            await MaxHpFills(6, 8.0);
            await RimPixels(1);
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures.Take(40)) GD.Print($"QA VH3 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA VH3 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- (1) discs and rings on a lip, one cell off on each side, and on a ramp ----

    private async Task Cliff(ulong seed)
    {
        StartMatch(seed, "--units", "0", "--no-bases", "--no-combat", "--no-fog");
        for (int t = 0; t < 4; t++) _sim.Tick();
        Heightmap hm = W.Heightmap;
        int fx = -1, fy = -1;
        for (int y = 12; y < hm.Height - 12 && fx < 0; y++)
            for (int x = 12; x < hm.Width - 12 && fx < 0; x++)
            {
                int level = hm.LevelAt(x, y);
                bool ok = level >= 1;
                for (int dy = -4; dy <= 4 && ok; dy++)
                    ok = hm.LevelAt(x, y + dy) == level && hm.LevelAt(x - 1, y + dy) == level && !hm.IsRamp(x, y + dy) && !hm.IsRamp(x - 1, y + dy)
                        && hm.LevelAt(x + 1, y + dy) == level - 1 && !hm.IsRamp(x + 1, y + dy);
                if (ok) (fx, fy) = (x, y);
            }
        int rx = -1, ry = -1;
        for (int y = 12; y < hm.Height - 12 && rx < 0; y++)
            for (int x = 12; x < hm.Width - 12 && rx < 0; x++)
                if (hm.IsRamp(x, y)) (rx, ry) = (x, y);
        if (!Check(fx >= 0 && rx >= 0, $"cliff seed {seed}: no straight cliff ({fx}, {fy}) or ramp ({rx}, {ry})")) { await EndMatch(); return; }
        const float cs = MapConstants.CellSize;
        float drop = (fx + 1) * cs, row = (fy + 0.5f) * cs;
        var centres = new (string Name, System.Numerics.Vector2 At)[]
        {
            ("lip", new(drop, row)),
            ("plateau+1", new(drop - cs, row)),
            ("low+1", new(drop + cs, row)),
            ("ramp", new((rx + 0.5f) * cs, (ry + 0.5f) * cs)),
        };
        float[] radii = { 0.15f, 0.3f, 0.45f, 0.6f, 0.75f, 0.85f, 0.93f, 0.97f };
        foreach ((string name, System.Numerics.Vector2 c) in centres) Zone(0, _storm, c);
        _views.Sync(W, 0.5f, Vector2.Zero, false);
        Check(_views.ShownZones == 4, $"cliff seed {seed}: {_views.ShownZones} discs, want 4");
        for (int k = 0; k < _views.ShownZones; k++)
        {
            ZoneDisc d = _views.ZoneDiscAt(k);
            string name = centres.First(e => e.At == d.Center).Name;
            int kk = k;
            float[] rs = radii.Select(f => f * d.Radius).Append(d.Radius - AbilityViews.ZoneRimWidth / 2f).ToArray();
            (int bad, int n, float lo, float hi, string first) = OnGround((x, z) => _views.ZoneSurfaceAt(kk, x, z), d.Center, rs, AbilityViews.ZoneLift);
            bool spans = name == "ramp" || hi - lo >= MapConstants.LevelHeight - 0.01f;
            GD.Print($"cliff seed {seed} disc {name} at {d.Center}: {n} points on {lo:0.00}-{hi:0.00} m, {bad} off");
            Check(bad == 0 && n > 400 && spans, $"cliff seed {seed} disc {name}: {bad} of {n} points off the ground ({lo:0.00}-{hi:0.00}), first {first}");
            (int off, int tris, int wall, string f2) = MeshCheck(_views.ZoneNode(d.Slot));
            Check(off == 0 && wall == 0 && tris == 2 * _views.ZoneNode(d.Slot).Cells, $"cliff seed {seed} disc {name} mesh: {off} vertices off, {wall} triangles across cells, {tris} triangles for {_views.ZoneNode(d.Slot).Cells} cells, first {f2}");
        }

        // The rings with the cursor on each centre: a mage on the plateau 5 m back from the lip.
        int mageType = _data.FindUnit("malazan_cadre_mage");
        _sim.Enqueue(Command.SpawnUnit(0, mageType, new System.Numerics.Vector2(drop - 5f, row - 3f)));
        _sim.Tick();
        _sim.Tick();
        EntityHandle mage = default;
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && U.TypeId[i] == mageType) mage = new EntityHandle(i, U.Generation[i]);
        if (!Check(mage.Generation != 0, $"cliff seed {seed}: no mage")) { await EndMatch(); return; }
        _sel.ClearBuilding();
        _sel.Selection.Clear();
        _sel.Selection.Add(mage);
        await Frames();
        foreach ((string name, System.Numerics.Vector2 c) in centres)
        {
            if (!_sel.Targeting) _sel.BeginAbility(_telas);
            _camera.SetFocus(c.X, c.Y);
            _camera.SetZoom(30f);
            await Frames();
            Vector2 px = _camera.UnprojectPosition(new Vector3(c.X, TerrainHeight.At(hm, c.X, c.Y), c.Y));
            _views.Sync(W, 0.5f, px, false);
            if (!Check(_views.TargetingShown, $"cliff seed {seed} ring {name}: not shown")) continue;
            (int bad, int n, float lo, float hi, string first) = OnGround((x, z) => _views.RingSurfaceAt(false, x, z), _views.RadiusCenter,
                new[] { _views.ShownRadius - 0.03f, _views.ShownRadius - AbilityViews.RingWidth / 2f, _views.ShownRadius - AbilityViews.RingWidth + 0.03f }, AbilityViews.Lift);
            Check(bad == 0 && n > 100, $"cliff seed {seed} radius ring {name} at {_views.RadiusCenter}: {bad} of {n} off ({lo:0.00}-{hi:0.00}), first {first}");
            foreach (GroundDisc ring in new[] { _views.RadiusRing, _views.RangeRing })
            {
                if (!ring.Visible) continue;
                (int off, int tris, int wall, string f2) = MeshCheck(ring);
                Check(off == 0 && wall == 0 && tris == 2 * ring.Cells, $"cliff seed {seed} {ring.Name} ({name}) mesh: {off} off, {wall} across cells, {tris} tris / {ring.Cells} cells, first {f2}");
            }
        }
        _sel.CancelTargeting();
        await Frames();

        // Windowed: the lip disc and its rings at each zoom.
        if (_shots != null && DisplayServer.GetName() != "headless")
        {
            System.Numerics.Vector2 lip = centres[0].At;
            foreach (float zoom in new[] { 20f, 30f, 50f, 60f })
            {
                _camera.SetZoom(zoom);
                _camera.SetFocus(lip.X + 2f, lip.Y + 2f);
                await Frames();
                await Shot($"qa-vh3-seed{seed}-cliff-zoom{zoom:0}");
            }
            _sel.BeginAbility(_telas);
            _camera.SetZoom(30f);
            _camera.SetFocus(lip.X, lip.Y + 6f);
            await Frames();
            Vector2 px = _camera.UnprojectPosition(new Vector3(lip.X, TerrainHeight.At(hm, lip.X, lip.Y), lip.Y));
            Input.WarpMouse(px);
            await Frames();
            await Shot($"qa-vh3-seed{seed}-cliff-rings-zoom30");
            _sel.CancelTargeting();
        }
        await EndMatch();
    }

    // Every triangle of the disc's mesh: its three vertices inside one cell (the centroid's, within 1 mm: none spans a
    // cliff wall), and each on that cell's surface + the disc's lift within 0.05 m. Returns (off, triangles, across, first).
    private (int Off, int Tris, int Across, string First) MeshCheck(GroundDisc disc)
    {
        if (disc.Mesh is not ImmediateMesh mesh || mesh.GetSurfaceCount() != 1) return (1, 0, 0, "no surface");
        Vector3[] v = mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        const float cs = MapConstants.CellSize;
        int off = 0, across = 0;
        string first = "";
        for (int t = 0; t + 2 < v.Length; t += 3)
        {
            Vector3 c = disc.Position + (v[t] + v[t + 1] + v[t + 2]) / 3f;
            int cx = (int)MathF.Floor(c.X / cs), cy = (int)MathF.Floor(c.Z / cs);
            for (int i = t; i < t + 3; i++)
            {
                Vector3 w = disc.Position + v[i];
                if (w.X < cx * cs - 1e-3f || w.X > (cx + 1) * cs + 1e-3f || w.Z < cy * cs - 1e-3f || w.Z > (cy + 1) * cs + 1e-3f)
                {
                    if (across++ == 0 && first == "") first = $"vertex {w} outside cell ({cx}, {cy})";
                }
                float ground = TerrainHeight.InCell(W.Heightmap, cx, cy, w.X, w.Z) + disc.Lift;
                if (!(Math.Abs(w.Y - ground) <= 0.05f) && off++ == 0 && first == "") first = $"{w} on cell ({cx}, {cy}) at {ground:0.00}";
            }
        }
        return (off, v.Length / 3, across, first);
    }

    // 180 points on each circle of `radii` round c, skipping 1 cm of each cell edge and off-map points.
    private (int Bad, int Points, float Lo, float Hi, string First) OnGround(Func<float, float, float> surface, System.Numerics.Vector2 c, float[] radii, float lift)
    {
        const float cs = MapConstants.CellSize;
        Heightmap hm = W.Heightmap;
        int bad = 0, n = 0;
        float lo = float.MaxValue, hi = float.MinValue;
        string first = "";
        foreach (float r in radii)
            for (int a = 0; a < 180; a++)
            {
                float x = c.X + r * MathF.Cos(a * MathF.Tau / 180f), z = c.Y + r * MathF.Sin(a * MathF.Tau / 180f);
                if (x < 0f || z < 0f || x >= hm.Width * cs || z >= hm.Height * cs) continue;
                float ux = x / cs - MathF.Floor(x / cs), uz = z / cs - MathF.Floor(z / cs);
                if (ux < 0.005f || ux > 0.995f || uz < 0.005f || uz > 0.995f) continue;
                float ground = TerrainHeight.At(hm, x, z), drawn = surface(x, z);
                n++;
                lo = Math.Min(lo, ground);
                hi = Math.Max(hi, ground);
                if (!(Math.Abs(drawn - (ground + lift)) <= 0.05f) && bad++ == 0) first = $"({x:0.00}, {z:0.00}) r {r:0.00} drawn {drawn:0.00}, ground {ground:0.00} + {lift}";
            }
        return (bad, n, lo, hi, first);
    }

    // ---- (2) the fog rule, node by node, with slots reused ----

    private async Task FogOracle(ulong seed)
    {
        StartMatch(seed, "--units", "60");
        for (int t = 0; t < 4; t++) _sim.Tick();
        System.Numerics.Vector2 west = Centroid(0), east = Centroid(1);
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i]) _sim.Enqueue(Command.AttackMove(U.Owner[i], new EntityHandle(i, U.Generation[i]), U.Owner[i] == 0 ? east : west));
        var rng = new Random((int)seed * 31 + 7); // test staging only
        float mw = G.Width * MapConstants.CellSize, mh = G.Height * MapConstants.CellSize;
        int checkedFrames = 0, wrongVisible = 0, wrongPlace = 0, countMismatch = 0, reused = 0, enemyShownFrames = 0, enemyHiddenFrames = 0;
        string first = "";
        var lastCentre = new System.Numerics.Vector2[W.Zones.Capacity];
        for (int t = 0; t < 600; t++)
        {
            // Keep 32 zones live: new ones over the armies' line and at random, both owners, slots reused as zones expire.
            while (W.Zones.Count < 32)
            {
                int owner = rng.Next(2);
                System.Numerics.Vector2 c = rng.Next(3) == 0
                    ? new System.Numerics.Vector2((float)rng.NextDouble() * mw, (float)rng.NextDouble() * mh)
                    : System.Numerics.Vector2.Lerp(west, east, (float)rng.NextDouble()) + new System.Numerics.Vector2(rng.Next(-30, 30), rng.Next(-30, 30));
                Zone(owner, _storm, c);
            }
            _sim.Tick();
            _views.Sync(W, 0.5f, Vector2.Zero, false);
            OracleFrame(t, lastCentre, ref wrongVisible, ref wrongPlace, ref reused, ref enemyShownFrames, ref enemyHiddenFrames, ref first);
            if (_views.ZoneNodesVisible != _views.ShownZones) countMismatch++;
            checkedFrames++;
            if (t % 60 == 0) await Frame();
        }
        GD.Print($"fog oracle seed {seed}: {checkedFrames} frames x {W.Zones.Capacity} slots; enemy zones shown {enemyShownFrames} slot-frames, hidden {enemyHiddenFrames}; slots reused at a new centre {reused}; wrong visibility {wrongVisible}, wrong place {wrongPlace}, count mismatch {countMismatch}");
        Check(wrongVisible == 0 && wrongPlace == 0 && countMismatch == 0, $"fog oracle seed {seed}: wrong visibility {wrongVisible}, wrong place {wrongPlace}, count mismatch {countMismatch}; first {first}");
        Check(enemyShownFrames > 0 && enemyHiddenFrames > 0 && reused > 0, $"fog oracle seed {seed}: the run must show and hide enemy zones and reuse slots (shown {enemyShownFrames}, hidden {enemyHiddenFrames}, reused {reused})");
        await EndMatch();
    }

    private void OracleFrame(int t, System.Numerics.Vector2[] lastCentre, ref int wrongVisible, ref int wrongPlace, ref int reused, ref int enemyShown, ref int enemyHidden, ref string first)
    {
        ReadOnlySpan<bool> alive = W.Zones.Alive;
        for (int s = 0; s < W.Zones.Capacity; s++)
        {
            bool want = alive[s] && (W.Zones.Owner[s] == 0 || AnyVisibleWithin(s));
            GroundDisc node = _views.ZoneNode(s);
            if (node.Visible != want && wrongVisible++ == 0) first = $"tick {t} slot {s} owner {(alive[s] ? W.Zones.Owner[s] : -1)}: node visible {node.Visible}, rule {want}";
            if (want)
            {
                if (W.Zones.Owner[s] != 0) enemyShown++;
                if (node.Center != W.Zones.Center[s] || node.Radius != W.Zones.Radius(s) || !float.IsFinite(node.SurfaceAt(node.Center.X + 0.37f, node.Center.Y + 0.29f)))
                    if (wrongPlace++ == 0) first += $" | tick {t} slot {s}: node at {node.Center} r {node.Radius}, zone at {W.Zones.Center[s]} r {W.Zones.Radius(s)}";
                if (lastCentre[s] != default && lastCentre[s] != W.Zones.Center[s]) reused++;
                lastCentre[s] = W.Zones.Center[s];
            }
            else if (alive[s] && W.Zones.Owner[s] != 0) enemyHidden++;
        }
    }

    private bool AnyVisibleWithin(int slot)
    {
        ReadOnlySpan<byte> vis = W.Fog.Visibility(0);
        System.Numerics.Vector2 c = W.Zones.Center[slot];
        float reach = W.Zones.Radius(slot) + 2f;
        int x0 = Math.Max(0, (int)((c.X - reach) / MapConstants.CellSize) - 1), x1 = Math.Min(G.Width - 1, (int)((c.X + reach) / MapConstants.CellSize) + 1);
        int y0 = Math.Max(0, (int)((c.Y - reach) / MapConstants.CellSize) - 1), y1 = Math.Min(G.Height - 1, (int)((c.Y + reach) / MapConstants.CellSize) + 1);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (vis[y * G.Width + x] == 2 && System.Numerics.Vector2.Distance(G.CellCenter(x, y), c) <= reach + 1e-4f) return true;
        return false;
    }

    // ---- (3) 32 zones, about 2,000 units at 8x; the rings rebuilt every frame ----

    private async Task Scale(ulong seed)
    {
        StartMatch(seed, "--units", "1000", "--speed", "8");
        for (int t = 0; t < 4; t++) _sim.Tick();
        _runner.GameSpeed = 8.0;
        System.Numerics.Vector2 west = Centroid(0), east = Centroid(1);
        // The same frames with no zone first (the fog refreshed outside the timing, as QaV6cTest): the discs' share.
        _runner.ProcessMode = ProcessModeEnum.Inherit;
        var bare = new System.Diagnostics.Stopwatch();
        for (int f = 0; f < 70; f++)
        {
            _runner._Process(1.0 / 60.0);
            _views.Fog?.Refreshed(W);
            if (f >= 10) bare.Start();
            _views._Process(1.0 / 60.0);
            bare.Stop();
        }
        for (int k = 0; k < 32; k++)
        {
            System.Numerics.Vector2 baseAt = (k & 1) == 0 ? west : east;
            Zone(k & 1, _storm, baseAt + new System.Numerics.Vector2((k / 2 % 8 - 4) * 9f, (k / 16 - 1) * 9f));
        }
        _runner.ProcessMode = ProcessModeEnum.Inherit;
        for (int f = 0; f < 10; f++) { _runner._Process(1.0 / 60.0); _views._Process(1.0 / 60.0); }
        var watch = new System.Diagnostics.Stopwatch();
        double total = 0, worst = 0;
        long bytes = 0;
        int frames = 0, minShown = int.MaxValue, start = W.TickNumber;
        int rebuildsBefore = 0;
        for (int s = 0; s < W.Zones.Capacity; s++) rebuildsBefore += _views.ZoneNode(s).Rebuilds;
        int liveAtStart = W.Zones.Count;
        while (frames < 60)
        {
            _runner._Process(1.0 / 60.0);
            _views.Fog?.Refreshed(W);
            long b0 = GC.GetAllocatedBytesForCurrentThread();
            watch.Restart();
            _views._Process(1.0 / 60.0);
            watch.Stop();
            bytes += GC.GetAllocatedBytesForCurrentThread() - b0;
            total += watch.Elapsed.TotalMilliseconds;
            worst = Math.Max(worst, watch.Elapsed.TotalMilliseconds);
            minShown = Math.Min(minShown, _views.ShownZones);
            frames++;
        }
        int rebuilds = -rebuildsBefore;
        for (int s = 0; s < W.Zones.Capacity; s++) rebuilds += _views.ZoneNode(s).Rebuilds;
        GD.Print($"scale seed {seed}: {U.Count} units, {W.Zones.Count} zones; {frames} frames = {W.TickNumber - start} ticks; AbilityViews._Process {total / frames:0.000} ms avg (no zones {bare.Elapsed.TotalMilliseconds / 60:0.000}), {worst:0.000} ms worst, {bytes} B; discs shown >= {minShown}; zone mesh rebuilds in the window {rebuilds}");
        Check(U.Count >= 1900 && liveAtStart == 32 && W.Zones.Count == 32, $"scale: {U.Count} units, {liveAtStart} zones at the start, {W.Zones.Count} at the end");
        Check(bytes == 0, $"scale: {bytes} B over {frames} frames with 32 zones");
        Check(minShown >= 16, $"scale: only {minShown} discs shown (16 own)");
        Check(rebuilds == 0, $"scale: {rebuilds} zone mesh rebuilds in a steady window (zones don't move)");

        // The rings: Telas Fire armed with every own mage selected, the cursor sweeping (a new centre every frame).
        _runner.ProcessMode = ProcessModeEnum.Disabled;
        int mageType = _data.FindUnit("malazan_cadre_mage");
        _sel.Selection.Clear();
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && U.Owner[i] == 0 && U.TypeId[i] == mageType) _sel.Selection.Add(new EntityHandle(i, U.Generation[i]));
        _camera.SetFocus(west.X, west.Y);
        await Frames();
        Vector2 screen = GetViewport().GetVisibleRect().Size;
        int r0 = 0, g0 = 0;
        for (int f = 0; f < 340; f++)
        {
            if (f == 40) { r0 = _views.RadiusRing.Rebuilds; g0 = _views.RangeRing.Rebuilds; bytes = 0; total = 0; worst = 0; }
            if (f % 3 == 0) _sim.Tick();
            if (!_sel.Targeting) _sel.BeginAbility(_telas);
            var mouse = new Vector2(screen.X * (0.1f + 0.8f * (f % 97) / 97f), screen.Y * (0.3f + 0.4f * (f % 13) / 13f));
            long b0 = GC.GetAllocatedBytesForCurrentThread();
            watch.Restart();
            _views.Sync(W, f % 3 / 3f, mouse, false);
            watch.Stop();
            if (f < 40) continue;
            bytes += GC.GetAllocatedBytesForCurrentThread() - b0;
            total += watch.Elapsed.TotalMilliseconds;
            worst = Math.Max(worst, watch.Elapsed.TotalMilliseconds);
        }
        int radiusRebuilds = _views.RadiusRing.Rebuilds - r0, rangeRebuilds = _views.RangeRing.Rebuilds - g0;
        GD.Print($"rings seed {seed}: {_sel.Selection.Count} mages, 300 frames armed with the cursor moving: radius ring {radiusRebuilds} rebuilds ({_views.RadiusRing.Cells} cells), range ring {rangeRebuilds} ({_views.RangeRing.Cells} cells); Sync {total / 300:0.000} ms avg, {worst:0.000} ms worst, {bytes} B");
        Check(bytes == 0, $"rings: {bytes} B over 300 armed frames with a rebuild a frame");
        Check(radiusRebuilds >= 250, $"rings: only {radiusRebuilds} radius ring rebuilds in 300 frames (the row must rebuild every frame)");

        // GroundDisc.Place alone, the centre moving 5 cm a call (a rebuild each): a 6 m disc, a 16 m range ring's band and
        // a 40 m disc (bigger than any shipped radius), 500 rebuilds each after a warm-up.
        _sel.CancelTargeting();
        var probe = new GroundDisc { Name = "QaProbe" };
        AddChild(probe);
        probe.Visible = true;
        foreach ((float radius, float hole) in new[] { (6f, 0f), (16f, 16f - AbilityViews.RingWidth), (40f, 0f) })
        {
            System.Numerics.Vector2 c0 = west;
            for (int i = 0; i < 20; i++) probe.Place(W.Heightmap, c0 + new System.Numerics.Vector2(i * 0.05f, 0f), radius, hole, 0.1f);
            long pb = GC.GetAllocatedBytesForCurrentThread();
            watch.Restart();
            int rb = probe.Rebuilds;
            for (int i = 0; i < 500; i++) probe.Place(W.Heightmap, c0 + new System.Numerics.Vector2(1f + i * 0.05f, i * 0.03f), radius, hole, 0.1f);
            watch.Stop();
            long pbytes = GC.GetAllocatedBytesForCurrentThread() - pb;
            GD.Print($"GroundDisc.Place r {radius} hole {hole:0.00}: {probe.Cells} cells, {probe.Rebuilds - rb} rebuilds, {watch.Elapsed.TotalMilliseconds * 1000.0 / 500.0:0.0} us a rebuild, {pbytes} B");
            Check(pbytes == 0 && probe.Rebuilds - rb == 500, $"GroundDisc.Place r {radius}: {pbytes} B, {probe.Rebuilds - rb} rebuilds");
        }
        probe.QueueFree();
        await EndMatch();
    }

    // ---- (4) max-hp fills per frame through the real tree ----

    private async Task MaxHpFills(ulong seed, double speed)
    {
        StartMatch(seed, "--units", "300");
        for (int t = 0; t < 4; t++) _sim.Tick();
        System.Numerics.Vector2 west = Centroid(0), east = Centroid(1);
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i]) _sim.Enqueue(Command.AttackMove(U.Owner[i], new EntityHandle(i, U.Generation[i]), U.Owner[i] == 0 ? east : west));
        _runner.GameSpeed = speed;
        _runner.ProcessMode = ProcessModeEnum.Inherit;
        UnitViews units = _match.GetNode<UnitViews>("World3D/UnitViews");
        CombatViews combat = _match.GetNode<CombatViews>("World3D/CombatViews");
        for (int f = 0; f < 10; f++) await Frame();
        var hist = new int[4];
        int frames = 0, ticks = 0, maxPerFrame = 0, startTick = W.TickNumber, prevTick = W.TickNumber, framesWithTick = 0, framesWithTickNoFill = 0;
        int prev = UnitViews.MaxHpFills;
        while (frames < 300)
        {
            await Frame();
            int now = UnitViews.MaxHpFills, d = now - prev;
            prev = now;
            hist[Math.Min(3, d)]++;
            maxPerFrame = Math.Max(maxPerFrame, d);
            if (W.TickNumber != prevTick) { framesWithTick++; if (d == 0) framesWithTickNoFill++; }
            prevTick = W.TickNumber;
            frames++;
        }
        ticks = W.TickNumber - startTick;
        GD.Print($"max hp fills seed {seed} at {speed}x: {frames} frames, {ticks} ticks; fills per frame: 0 x{hist[0]}, 1 x{hist[1]}, 2 x{hist[2]}, 3+ x{hist[3]}; frames with a tick {framesWithTick} ({framesWithTickNoFill} of them without a fill); shared {ReferenceEquals(units.MaxHp, combat.MaxHp)}");
        Check(maxPerFrame <= 1 && ReferenceEquals(units.MaxHp, combat.MaxHp), $"max hp fills seed {seed} at {speed}x: up to {maxPerFrame} fills a frame (2: {hist[2]}, 3+: {hist[3]}), shared {ReferenceEquals(units.MaxHp, combat.MaxHp)}");
        Check(hist[1] > 0 && ticks > 0, $"max hp fills seed {seed} at {speed}x: no fill in {frames} frames ({ticks} ticks)");
        _runner.ProcessMode = ProcessModeEnum.Disabled;
        await EndMatch();
    }

    // ---- (5) windowed: own and enemy rims measured from the still ----

    private async Task RimPixels(ulong seed)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        StartMatch(seed, "--units", "0", "--no-bases", "--no-combat", "--no-fog");
        for (int t = 0; t < 4; t++) _sim.Tick();
        Heightmap hm = W.Heightmap;
        // Two flat spots 16 m apart (all cells of each storm's box on one level, no ramp), near the map centre.
        int cells = (int)MathF.Ceiling((_storm.Radius + 2f) / MapConstants.CellSize);
        System.Numerics.Vector2 a = default;
        bool found = false;
        for (int r = 0; r < hm.Width / 2 - cells - 10 && !found; r++)
            for (int dx = -r; dx <= r && !found; dx++)
            {
                int cx = hm.Width / 2 + dx, cy = hm.Height / 2 + r - Math.Abs(dx);
                if (Flat(hm, cx, cy, cells) && Flat(hm, cx + 8, cy, cells) && hm.LevelAt(cx, cy) == hm.LevelAt(cx + 8, cy))
                {
                    a = new System.Numerics.Vector2((cx + 0.5f) * MapConstants.CellSize, (cy + 0.5f) * MapConstants.CellSize);
                    found = true;
                }
            }
        if (!Check(found, "rim pixels: no flat pair")) { await EndMatch(); return; }
        System.Numerics.Vector2 b = a + new System.Numerics.Vector2(16f, 0f);
        Zone(0, _storm, a);
        Zone(1, _storm, b);
        _camera.SetZoom(30f);
        _camera.SetFocus((a.X + b.X) / 2f, a.Y);
        await Frames();
        await Shot($"qa-vh3-seed{seed}-rims-own-left-enemy-right");
        Image img = GetViewport().GetTexture().GetImage();
        Color own = Mean(img, a, _storm.Radius - AbilityViews.ZoneRimWidth / 2f), enemy = Mean(img, b, _storm.Radius - AbilityViews.ZoneRimWidth / 2f);
        Color ownFill = Mean(img, a, _storm.Radius * 0.5f), enemyFill = Mean(img, b, _storm.Radius * 0.5f);
        Color gold = UnitViews.ColorFromRgb(_match.PlayerRgb[1]); // player 1's colour (M6-V3, BUG-0631 item 2)
        float gap = ChannelGap(own, enemy), euclid = (float)Math.Sqrt((own.R - enemy.R) * (own.R - enemy.R) + (own.G - enemy.G) * (own.G - enemy.G) + (own.B - enemy.B) * (own.B - enemy.B));
        GD.Print($"rim pixels: own rim {own.ToHtml(false)}, enemy rim {enemy.ToHtml(false)} (enemy colour {gold.ToHtml(false)}); fills {ownFill.ToHtml(false)} / {enemyFill.ToHtml(false)}; max channel gap {gap:0.000}, euclidean {euclid:0.000}; own rim vs fill {ChannelGap(own, ownFill):0.000}");
        Check(gap > 0.06f && euclid > 0.06f, $"rim pixels: own {own.ToHtml(false)} vs enemy {enemy.ToHtml(false)}: gap {gap:0.000}, euclidean {euclid:0.000}");
        Check(ChannelGap(own, ownFill) > 0.06f && ChannelGap(enemy, enemyFill) > 0.06f, $"rim pixels: a rim does not stand out from its fill (own {ChannelGap(own, ownFill):0.000}, enemy {ChannelGap(enemy, enemyFill):0.000})");
        await EndMatch();
    }

    private static bool Flat(Heightmap hm, int cx, int cy, int cells)
    {
        if (cx - cells < 0 || cy - cells < 0 || cx + cells >= hm.Width || cy + cells >= hm.Height) return false;
        int level = hm.LevelAt(cx, cy);
        for (int y = cy - cells; y <= cy + cells; y++)
            for (int x = cx - cells; x <= cx + cells; x++)
                if (hm.LevelAt(x, y) != level || hm.IsRamp(x, y)) return false;
        return true;
    }

    // The mean colour of the still's pixels on the circle of radius r round c (on the terrain + the zone's lift).
    private Color Mean(Image img, System.Numerics.Vector2 c, float r)
    {
        float sr = 0, sg = 0, sb = 0;
        int n = 0;
        for (int k = 0; k < 72; k++)
        {
            float x = c.X + r * MathF.Cos(k * MathF.Tau / 72f), z = c.Y + r * MathF.Sin(k * MathF.Tau / 72f);
            Vector2 p = _camera.UnprojectPosition(new Vector3(x, TerrainHeight.At(W.Heightmap, x, z) + AbilityViews.ZoneLift, z));
            int px = (int)p.X, py = (int)p.Y;
            if (px < 0 || py < 0 || px >= img.GetWidth() || py >= img.GetHeight()) continue;
            Color col = img.GetPixel(px, py);
            sr += col.R; sg += col.G; sb += col.B;
            n++;
        }
        return n == 0 ? new Color(0, 0, 0) : new Color(sr / n, sg / n, sb / n);
    }

    private static float ChannelGap(Color a, Color b) => Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

    // ---- harness ----

    private void StartMatch(ulong seed, params string[] extra)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _runner = _match.GetNode<SimRunner>("SimRunner");
        var args = new List<string> { "--seed", seed.ToString(CultureInfo.InvariantCulture), "--mute", "--zoom", "30" };
        args.AddRange(extra);
        _match.Start(_data, LaunchOptions.Parse(args.ToArray()));
        _runner.ProcessMode = ProcessModeEnum.Disabled;
        _sim = _runner.Simulation!;
        _views = _match.GetNode<AbilityViews>("World3D/AbilityViews");
        _sel = _match.GetNode<SelectionController>("SelectionController");
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

    private System.Numerics.Vector2 Centroid(int player)
    {
        var sum = System.Numerics.Vector2.Zero;
        int n = 0;
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && U.Owner[i] == player && _data.Units[U.TypeId[i]].Slot != UnitSlot.Worker) { sum += U.Position[i]; n++; }
        return sum / Math.Max(1, n);
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
