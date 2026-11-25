using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace Rts.Game.Tests;

/// <summary>
/// QA re-check for M6-VH9's fix round (BUG-1680, BUG-1681): the drawn-first ray walk and the lowered bar's new place.
/// </summary>
/// <remarks>
/// Headless, in the scene loop: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6VH9FixTest.tscn</c>; prints
/// "QA M6-VH9 FIX TEST PASS" or FAIL lines. Rows:
/// (O) an independent oracle on seeded random layouts (own and enemy Keeps, Barracks, a tall Malazan box, a Teblor 1.25 box,
/// own and enemy units, the map's trees) at zoom 20, 40 and 60: per sampled pixel, the nearest drawn thing is found slot by slot
/// (a model at its triangles, a box at its box, a unit's body, a tree's prop); when none is drawn, the nearest pick box's
/// building (its air). Right click (<c>EnemyAt</c>), the right-click point (<c>ContextTarget</c>) and the left click must give
/// what the oracle gives;
/// (GH) the same with fog on and enemy buildings remembered as ghosts (models and boxes), trees among them;
/// (A) the ray walk allocates nothing: 4 x 300 calls (EnemyAt, ContextTarget, PickBuilding, PickResource) over a stacked layout,
/// without fog and with ghosts;
/// (BZ) a Keep, a Barracks, the Watchtower and a Teblor Longhouse on the highest terrace at zoom 20 with the camera moved so the
/// building sits at each screen edge (its drawn box wholly on screen): the bar on screen and not behind its own drawn shape.
/// </remarks>
public partial class QaM6VH9FixTest : Node
{
    private const string Tag = "QA M6-VH9 FIX TEST";

    private readonly List<string> _failures = new();
    private readonly List<string> _offNotes = new();
    private GameData _data = null!;
    private Match _match = null!;
    private Simulation _sim = null!;
    private BuildingViews _views = null!;
    private RtsCamera _camera = null!;
    private SelectionController _sel = null!;
    private SimRunner _runner = null!;

    private World W => _sim.World;
    private BuildingStore B => _sim.World.Buildings;

    public override async void _Ready()
    {
        UnitViews.UseModels = true;
        BuildingViews.UseModels = true;
        try
        {
            GetTree().Root.Size = new Vector2I(1600, 900);
            await Frames(1);
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            // QA M6-V27: `-- --seeds <first> <last>` runs only the (O) rows on those layout seeds (BUG-1682 on new seeds); default 1-4 and every row.
            string[] user = OS.GetCmdlineUserArgs();
            int at = Array.IndexOf(user, "--seeds");
            bool onlyOracle = at >= 0 && at + 2 < user.Length;
            int first = onlyOracle ? int.Parse(user[at + 1], CultureInfo.InvariantCulture) : 1, last = onlyOracle ? int.Parse(user[at + 2], CultureInfo.InvariantCulture) : 4;
            for (int seed = first; seed <= last; seed++) await OracleRows(seed);
            if (!onlyOracle)
            {
                await GhostRows();
                await BarEdgeRows();
            }
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures) GD.Print($"{Tag} FAIL: {f}");
        if (_failures.Count == 0) GD.Print($"{Tag} PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- (O) the oracle on random layouts ----

    private enum Kind { None, Unit, Building, Ghost, Node }

    private readonly struct Hit
    {
        public readonly Kind Kind;
        public readonly int Slot;
        public readonly float T;
        public Hit(Kind kind, int slot, float t) { Kind = kind; Slot = slot; T = t; }
    }

    private bool[] _mask = Array.Empty<bool>();

    // The nearest drawn live building, slot by slot; air: the nearest pick box's slot among those with a model (owner filter optional).
    private Hit NearestBuilding(Vector3 o, Vector3 d, int owner, out int air, out float second)
    {
        var so = new NVector3(o.X, o.Y, o.Z);
        var sd = new NVector3(d.X, d.Y, d.Z);
        if (_mask.Length < B.Capacity) _mask = new bool[B.Capacity];
        Hit best = new(Kind.None, -1, float.PositiveInfinity);
        second = float.PositiveInfinity;
        air = -1;
        float airT = float.PositiveInfinity;
        for (int s = 0; s < B.Capacity; s++)
        {
            if (!B.Alive[s] || (owner >= 0 && B.Owner[s] != owner)) continue;
            Array.Clear(_mask);
            _mask[s] = true;
            int got = BuildingPicker.PickRay(B, _data.Buildings, W.NavGrid, W.Heightmap, -1, so, sd, _views.PickTops, _views.SiteHeights,
                BuildingViews.SiteMinHeight, _mask, out float boxT);
            if (got != s) continue;
            float drawn = _views.ShownAsModel(s) ? _views.ModelRayDistance(s, o, d) : boxT;
            if (boxT < airT) { airT = boxT; air = s; }
            if (drawn < best.T) { second = best.T; best = new Hit(Kind.Building, s, drawn); }
            else if (drawn < second) second = drawn;
        }
        if (best.Kind != Kind.None) air = -1;
        return best;
    }

    private Hit NearestUnit(Vector3 o, Vector3 d, ReadOnlySpan<bool> shown, out float second)
    {
        UnitStore u = W.Units;
        var so = new NVector3(o.X, o.Y, o.Z);
        var sd = new NVector3(d.X, d.Y, d.Z);
        float alpha = (float)_runner.Alpha;
        int unit = UnitPicker.PickRay(shown, u.PrevPosition, u.Position, u.Radius, W.Heightmap, alpha, so, sd, UnitViews.ExtraBodyHeight, out float t);
        second = float.PositiveInfinity;
        return unit < 0 ? new Hit(Kind.None, -1, float.PositiveInfinity) : new Hit(Kind.Unit, unit, t);
    }

    private Hit NearestNode(Vector3 o, Vector3 d)
    {
        ResourceStore r = W.Resources;
        int node = ResourcePicker.PickRay(W.NavGrid, W.Data.Resources, r.Alive, r.TypeId, r.Cell, W.Heightmap, new(o.X, o.Y, o.Z), new(d.X, d.Y, d.Z),
            PropsView.Shape, out float t);
        return node < 0 ? new Hit(Kind.None, -1, float.PositiveInfinity) : new Hit(Kind.Node, node, t);
    }

    private static Hit Min(Hit a, Hit b) => b.T < a.T ? b : a;

    private const float Tie = 0.03f;

    private static bool Close(float a, float b) => float.IsFinite(a) && float.IsFinite(b) && Math.Abs(a - b) < Tie;

    private async Task OracleRows(int seed)
    {
        StartMatch("--players", "2", "--no-fog", "--no-combat", "--faction", "1", "malazan", "--faction", "2", "teblor", "--resources", "high");
        int keep = _data.FindBuilding("malazan_garrison_keep"), barracks = _data.FindBuilding("malazan_barracks");
        int tower = _data.FindBuilding("malazan_watchtower"), longhouse = _data.FindBuilding("teblor_longhouse");
        int heavy = _data.FindUnit("malazan_heavy_infantry");
        NVector2 near = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-40f, 10f);
        var rng = new Random(seed * 7919);
        var picks = new (int Player, int Type)[] { (0, keep), (1, keep), (0, barracks), (1, barracks), (0, tower), (1, longhouse), (1, keep) };
        int placed = 0;
        foreach ((int player, int type) in picks)
        {
            var at = near + new NVector2(rng.Next(-14, 15), rng.Next(-14, 15));
            if (Spawn(player, type, at) >= 0) placed++;
        }
        int ownUnits = 0, enemyUnits = 0;
        for (int k = 0; k < 14; k++)
        {
            int player = k < 4 ? 0 : 1;
            var at = near + new NVector2(rng.Next(-16, 17) + 0.5f, rng.Next(-16, 17) + 0.5f);
            if (SpawnUnit(player, heavy, at) >= 0) { if (player == 0) ownUnits++; else enemyUnits++; }
        }
        _views.Sync(W);
        GD.Print($"  (O) seed {seed}: {placed} of {picks.Length} buildings, {ownUnits} own and {enemyUnits} enemy units near {near}");
        Check(placed >= 4, $"(O) seed {seed}: only {placed} buildings placed");
        foreach (float zoom in new[] { 20f, 40f, 60f })
        {
            _camera.SetZoom(zoom);
            _camera.SetFocus(near.X, near.Y);
            await Frames(2);
            OraclePass($"(O) seed {seed} zoom {zoom}", fog: null, leftClicks: true);
        }
        if (seed == 1) AllocationRow("(A) no fog");
        _camera.SetZoom(CameraLimits.DefaultZoom);
        await EndMatch();
    }

    // Compares EnemyAt, ContextTarget and (optionally) the left click with the oracle over the screen at a 10 px step.
    private void OraclePass(string label, FogView? fog, bool leftClicks)
    {
        Rect2 screen = GetViewport().GetVisibleRect();
        UnitStore u = W.Units;
        int pixels = 0, ties = 0, enemyOk = 0, enemyBad = 0, ctxOk = 0, ctxBad = 0, leftOk = 0, leftBad = 0, leftSkipped = 0;
        int byKind0 = 0, byUnit = 0, byBuilding = 0, byGhost = 0, byNode = 0, byAir = 0, enemyFrontOwnSelected = 0, enemyFrontOverOwn = 0;
        var examples = new List<string>();
        for (float py = 4f; py < screen.Size.Y - 4f; py += 10f)
            for (float px = 4f; px < screen.Size.X - 4f; px += 10f)
            {
                var p = new Vector2(px, py);
                Vector3 o = _camera.ProjectRayOrigin(p), d = _camera.ProjectRayNormal(p).Normalized();
                Hit unit = NearestUnit(o, d, fog != null ? fog.UnitShown : u.Alive, out _);
                Hit bld = new(Kind.None, -1, float.PositiveInfinity), ghost = bld;
                int air = -1, ghostAir = -1;
                float bld2 = float.PositiveInfinity;
                if (fog == null) bld = NearestBuilding(o, d, -1, out air, out bld2);
                else ghost = NearestGhost(fog, o, d, out ghostAir);
                Hit node = NearestNode(o, d);
                Hit all = Min(Min(Min(unit, bld), ghost), node);
                Hit noUnit = Min(Min(bld, ghost), node);
                pixels++;
                // Two different things drawn within a few cm on the ray: either answer is right.
                if (Close(unit.T, bld.T) || Close(unit.T, node.T) || Close(bld.T, node.T) || Close(bld.T, bld2) || Close(ghost.T, node.T) || Close(unit.T, ghost.T))
                {
                    ties++;
                    continue;
                }
                switch (all.Kind)
                {
                    case Kind.None: if (air >= 0 || ghostAir >= 0) byAir++; else byKind0++; break;
                    case Kind.Unit: byUnit++; break;
                    case Kind.Building: byBuilding++; break;
                    case Kind.Ghost: byGhost++; break;
                    case Kind.Node: byNode++; break;
                }

                // Right click: the enemy drawn first, or (nothing drawn) the enemy building whose air is met; a live building's air beats a ghost's.
                bool wantEnemy = false, wantBuilding = false;
                int wantIndex = -1;
                if (all.Kind == Kind.Unit && u.Owner[all.Slot] == 1) { wantEnemy = true; wantIndex = all.Slot; }
                else if (all.Kind == Kind.Building && B.Owner[all.Slot] == 1) { wantEnemy = wantBuilding = true; wantIndex = all.Slot; }
                else if (all.Kind == Kind.Ghost && fog!.Ghosts[all.Slot].Owner == 1) { wantEnemy = wantBuilding = true; wantIndex = all.Slot; }
                else if (all.Kind == Kind.None && air >= 0) { if (B.Owner[air] == 1) { wantEnemy = wantBuilding = true; wantIndex = air; } }
                else if (all.Kind == Kind.None && ghostAir >= 0 && fog!.Ghosts[ghostAir].Owner == 1) { wantEnemy = wantBuilding = true; wantIndex = ghostAir; }
                bool gotEnemy = _sel.EnemyAt(p, out EntityHandle target, out bool isBuilding);
                bool okEnemy = gotEnemy == wantEnemy && (!wantEnemy || (target.Index == wantIndex && isBuilding == wantBuilding));
                if (okEnemy) enemyOk++;
                else
                {
                    enemyBad++;
                    if (examples.Count < 6) examples.Add($"EnemyAt {p}: want {(wantEnemy ? $"{(wantBuilding ? "building" : "unit")} {wantIndex}" : "none")} (oracle {all.Kind} {all.Slot} at {all.T:0.00}, air {air}, ghost air {ghostAir}), got {(gotEnemy ? $"{(isBuilding ? "building" : "unit")} {target.Index}" : "none")}");
                }

                // Right-click point: units do not count; a drawn building or (nothing drawn) the air building names the slot.
                int wantCtx = noUnit.Kind == Kind.Building ? noUnit.Slot : noUnit.Kind == Kind.None && air >= 0 ? air : -1;
                bool ctx = _sel.ContextTarget(p, out NVector2 point, out int ctxBuilding);
                bool okCtx = ctxBuilding == wantCtx;
                if (okCtx && noUnit.Kind == Kind.Ghost) okCtx = ctx && NVector2.Distance(point, SelectionController.GhostCenter(W, fog!, noUnit.Slot)) < 1e-3f;
                if (okCtx && noUnit.Kind == Kind.None && air < 0 && ghostAir >= 0) okCtx = ctx && NVector2.Distance(point, SelectionController.GhostCenter(W, fog!, ghostAir)) < 1e-3f;
                if (okCtx) ctxOk++;
                else
                {
                    ctxBad++;
                    if (examples.Count < 12) examples.Add($"ContextTarget {p}: want building {wantCtx} (oracle {noUnit.Kind} {noUnit.Slot} at {noUnit.T:0.00}, air {air}, ghost air {ghostAir}), got {ctxBuilding} at {point}");
                }

                if (!leftClicks) continue;
                // Left click: skip where an own unit could be the click (its body on the ray, or near on screen).
                if (unit.Kind == Kind.Unit || NearOwnUnit(p, 40f)) { leftSkipped++; continue; }
                Hit own = NearestBuilding(o, d, 0, out int ownAir, out _);
                // Node: drawn in front of every building (any owner), else the own building drawn first, else its air.
                bool nodeWins = node.Kind == Kind.Node && node.T < bld.T;
                int wantNode = nodeWins ? node.Slot : -1;
                // M6-V27 (BUG-1682): an enemy building drawn first (before any own one) means the click is on it: nothing is selected.
                bool enemyFront = !nodeWins && bld.Kind == Kind.Building && B.Owner[bld.Slot] == 1;
                int wantSel = nodeWins || enemyFront ? -1 : own.Kind == Kind.Building ? own.Slot : ownAir;
                if (enemyFront && (own.Kind == Kind.Building || ownAir >= 0)) enemyFrontOverOwn++;
                _sel.ClearBuilding();
                _sel.ClearResource();
                _sel.Selection.Clear();
                Click(p);
                if (enemyFront && _sel.SelectedBuilding >= 0 && B.Owner[_sel.SelectedBuilding] == 0) enemyFrontOwnSelected++;
                if (_sel.SelectedBuilding == wantSel && _sel.SelectedResource == wantNode) leftOk++;
                else
                {
                    leftBad++;
                    if (examples.Count < 18) examples.Add($"left click {p}: want building {wantSel} node {wantNode} (oracle {noUnit.Kind} {noUnit.Slot} at {noUnit.T:0.00}, own {own.Slot}, own air {ownAir}), got building {_sel.SelectedBuilding} node {_sel.SelectedResource}");
                }
            }
        string msg = $"{label}: {pixels} pixels ({ties} near-ties skipped; first drawn: unit {byUnit}, building {byBuilding}, ghost {byGhost}, tree {byNode}, air only {byAir}, ground {byKind0}); EnemyAt {enemyOk} right / {enemyBad} wrong; ContextTarget {ctxOk} / {ctxBad}; left click {leftOk} / {leftBad} ({leftSkipped} skipped near own units; {enemyFrontOwnSelected} select an own building behind a drawn enemy building, of {enemyFrontOverOwn} with one there)";
        GD.Print($"  {msg}");
        foreach (string e in examples) GD.Print($"    {e}");
        Check(enemyBad == 0 && ctxBad == 0 && leftBad == 0, msg);
    }

    private bool NearOwnUnit(Vector2 p, float px)
    {
        UnitStore u = W.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.Owner[i] != 0) continue;
            if (_sel.TryScreenPosition(i, out Vector2 s) && s.DistanceTo(p) < px) return true;
        }
        return false;
    }

    // ---- (GH) ghosts ----

    private Hit NearestGhost(FogView fog, Vector3 o, Vector3 d, out int air)
    {
        var so = new NVector3(o.X, o.Y, o.Z);
        var sd = new NVector3(d.X, d.Y, d.Z);
        ReadOnlySpan<bool> shown = fog.GhostShown;
        if (_mask.Length < shown.Length) _mask = new bool[shown.Length];
        Hit best = new(Kind.None, -1, float.PositiveInfinity);
        air = -1;
        float airT = float.PositiveInfinity;
        for (int s = 0; s < shown.Length; s++)
        {
            if (!shown[s]) continue;
            Array.Clear(_mask);
            _mask[s] = true;
            int got = BuildingPicker.PickGhostRay(_mask.AsSpan(0, shown.Length), fog.Ghosts, _data.Buildings, W.NavGrid, W.Heightmap, so, sd, _views.PickTops,
                _views.SiteHeights, BuildingViews.SiteMinHeight, out float boxT);
            if (got != s) continue;
            float drawn = _views.GhostShownAsModel(s) ? _views.GhostModelRayDistance(s, o, d) : boxT;
            if (boxT < airT) { airT = boxT; air = s; }
            if (drawn < best.T) best = new Hit(Kind.Ghost, s, drawn);
        }
        if (best.Kind != Kind.None) air = -1;
        return best;
    }

    private async Task GhostRows()
    {
        StartMatch("--players", "2", "--no-combat", "--faction", "1", "malazan", "--faction", "2", "teblor", "--resources", "high");
        int keep = _data.FindBuilding("malazan_garrison_keep"), barracks = _data.FindBuilding("malazan_barracks");
        int longhouse = _data.FindBuilding("teblor_longhouse"), laborer = _data.FindUnit("malazan_laborer");
        NVector2 near = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-40f, 10f);
        var enemy = new List<int>();
        foreach ((int type, NVector2 off) in new[] { (keep, new NVector2(0f, 0f)), (barracks, new NVector2(0f, 13f)), (longhouse, new NVector2(-12f, 6f)), (keep, new NVector2(10f, -10f)) })
        {
            int s = Spawn(1, type, near + off);
            if (s >= 0) enemy.Add(s);
        }
        if (!Check(enemy.Count >= 3, $"(GH) only {enemy.Count} enemy buildings")) { await EndMatch(); return; }
        _sim.Enqueue(Command.SpawnUnit(0, laborer, near + new NVector2(0f, 6f)));
        _sim.Tick();
        _sim.Tick();
        int scout = -1;
        for (int i = 0; i < W.Units.Capacity; i++) if (W.Units.Alive[i] && W.Units.Owner[i] == 0) scout = i;
        if (!Check(scout >= 0, "(GH) no scout")) { await EndMatch(); return; }
        for (int t = 0; t < 100 && !enemy.TrueForAll(s => W.Fog.CanSeeBuilding(0, s)); t++) _sim.Tick();
        enemy.RemoveAll(s => !W.Fog.CanSeeBuilding(0, s)); // one the scout never saw is never remembered
        _views.Sync(W);
        W.Units.Free(new EntityHandle(scout, W.Units.Generation[scout])); // staging between ticks: the spot falls out of sight
        for (int t = 0; t < 1500 && !enemy.TrueForAll(s => _views.IsGhostShown(s)); t++)
        {
            _sim.Tick();
            _views.Sync(W);
        }
        int ghosts = 0, models = 0;
        foreach (int s in enemy) { if (_views.IsGhostShown(s)) ghosts++; if (_views.GhostShownAsModel(s)) models++; }
        GD.Print($"  (GH) {ghosts} of {enemy.Count} enemy buildings remembered ({models} as models)");
        if (!Check(ghosts == enemy.Count && models >= 2, $"(GH) {ghosts} of {enemy.Count} remembered, {models} as models")) { await EndMatch(); return; }
        FogView fog = _sel.Fog!.Refreshed(W)!;
        foreach (float zoom in new[] { 20f, 40f, 60f })
        {
            _camera.SetZoom(zoom);
            _camera.SetFocus(near.X, near.Y);
            await Frames(2);
            fog = _sel.Fog!.Refreshed(W)!;
            OraclePass($"(GH) zoom {zoom}", fog, leftClicks: false);
        }
        AllocationRow("(A) fog with ghosts");
        _camera.SetZoom(CameraLimits.DefaultZoom);
        await EndMatch();
    }

    // ---- (A) allocation ----

    private void AllocationRow(string label)
    {
        Rect2 screen = GetViewport().GetVisibleRect();
        var pts = new Vector2[300];
        var rng = new Random(5);
        for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2(rng.Next(0, (int)screen.Size.X), rng.Next(0, (int)screen.Size.Y));
        foreach (Vector2 p in pts) { _sel.EnemyAt(p, out _, out _); _sel.ContextTarget(p, out _, out _); _sel.PickBuilding(p); _sel.PickResource(p); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        foreach (Vector2 p in pts) { _sel.EnemyAt(p, out _, out _); _sel.ContextTarget(p, out _, out _); _sel.PickBuilding(p); _sel.PickResource(p); }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        foreach (Vector2 p in pts) _sel.EnemyAt(p, out _, out _);
        double us = watch.Elapsed.TotalMilliseconds * 1000.0 / pts.Length;
        GD.Print(string.Create(CultureInfo.InvariantCulture, $"  {label}: 4 x {pts.Length} picks allocated {bytes} bytes; EnemyAt {us:0.0} us a call"));
        Check(bytes == 0, $"{label}: the picks allocated {bytes} bytes");
    }

    // ---- (BZ) the bar at the screen edges ----

    private async Task BarEdgeRows()
    {
        int keep = _data.FindBuilding("malazan_garrison_keep"), barracks = _data.FindBuilding("malazan_barracks");
        int tower = _data.FindBuilding("malazan_watchtower"), longhouse = _data.FindBuilding("teblor_longhouse");
        var todo = new List<int> { keep, barracks, tower, longhouse };
        for (int seed = 1; seed <= 16 && todo.Count > 0; seed++)
        {
            StartMatch("--players", "2", "--no-fog", "--no-combat", "--faction", "1", "malazan", "--faction", "2", "teblor", "--resources", "high", "--seed", $"{seed}");
            foreach (int type in todo.ToArray())
            {
                int anchor = LevelAnchor(_data.Buildings[type], MapConstants.MaxLevel);
                if (anchor < 0) continue;
                int owner = type == longhouse ? 1 : 0;
                _sim.Enqueue(Command.SpawnBuilding(owner, type, PlacementGhost.AnchorPoint(W.NavGrid, anchor)));
                _sim.Tick();
                _sim.Tick();
                int slot = B.SlotAt(anchor % W.NavGrid.Width, anchor / W.NavGrid.Width);
                if (slot < 0 || B.TypeId[slot] != type) continue;
                todo.Remove(type);
                _views.Sync(W);
                await EdgeRow(slot, seed);
            }
            await EndMatch();
        }
        foreach (int type in todo) GD.Print($"  (BZ) {_data.Buildings[type].Key}: no anchor on the top level on seeds 1-16 (not tested)");
        GD.Print($"  (BZ) note: {_offNotes.Count} edge placements with part of the bar off screen (reported, not failed: the base is on screen there)");
    }

    private async Task EdgeRow(int slot, int seed)
    {
        BuildingDef def = _data.Buildings[B.TypeId[slot]];
        NVector2 c = Centre(slot);
        float ground = TerrainHeight.At(W.Heightmap, c.X, c.Y);
        bool model = _views.ShownAsModel(slot);
        float top = model ? _views.Models!.Top(B.TypeId[slot]) : _views.PickTops[B.TypeId[slot]];
        float hx = def.FootprintWidth * MapConstants.CellSize / 2f, hz = def.FootprintHeight * MapConstants.CellSize / 2f;
        // The building's base (its footprint on the ground): a tall building's top is off screen at zoom 20 even centred.
        var box = new Aabb(new Vector3(c.X - hx, ground, c.Y - hz), new Vector3(2f * hx, 0f, 2f * hz));
        _camera.SetZoom(CameraLimits.MinZoom);
        // Four edges: the focus moved until the base is just on screen at that edge.
        foreach ((string edge, NVector2 dir) in new[] { ("left", new NVector2(1f, 0f)), ("right", new NVector2(-1f, 0f)), ("top", new NVector2(0f, 1f)), ("bottom", new NVector2(0f, -1f)) })
        {
            float lastOn = 0f;
            for (float k = 0f; k <= 40f; k += 0.5f)
            {
                NVector2 f = c + dir * k;
                _camera.SetFocus(f.X, f.Y);
                await Frames(1);
                if (!BoxOnScreen(box)) break;
                lastOn = k;
            }
            NVector2 focus = c + dir * lastOn;
            _camera.SetFocus(focus.X, focus.Y);
            await Frames(2);
            Rect2 screen = GetViewport().GetVisibleRect();
            Vector3 bar = _views.ViewOf(slot)!.GetNode<MeshInstance3D>("BarBack").GlobalPosition;
            float half = def.FootprintWidth * MapConstants.CellSize * BuildingViews.BarLengthShare / 2f;
            int behind = 0, off = 0;
            for (int k = 0; k < 9; k++)
            {
                var pt = new Vector3(bar.X - half + 2f * half * k / 8f, bar.Y, bar.Z);
                if (!screen.HasPoint(_camera.UnprojectPosition(pt))) off++;
                Vector3 o = _camera.GlobalPosition, d = (pt - o).Normalized();
                float dist = (pt - o).Length();
                int front = _sel.BuildingOnRay(W, null, -1, o, d, out float t);
                if (front == slot && t < dist - 0.05f) behind++;
            }
            bool lowered = _views.BarY(slot) < _views.BarWanted(slot) - 1e-4f;
            string msg = string.Create(CultureInfo.InvariantCulture,
                $"(BZ) {def.Key} on level {MapConstants.MaxLevel} (seed {seed}, ground {ground:0.00} m) at zoom {CameraLimits.MinZoom}, at the {edge} edge (focus moved {lastOn:0.0} m): bar {_views.BarY(slot):0.00} m (look's {_views.BarWanted(slot):0.00}, {(lowered ? "lowered" : "not lowered")}), {_views.BarZ(slot):0.00} m toward the camera; {off} of 9 points off screen, {behind} behind its own drawn shape");
            GD.Print($"  {msg}");
            Check(behind == 0, msg);
            if (off > 0) _offNotes.Add(msg);
            Check(lowered ? _views.BarZ(slot) > 0f : _views.BarZ(slot) == 0f, msg + " (BarZ does not follow the lowering)");
            // Windowed with -- --shots <dir>: the bar where it hangs, for a look.
            int at = Array.IndexOf(OS.GetCmdlineUserArgs(), "--shots");
            if (at >= 0 && at + 1 < OS.GetCmdlineUserArgs().Length && DisplayServer.GetName() != "headless")
            {
                // Test seam (as QaM7V4Test): a hurt building shows its bar.
                if (B.Hp[slot] == def.Hp) typeof(BuildingStore).GetMethod("Damage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(B, new object[] { B.HandleOf(slot), def.Hp / 2 });
                _views.Sync(W);
                await Frames(10);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(OS.GetCmdlineUserArgs()[at + 1], $"qa-vh9fix-bar-{def.Key}-{edge}.png"));
            }
        }
        _camera.SetZoom(CameraLimits.DefaultZoom);
    }

    private bool BoxOnScreen(Aabb box)
    {
        Rect2 screen = GetViewport().GetVisibleRect();
        for (int k = 0; k < 8; k++)
        {
            Vector3 p = box.GetEndpoint(k);
            if (_camera.IsPositionBehind(p) || !screen.HasPoint(_camera.UnprojectPosition(p))) return false;
        }
        return true;
    }

    // ---- helpers ----

    private int LevelAnchor(BuildingDef def, int level)
    {
        NavGrid g = W.NavGrid;
        int type = _data.FindBuilding(def.Key);
        for (int cy = 4; cy + def.FootprintHeight < g.Height - 4; cy += 2)
            for (int cx = 4; cx + def.FootprintWidth < g.Width - 4; cx += 2)
            {
                int a = cy * g.Width + cx;
                if (g.LevelAt(cx, cy) != level || !B.Fits(type, a)) continue;
                bool ok = true;
                for (int y = cy - 2; y <= cy + def.FootprintHeight + 1 && ok; y++)
                    for (int x = cx - 2; x <= cx + def.FootprintWidth + 1 && ok; x++)
                        if (B.SlotAt(x, y) >= 0 || !g.IsPassable(x, y) || g.LevelAt(x, y) != level) ok = false;
                if (ok) return a;
            }
        return -1;
    }

    private void StartMatch(params string[] extra)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        var args = new List<string> { "--seed", "1", "--units", "0", "--no-bases", "--mute" };
        args.AddRange(extra);
        _match.Start(_data, LaunchOptions.Parse(args.ToArray()));
        _runner = _match.GetNode<SimRunner>("SimRunner");
        _runner.ProcessMode = ProcessModeEnum.Disabled;
        _sim = _runner.Simulation!;
        _views = _match.GetNode<BuildingViews>("World3D/BuildingViews");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _sim.Tick();
        _sim.Tick();
    }

    private async Task EndMatch()
    {
        RemoveChild(_match);
        _match.QueueFree();
        await Frames(1);
    }

    private int Spawn(int player, int type, NVector2 near)
    {
        BuildingDef def = _data.Buildings[type];
        int anchor = OpenAnchor(def, near);
        if (anchor < 0) return -1;
        _sim.Enqueue(Command.SpawnBuilding(player, type, PlacementGhost.AnchorPoint(W.NavGrid, anchor)));
        _sim.Tick();
        _sim.Tick();
        int slot = B.SlotAt(anchor % W.NavGrid.Width, anchor / W.NavGrid.Width);
        return slot >= 0 && B.Cell[slot] == anchor && B.TypeId[slot] == type ? slot : -1;
    }

    private int SpawnUnit(int player, int type, NVector2 at)
    {
        UnitStore u = W.Units;
        var alive = new bool[u.Capacity];
        for (int i = 0; i < u.Capacity; i++) alive[i] = u.Alive[i];
        _sim.Enqueue(Command.SpawnUnit(player, type, at));
        _sim.Tick();
        _sim.Tick();
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && !alive[i]) return i;
        return -1;
    }

    private int OpenAnchor(BuildingDef def, NVector2 near)
    {
        int type = _data.FindBuilding(def.Key);
        NavGrid g = W.NavGrid;
        for (int r = 0; r <= 12; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    if (!g.WorldToCell(near + new NVector2(dx, dy) * MapConstants.CellSize, out int cx, out int cy)) continue;
                    if (cx < 3 || cy < 3 || cx + def.FootprintWidth >= g.Width - 3 || cy + def.FootprintHeight >= g.Height - 3) continue;
                    int a = cy * g.Width + cx;
                    if (!B.Fits(type, a)) continue;
                    int level = g.LevelAt(cx, cy);
                    bool ok = true;
                    for (int y = cy - 1; y <= cy + def.FootprintHeight && ok; y++)
                        for (int x = cx - 1; x <= cx + def.FootprintWidth && ok; x++)
                            if (B.SlotAt(x, y) >= 0 || !g.IsPassable(x, y) || g.LevelAt(x, y) != level) ok = false;
                    if (ok) return a;
                }
        return -1;
    }

    private NVector2 Centre(int slot) => StartBase.FootprintCenter(W.NavGrid, _data.Buildings[B.TypeId[slot]], B.Cell[slot]);

    private void Click(Vector2 at)
    {
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
