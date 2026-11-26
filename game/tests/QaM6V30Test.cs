using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Save;
using Rts.Sim.ViewApi;
using NVector2 = System.Numerics.Vector2;

namespace Rts.Game.Tests;

/// <summary>
/// QA for M6-V30 (the Teblor building models, the data <c>scale</c> drawn on a model's height): the Producer's QA focus rows
/// the developer's <c>TeblorBuildingsTest</c> does not make.
/// </summary>
/// <remarks>
/// Headless, in the scene loop: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6V30Test.tscn</c>; prints
/// "QA M6-V30 TEST PASS" or FAIL lines. Every model type is found by its file (no id literal but the faction keys). Rows:
/// <list type="bullet">
/// <item>(U) Malazan unchanged: every scale-1 model's fit is a uniform scale (x = y = z, bit for bit) and its drawn bounds are
/// what M6-V26's rule gives from the file (computed here again).</item>
/// <item>(L) Every terrace level and the map's corners: each Teblor model spawned wholly on level 0, 1 and 2 (where the map
/// has room) and at the anchor nearest each map corner: its base on the ground, its height the fitted height x scale within
/// 1 cm, its plan FitMargin inside the footprint, every corner inside the map.</item>
/// <item>(C) A site finishing under the camera: a Gatherer builds each Teblor model type with the camera on it; on the first
/// sync after the tick the sim finishes it, the model is shown at its scaled height on the ground (no box, no site).</item>
/// <item>(G) The remembered model of a razed Teblor building in fog: seen by player 0, left, razed by player 2 out of sight:
/// player 0 still draws its ghost model at the scaled height on the ground, the ghost's pick ray reaches the scaled top.</item>
/// <item>(S) Save / load with Teblor models standing: every building drawn after the load as before (model, transform).</item>
/// <item>(K) Picking beside the tall models: an own Gatherer wholly behind the Longhouse (a click on it selects the
/// Longhouse at zoom 40 and 60 when the model hides it); an own Warrior in front of a Hut at zoom 60 is selected; a click on
/// the topmost model pixels of the Stone Cairn at zoom 20, 40, 60 selects the Cairn.</item>
/// </list>
/// </remarks>
public partial class QaM6V30Test : Node
{
    private const string Tag = "QA M6-V30 TEST";

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private Match _match = null!;
    private Simulation _sim = null!;
    private BuildingViews _views = null!;
    private RtsCamera _camera = null!;
    private SelectionController _sel = null!;
    private int _teblor;
    private string _scratch = "";

    private World W => _sim.World;
    private BuildingStore B => _sim.World.Buildings;

    public override async void _Ready()
    {
        _scratch = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://qa-m6v30-{OS.GetProcessId()}"));
        UnitViews.UseModels = false;
        BuildingViews.UseModels = true;
        BuildingModels.ModelRoot = "res://assets/models/";
        try
        {
            GetTree().Root.Size = new Vector2I(1600, 900);
            await Frames(1);
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            _teblor = _data.FindFaction("teblor");
            int[] teblor = ModelTypes(_teblor);
            GD.Print($"{teblor.Length} Teblor model types");
            if (Check(teblor.Length >= 6, $"only {teblor.Length} Teblor building types have a model file (want six)"))
            {
                UnchangedRows();
                await LevelAndEdgeRows(teblor);
                await SiteRows(teblor);
                await GhostRows(teblor);
                await SaveLoadRows(teblor);
                await PickRows();
            }
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        UnitViews.UseModels = true;
        BuildingViews.UseModels = true;
        try { if (Directory.Exists(_scratch)) Directory.Delete(_scratch, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        foreach (string f in _failures) GD.Print($"{Tag} FAIL: {f}");
        if (_failures.Count == 0) GD.Print($"{Tag} PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private int[] ModelTypes(int faction)
    {
        var list = new List<int>();
        for (int t = 0; t < _data.Buildings.Length; t++)
            if (_data.Buildings[t].Faction == faction && ResourceLoader.Exists(BuildingModels.PathFor(_data, _data.Buildings[t]))) list.Add(t);
        return list.ToArray();
    }

    // M6-V26's fit computed from the file (uniform scale, plan centred, lowest point on the ground): the unscaled bounds.
    private Aabb BaseFit(BuildingDef def)
    {
        Node probe = GD.Load<PackedScene>(BuildingModels.PathFor(_data, def)).Instantiate();
        Aabb b = new();
        foreach (Node n in probe.FindChildren("*", "MeshInstance3D", true, false))
        {
            var mi = (MeshInstance3D)n;
            if (mi.Mesh == null) continue;
            Transform3D toRoot = Transform3D.Identity;
            for (Node? x = mi; x != null && x != probe; x = x.GetParent())
                if (x is Node3D n3) toRoot = n3.Transform * toRoot;
            b = toRoot * mi.Mesh.GetAabb();
            break;
        }
        probe.Free();
        float w = def.FootprintWidth * MapConstants.CellSize - 2f * BuildingModels.FitMargin;
        float d = def.FootprintHeight * MapConstants.CellSize - 2f * BuildingModels.FitMargin;
        float s = Math.Min(w / Math.Max(b.Size.X, 1e-4f), d / Math.Max(b.Size.Z, 1e-4f));
        return new Aabb(new Vector3(-b.Size.X * s / 2f, 0f, -b.Size.Z * s / 2f), b.Size * s);
    }

    // ---- (U) ----

    private void UnchangedRows()
    {
        var models = new BuildingModels(_data, new uint[] { 0xC03030 });
        int n = 0;
        for (int t = 0; t < _data.Buildings.Length; t++)
        {
            if (!models.Has(t)) continue;
            BuildingDef def = _data.Buildings[t];
            Aabb drawn = models.FittedBounds(t), want = BaseFit(def);
            Vector3 sc = models.Fit(t).Basis.Scale;
            if (def.Scale == 1f)
            {
                n++;
                bool uniform = sc.X == sc.Y && sc.Y == sc.Z;
                bool same = (drawn.Size - want.Size).Length() < 1e-4f && Math.Abs(drawn.Position.Y) < 1e-5f
                            && Math.Abs(drawn.GetCenter().X) < 1e-4f && Math.Abs(drawn.GetCenter().Z) < 1e-4f;
                GD.Print($"(U) {def.Key}: drawn {drawn.Size.X:0.####} x {drawn.Size.Z:0.####} x {drawn.Size.Y:0.####} m, M6-V26 rule {want.Size.X:0.####} x {want.Size.Z:0.####} x {want.Size.Y:0.####}, fit scale {sc}");
                Check(uniform && same, $"(U) {def.Key} (scale 1): drawn {drawn.Size}, the M6-V26 fit {want.Size}, scale {sc}");
            }
            else
            {
                bool ok = Math.Abs(sc.X - sc.Z) < 1e-6f && Math.Abs(sc.Y - sc.X * def.Scale) < 1e-5f;
                Check(ok, $"(U) {def.Key} (scale {def.Scale}): fit scale {sc}, want y = x * scale");
            }
        }
        Check(n >= 6, $"(U) only {n} scale-1 model types");
    }

    // ---- (L) ----

    private async Task LevelAndEdgeRows(int[] types)
    {
        StartMatch("--players", "2", "--no-fog", "--no-combat", "--faction", "1", "teblor", "--faction", "2", "teblor", "--resources", "high");
        KeepAlive(0);
        NavGrid g = W.NavGrid;
        int maxLevel = 0;
        for (int y = 0; y < g.Height; y++) for (int x = 0; x < g.Width; x++) maxLevel = Math.Max(maxLevel, g.LevelAt(x, y));
        GD.Print($"(L) map {g.Width} x {g.Height} cells, levels 0-{maxLevel}");
        int placedOnLevel = 0;
        foreach (int type in types)
        {
            BuildingDef def = _data.Buildings[type];
            Aabb want = BaseFit(def);
            for (int level = 0; level <= maxLevel; level++)
            {
                int a = LevelAnchor(type, level);
                if (a < 0) { GD.Print($"  (L) {def.Key}: no room on level {level}"); continue; }
                int slot = SpawnAt(0, type, a);
                if (!Check(slot >= 0, $"(L) {def.Key}: not spawned on level {level}")) continue;
                placedOnLevel++;
                MeasureRow(slot, want, $"level {level}");
            }
            // The anchors nearest the four map corners.
            foreach ((int cx0, int cy0) in new[] { (0, 0), (g.Width - 1, 0), (0, g.Height - 1), (g.Width - 1, g.Height - 1) })
            {
                // An anchor the sim's spawn refuses (B.Fits is not the whole rule: a tree or a unit there) is skipped; the next nearest is tried.
                var refused = new HashSet<int>();
                int a = -1, slot = -1;
                for (int tries = 0; tries < 12 && slot < 0; tries++)
                {
                    a = CornerAnchor(type, cx0, cy0, refused);
                    if (a < 0) break;
                    slot = SpawnAt(0, type, a);
                    if (slot < 0) refused.Add(a);
                }
                if (!Check(slot >= 0, $"(L) {def.Key}: not spawned near corner ({cx0},{cy0}) ({refused.Count} anchors refused)")) continue;
                MeasureRow(slot, want, $"corner ({cx0},{cy0}) anchor {a % g.Width},{a / g.Width}");
            }
        }
        Check(placedOnLevel >= types.Length * 2, $"(L) only {placedOnLevel} level placements");
        await EndMatch();
    }

    private void MeasureRow(int slot, Aabb want, string where)
    {
        BuildingDef def = _data.Buildings[B.TypeId[slot]];
        _views.Sync(W);
        MeshInstance3D? m = _views.ModelOf(slot);
        if (!Check(_views.ShownAsModel(slot) && m != null && m.Visible, $"(L) {def.Key} {where}: not drawn as a model")) return;
        Aabb box = m!.GlobalTransform * m.Mesh.GetAabb();
        int a = B.Cell[slot];
        float x0 = a % W.NavGrid.Width * MapConstants.CellSize, z0 = a / W.NavGrid.Width * MapConstants.CellSize;
        float x1 = x0 + def.FootprintWidth * MapConstants.CellSize, z1 = z0 + def.FootprintHeight * MapConstants.CellSize;
        NVector2 c = Centre(slot);
        float ground = TerrainHeight.At(W.Heightmap, c.X, c.Y);
        float gap = Math.Min(Math.Min(box.Position.X - x0, x1 - box.End.X), Math.Min(box.Position.Z - z0, z1 - box.End.Z));
        float mapW = W.NavGrid.Width * MapConstants.CellSize, mapH = W.NavGrid.Height * MapConstants.CellSize;
        bool inMap = box.Position.X >= 0f && box.Position.Z >= 0f && box.End.X <= mapW && box.End.Z <= mapH;
        bool finite = float.IsFinite(box.Position.X) && float.IsFinite(box.Size.Y);
        float h = want.Size.Y * def.Scale;
        GD.Print($"  (L) {def.Key} {where}: base {box.Position.Y - ground:0.####} m over the ground, {box.Size.Y:0.###} m tall (want {h:0.###}), plan {box.Size.X:0.###} x {box.Size.Z:0.###} (want {want.Size.X:0.###} x {want.Size.Z:0.###}), edge gap {gap:0.###}, bar {_views.BarY(slot):0.###}");
        Check(finite && Math.Abs(box.Position.Y - ground) < 0.01f, $"(L) {def.Key} {where}: base {box.Position.Y} m, ground {ground} m");
        Check(Math.Abs(box.Size.Y - h) < 0.01f, $"(L) {def.Key} {where}: {box.Size.Y} m tall, want {h}");
        Check(Math.Abs(box.Size.X - want.Size.X) < 1e-3f && Math.Abs(box.Size.Z - want.Size.Z) < 1e-3f, $"(L) {def.Key} {where}: plan {box.Size.X} x {box.Size.Z}, want {want.Size.X} x {want.Size.Z}");
        Check(gap >= BuildingModels.FitMargin - 1e-3f, $"(L) {def.Key} {where}: {gap} m inside its footprint");
        Check(inMap, $"(L) {def.Key} {where}: drawn outside the map ({box.Position} .. {box.End})");
        Check(_views.BarY(slot) - BuildingViews.BarThickness / 2f > box.Size.Y || _views.BarZ(slot) != 0f, $"(L) {def.Key} {where}: bar {_views.BarY(slot)} under the top {box.Size.Y}");
    }

    // An anchor whose footprint and a ring of one cell are all on `level`, passable and free (first in row order from the centre out).
    private int LevelAnchor(int type, int level)
    {
        BuildingDef def = _data.Buildings[type];
        NavGrid g = W.NavGrid;
        for (int cy = 2; cy + def.FootprintHeight < g.Height - 2; cy++)
            for (int cx = 2; cx + def.FootprintWidth < g.Width - 2; cx++)
            {
                if (g.LevelAt(cx, cy) != level) continue;
                int a = cy * g.Width + cx;
                if (!B.Fits(type, a)) continue;
                bool ok = true;
                for (int y = cy - 1; y <= cy + def.FootprintHeight && ok; y++)
                    for (int x = cx - 1; x <= cx + def.FootprintWidth && ok; x++)
                        if (B.SlotAt(x, y) >= 0 || !g.IsPassable(x, y) || g.LevelAt(x, y) != level) ok = false;
                if (ok) return a;
            }
        return -1;
    }

    // The anchor nearest map cell (cx0, cy0) where the type fits (no margin asked: the edge is the point).
    private int CornerAnchor(int type, int cx0, int cy0, HashSet<int> refused)
    {
        BuildingDef def = _data.Buildings[type];
        NavGrid g = W.NavGrid;
        int best = -1;
        long bestD = long.MaxValue;
        for (int cy = 0; cy + def.FootprintHeight <= g.Height; cy++)
            for (int cx = 0; cx + def.FootprintWidth <= g.Width; cx++)
            {
                int ex = cx0 == 0 ? cx : cx + def.FootprintWidth - 1, ey = cy0 == 0 ? cy : cy + def.FootprintHeight - 1;
                long d = (long)(ex - cx0) * (ex - cx0) + (long)(ey - cy0) * (ey - cy0);
                if (d >= bestD) continue;
                int a = cy * g.Width + cx;
                if (refused.Contains(a) || !B.Fits(type, a)) continue;
                bestD = d;
                best = a;
            }
        return best;
    }

    // ---- (C) ----

    private async Task SiteRows(int[] types)
    {
        int age = _data.FindTech("age_ii");
        int gatherer = UnitOf(UnitSlot.Worker);
        foreach (int type in types)
        {
            // A match per type (one player's stock pays one site and Age II).
            StartMatch("--players", "2", "--no-fog", "--no-combat", "--faction", "1", "teblor", "--faction", "2", "teblor", "--resources", "high");
            KeepAlive(0);
            NVector2 block = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0];
            BuildingDef def = _data.Buildings[type];
            if (def.RequiresTechs.Contains(age))
            {
                Check(Spawn(0, BuildingOf(BuildingSlot.InfantryHall), block + new NVector2(-16f, 60f)) >= 0, "(C) no infantry hall");
                Check(Spawn(0, BuildingOf(BuildingSlot.RangedHall), block + new NVector2(-4f, 60f)) >= 0, "(C) no ranged hall");
                if (_keep >= 0)
                {
                    _sim.Enqueue(Command.Research(0, Centre(_keep), age));
                    for (int t = 0; t < 4000 && !W.HasTech(0, age); t++) _sim.Tick();
                }
                Check(W.HasTech(0, age), $"(C) {def.Key}: Age II not researched");
            }
            await SiteRow(type, block + new NVector2(-40f, 0f), gatherer);
            await EndMatch();
        }
    }

    private async Task SiteRow(int type, NVector2 near, int gatherer)
    {
        BuildingDef def = _data.Buildings[type];
        int anchor = OpenAnchor(def, near, clearUnits: true);
        if (!Check(anchor >= 0, $"(C) {def.Key}: no open anchor")) return;
        NVector2 c = StartBase.FootprintCenter(W.NavGrid, def, anchor);
        var builders = new List<int>();
        for (int b = 0; b < 3; b++)
        {
            int u = SpawnUnit(0, gatherer, c + new NVector2(-2.5f + 2.5f * b, def.FootprintHeight * MapConstants.CellSize / 2f + 1.6f));
            if (u >= 0) builders.Add(u);
        }
        if (!Check(builders.Count > 0, $"(C) {def.Key}: no builder")) return;
        for (int t = 0; t < 6; t++) _sim.Tick();
        foreach (int u in builders)
            _sim.Enqueue(Command.Build(0, new EntityHandle(u, W.Units.Generation[u]), type, PlacementGhost.AnchorPoint(W.NavGrid, anchor)));
        _camera.SetZoom(CameraLimits.DefaultZoom);
        _camera.SetFocus(c.X, c.Y);
        int site = -1;
        bool done = false;
        float siteMax = 0f;
        for (int t = 0; t < 20000 && !done; t++)
        {
            _sim.Tick();
            int s = B.SlotAt(anchor % W.NavGrid.Width, anchor / W.NavGrid.Width);
            if (s >= 0 && B.TypeId[s] == type) site = s;
            if (site >= 0 && B.Alive[site] && !B.UnderConstruction[site]) done = true;
            else if (site >= 0 && t % 20 == 0)
            {
                _views.Sync(W);
                MeshInstance3D sb = _views.BoxOf(site);
                siteMax = Math.Max(siteMax, sb.Visible && sb.Mesh is BoxMesh bm ? bm.Size.Y * sb.Scale.Y : 0f);
                Check(!_views.ShownAsModel(site), $"(C) {def.Key}: the site is drawn as the model before it is finished");
            }
        }
        if (!Check(done, $"(C) {def.Key}: the site was not finished (site {site}, gold {W.Gold[0]}, wood {W.Wood[0]})")) return;
        _views.Sync(W);
        await Frames(1);
        MeshInstance3D? m = _views.ModelOf(site);
        bool shown = _views.ShownAsModel(site) && m != null && m.Visible && !_views.BoxOf(site).Visible && !_views.ShownAsSite(site);
        if (!Check(shown, $"(C) {def.Key}: not its model on the first sync after it finished")) return;
        Aabb box = m!.GlobalTransform * m.Mesh.GetAabb();
        float ground = TerrainHeight.At(W.Heightmap, c.X, c.Y), h = BaseFit(def).Size.Y * def.Scale;
        GD.Print($"  (C) {def.Key}: site box at most {siteMax:0.##} m, the model on the first sync {box.Size.Y:0.###} m tall (want {h:0.###}), base {box.Position.Y - ground:0.####} m, bar {_views.BarY(site):0.###}");
        Check(Math.Abs(box.Size.Y - h) < 0.01f && Math.Abs(box.Position.Y - ground) < 0.01f, $"(C) {def.Key}: finished model {box.Size.Y} m tall at {box.Position.Y - ground}, want {h} on the ground");
        Check(_views.BarY(site) - BuildingViews.BarThickness / 2f > box.Size.Y || _views.BarZ(site) != 0f, $"(C) {def.Key}: bar {_views.BarY(site)} under the finished top {box.Size.Y}");
    }

    // ---- (G) ----

    private async Task GhostRows(int[] types)
    {
        StartMatch("--players", "3", "--faction", "1", "teblor", "--faction", "2", "teblor", "--faction", "3", "teblor", "--resources", "high");
        KeepAlive(0);
        KeepAlive(1, new NVector2(-30f, 90f));
        KeepAlive(2, new NVector2(-30f, 120f));
        NVector2 o = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-46f, -30f);
        var enemy = new List<int>();
        int k = 0;
        foreach (int type in types)
        {
            int s = Spawn(1, type, o + new NVector2(14f * (k % 4), 18f * (k / 4)));
            k++;
            if (Check(s >= 0, $"(G) {_data.Buildings[type].Key} not spawned")) enemy.Add(s);
        }
        // A player-0 scout in front of each, until all are seen, then the scouts go.
        int gatherer = UnitOf(UnitSlot.Worker);
        var scouts = new List<int>();
        foreach (int s in enemy) { int u = SpawnUnit(0, gatherer, Centre(s) + new NVector2(0f, _data.Buildings[B.TypeId[s]].FootprintHeight * MapConstants.CellSize / 2f + 3f)); if (u >= 0) scouts.Add(u); }
        for (int t = 0; t < 12; t++) _sim.Tick();
        foreach (int u in scouts) if (W.Units.Alive[u]) W.Units.Free(new EntityHandle(u, W.Units.Generation[u]));
        for (int t = 0; t < 12; t++) _sim.Tick();
        // Player 2 razes them all (out of player 0's sight).
        int warrior = UnitOf(UnitSlot.Line);
        var gens = new Dictionary<int, int>();
        foreach (int s in enemy)
        {
            gens[s] = B.Generation[s];
            NVector2 c = Centre(s);
            float r = _data.Buildings[B.TypeId[s]].FootprintHeight * MapConstants.CellSize / 2f + 1.5f;
            for (int j = 0; j < 8; j++) _sim.Enqueue(Command.SpawnUnit(2, warrior, c + new NVector2(-4f + 1.2f * j, j % 2 == 0 ? r : -r)));
        }
        _sim.Tick();
        _sim.Tick();
        for (int t = 0; t < 6000; t++)
        {
            bool any = false;
            foreach (int s in enemy) if (B.Alive[s] && B.Generation[s] == gens[s]) any = true;
            if (!any) break;
            _sim.Tick();
        }
        foreach (int s in enemy) Check(!(B.Alive[s] && B.Generation[s] == gens[s]), $"(G) slot {s} not razed in 6000 ticks");
        // Player 2's warriors go before player 0 might see them.
        for (int i = 0; i < W.Units.Capacity; i++) if (W.Units.Alive[i] && W.Units.Owner[i] == 2) W.Units.Free(new EntityHandle(i, W.Units.Generation[i]));
        for (int t = 0; t < 6; t++) _sim.Tick();
        _views.Sync(W);
        await Frames(2);
        _views.Sync(W);
        foreach (int s in enemy)
        {
            int type = -1;
            // The remembered type: the ghost's mesh is the type's mesh.
            MeshInstance3D? gm = _views.GhostModelOf(s);
            if (!Check(_views.GhostShownAsModel(s) && gm != null && gm.Visible, $"(G) slot {s}: no remembered model after it was razed in fog")) continue;
            for (int t = 0; t < _data.Buildings.Length; t++) if (_views.Models!.Has(t) && _views.Models.MeshOf(t) == gm!.Mesh) type = t;
            if (!Check(type >= 0, $"(G) slot {s}: the ghost's mesh is no type's")) continue;
            BuildingDef def = _data.Buildings[type];
            Aabb box = gm!.GlobalTransform * gm.Mesh.GetAabb();
            NVector2 c = new(box.GetCenter().X, box.GetCenter().Z);
            float ground = TerrainHeight.At(W.Heightmap, c.X, c.Y), h = BaseFit(def).Size.Y * def.Scale;
            Vector3 top = new(c.X, ground + h - 0.05f, c.Y);
            GD.Print($"  (G) {def.Key}: ghost model {box.Size.Y:0.###} m tall (want {h:0.###}), base {box.Position.Y - ground:0.####} m");
            Check(Math.Abs(box.Size.Y - h) < 0.01f && Math.Abs(box.Position.Y - ground) < 0.01f, $"(G) {def.Key}: ghost {box.Size.Y} m tall at {box.Position.Y - ground}, want {h} on the ground");
        }
        await EndMatch();
    }

    // ---- (S) ----

    private async Task SaveLoadRows(int[] types)
    {
        StartMatch("--players", "2", "--no-fog", "--no-combat", "--faction", "1", "teblor", "--faction", "2", "malazan", "--resources", "high");
        KeepAlive(0);
        NVector2 o = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-46f, -30f);
        int k = 0;
        foreach (int type in types) { Spawn(0, type, o + new NVector2(14f * (k % 4), 18f * (k / 4))); k++; }
        int[] mal = ModelTypes(_data.FindFaction("malazan"));
        Spawn(1, BuildingOf(BuildingSlot.TownHall, _data.FindFaction("malazan")), o + new NVector2(0f, 60f));
        k = 0;
        foreach (int type in mal) { Spawn(1, type, o + new NVector2(14f * (k % 4), 80f + 18f * (k / 4))); k++; }
        _views.Sync(W);
        var before = new Dictionary<int, (int Type, Transform3D T)>();
        for (int i = 0; i < B.Capacity; i++)
            if (B.Alive[i] && _views.ShownAsModel(i)) before[i] = (B.TypeId[i], _views.ModelOf(i)!.GlobalTransform);
        ulong hash = _sim.StateHash();
        Directory.CreateDirectory(_scratch);
        string path = Path.Combine(_scratch, "teblor.save");
        bool wrote = SaveStore.Write(_sim, path, out SaveError refused, out string io);
        await EndMatch();
        if (!Check(wrote, $"(S) save not written: {refused} {io}")) return;
        Simulation? sim = SaveStore.Load(_data, path, out SaveError err, out bool openFailed);
        if (!Check(sim != null, $"(S) save not loaded: {err} {openFailed}")) return;
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        _match.StartLoaded(_data, sim!, LaunchOptions.Parse(new[] { "--no-fog", "--mute" }), path);
        Bind();
        await Frames(2);
        _views.Sync(W);
        Check(_sim.StateHash() == hash, "(S) the loaded state hash differs");
        int same = 0;
        foreach (var (slot, (type, t)) in before)
        {
            bool ok = B.Alive[slot] && B.TypeId[slot] == type && _views.ShownAsModel(slot) && _views.ModelOf(slot) is MeshInstance3D m
                      && m.GlobalTransform.IsEqualApprox(t);
            if (Check(ok, $"(S) {_data.Buildings[type].Key} (slot {slot}): not the same model after the load")) same++;
        }
        GD.Print($"(S) {same} of {before.Count} models drawn the same after the load (Teblor {types.Length}, Malazan {mal.Length} types)");
        Check(before.Count >= types.Length + mal.Length, $"(S) only {before.Count} models before the save");
        await EndMatch();
    }

    // ---- (K) ----

    private async Task PickRows()
    {
        StartMatch("--players", "2", "--no-fog", "--no-combat", "--faction", "1", "teblor", "--faction", "2", "teblor", "--resources", "high");
        KeepAlive(0, new NVector2(-30f, 80f));
        NVector2 block = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0];

        // A Gatherer wholly behind the Longhouse (north of it, away from the camera).
        int hall = BuildingOf(BuildingSlot.TownHall);
        int ls = Spawn(0, hall, block + new NVector2(-30f, 0f));
        if (Check(ls >= 0 && _views.Models!.Has(hall), "(K) no Longhouse model"))
        {
            BuildingDef hd = _data.Buildings[hall];
            NVector2 lc = Centre(ls);
            // Gatherers along the back (north) edge, 2 m apart, 1 m behind it.
            var gs = new List<int>();
            for (int k = -2; k <= 2; k++)
            {
                int g = SpawnUnit(0, UnitOf(UnitSlot.Worker), lc + new NVector2(2f * k, -(hd.FootprintHeight * MapConstants.CellSize / 2f + 1.0f)));
                if (g >= 0) gs.Add(g);
            }
            Check(gs.Count > 0, "(K) no Gatherer behind the Longhouse");
            _views.Sync(W);
            int wholly = 0;
            foreach (float zoom in new[] { CameraLimits.DefaultZoom, CameraLimits.MaxZoom })
            {
                _camera.SetZoom(zoom);
                _camera.SetFocus(lc.X, lc.Y);
                await Frames(2);
                foreach (int g in gs)
                {
                    NVector2 up = W.Units.Position[g];
                    float gy = TerrainHeight.At(W.Heightmap, up.X, up.Y);
                    float uh = UnitViews.DrawnHeight(_data.Units[W.Units.TypeId[g]]);
                    // Wholly hidden: its body's axis from the feet to the top, each ray from the camera meets the model first.
                    int hidden = 0, samples = 0;
                    bool topHidden = false;
                    for (int s = 0; s <= 8; s++)
                    {
                        Vector3 pt = new(up.X, gy + uh * s / 8f, up.Y);
                        Vector2 p = _camera.UnprojectPosition(pt);
                        Vector3 o = _camera.ProjectRayOrigin(p), d = _camera.ProjectRayNormal(p);
                        samples++;
                        bool h = _views.ModelRayDistance(ls, o, d) < (pt - o).Length() - 0.05f;
                        if (h) hidden++;
                        if (s == 8) topHidden = h;
                    }
                    if (hidden == samples) wholly++;
                    _sel.ClearBuilding();
                    _sel.Selection.Clear();
                    _sel.TryScreenPosition(g, out Vector2 at);
                    Vector3 ro = _camera.ProjectRayOrigin(at), rd = _camera.ProjectRayNormal(at);
                    float tModel = _views.ModelRayDistance(ls, ro, rd);
                    Vector3 bc = new(up.X, gy + uh / 2f, up.Y);
                    bool modelFirst = tModel < (bc - ro).Length() - 0.5f;
                    Click(at);
                    bool unitPicked = _sel.Selection.Contains(new EntityHandle(g, W.Units.Generation[g]));
                    string what = unitPicked ? "the Gatherer" : _sel.SelectedBuilding == ls ? "the Longhouse" : $"building {_sel.SelectedBuilding}, {_sel.Selection.Count} units";
                    GD.Print($"  (K) zoom {zoom}: Gatherer at x {up.X - lc.X:0.#} ({uh:0.##} m) behind the Longhouse ({_views.Models!.Top(hall):0.##} m): {hidden} of {samples} body points hidden, the model {(modelFirst ? "in front of" : "not in front of")} its centre; a click on it selected {what}");
                    // The rule (SelectionController.HiddenByBuilding, M6-VH9): the building when it is drawn in front of the click and
                    // over the unit's top; a unit whose head shows is still the unit.
                    if (hidden == samples || (modelFirst && topHidden)) Check(!unitPicked && _sel.SelectedBuilding == ls, $"(K) zoom {zoom}: a click where the Longhouse hides a Gatherer (hidden {hidden}/{samples}) selected {what}");
                    else if (!modelFirst && hidden == 0) Check(unitPicked, $"(K) zoom {zoom}: a click on a visible Gatherer behind the Longhouse selected {what}");
                    else Check(unitPicked || _sel.SelectedBuilding == ls, $"(K) zoom {zoom}: a click on a partly hidden Gatherer selected {what}");
                }
            }
            GD.Print($"  (K) {wholly} Gatherer-zoom cases wholly hidden behind the Longhouse");
        }

        // A Warrior in front of a Hut at zoom 60 (and 20, 40).
        int hut = BuildingOf(BuildingSlot.House);
        int hs = Spawn(0, hut, block + new NVector2(0f, 0f));
        if (Check(hs >= 0 && _views.Models!.Has(hut), "(K) no Hut model"))
        {
            NVector2 hc = Centre(hs);
            int wr = SpawnUnit(0, UnitOf(UnitSlot.Line), hc + new NVector2(0f, _data.Buildings[hut].FootprintHeight * MapConstants.CellSize / 2f + 1.2f));
            if (Check(wr >= 0, "(K) no Warrior"))
            {
                _views.Sync(W);
                foreach (float zoom in new[] { CameraLimits.MinZoom, CameraLimits.DefaultZoom, CameraLimits.MaxZoom })
                {
                    _camera.SetZoom(zoom);
                    _camera.SetFocus(hc.X, hc.Y);
                    await Frames(2);
                    _sel.ClearBuilding();
                    _sel.Selection.Clear();
                    _sel.TryScreenPosition(wr, out Vector2 at);
                    Click(at);
                    bool ok = _sel.Selection.Contains(new EntityHandle(wr, W.Units.Generation[wr])) && _sel.SelectedBuilding < 0;
                    GD.Print($"  (K) zoom {zoom}: a click on the Warrior in front of the Hut selected {(ok ? "the Warrior" : $"building {_sel.SelectedBuilding}")}");
                    Check(ok, $"(K) zoom {zoom}: a click on the Warrior in front of the Hut did not select it (building {_sel.SelectedBuilding})");
                }
                W.Units.Free(new EntityHandle(wr, W.Units.Generation[wr]));
                _sim.Tick();
                _views.Sync(W);
            }
        }

        // The Cairn's top: the highest model pixels at each zoom.
        int cairn = BuildingOf(BuildingSlot.WatchTower);
        int cs = Spawn(0, cairn, block + new NVector2(16f, 0f));
        if (Check(cs >= 0 && _views.Models!.Has(cairn), "(K) no Cairn model"))
        {
            NVector2 cc = Centre(cs);
            _views.Sync(W);
            foreach (float zoom in new[] { CameraLimits.MinZoom, CameraLimits.DefaultZoom, CameraLimits.MaxZoom })
            {
                _camera.SetZoom(zoom);
                _camera.SetFocus(cc.X, cc.Y);
                await Frames(2);
                // Scan screen rows from the top of the model's screen rectangle; the first row with a model hit.
                MeshInstance3D m = _views.ModelOf(cs)!;
                Aabb box = m.GlobalTransform * m.Mesh.GetAabb();
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                for (int e = 0; e < 8; e++)
                {
                    Vector2 s = _camera.UnprojectPosition(box.GetEndpoint(e));
                    x0 = Math.Min(x0, s.X); y0 = Math.Min(y0, s.Y); x1 = Math.Max(x1, s.X); y1 = Math.Max(y1, s.Y);
                }
                int tested = 0, picked = 0;
                for (float y = y0; y <= y1 && tested < 6; y += 1f)
                    for (float x = x0; x <= x1 && tested < 6; x += 2f)
                    {
                        var p = new Vector2(x, y);
                        if (!float.IsFinite(_views.ModelRayDistance(cs, _camera.ProjectRayOrigin(p), _camera.ProjectRayNormal(p)))) continue;
                        tested++;
                        _sel.ClearBuilding();
                        _sel.Selection.Clear();
                        Click(p);
                        if (_sel.SelectedBuilding == cs) picked++;
                    }
                GD.Print($"  (K) zoom {zoom}: {picked} of {tested} clicks on the Cairn's topmost model pixels selected it");
                Check(tested > 0 && picked == tested, $"(K) zoom {zoom}: {picked} of {tested} clicks on the Cairn's top selected it");
            }
        }
        await EndMatch();
    }

    // ---- helpers ----

    private int _keep = -1;

    private void KeepAlive(int player, NVector2? spot = null)
    {
        NVector2 at = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + (spot ?? new NVector2(-30f, 60f));
        int faction = W.FactionOf(player);
        int slot = Spawn(player, BuildingOf(BuildingSlot.TownHall, faction), at);
        if (player == 0) _keep = slot;
        Check(slot >= 0, $"no keep-alive hall for player {player}");
    }

    private int UnitOf(UnitSlot slot)
    {
        for (int u = 0; u < _data.Units.Length; u++) if (_data.Units[u].Faction == _teblor && _data.Units[u].Slot == slot) return u;
        return -1;
    }

    private int BuildingOf(BuildingSlot slot, int faction = -1)
    {
        if (faction < 0) faction = _teblor;
        for (int b = 0; b < _data.Buildings.Length; b++) if (_data.Buildings[b].Faction == faction && _data.Buildings[b].Slot == slot) return b;
        return -1;
    }

    private void StartMatch(params string[] extra)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        var args = new List<string> { "--seed", "1", "--units", "0", "--no-bases", "--mute" };
        args.AddRange(extra);
        _match.Start(_data, LaunchOptions.Parse(args.ToArray()));
        Bind();
        _sim.Tick();
        _sim.Tick();
    }

    private void Bind()
    {
        var runner = _match.GetNode<SimRunner>("SimRunner");
        runner.ProcessMode = ProcessModeEnum.Disabled;
        _sim = runner.Simulation!;
        _views = _match.GetNode<BuildingViews>("World3D/BuildingViews");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _sel = _match.GetNode<SelectionController>("SelectionController");
    }

    private async Task EndMatch()
    {
        RemoveChild(_match);
        _match.QueueFree();
        await Frames(1);
    }

    private int Spawn(int player, int type, NVector2 near)
    {
        if (type < 0) return -1;
        int anchor = OpenAnchor(_data.Buildings[type], near);
        return anchor < 0 ? -1 : SpawnAt(player, type, anchor);
    }

    private int SpawnAt(int player, int type, int anchor)
    {
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

    private int OpenAnchor(BuildingDef def, NVector2 near, bool clearUnits = false)
    {
        int type = _data.FindBuilding(def.Key);
        NavGrid g = W.NavGrid;
        for (int r = 0; r <= 14; r++)
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
                    if (ok && clearUnits)
                    {
                        float x0 = cx * MapConstants.CellSize - 2f, z0 = cy * MapConstants.CellSize - 2f;
                        float x1 = (cx + def.FootprintWidth) * MapConstants.CellSize + 2f, z1 = (cy + def.FootprintHeight) * MapConstants.CellSize + 2f;
                        for (int i = 0; i < W.Units.Capacity && ok; i++)
                        {
                            if (!W.Units.Alive[i]) continue;
                            NVector2 p = W.Units.Position[i];
                            if (p.X > x0 && p.X < x1 && p.Y > z0 && p.Y < z1) ok = false;
                        }
                    }
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
