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
using Rts.Sim.ViewApi;
using NVector2 = System.Numerics.Vector2;

namespace Rts.Game.Tests;

/// <summary>
/// M6-V28: the Billet, the Quartermaster's Depot, the Crossbow Range and the Watchtower drawn as models: the rows
/// <c>BuildingModelTest</c> (files, fit on flat ground, life cycle, fallbacks, team colour, for every manifest building) and
/// <c>QaM6V26Test</c> (every terrace anchor, every model pixel, the bar at both zoom ends) don't make for these four.
/// </summary>
/// <remarks>
/// Headless, in the scene loop: <c>&amp; $env:GODOT --headless --path game res://tests/MalazanBuildingsTest.tscn</c>; prints
/// "MALAZAN BUILDINGS TEST PASS" or FAIL lines. The four are the manifest's buildings past the M6-V26 pair, read by id from
/// the data (no count literal). Rows:
/// <list type="bullet">
/// <item>(T) Terrace edge (criterion 3): each of the four spawned at a terrace's foot and on its edge (anchors whose
/// surroundings hold another level, the first on level 0 and the first on level 1 or more): the model
/// inside its footprint by <see cref="BuildingModels.FitMargin"/>, its base on the ground, the ground flat under its plan.</item>
/// <item>(P) Picking (criterion 5): an own Laborer standing in front of each (toward the camera) is what a click on it
/// selects at zoom 20, 40 and 60; a click on a sampled pixel of the model (every 12th, zoom 40) selects the building; the
/// bar's both ends on screen at the closest zoom and over the drawn top when not lowered.</item>
/// <item>(S) The Watchtower's shot (criterion 6): on flat ground and on a terrace above its target, the aimed shot's start
/// height is the type's <see cref="BuildingViews.ShotHeights"/> entry, that is <see cref="BuildingViews.ModelShotShare"/> of the
/// model's fitted top, not the 1.2 m a unit's chest is; the shot is drawn there on its first frame (over the ground at its
/// launch point); the roof's lowest point (the team tile's faces) is printed beside it. A Watchtower site with an enemy in
/// range fires nothing in 200 ticks.</item>
/// </list>
/// Windowed with <c>-- --shots &lt;dir&gt;</c> it saves <c>m6v28-&lt;id&gt;-40m</c> / <c>-20m</c> for each of the four,
/// <c>m6v28-base-40m</c> / <c>-60m</c> / <c>-40m-gray</c> (one Malazan base: the six models and the four box types),
/// <c>m6v28-billet-depot-40m</c>, <c>m6v28-range-barracks-40m</c>, <c>m6v28-four-colours-40m</c> (Bone, Crimson, Iron, Jade),
/// <c>m6v28-sites-40m</c> and <c>m6v28-tower-shot-20m</c>.
/// </remarks>
public partial class MalazanBuildingsTest : Node
{
    private const string Tag = "MALAZAN BUILDINGS TEST";

    // The four building ids this task draws as models (docs/04 "Building models, as built", M6-V28).
    private static readonly string[] Ids = { "malazan_billet", "malazan_depot", "malazan_crossbow_range", "malazan_watchtower" };

    private readonly List<string> _failures = new();
    private string? _shots;
    private GameData _data = null!;
    private Match _match = null!;
    private Simulation _sim = null!;
    private BuildingViews _views = null!;
    private ProjectileViews _shotViews = null!;
    private RtsCamera _camera = null!;
    private SelectionController _sel = null!;

    private World W => _sim.World;
    private BuildingStore B => _sim.World.Buildings;

    public override async void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(args, "--shots");
        if (at >= 0 && at + 1 < args.Length && DisplayServer.GetName() != "headless") { _shots = args[at + 1]; Directory.CreateDirectory(_shots); }
        UnitViews.UseModels = _shots != null; // figures only for the pictures
        BuildingViews.UseModels = true;
        try
        {
            GetTree().Root.Size = new Vector2I(1600, 900);
            await Frames(1);
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            int[] types = Types();
            if (types.Length == Ids.Length)
            {
                await TerraceAndPickRows(types);
                await ShotRows();
            }
            if (_shots != null) await Pictures(types);
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        UnitViews.UseModels = true;
        BuildingViews.UseModels = true;
        foreach (string f in _failures) GD.Print($"{Tag} FAIL: {f}");
        if (_failures.Count == 0) GD.Print($"{Tag} PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private int[] Types()
    {
        var list = new List<int>();
        foreach (string id in Ids)
        {
            int t = _data.FindBuilding(id);
            if (Check(t >= 0, $"no building {id} in the data")) list.Add(t);
        }
        return list.ToArray();
    }

    // ---- (T) terrace edge, (P) picking and the bar ----

    private async Task TerraceAndPickRows(int[] types)
    {
        StartMatch("--players", "2", "--no-fog", "--no-combat", "--faction", "1", "malazan", "--faction", "2", "malazan", "--resources", "high");
        if (!Check(_views.Models is BuildingModels, "the match's building views have no models")) { await EndMatch(); return; }
        foreach (int type in types) Check(_views.Models!.Has(type), $"{_data.Buildings[type].Key}: not drawn as a model");
        KeepAlive();

        // (T) Two anchors per type beside a step, the first in row order whose surroundings hold another level: one at the foot
        // of a terrace (level 0) and one on its edge (level 1 or more).
        foreach (int type in types)
        foreach (bool high in new[] { false, true })
        {
            BuildingDef def = _data.Buildings[type];
            int anchor = StepAnchor(def, high);
            if (!Check(anchor >= 0, $"(T) {def.Key}: no anchor beside a step {(high ? "on a terrace" : "at a terrace's foot")} on this map")) continue;
            _sim.Enqueue(Command.SpawnBuilding(0, type, PlacementGhost.AnchorPoint(W.NavGrid, anchor)));
            _sim.Tick();
            _sim.Tick();
            int slot = B.SlotAt(anchor % W.NavGrid.Width, anchor / W.NavGrid.Width);
            if (!Check(slot >= 0 && B.TypeId[slot] == type, $"(T) {def.Key}: not spawned at {anchor}")) continue;
            _views.Sync(W);
            MeshInstance3D? m = _views.ModelOf(slot);
            if (!Check(_views.ShownAsModel(slot) && m != null && m.Visible, $"(T) {def.Key}: not drawn as its model")) continue;
            Aabb box = m!.GlobalTransform * m.Mesh.GetAabb();
            float x0 = anchor % W.NavGrid.Width * MapConstants.CellSize, z0 = anchor / W.NavGrid.Width * MapConstants.CellSize;
            float x1 = x0 + def.FootprintWidth * MapConstants.CellSize, z1 = z0 + def.FootprintHeight * MapConstants.CellSize;
            float gap = Math.Min(Math.Min(box.Position.X - x0, x1 - box.End.X), Math.Min(box.Position.Z - z0, z1 - box.End.Z));
            NVector2 c = Centre(slot);
            float ground = TerrainHeight.At(W.Heightmap, c.X, c.Y), worst = 0f;
            for (int sy = 0; sy <= 8; sy++)
                for (int sx = 0; sx <= 8; sx++)
                    worst = Math.Max(worst, Math.Abs(TerrainHeight.At(W.Heightmap, box.Position.X + box.Size.X * sx / 8f, box.Position.Z + box.Size.Z * sy / 8f) - ground));
            GD.Print($"  (T) {def.Key} beside a step (level {W.NavGrid.LevelAt(anchor % W.NavGrid.Width, anchor / W.NavGrid.Width)}): nearest edge gap {gap:0.###} m, "
                     + $"base {box.Position.Y - ground:0.####} m over the ground, ground under the plan within {worst:0.####} m");
            Check(gap >= BuildingModels.FitMargin - 1e-3f, $"(T) {def.Key}: {gap} m from its footprint's nearest edge, want {BuildingModels.FitMargin}");
            Check(Math.Abs(box.Position.Y - ground) < 0.01f, $"(T) {def.Key}: base {box.Position.Y} m, ground {ground} m");
            Check(worst < 0.01f, $"(T) {def.Key}: the ground under the plan differs from the base by {worst} m");
        }

        // (P) A Laborer in front of each; the model's pixels; the bar at the closest zoom.
        NVector2 near = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-30f, 0f);
        foreach (int type in types)
        {
            BuildingDef def = _data.Buildings[type];
            int slot = Spawn(0, type, near);
            near += new NVector2(0f, 22f);
            if (!Check(slot >= 0, $"(P) {def.Key} not spawned")) continue;
            NVector2 c = Centre(slot);
            int unit = SpawnUnit(0, _data.FindUnit("malazan_laborer"), c + new NVector2(0f, def.FootprintHeight * MapConstants.CellSize / 2f + 1.2f));
            if (!Check(unit >= 0, $"(P) {def.Key}: no laborer in front")) continue;
            _views.Sync(W);
            foreach (float zoom in new[] { CameraLimits.MinZoom, CameraLimits.DefaultZoom, CameraLimits.MaxZoom })
            {
                _camera.SetZoom(zoom);
                _camera.SetFocus(c.X, c.Y);
                await Frames(2);
                _sel.ClearBuilding();
                _sel.Selection.Clear();
                if (!Check(_sel.TryScreenPosition(unit, out Vector2 p), $"(P) {def.Key}: the unit is behind the camera")) continue;
                Click(p);
                bool picked = _sel.Selection.Contains(new EntityHandle(unit, W.Units.Generation[unit])) && _sel.SelectedBuilding < 0;
                string msg = $"(P) {def.Key} at zoom {zoom}: a click on the Laborer in front selected "
                             + (picked ? "the Laborer" : _sel.SelectedBuilding == slot ? "the building" : $"{_sel.Selection.Count} units, building {_sel.SelectedBuilding}");
                GD.Print($"  {msg}");
                Check(picked, msg);
            }
            // The model's pixels at the default zoom (the Laborer gone, so only the building is there).
            W.Units.Free(new EntityHandle(unit, W.Units.Generation[unit])); // staging between ticks
            _sim.Tick();
            _views.Sync(W);
            _camera.SetZoom(CameraLimits.DefaultZoom);
            _camera.SetFocus(c.X, c.Y);
            await Frames(2);
            (int hits, int sel) = ModelPixels(slot);
            GD.Print($"  (P) {def.Key} at zoom {CameraLimits.DefaultZoom}: {sel} of {hits} sampled model pixels select it");
            Check(hits > 0 && sel == hits, $"(P) {def.Key}: {sel} of {hits} sampled model pixels select it");
            // The bar at the closest zoom: both ends on screen, over the drawn top unless the ceiling lowered it to the front.
            _camera.SetZoom(CameraLimits.MinZoom);
            _camera.SetFocus(c.X, c.Y);
            await Frames(2);
            _views.Sync(W);
            float ground = TerrainHeight.At(W.Heightmap, c.X, c.Y), half = def.FootprintWidth * MapConstants.CellSize * BuildingViews.BarLengthShare / 2f;
            Vector3 l = new(c.X - half, ground + _views.BarY(slot), c.Y + _views.BarZ(slot)), r = new(c.X + half, ground + _views.BarY(slot), c.Y + _views.BarZ(slot));
            Rect2 screen = GetViewport().GetVisibleRect();
            bool on = screen.HasPoint(_camera.UnprojectPosition(l)) && screen.HasPoint(_camera.UnprojectPosition(r));
            float top = _views.Models!.Top(type);
            bool over = _views.BarZ(slot) != 0f || _views.BarY(slot) - BuildingViews.BarThickness / 2f > top;
            GD.Print($"  (P) {def.Key} bar at zoom {CameraLimits.MinZoom}: {_views.BarY(slot):0.###} m (model top {top:0.###} m, front offset {_views.BarZ(slot):0.##} m), {(on ? "on" : "off")} screen");
            Check(on && over, $"(P) {def.Key}: bar at {_views.BarY(slot)} m (top {top} m) on screen {on} at the closest zoom");
        }
        await EndMatch();
    }

    // The first anchor (row order) where the type fits with a free cell round it and a cell of another level lies within two cells.
    private int StepAnchor(BuildingDef def, bool high)
    {
        int type = _data.FindBuilding(def.Key);
        NavGrid g = W.NavGrid;
        for (int cy = 4; cy + def.FootprintHeight < g.Height - 4; cy++)
            for (int cx = 4; cx + def.FootprintWidth < g.Width - 4; cx++)
            {
                int a = cy * g.Width + cx;
                if (!B.Fits(type, a)) continue;
                int level = g.LevelAt(cx, cy);
                if (high != level >= 1) continue;
                bool free = true, step = false;
                for (int y = cy - 2; y <= cy + def.FootprintHeight + 1 && free; y++)
                    for (int x = cx - 2; x <= cx + def.FootprintWidth + 1; x++)
                    {
                        bool inside = x >= cx && x < cx + def.FootprintWidth && y >= cy && y < cy + def.FootprintHeight;
                        if (B.SlotAt(x, y) >= 0) { free = false; break; }
                        if (!inside && g.LevelAt(x, y) != level) step = true;
                    }
                if (free && step) return a;
            }
        return -1;
    }

    // Every 12th pixel over the model's screen rectangle whose ray meets the drawn model: how many, and how many a click selects it at.
    private (int Hits, int Picked) ModelPixels(int slot)
    {
        MeshInstance3D m = _views.ModelOf(slot)!;
        Aabb box = m.GlobalTransform * m.Mesh.GetAabb();
        Rect2 screen = GetViewport().GetVisibleRect();
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        for (int k = 0; k < 8; k++)
        {
            Vector2 s = _camera.UnprojectPosition(box.GetEndpoint(k));
            x0 = Math.Min(x0, s.X); y0 = Math.Min(y0, s.Y); x1 = Math.Max(x1, s.X); y1 = Math.Max(y1, s.Y);
        }
        x0 = Math.Max(x0, 1f); y0 = Math.Max(y0, 1f); x1 = Math.Min(x1, screen.Size.X - 2f); y1 = Math.Min(y1, screen.Size.Y - 2f);
        int hits = 0, picked = 0;
        for (float y = y0; y <= y1; y += 12f)
            for (float x = x0; x <= x1; x += 12f)
            {
                var p = new Vector2(x, y);
                if (!float.IsFinite(_views.ModelRayDistance(slot, _camera.ProjectRayOrigin(p), _camera.ProjectRayNormal(p)))) continue;
                hits++;
                _sel.ClearBuilding();
                _sel.Selection.Clear();
                Click(p);
                if (_sel.SelectedBuilding == slot) picked++;
            }
        _sel.ClearBuilding();
        return (hits, picked);
    }

    // ---- (S) the Watchtower's shot ----

    private async Task ShotRows()
    {
        StartMatch("--players", "2", "--no-fog", "--faction", "1", "malazan", "--faction", "2", "malazan", "--resources", "high");
        int tower = _data.FindBuilding("malazan_watchtower");
        if (!Check(_views.Models is BuildingModels models && models.Has(tower), "(S) the Watchtower has no model")) { await EndMatch(); return; }
        KeepAlive();
        float top = _views.Models!.Top(tower), want = _views.ShotHeights[tower], eaves = RoofLowest(tower);
        GD.Print($"(S) Watchtower: model top {top:0.###} m, roof's lowest point {eaves:0.###} m ({eaves / top:0.###} of the top), shot height {want:0.###} m");
        Check(Math.Abs(want - top * BuildingViews.ModelShotShare) < 1e-4f, $"(S) the Watchtower's shot height {want} m is not {BuildingViews.ModelShotShare} of its {top} m top");
        Check(want > ProjectileViews.LaunchHeight && want < top && Math.Abs(want - eaves) < 0.5f,
            $"(S) the shot height {want} m is not on the drawn tower under its roof (top {top} m, roof from {eaves} m)");

        // Flat ground: an enemy Heavy Infantry 10 m south, held there.
        NVector2 near = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-30f, 0f);
        int flat = Spawn(0, tower, near);
        if (Check(flat >= 0, "(S) no Watchtower on flat ground"))
            await ShotRow(flat, Centre(flat) + new NVector2(0f, 10f), "flat ground");

        // High ground: a tower on a terrace, its target on a lower level within range.
        (int anchor, NVector2 below) = HighAnchor(tower);
        if (Check(anchor >= 0, "(S) no terrace anchor with lower ground in range on this map"))
        {
            _sim.Enqueue(Command.SpawnBuilding(0, tower, PlacementGhost.AnchorPoint(W.NavGrid, anchor)));
            _sim.Tick();
            _sim.Tick();
            int high = B.SlotAt(anchor % W.NavGrid.Width, anchor / W.NavGrid.Width);
            if (Check(high >= 0 && B.TypeId[high] == tower, "(S) the terrace Watchtower not spawned"))
                await ShotRow(high, below, "a terrace over its target");
        }

        // A tower site with an enemy in range shoots nothing (Age II first: the Watchtower needs it).
        await SiteRow(tower);
        await EndMatch();
    }

    // The tower in `slot` shoots an enemy Heavy Infantry held at `target`: the first shot's start height, as kept and as drawn.
    private async Task ShotRow(int slot, NVector2 target, string where)
    {
        int heavy = SpawnUnit(1, _data.FindUnit("malazan_heavy_infantry"), target);
        if (!Check(heavy >= 0, $"(S) {where}: no target")) return;
        _sim.Enqueue(Command.HoldPosition(1, new EntityHandle(heavy, W.Units.Generation[heavy])));
        var handle = new EntityHandle(slot, B.Generation[slot]);
        int shot = -1;
        for (int t = 0; t < 400 && shot < 0; t++)
        {
            _sim.Tick();
            _shotViews.CollectTick(W);
            for (int i = 0; i < W.Projectiles.Capacity && shot < 0; i++)
            {
                ShotEnds e = ShotEnds.Of(W.Projectiles, i);
                if (e.Live && e.AttackerIsBuilding && e.Attacker == handle) shot = i;
            }
        }
        if (!Check(shot >= 0, $"(S) {where}: the Watchtower fired nothing in 400 ticks")) return;
        _shotViews.Sync(W, 0f);
        int k = -1;
        for (int j = 0; j < _shotViews.Shown; j++) if (_shotViews.DrawnSlot(j) == shot) k = j;
        float from = _shotViews.ShotFromHeight(shot), want = _views.ShotHeights[B.TypeId[slot]];
        System.Numerics.Vector2 launch = _shotViews.Tracker.Launch[shot];
        NVector2 c = Centre(slot);
        float towerGround = TerrainHeight.At(W.Heightmap, c.X, c.Y), targetGround = TerrainHeight.At(W.Heightmap, target.X, target.Y);
        float drawnOver = k >= 0 ? _shotViews.DrawnAt(k).Y - TerrainHeight.At(W.Heightmap, _shotViews.DrawnAt(k).X, _shotViews.DrawnAt(k).Z) : float.NaN;
        float drawnWorld = k >= 0 ? _shotViews.DrawnAt(k).Y : float.NaN;
        GD.Print($"  (S) {where}: tower ground {towerGround:0.##} m, target ground {targetGround:0.##} m; the shot leaves {from:0.###} m up "
                 + $"(want {want:0.###}), drawn on its first frame at {drawnWorld:0.###} m ({drawnOver:0.###} m over the ground there, launch {launch.X:0.#}, {launch.Y:0.#})");
        Check(Math.Abs(from - want) < 1e-4f, $"(S) {where}: the shot leaves {from} m up, want the drawn tower's {want} m");
        Check(k >= 0 && Math.Abs(drawnOver - from) < 0.05f, $"(S) {where}: drawn {drawnOver} m over the ground on its first frame, want {from}");
        Check(k >= 0 && drawnWorld > towerGround + ProjectileViews.LaunchHeight, $"(S) {where}: drawn at {drawnWorld} m, under the old 1.2 m launch over the tower's {towerGround} m ground");
        if (W.Units.Alive[heavy]) W.Units.Free(new EntityHandle(heavy, W.Units.Generation[heavy])); // staging: the target goes
        _sim.Tick();
        await Frames(1);
    }

    // A tower anchor on level 1 or more, flat round it, with a passable cell of a lower level 8-14 m from its centre.
    private (int Anchor, NVector2 Below) HighAnchor(int tower)
    {
        BuildingDef def = _data.Buildings[tower];
        NavGrid g = W.NavGrid;
        for (int cy = 6; cy + def.FootprintHeight < g.Height - 6; cy++)
            for (int cx = 6; cx + def.FootprintWidth < g.Width - 6; cx++)
            {
                int level = g.LevelAt(cx, cy), a = cy * g.Width + cx;
                if (level < 1 || !B.Fits(tower, a)) continue;
                bool flat = true;
                for (int y = cy - 1; y <= cy + def.FootprintHeight && flat; y++)
                    for (int x = cx - 1; x <= cx + def.FootprintWidth; x++)
                        if (g.LevelAt(x, y) != level || B.SlotAt(x, y) >= 0 || !g.IsPassable(x, y)) { flat = false; break; }
                if (!flat) continue;
                NVector2 c = StartBase.FootprintCenter(g, def, a);
                for (int y = cy - 7; y <= cy + 8; y++)
                    for (int x = cx - 7; x <= cx + 8; x++)
                    {
                        if (!g.IsPassable(x, y) || g.LevelAt(x, y) >= level) continue;
                        NVector2 p = g.CellCenter(x, y);
                        float d = NVector2.Distance(p, c);
                        if (d >= 8f && d <= 14f) return (a, p);
                    }
            }
        return (-1, default);
    }

    // The lowest point of the type's fitted faces on the team tile (the Watchtower's roof), meters over the ground.
    private float RoofLowest(int type)
    {
        BuildingModels m = _views.Models!;
        Mesh mesh = m.MeshOf(type)!;
        Godot.Collections.Array arrays = mesh.SurfaceGetArrays(0);
        Vector3[] v = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        Vector2[] uv = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        UnitModels.Atlas atlas = m.AtlasOf(type)!;
        float lowest = float.MaxValue;
        for (int i = 0; i < v.Length; i++)
            if (atlas.TileAt(uv[i]) == "team") lowest = Math.Min(lowest, (m.Fit(type) * v[i]).Y);
        return lowest;
    }

    // Player 0 reaches Age II, a Laborer starts a Watchtower site with an enemy held 10 m away, the builder goes: 200 ticks, no shot.
    private async Task SiteRow(int tower)
    {
        NVector2 block = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0];
        int age = _data.FindTech("age_ii");
        int keep = _keepAlive;
        Check(Spawn(0, _data.FindBuilding("malazan_barracks"), block + new NVector2(-16f, 60f)) >= 0, "(S) site: no Barracks");
        Check(Spawn(0, _data.FindBuilding("malazan_crossbow_range"), block + new NVector2(-4f, 60f)) >= 0, "(S) site: no Crossbow Range");
        if (!Check(keep >= 0, "(S) site: no Keep")) return;
        _sim.Enqueue(Command.Research(0, Centre(keep), age));
        for (int t = 0; t < 4000 && !W.HasTech(0, age); t++) _sim.Tick();
        if (!Check(W.HasTech(0, age), "(S) site: Age II not researched")) return;
        BuildingDef def = _data.Buildings[tower];
        int anchor = OpenAnchor(def, block + new NVector2(-30f, 84f));
        if (!Check(anchor >= 0, "(S) site: no open anchor")) return;
        NVector2 c = StartBase.FootprintCenter(W.NavGrid, def, anchor);
        int builder = SpawnUnit(0, _data.FindUnit("malazan_laborer"), c + new NVector2(0f, def.FootprintHeight * MapConstants.CellSize / 2f + 0.8f));
        if (!Check(builder >= 0, "(S) site: no builder")) return;
        for (int t = 0; t < 10; t++) _sim.Tick(); // its sight explores the footprint
        _sim.Enqueue(Command.Build(0, new EntityHandle(builder, W.Units.Generation[builder]), tower, PlacementGhost.AnchorPoint(W.NavGrid, anchor)));
        int site = -1;
        for (int t = 0; t < 400 && site < 0; t++)
        {
            _sim.Tick();
            int s = B.SlotAt(anchor % W.NavGrid.Width, anchor / W.NavGrid.Width);
            if (s >= 0 && B.UnderConstruction[s]) site = s;
        }
        if (!Check(site >= 0, "(S) site: no Watchtower site placed")) return;
        W.Units.Free(new EntityHandle(builder, W.Units.Generation[builder])); // staging: the builder goes, so the site stays a site
        int heavy = SpawnUnit(1, _data.FindUnit("malazan_heavy_infantry"), c + new NVector2(0f, -10f));
        if (heavy >= 0) _sim.Enqueue(Command.HoldPosition(1, new EntityHandle(heavy, W.Units.Generation[heavy])));
        var handle = new EntityHandle(site, B.Generation[site]);
        int fired = 0;
        for (int t = 0; t < 200; t++)
        {
            _sim.Tick();
            _shotViews.CollectTick(W);
            for (int i = 0; i < W.Projectiles.Capacity; i++)
            {
                ShotEnds e = ShotEnds.Of(W.Projectiles, i);
                if (e.Live && e.AttackerIsBuilding && e.Attacker == handle) fired++;
            }
        }
        _views.Sync(W);
        GD.Print($"  (S) a Watchtower site (still a site: {B.UnderConstruction[site]}, drawn as its box: {_views.ShownAsSite(site) && !_views.ShownAsModel(site)}) with an enemy 10 m away: {fired} shot-ticks in 200 ticks");
        Check(heavy >= 0 && B.UnderConstruction[site] && fired == 0, $"(S) site: {fired} shot-ticks from a Watchtower site (target {heavy})");
        await Frames(1);
    }

    // ---- pictures (windowed) ----

    private async Task Pictures(int[] types)
    {
        int keep = _data.FindBuilding("malazan_garrison_keep"), barracks = _data.FindBuilding("malazan_barracks");
        int laborer = _data.FindUnit("malazan_laborer"), heavy = _data.FindUnit("malazan_heavy_infantry");

        // Each of the four at zoom 40 and 20, with a Laborer and a Heavy Infantry before it for scale.
        StartMatch("--players", "4", "--no-fog", "--no-combat", "--resources", "high", "--faction", "1", "malazan", "--faction", "2", "malazan",
            "--faction", "3", "malazan", "--faction", "4", "malazan", "--colour", "1", "8", "--colour", "2", "3", "--colour", "3", "1", "--colour", "4", "5");
        _match.GetNode<SimRunner>("SimRunner").ProcessMode = ProcessModeEnum.Inherit; // figures animate
        KeepAlive(4, new NVector2(40f, -40f)); // away from the picture rows
        NVector2 o = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-40f, -16f);
        var centre = new Dictionary<int, NVector2>();
        float x = 0f;
        foreach (int type in types)
        {
            int slot = Spawn(0, type, o + new NVector2(x, 0f));
            x += 20f;
            if (slot < 0) continue;
            NVector2 c = Centre(slot);
            centre[type] = c;
            float front = _data.Buildings[type].FootprintHeight * MapConstants.CellSize / 2f + 1.6f;
            _sim.Enqueue(Command.SpawnUnit(0, laborer, c + new NVector2(-1.2f, front)));
            _sim.Enqueue(Command.SpawnUnit(0, heavy, c + new NVector2(1.2f, front)));
        }
        for (int t = 0; t < 4; t++) _sim.Tick();
        foreach (int type in types)
        {
            if (!centre.TryGetValue(type, out NVector2 c)) continue;
            _camera.SetZoom(CameraLimits.DefaultZoom);
            _camera.SetFocus(c.X, c.Y + 2f);
            await Shot($"m6v28-{_data.Buildings[type].Key}-40m");
            _camera.SetZoom(CameraLimits.MinZoom);
            _camera.SetFocus(c.X, c.Y + 2f);
            await Shot($"m6v28-{_data.Buildings[type].Key}-20m");
        }

        // The Billet beside the Depot, the Crossbow Range beside the Barracks (same owner, the default zoom).
        NVector2 pair = o + new NVector2(0f, 30f);
        int billet = _data.FindBuilding("malazan_billet"), depot = _data.FindBuilding("malazan_depot"), range = _data.FindBuilding("malazan_crossbow_range");
        int a = Spawn(0, billet, pair), b = Spawn(0, depot, pair + new NVector2(8f, 0f));
        if (a >= 0 && b >= 0)
        {
            NVector2 m = (Centre(a) + Centre(b)) / 2f;
            _camera.SetZoom(CameraLimits.DefaultZoom);
            _camera.SetFocus(m.X, m.Y + 2f);
            await Shot("m6v28-billet-depot-40m");
        }
        a = Spawn(0, range, pair + new NVector2(30f, 0f));
        b = Spawn(0, barracks, pair + new NVector2(40f, 0f));
        if (a >= 0 && b >= 0)
        {
            NVector2 m = (Centre(a) + Centre(b)) / 2f;
            _camera.SetZoom(CameraLimits.DefaultZoom);
            _camera.SetFocus(m.X, m.Y + 2f);
            await Shot("m6v28-range-barracks-40m");
        }

        // The four in Bone, Crimson, Iron and Jade (players 1-4).
        NVector2 rows = o + new NVector2(0f, 56f);
        var four = new List<NVector2>();
        float z = 0f;
        foreach (int type in types)
        {
            float xx = 0f;
            for (int p = 0; p < 4; p++)
            {
                int slot = Spawn(p, type, rows + new NVector2(xx, z));
                if (slot >= 0) four.Add(Centre(slot));
                xx += _data.Buildings[type].FootprintWidth * MapConstants.CellSize + 3f;
            }
            z += _data.Buildings[type].FootprintHeight * MapConstants.CellSize + 4f;
        }
        if (four.Count > 0)
        {
            _camera.SetZoom(CameraLimits.DefaultZoom);
            _camera.SetFocus((four[0].X + four[^1].X) / 2f, (four[0].Y + four[^1].Y) / 2f + 2f);
            await Shot("m6v28-four-colours-40m");
        }
        await EndMatch();

        // One Malazan base: the Keep, the five other model types and the four box types (player 1, Crimson), Laborers about.
        StartMatch("--players", "2", "--no-fog", "--no-combat", "--resources", "high", "--faction", "1", "malazan", "--faction", "2", "malazan", "--colour", "1", "3");
        _match.GetNode<SimRunner>("SimRunner").ProcessMode = ProcessModeEnum.Inherit;
        NVector2 hq = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-24f, 0f);
        int malazan = _data.FindFaction("malazan");
        // The model types in a ring round the Keep (halls beside it, the small ones in front), the box types behind.
        var layout = new List<(int Type, NVector2 At)>
        {
            (keep, new(0f, 0f)), (barracks, new(-12f, 0f)), (range, new(12f, 0f)),
            (billet, new(-9f, 10f)), (depot, new(-3f, 10f)), (billet, new(3f, 10f)), (_data.FindBuilding("malazan_watchtower"), new(10f, 10f)),
        };
        float bx = -13f;
        for (int t = 0; t < _data.Buildings.Length; t++)
            if (_data.Buildings[t].Faction == malazan && !_views.Models!.Has(t))
            {
                layout.Add((t, new(bx, -12f)));
                bx += 9f;
            }
        int models = 0, boxes = 0;
        foreach ((int type, NVector2 off) in layout)
        {
            int slot = Spawn(0, type, hq + off);
            if (slot < 0) continue;
            if (_views.Models!.Has(type)) models++; else boxes++;
        }        GD.Print($"picture base: {models} model buildings and {boxes} box buildings placed");
        for (int k = 0; k < 6; k++) _sim.Enqueue(Command.SpawnUnit(0, laborer, hq + new NVector2(-5f + 2f * k, 16f)));
        for (int t = 0; t < 4; t++) _sim.Tick();
        _camera.SetZoom(CameraLimits.DefaultZoom);
        _camera.SetFocus(hq.X, hq.Y + 2f);
        await Shot("m6v28-base-40m");
        await Shot("m6v28-base-40m-gray", gray: true);
        _camera.SetZoom(CameraLimits.MaxZoom);
        _camera.SetFocus(hq.X, hq.Y + 4f);
        await Shot("m6v28-base-60m");
        // A Billet and a Depot site rising in front of the finished ones (a site is its box until it is finished).
        _match.GetNode<SimRunner>("SimRunner").ProcessMode = ProcessModeEnum.Disabled;
        foreach ((int type, NVector2 off) in new[] { (billet, new NVector2(-10f, 24f)), (depot, new NVector2(-2f, 24f)) })
        {
            int anchor = OpenAnchor(_data.Buildings[type], hq + off);
            if (anchor < 0) continue;
            NVector2 c = StartBase.FootprintCenter(W.NavGrid, _data.Buildings[type], anchor);
            int builder = SpawnUnit(0, laborer, c + new NVector2(0f, _data.Buildings[type].FootprintHeight * MapConstants.CellSize / 2f + 0.8f));
            for (int t = 0; t < 10; t++) _sim.Tick();
            if (builder >= 0) _sim.Enqueue(Command.Build(0, new EntityHandle(builder, W.Units.Generation[builder]), type, PlacementGhost.AnchorPoint(W.NavGrid, anchor)));
        }
        for (int t = 0; t < 160; t++) _sim.Tick();
        _camera.SetZoom(CameraLimits.DefaultZoom);
        _camera.SetFocus(hq.X - 6f, hq.Y + 16f);
        await Shot("m6v28-sites-40m");
        await EndMatch();

        // The Watchtower shooting at zoom 20.
        StartMatch("--players", "2", "--no-fog", "--faction", "1", "malazan", "--faction", "2", "malazan", "--resources", "high");
        KeepAlive();
        int tower = _data.FindBuilding("malazan_watchtower");
        NVector2 tp = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-30f, 0f);
        int ts = Spawn(0, tower, tp);
        if (ts >= 0)
        {
            NVector2 c = Centre(ts);
            int enemy = SpawnUnit(1, heavy, c + new NVector2(4f, 11f));
            if (enemy >= 0) _sim.Enqueue(Command.HoldPosition(1, new EntityHandle(enemy, W.Units.Generation[enemy])));
            var handle = new EntityHandle(ts, B.Generation[ts]);
            int flying = -1;
            for (int t = 0; t < 400 && flying < 0; t++)
            {
                _sim.Tick();
                _shotViews.CollectTick(W);
                for (int i = 0; i < W.Projectiles.Capacity && flying < 0; i++)
                {
                    ShotEnds e = ShotEnds.Of(W.Projectiles, i);
                    if (e.Live && e.AttackerIsBuilding && e.Attacker == handle) flying = i;
                }
            }
            // A third of the way: the bolt between the tower's top and the target.
            for (int t = 0; t < 2 && flying >= 0 && W.Projectiles.Alive[flying]; t++) { _sim.Tick(); _shotViews.CollectTick(W); }
            _camera.SetZoom(CameraLimits.MinZoom);
            _camera.SetFocus(c.X + 2f, c.Y + 3f);
            await Frames(1);
            await Shot("m6v28-tower-shot-20m");
        }
        await EndMatch();
    }

    // ---- helpers (ViewHardenVh9Test's) ----

    private int _keepAlive = -1;

    // A Garrison Keep for player 0 before anything else: a player whose buildings train nothing and who has no unit is
    // defeated at once (docs/02 "Game flow" 4), and a defeated player's commands are refused.
    private void KeepAlive(int players = 1, NVector2? spot = null)
    {
        NVector2 at = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + (spot ?? new NVector2(-30f, 60f));
        for (int p = 0; p < players; p++)
        {
            int slot = Spawn(p, _data.FindBuilding("malazan_garrison_keep"), at + new NVector2(-14f * p, 0f));
            if (p == 0) _keepAlive = slot;
            Check(slot >= 0, $"no keep-alive Garrison Keep for player {p}");
        }
    }

    private void StartMatch(params string[] extra)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        var args = new List<string> { "--seed", "1", "--units", "0", "--no-bases", "--mute" };
        args.AddRange(extra);
        _match.Start(_data, LaunchOptions.Parse(args.ToArray()));
        var runner = _match.GetNode<SimRunner>("SimRunner");
        runner.ProcessMode = ProcessModeEnum.Disabled; // ticked here
        _sim = runner.Simulation!;
        _views = _match.GetNode<BuildingViews>("World3D/BuildingViews");
        _shotViews = _match.GetNode<ProjectileViews>("World3D/ProjectileViews");
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
        if (!(slot >= 0 && B.Cell[slot] == anchor && B.TypeId[slot] == type))
            GD.Print($"spawn {def.Key} for {player} at {anchor}: slot {slot}, tick {W.TickNumber}, fits {B.Fits(type, anchor)}");
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

    // An anchor near `near` with a free, passable cell of one level round the footprint.
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

    private async Task Shot(string name, bool gray = false)
    {
        await Frames(6);
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image img = GetViewport().GetTexture().GetImage();
        if (gray)
            for (int y = 0; y < img.GetHeight(); y++)
                for (int x = 0; x < img.GetWidth(); x++)
                {
                    Color px = img.GetPixel(x, y);
                    float l = 0.2126f * px.R + 0.7152f * px.G + 0.0722f * px.B;
                    img.SetPixel(x, y, new Color(l, l, l));
                }
        string path = Path.Combine(_shots!, name + ".png");
        GD.Print($"screenshot {path} ({img.SavePng(path)})");
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
