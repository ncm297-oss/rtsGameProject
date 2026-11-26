using System;
using System.Collections.Generic;
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
/// QA for M6-V28 (session 2026-11-25-1437): the four new Malazan building models (Billet, Depot, Crossbow Range,
/// Watchtower) under the clicks and the fog the developer's rows don't reach.
/// </summary>
/// <remarks>
/// Headless, in the scene loop: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6V28Test.tscn</c>; prints
/// "QA M6-V28 TEST PASS" or FAIL lines. Rows:
/// (O) an independent oracle (QaM6VH9FixTest's) on seeded layouts packed tight with the four new types for both players
/// (so enemy models stand in front of own ones and units in front of and behind models) at zoom 20, 40 and 60: per
/// sampled pixel the nearest drawn thing is found slot by slot; the left click, <c>EnemyAt</c> and <c>ContextTarget</c>
/// must give what the oracle gives (an enemy building drawn first selects nothing on a left click: BUG-1682's shape);
/// (R) each of the four, an enemy's, scouted and left, then razed out of player 0's sight: still remembered as its darkened
/// model, gone once seen;
/// (C) each of the four, an enemy's site, seen and left, then cancelled unseen: remembered as its box (never the model),
/// gone once seen; and one that finishes unseen is remembered as the site's box until seen, then the live model;
/// (W) an enemy Watchtower shooting at an own unit with fog on: the shot leaves at the type's drawn shot height.
/// </remarks>
public partial class QaM6V28Test : Node
{
    private const string Tag = "QA M6-V28 TEST";
    private static readonly string[] Ids = { "malazan_billet", "malazan_depot", "malazan_crossbow_range", "malazan_watchtower" };

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private Match _match = null!;
    private Simulation _sim = null!;
    private BuildingViews _views = null!;
    private ProjectileViews _shots = null!;
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
            var types = new List<int>();
            foreach (string id in Ids)
            {
                int t = _data.FindBuilding(id);
                if (Check(t >= 0, $"no building {id}")) types.Add(t);
            }
            for (int seed = 1; seed <= 4; seed++) await OracleRows(seed, types.ToArray());
            await FogRows(types.ToArray());
            await EnemyTowerShotRow();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures) GD.Print($"{Tag} FAIL: {f}");
        if (_failures.Count == 0) GD.Print($"{Tag} PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- (O) the oracle ----

    private enum Kind { None, Unit, Building, Node }

    private readonly struct Hit
    {
        public readonly Kind Kind;
        public readonly int Slot;
        public readonly float T;
        public Hit(Kind kind, int slot, float t) { Kind = kind; Slot = slot; T = t; }
    }

    private bool[] _mask = Array.Empty<bool>();

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

    private Hit NearestUnit(Vector3 o, Vector3 d)
    {
        UnitStore u = W.Units;
        int unit = UnitPicker.PickRay(u.Alive, u.PrevPosition, u.Position, u.Radius, W.Heightmap, (float)_runner.Alpha,
            new NVector3(o.X, o.Y, o.Z), new NVector3(d.X, d.Y, d.Z), UnitViews.ExtraBodyHeight, out float t);
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

    private static bool Close(float a, float b) => float.IsFinite(a) && float.IsFinite(b) && Math.Abs(a - b) < 0.03f;

    private async Task OracleRows(int seed, int[] types)
    {
        StartMatch("--players", "2", "--no-fog", "--no-combat", "--faction", "1", "malazan", "--faction", "2", "malazan", "--resources", "high");
        int heavy = _data.FindUnit("malazan_heavy_infantry");
        NVector2 near = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-40f, 10f);
        // Keep-alive Keeps off screen (a player with nothing that trains and no unit is defeated, its commands refused).
        int keep = _data.FindBuilding("malazan_garrison_keep");
        for (int p = 0; p < 2; p++) Check(Spawn(p, keep, near + new NVector2(-10f + 14f * p, 60f)) >= 0, $"(O) keep-alive Keep {p}");
        var rng = new Random(seed * 104729);
        int placed = 0, tries = 0;
        // Every new type for each player, twice, packed within +-10 m (tight enough that models overlap on screen).
        for (int round = 0; round < 2; round++)
            foreach (int type in types)
                for (int player = 0; player < 2; player++)
                {
                    tries++;
                    if (Spawn(player, type, near + new NVector2(rng.Next(-10, 11), rng.Next(-10, 11))) >= 0) placed++;
                }
        int ownUnits = 0, enemyUnits = 0;
        for (int k = 0; k < 16; k++)
        {
            int player = k < 6 ? 0 : 1;
            var at = near + new NVector2(rng.Next(-14, 15) + 0.5f, rng.Next(-14, 15) + 0.5f);
            if (SpawnUnit(player, heavy, at) >= 0) { if (player == 0) ownUnits++; else enemyUnits++; }
        }
        _views.Sync(W);
        int models = 0;
        for (int s = 0; s < B.Capacity; s++) if (B.Alive[s] && _views.ShownAsModel(s)) models++;
        GD.Print($"  (O) seed {seed}: {placed} of {tries} buildings ({models} drawn as models), {ownUnits} own and {enemyUnits} enemy units near {near}");
        Check(placed >= 8, $"(O) seed {seed}: only {placed} buildings placed");
        foreach (float zoom in new[] { 20f, 40f, 60f })
        {
            _camera.SetZoom(zoom);
            _camera.SetFocus(near.X, near.Y);
            await Frames(2);
            OraclePass($"(O) seed {seed} zoom {zoom}");
        }
        await EndMatch();
    }

    private void OraclePass(string label)
    {
        Rect2 screen = GetViewport().GetVisibleRect();
        UnitStore u = W.Units;
        int pixels = 0, ties = 0, enemyBad = 0, ctxBad = 0, leftOk = 0, leftBad = 0, skipped = 0, onNew = 0, enemyFrontOverOwn = 0;
        var examples = new List<string>();
        for (float py = 4f; py < screen.Size.Y - 4f; py += 10f)
            for (float px = 4f; px < screen.Size.X - 4f; px += 10f)
            {
                var p = new Vector2(px, py);
                Vector3 o = _camera.ProjectRayOrigin(p), d = _camera.ProjectRayNormal(p).Normalized();
                Hit unit = NearestUnit(o, d);
                Hit bld = NearestBuilding(o, d, -1, out int air, out float bld2);
                Hit node = NearestNode(o, d);
                Hit all = Min(Min(unit, bld), node);
                Hit noUnit = Min(bld, node);
                pixels++;
                if (Close(unit.T, bld.T) || Close(unit.T, node.T) || Close(bld.T, node.T) || Close(bld.T, bld2)) { ties++; continue; }
                if (bld.Kind == Kind.Building) onNew++;

                bool wantEnemy = false, wantBuilding = false;
                int wantIndex = -1;
                if (all.Kind == Kind.Unit && u.Owner[all.Slot] == 1) { wantEnemy = true; wantIndex = all.Slot; }
                else if (all.Kind == Kind.Building && B.Owner[all.Slot] == 1) { wantEnemy = wantBuilding = true; wantIndex = all.Slot; }
                else if (all.Kind == Kind.None && air >= 0 && B.Owner[air] == 1) { wantEnemy = wantBuilding = true; wantIndex = air; }
                bool gotEnemy = _sel.EnemyAt(p, out EntityHandle target, out bool isBuilding);
                if (!(gotEnemy == wantEnemy && (!wantEnemy || (target.Index == wantIndex && isBuilding == wantBuilding))))
                {
                    enemyBad++;
                    if (examples.Count < 6) examples.Add($"EnemyAt {p}: want {(wantEnemy ? $"{(wantBuilding ? "building" : "unit")} {wantIndex}" : "none")} (oracle {all.Kind} {all.Slot}), got {(gotEnemy ? $"{(isBuilding ? "building" : "unit")} {target.Index}" : "none")}");
                }

                int wantCtx = noUnit.Kind == Kind.Building ? noUnit.Slot : noUnit.Kind == Kind.None && air >= 0 ? air : -1;
                _sel.ContextTarget(p, out _, out int ctxBuilding);
                if (ctxBuilding != wantCtx)
                {
                    ctxBad++;
                    if (examples.Count < 12) examples.Add($"ContextTarget {p}: want building {wantCtx} (oracle {noUnit.Kind} {noUnit.Slot}, air {air}), got {ctxBuilding}");
                }

                // Left click: a pixel on or near an own unit may select the unit; skipped.
                if (unit.Kind == Kind.Unit || NearOwnUnit(p, 40f)) { skipped++; continue; }
                Hit own = NearestBuilding(o, d, 0, out int ownAir, out _);
                bool nodeWins = node.Kind == Kind.Node && node.T < bld.T;
                bool enemyFront = !nodeWins && bld.Kind == Kind.Building && B.Owner[bld.Slot] == 1;
                int wantSel = nodeWins || enemyFront ? -1 : own.Kind == Kind.Building ? own.Slot : ownAir;
                if (enemyFront && (own.Kind == Kind.Building || ownAir >= 0)) enemyFrontOverOwn++;
                _sel.ClearBuilding();
                _sel.ClearResource();
                _sel.Selection.Clear();
                Click(p);
                if (_sel.SelectedBuilding == wantSel && _sel.SelectedResource == (nodeWins ? node.Slot : -1)) leftOk++;
                else
                {
                    leftBad++;
                    if (examples.Count < 18) examples.Add($"left click {p}: want building {wantSel} (oracle {noUnit.Kind} {noUnit.Slot}, own {own.Slot}, own air {ownAir}), got building {_sel.SelectedBuilding} node {_sel.SelectedResource}");
                }
            }
        _sel.ClearBuilding();
        _sel.Selection.Clear();
        string msg = $"{label}: {pixels} pixels ({ties} near-ties skipped, {onNew} on a building); EnemyAt wrong {enemyBad}; ContextTarget wrong {ctxBad}; left click {leftOk} right / {leftBad} wrong ({skipped} skipped near own units; {enemyFrontOverOwn} with an enemy building drawn over an own one)";
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

    // ---- (R) razed unseen, (C) a site cancelled unseen / finished unseen ----

    private async Task FogRows(int[] types)
    {
        StartMatch("--players", "3", "--faction", "1", "malazan", "--faction", "2", "malazan", "--faction", "3", "malazan", "--resources", "high");
        NVector2 block = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0];
        int keep = _data.FindBuilding("malazan_garrison_keep");
        // Keep-alive Keeps (a player with nothing that trains is defeated).
        for (int p = 0; p < 3; p++) Check(Spawn(p, keep, block + new NVector2(-30f + 14f * p, 44f)) >= 0, $"keep-alive Keep {p}");
        AgeTwo(1, block + new NVector2(-30f, 70f));
        int laborer = _data.FindUnit("malazan_laborer");

        // (R) razed out of sight.
        NVector2 at = block + new NVector2(-40f, -10f);
        foreach (int type in types)
        {
            BuildingDef def = _data.Buildings[type];
            int slot = Spawn(1, type, at);
            at += new NVector2(14f, 0f);
            if (!Check(slot >= 0, $"(R) {def.Key}: not spawned")) continue;
            int gen = B.Generation[slot];
            NVector2 c = Centre(slot);
            int scout = SpawnUnit(0, laborer, c + new NVector2(0f, def.FootprintHeight * MapConstants.CellSize / 2f + 4f));
            for (int t = 0; t < 100 && !W.Fog.CanSeeBuilding(0, slot); t++) _sim.Tick();
            _views.Sync(W);
            Check(_views.ShownAsModel(slot), $"(R) {def.Key}: seen, not drawn as its model");
            if (scout >= 0 && W.Units.Alive[scout]) W.Units.Free(new EntityHandle(scout, W.Units.Generation[scout]));
            for (int t = 0; t < 400 && !_views.GhostShownAsModel(slot); t++) { _sim.Tick(); _views.Sync(W); }
            if (!Check(_views.GhostShownAsModel(slot), $"(R) {def.Key}: left, not remembered as its model")) continue;
            Raze(slot);
            _views.Sync(W);
            bool razed = !(B.Alive[slot] && B.Generation[slot] == gen);
            Check(razed && !W.Fog.CanSeeBuilding(0, slot), $"(R) {def.Key}: not razed out of sight");
            Check(_views.GhostShownAsModel(slot) && _views.GhostModelOf(slot)!.Visible && !(_views.GhostBoxOf(slot)?.Visible ?? false),
                $"(R) {def.Key}: razed unseen, the remembered model went (or turned into a box) before the spot was seen");
            int eye = SpawnUnit(0, laborer, c + new NVector2(0f, 3f));
            for (int t = 0; t < 200 && _views.IsGhostShown(slot); t++) { _sim.Tick(); _views.Sync(W); }
            MeshInstance3D? g = _views.GhostModelOf(slot);
            Check(!_views.IsGhostShown(slot) && (g == null || !g.Visible) && !(_views.GhostBoxOf(slot)?.Visible ?? false) && !_views.IsShown(slot),
                $"(R) {def.Key}: seen razed, still drawn");
            if (eye >= 0 && W.Units.Alive[eye]) W.Units.Free(new EntityHandle(eye, W.Units.Generation[eye]));
            _sim.Tick();
            GD.Print($"  (R) {def.Key}: razed out of sight stayed its remembered model, gone once seen");
        }

        // (C) an enemy site: cancelled unseen, and finished unseen.
        at = block + new NVector2(-40f, 20f);
        foreach (int type in types)
            foreach (bool finish in new[] { false, true })
            {
                BuildingDef def = _data.Buildings[type];
                string row = $"(C) {def.Key} {(finish ? "finished" : "cancelled")} unseen";
                int anchor = OpenAnchor(def, at);
                at += new NVector2(14f, 0f);
                if (!Check(anchor >= 0, $"{row}: no anchor")) continue;
                NVector2 c = StartBase.FootprintCenter(W.NavGrid, def, anchor);
                float front = def.FootprintHeight * MapConstants.CellSize / 2f + 0.8f;
                int builder = SpawnUnit(1, laborer, c + new NVector2(0f, front));
                int scout = SpawnUnit(0, laborer, c + new NVector2(0f, -front - 3f));
                if (!Check(builder >= 0 && scout >= 0, $"{row}: no units")) continue;
                for (int t = 0; t < 10; t++) _sim.Tick();
                _sim.Enqueue(Command.Build(1, new EntityHandle(builder, W.Units.Generation[builder]), type, PlacementGhost.AnchorPoint(W.NavGrid, anchor)));
                int site = -1;
                for (int t = 0; t < 400 && site < 0; t++)
                {
                    _sim.Tick();
                    int s = B.SlotAt(anchor % W.NavGrid.Width, anchor / W.NavGrid.Width);
                    if (s >= 0 && B.UnderConstruction[s]) site = s;
                }
                if (!Check(site >= 0, $"{row}: no site placed")) continue;
                for (int t = 0; t < 20; t++) _sim.Tick();
                _views.Sync(W);
                Check(W.Fog.CanSeeBuilding(0, site) && _views.ShownAsSite(site) && !_views.ShownAsModel(site), $"{row}: the seen site not drawn as its box");
                int gen = B.Generation[site];
                W.Units.Free(new EntityHandle(scout, W.Units.Generation[scout])); // staging: the spot falls out of sight
                for (int t = 0; t < 400 && !_views.IsGhostShown(site); t++) { _sim.Tick(); _views.Sync(W); }
                if (!Check(_views.IsGhostShown(site) && !_views.GhostShownAsModel(site), $"{row}: the left site not remembered as its box")) continue;
                if (finish)
                {
                    for (int t = 0; t < 12000 && B.Alive[site] && B.Generation[site] == gen && B.UnderConstruction[site]; t++) _sim.Tick();
                    if (!Check(B.Alive[site] && B.Generation[site] == gen && !B.UnderConstruction[site], $"{row}: did not finish")) continue;
                }
                else
                {
                    _sim.Enqueue(Command.Cancel(1, c));
                    for (int t = 0; t < 4 && B.Alive[site] && B.Generation[site] == gen; t++) _sim.Tick();
                    if (!Check(!(B.Alive[site] && B.Generation[site] == gen), $"{row}: not cancelled")) continue;
                }
                _views.Sync(W);
                Check(!W.Fog.CanSeeBuilding(0, site) && _views.IsGhostShown(site) && !_views.GhostShownAsModel(site)
                      && !(_views.GhostModelOf(site)?.Visible ?? false) && !_views.IsShown(site),
                    $"{row}: before it is seen again, not its remembered box alone (ghost {_views.IsGhostShown(site)}, as model {_views.GhostShownAsModel(site)}, live shown {_views.IsShown(site)})");
                if (W.Units.Alive[builder]) W.Units.Free(new EntityHandle(builder, W.Units.Generation[builder]));
                int eye = SpawnUnit(0, laborer, c + new NVector2(0f, -front - 1f));
                for (int t = 0; t < 200 && _views.IsGhostShown(site); t++) { _sim.Tick(); _views.Sync(W); }
                _sim.Tick();
                _views.Sync(W);
                if (finish)
                    Check(!_views.IsGhostShown(site) && _views.ShownAsModel(site) && !_views.ShownAsSite(site),
                        $"{row}: seen again, not the live model (ghost {_views.IsGhostShown(site)}, model {_views.ShownAsModel(site)})");
                else
                    Check(!_views.IsGhostShown(site) && !_views.IsShown(site) && !(_views.GhostBoxOf(site)?.Visible ?? false),
                        $"{row}: seen again, still drawn");
                GD.Print($"  {row}: remembered as its box until seen, then {(finish ? "the live model" : "nothing")}");
                if (eye >= 0 && W.Units.Alive[eye]) W.Units.Free(new EntityHandle(eye, W.Units.Generation[eye]));
                if (finish && B.Alive[site]) Raze(site);
                _sim.Tick();
            }
        await EndMatch();
    }

    // Player `pl` spawns a Barracks and a Crossbow Range (Age II needs two kinds of hall) and researches Age II at its Keep.
    private void AgeTwo(int pl, NVector2 near)
    {
        int age = _data.FindTech("age_ii");
        if (W.HasTech(pl, age)) return;
        Check(Spawn(pl, _data.FindBuilding("malazan_barracks"), near) >= 0, "Age II: no Barracks");
        Check(Spawn(pl, _data.FindBuilding("malazan_crossbow_range"), near + new NVector2(14f, 0f)) >= 0, "Age II: no Crossbow Range");
        int keep = -1;
        for (int i = 0; i < B.Capacity; i++)
            if (B.Alive[i] && B.Owner[i] == pl && !B.UnderConstruction[i] && _data.Buildings[B.TypeId[i]].Key == "malazan_garrison_keep") keep = i;
        if (!Check(keep >= 0, "Age II: no keep")) return;
        _sim.Enqueue(Command.Research(pl, Centre(keep), age));
        for (int t = 0; t < 4000 && !W.HasTech(pl, age); t++) _sim.Tick();
        Check(W.HasTech(pl, age), $"Age II not researched by player {pl}");
    }

    // Player 2 razes `slot` with 16 Heavy Infantry (out of player 0's sight), then they go.
    private void Raze(int slot)
    {
        int gen = B.Generation[slot];
        BuildingDef def = _data.Buildings[B.TypeId[slot]];
        NVector2 c = Centre(slot);
        int heavy = _data.FindUnit("malazan_heavy_infantry");
        float r = def.FootprintHeight * MapConstants.CellSize / 2f + 1.2f;
        for (int k = 0; k < 16; k++) _sim.Enqueue(Command.SpawnUnit(2, heavy, c + new NVector2(-6f + 0.8f * k, k % 2 == 0 ? r : -r)));
        _sim.Tick();
        _sim.Tick();
        var attackers = new List<int>();
        for (int i = 0; i < W.Units.Capacity; i++)
            if (W.Units.Alive[i] && W.Units.Owner[i] == 2 && NVector2.Distance(W.Units.Position[i], c) < r + 6f) attackers.Add(i);
        foreach (int u in attackers) _sim.Enqueue(Command.Attack(2, new EntityHandle(u, W.Units.Generation[u]), new EntityHandle(slot, gen), isBuilding: true));
        for (int t = 0; t < 8000 && B.Alive[slot] && B.Generation[slot] == gen; t++) _sim.Tick();
        Check(!(B.Alive[slot] && B.Generation[slot] == gen), $"raze: slot {slot} not destroyed");
        foreach (int u in attackers) if (W.Units.Alive[u]) W.Units.Free(new EntityHandle(u, W.Units.Generation[u]));
        _sim.Tick();
    }

    // ---- (W) an enemy Watchtower's shot with fog on ----

    private async Task EnemyTowerShotRow()
    {
        StartMatch("--players", "2", "--faction", "1", "malazan", "--faction", "2", "malazan", "--resources", "high");
        NVector2 block = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0];
        int keep = _data.FindBuilding("malazan_garrison_keep"), tower = _data.FindBuilding("malazan_watchtower");
        for (int p = 0; p < 2; p++) Check(Spawn(p, keep, block + new NVector2(-30f + 14f * p, 44f)) >= 0, $"keep-alive Keep {p}");
        int ts = Spawn(1, tower, block + new NVector2(-30f, 0f));
        if (!Check(ts >= 0, "(W) no enemy Watchtower")) { await EndMatch(); return; }
        NVector2 c = Centre(ts);
        int heavy = SpawnUnit(0, _data.FindUnit("malazan_heavy_infantry"), c + new NVector2(0f, 12f));
        if (!Check(heavy >= 0, "(W) no target")) { await EndMatch(); return; }
        _sim.Enqueue(Command.HoldPosition(0, new EntityHandle(heavy, W.Units.Generation[heavy])));
        var handle = new EntityHandle(ts, B.Generation[ts]);
        int shot = -1;
        for (int t = 0; t < 400 && shot < 0; t++)
        {
            _sim.Tick();
            _shots.CollectTick(W);
            for (int i = 0; i < W.Projectiles.Capacity && shot < 0; i++)
            {
                ShotEnds e = ShotEnds.Of(W.Projectiles, i);
                if (e.Live && e.AttackerIsBuilding && e.Attacker == handle) shot = i;
            }
        }
        if (!Check(shot >= 0, "(W) the enemy Watchtower fired nothing")) { await EndMatch(); return; }
        float from = _shots.ShotFromHeight(shot), want = _views.ShotHeights[tower], top = _views.Models!.Top(tower);
        GD.Print($"  (W) an enemy Watchtower (fog on, seen {W.Fog.CanSeeBuilding(0, ts)}): the shot leaves {from:0.###} m up (want {want:0.###}, top {top:0.###})");
        Check(Math.Abs(from - want) < 1e-4f && from > ProjectileViews.LaunchHeight && from < top, $"(W) the enemy tower's shot leaves {from} m up, want {want}");
        await EndMatch();
    }

    // ---- helpers ----

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
        _shots = _match.GetNode<ProjectileViews>("World3D/ProjectileViews");
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
