using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using NVector2 = System.Numerics.Vector2;

namespace Rts.Game.Tests;

/// <summary>
/// M6-VH9 (view hardening after the art slices): building picking by drawn height (BUG-1630), the new figures' colours
/// (BUG-1600), the Sapper's throw (BUG-1601) and the Keep's bar at the closest zoom (BUG-1631).
/// </summary>
/// <remarks>
/// Headless, in the scene loop: <c>&amp; $env:GODOT --headless --path game res://tests/ViewHardenVh9Test.tscn</c>; prints
/// "VIEW HARDEN VH9 TEST PASS" and exits 0, or each failure and exits 1. Rows:
/// (P1) an own unit standing between the camera and a building (the Keep's model, a box type's box) is what a click on the
/// unit selects, at the closest, default and farthest zoom; (P2) a box type at data scale 1.25 (a Teblor building) is picked
/// at the height it is drawn (a click between the old 3 m box's top and its 3.75 m top selects it); (P3, measured) the share of
/// the sampled pixels inside each model type's pick box but off its model that select it, at zoom 20, 40 and 60.
/// (C) The asset manifest's colours against the setup's player colours (ui.json): the Priest's non-team tints and props are
/// at least <see cref="MinDistance"/> (RGB 0-255, Euclidean) from the Bone colour its team parts take, and no non-team tint or
/// prop of the Priest or the Zealot is within it of Crimson; every model type against every colour is printed (a table).
/// (B) A hurt Cadre Mage's hp bar line is above its staff head's top (the props' highest point in the pose drawn) at the
/// default zoom and at 20 m, in the world and on screen; every model type's line before (bones only) and after is printed,
/// and a type whose props stay under its head line in the clips it plays (<see cref="UnitViews.PlayedPropTop"/>) keeps it.
/// (S) A Sapper (data cooldown 3 s) attacking a held enemy: over 5 shots its one-shot throw clip passes its release once per
/// shot, and each shot first exists within <see cref="ShotTolerance"/> of the clip's length of the release; the clip map's
/// release share is within 0.1 of the throwing hand's fastest frame on the skeleton.
/// (K) The bar of a Garrison Keep with the camera centred on it at zoom 20 is inside the screen; at 40 and 60, and every other
/// model type's at every zoom, it is where its look puts it (no ceiling applied); no point along the bar, where it is drawn, is
/// behind its own model from the camera (BUG-1680: a lowered bar sits on the building's camera side).
/// (E) Enemy units behind an own Keep: every sampled pixel where the camera ray meets an enemy before the Keep model's triangles
/// (and before any tree) is an Attack on it, and none where the model is met first (BUG-1681: the pick box's air is not in front of what is seen).
/// Windowed with <c>-- --shots &lt;dir&gt;</c>: the Priest in the eight setup colours at 40 m and 20 m, the Zealot in Cobalt,
/// Jade and Crimson, a hurt Cadre Mage at the default zoom and 20 m, a Sapper as its munition leaves, the Keep centred at 20 m.
/// </remarks>
public partial class ViewHardenVh9Test : Node
{
    private const string Tag = "VIEW HARDEN VH9 TEST";

    /// <summary>The least RGB distance (0-255 a channel, Euclidean) the brief asks between a player colour and a non-team colour.</summary>
    public const float MinDistance = 60f;

    /// <summary>How far (a share of the clip's length) a shot may first exist from the clip's release.</summary>
    public const float ShotTolerance = 0.15f;

    private readonly List<string> _failures = new();
    private string? _shots;
    private GameData _data = null!;
    private Match _match = null!;
    private Simulation _sim = null!;
    private BuildingViews _views = null!;
    private RtsCamera _camera = null!;
    private SelectionController _sel = null!;

    private World W => _sim.World;
    private BuildingStore B => _sim.World.Buildings;

    public override async void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(args, "--shots");
        if (at >= 0 && at + 1 < args.Length && DisplayServer.GetName() != "headless") { _shots = args[at + 1]; Directory.CreateDirectory(_shots); }
        UnitViews.UseModels = true;
        BuildingViews.UseModels = true;
        try
        {
            GetTree().Root.Size = new Vector2I(1600, 900);
            await Frames(1);
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            ColourRows();
            await PickRows();
            await SeenBehindRows();
            await BarCeilingRows();
            await MageBarRows();
            await ThrowRows();
            if (_shots != null) await Pictures();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures) GD.Print($"{Tag} FAIL: {f}");
        if (_failures.Count == 0) GD.Print($"{Tag} PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- (C) colours, from the manifest and ui.json ----

    private void ColourRows()
    {
        string res = ProjectSettings.GlobalizePath("res://");
        JsonNode manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(res, "..", "tools", "asset-manifest.json")))!;
        JsonNode ui = JsonNode.Parse(File.ReadAllText(Path.Combine(res, "data", "common", "ui.json")))!;
        // The setup's colours: every "colour_N" entry of the HUD text with an rgb (N from 1).
        var names = new List<string>();
        var rgbs = new List<string>();
        Find(ui, names, rgbs);
        Check(rgbs.Count >= 8, $"(C) {rgbs.Count} setup colours in ui.json, want 8");
        string header = "| Type | Part | Colour | " + string.Join(" | ", names) + " |";
        GD.Print("(C) RGB distance of each non-team tint and prop colour to each setup colour (under 60 in brackets):");
        GD.Print(header);
        foreach (JsonNode? m in manifest["models"]!.AsArray())
        {
            string id = (string)m!["id"]!;
            foreach ((string part, string colour) in NonTeam(m))
            {
                var cells = new List<string>();
                for (int c = 0; c < rgbs.Count; c++)
                {
                    float d = Distance(colour, rgbs[c]);
                    cells.Add(d < MinDistance ? $"[{d:0}]" : $"{d:0}");
                }
                GD.Print($"| {id} | {part} | {colour} | {string.Join(" | ", cells)} |");
            }
        }
        // The Priest in Bone (colour 8): its team parts sit among these. Crimson (colour 3) against the Priest and the Zealot.
        string bone = ColourOf(ui, 8), crimson = ColourOf(ui, 3);
        foreach ((string id, string against, string label) in new[] { ("whirlwind_priest", bone, "Bone"), ("whirlwind_priest", crimson, "Crimson"), ("whirlwind_zealot", crimson, "Crimson") })
        {
            JsonNode? m = Model(manifest, id);
            if (!Check(m != null, $"(C) {id}: no manifest entry")) continue;
            float worst = float.MaxValue;
            string worstPart = "";
            foreach ((string part, string colour) in NonTeam(m!))
            {
                float d = Distance(colour, against);
                if (d < worst) (worst, worstPart) = (d, part);
                Check(d >= MinDistance, $"(C) {id}: {part} {colour} is {d:0.0} from the {label} player colour {against} (want at least {MinDistance})");
            }
            GD.Print($"(C) {id} against {label} {against}: nearest non-team colour {worstPart} at {worst:0.0}");
        }
    }

    // Non-team colours of a manifest model: each role tint and each prop not drawn on the team surface.
    private static IEnumerable<(string Part, string Colour)> NonTeam(JsonNode m)
    {
        if (m["tints"] is JsonObject tints)
            foreach (KeyValuePair<string, JsonNode?> t in tints) yield return ($"{t.Key} tint", (string)t.Value!["colour"]!);
        if (m["props"] is JsonArray props)
            foreach (JsonNode? p in props)
                if ((string)p!["material"]! != UnitModels.TeamMaterialName) yield return ((string)p["name"]!, (string)p["colour"]!);
    }

    private static JsonNode? Model(JsonNode manifest, string id)
    {
        foreach (JsonNode? m in manifest["models"]!.AsArray()) if ((string?)m!["id"] == id) return m;
        return null;
    }

    // Every object key "colour_N" with an "rgb" under the node, in N order.
    private static void Find(JsonNode node, List<string> names, List<string> rgbs)
    {
        for (int n = 1; n <= 9; n++)
        {
            JsonNode? hit = FindKey(node, $"colour_{n}");
            if (hit?["rgb"] is JsonNode rgb)
            {
                names.Add((string?)hit["displayName"] ?? $"colour {n}");
                rgbs.Add((string)rgb!);
            }
        }
    }

    private static string ColourOf(JsonNode ui, int n) => (string)FindKey(ui, $"colour_{n}")!["rgb"]!;

    private static JsonNode? FindKey(JsonNode node, string key)
    {
        if (node is JsonObject o)
            foreach (KeyValuePair<string, JsonNode?> kv in o)
            {
                if (kv.Key == key) return kv.Value;
                if (kv.Value != null && FindKey(kv.Value, key) is JsonNode found) return found;
            }
        return null;
    }

    private static float Distance(string a, string b)
    {
        Color x = Color.FromHtml(a), y = Color.FromHtml(b);
        float dr = (x.R - y.R) * 255f, dg = (x.G - y.G) * 255f, db = (x.B - y.B) * 255f;
        return MathF.Sqrt(dr * dr + dg * dg + db * db);
    }

    // ---- (P) picking ----

    private async Task PickRows()
    {
        StartMatch("--players", "2", "--no-fog", "--no-combat", "--faction", "1", "malazan", "--faction", "2", "teblor", "--resources", "high");
        int keep = _data.FindBuilding("malazan_garrison_keep");
        int boxType = -1;
        int malazan = _data.FindFaction("malazan");
        for (int t = 0; t < _data.Buildings.Length && boxType < 0; t++)
            if (_data.Buildings[t].Faction == malazan && !_views.Models!.Has(t) && _data.Buildings[t].FootprintWidth == 3) boxType = t;
        if (!Check(keep >= 0 && _views.Models!.Has(keep) && boxType >= 0, "(P1) no Keep model or no box type")) { await EndMatch(); return; }
        NVector2 near = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-30f, 0f);
        foreach (int type in new[] { keep, boxType })
        {
            BuildingDef def = _data.Buildings[type];
            int slot = Spawn(0, type, near);
            near += new NVector2(0f, 26f);
            if (!Check(slot >= 0, $"(P1) {def.Key} not spawned")) continue;
            NVector2 c = Centre(slot);
            // South of the footprint (+z, toward the camera), 1.2 m off its edge.
            NVector2 front = c + new NVector2(0f, def.FootprintHeight * MapConstants.CellSize / 2f + 1.2f);
            int unit = SpawnUnit(0, _data.FindUnit("malazan_laborer"), front);
            if (!Check(unit >= 0, $"(P1) {def.Key}: no laborer in front")) continue;
            _views.Sync(W);
            foreach (float zoom in new[] { CameraLimits.MinZoom, CameraLimits.DefaultZoom, CameraLimits.MaxZoom })
            {
                _camera.SetZoom(zoom);
                _camera.SetFocus(c.X, c.Y);
                await Frames(2);
                _sel.ClearBuilding();
                _sel.Selection.Clear();
                if (!Check(_sel.TryScreenPosition(unit, out Vector2 p), $"(P1) {def.Key}: the unit is behind the camera")) continue;
                Click(p);
                bool picked = _sel.Selection.Contains(new EntityHandle(unit, W.Units.Generation[unit])) && _sel.SelectedBuilding < 0;
                string msg = $"(P1) {def.Key} ({(_views.Models!.Has(type) ? "model" : "box")}) at zoom {zoom}: a click on the laborer standing in front selected "
                             + (picked ? "the laborer" : _sel.SelectedBuilding == slot ? "the building" : $"{_sel.Selection.Count} units, building {_sel.SelectedBuilding}");
                GD.Print($"  {msg}");
                Check(picked, msg);
            }
        }
        // (P1b) Partly hidden: a Laborer just behind the box type (north, away from the camera), the box covering its body
        // centre on the click's ray but its head showing over the box: a click on it is still the Laborer.
        {
            BuildingDef def = _data.Buildings[boxType];
            int slot = Spawn(0, boxType, near);
            near += new NVector2(0f, 26f);
            NVector2 c = slot >= 0 ? Centre(slot) : default;
            NVector2 back = c - new NVector2(0f, def.FootprintHeight * MapConstants.CellSize / 2f + 1.2f);
            int unit = slot >= 0 ? SpawnUnit(0, _data.FindUnit("malazan_laborer"), back) : -1;
            if (Check(slot >= 0 && unit >= 0, "(P1b) no box building or laborer behind it"))
            {
                _views.Sync(W);
                _camera.SetZoom(CameraLimits.DefaultZoom);
                _camera.SetFocus(c.X, c.Y);
                await Frames(2);
                _sel.TryScreenPosition(unit, out Vector2 p);
                Vector3 o = _camera.ProjectRayOrigin(p), d = _camera.ProjectRayNormal(p);
                int boxFirst = BuildingPicker.PickRay(B, _data.Buildings, W.NavGrid, W.Heightmap, -1, new(o.X, o.Y, o.Z), new(d.X, d.Y, d.Z),
                    _views.PickTops, _views.SiteHeights, BuildingViews.SiteMinHeight, ReadOnlySpan<bool>.Empty, out float boxT);
                Vector3 body = UnitViews.GroundPoint(W, unit, 1f) + new Vector3(0f, UnitViews.BodyHeight(W.Units.Radius[unit]) / 2f, 0f);
                bool covered = boxFirst == slot && boxT < (body - o).Length() - W.Units.Radius[unit];
                _sel.ClearBuilding();
                _sel.Selection.Clear();
                Click(p);
                bool picked = _sel.Selection.Contains(new EntityHandle(unit, W.Units.Generation[unit]));
                GD.Print($"  (P1b) a laborer behind {def.Key}, its body centre {(covered ? "behind" : "not behind")} the box on the click's ray, its head over it: the click selected {(picked ? "the laborer" : $"building {_sel.SelectedBuilding}")}");
                Check(covered, $"(P1b) the box is not in front of the laborer's centre on the ray (the row would not test what it says)");
                Check(picked, $"(P1b) a laborer whose head shows over {def.Key} was not selected by a click on it");
            }
        }

        // (P3) every model type's pick box against its model (measured).
        for (int type = 0; type < _data.Buildings.Length; type++)
        {
            if (!_views.Models!.Has(type)) continue;
            int slot = Spawn(0, type, near);
            near += new NVector2(0f, 26f);
            if (!Check(slot >= 0, $"(P3) {_data.Buildings[type].Key} not spawned")) continue;
            _views.Sync(W);
            await ShareRow(slot);
        }

        // (P2) The Teblor hall (data scale 1.25, a box; the type a player may spawn first): a click on its back edge between 3 m
        // and its 3.75 m top selects it.
        int scaled = -1;
        int teblor = _data.FindFaction("teblor");
        for (int t = 0; t < _data.Buildings.Length && scaled < 0; t++)
            if (_data.Buildings[t].Faction == teblor && _data.Buildings[t].Slot == BuildingSlot.TownHall && _data.Buildings[t].Scale > 1.01f && !_views.Models!.Has(t)) scaled = t;
        if (Check(scaled >= 0, "(P2) no Teblor hall drawn as a box at a scale above 1"))
        {
            BuildingDef def = _data.Buildings[scaled];
            // The spot the first runs used (west block, 30 m west, 116 m south): open, level ground on seed 1.
            int slot = Spawn(1, scaled, StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-30f, 116f));
            if (Check(slot >= 0, $"(P2) {def.Key} not spawned"))
            {
                _views.Sync(W);
                NVector2 c = Centre(slot);
                float g = TerrainHeight.At(W.Heightmap, c.X, c.Y), drawn = BuildingViews.DrawnHeight(def);
                float z0 = c.Y - def.FootprintHeight * MapConstants.CellSize / 2f;
                _camera.SetZoom(CameraLimits.DefaultZoom);
                _camera.SetFocus(c.X, c.Y);
                await Frames(2);
                // Owned by player 1: the enemy pick (EnemyAt) is the click's building pick for another player's building.
                float y = (BuildingViews.BoxHeight + drawn) / 2f;
                Vector2 p = _camera.UnprojectPosition(new Vector3(c.X, g + y, z0 + 0.05f));
                bool enemy = _sel.EnemyAt(p, out EntityHandle target, out bool isBuilding);
                bool hit = enemy && isBuilding && target.Index == slot;
                Vector2 over = _camera.UnprojectPosition(new Vector3(c.X, g + drawn + 0.3f, z0 + 0.05f));
                bool overHit = _sel.EnemyAt(over, out EntityHandle t2, out bool b2) && b2 && t2.Index == slot;
                GD.Print($"  (P2) {def.Key} (scale {def.Scale}, drawn {drawn:0.00} m): a click on its back edge at {y:0.00} m {(hit ? "picks it" : "misses it")}; 0.3 m over its top {(overHit ? "picks it" : "misses it")}");
                Check(hit, $"(P2) {def.Key}: a click on its drawn box at {y:0.00} m (over the old 3 m box) does not pick it");
                Check(!overHit, $"(P2) {def.Key}: a click 0.3 m over its drawn top picks it");
            }
        }
        _camera.SetZoom(CameraLimits.DefaultZoom);
        await EndMatch();
    }

    // (P3) measured: of the sampled pixels whose ray enters the building's pick box but meets none of its model's triangles, the share that selects it.
    private async Task ShareRow(int slot)
    {
        int type = B.TypeId[slot];
        BuildingDef def = _data.Buildings[type];
        NVector2 c = Centre(slot);
        float g = TerrainHeight.At(W.Heightmap, c.X, c.Y);
        float hw = def.FootprintWidth * MapConstants.CellSize / 2f, hd = def.FootprintHeight * MapConstants.CellSize / 2f;
        var box = new Aabb(new Vector3(c.X - hw, g, c.Y - hd), new Vector3(2f * hw, _views.PickTops[type], 2f * hd));
        foreach (float zoom in new[] { CameraLimits.MinZoom, CameraLimits.DefaultZoom, CameraLimits.MaxZoom })
        {
            _camera.SetZoom(zoom);
            _camera.SetFocus(c.X, c.Y);
            await Frames(2);
            Rect2 screen = GetViewport().GetVisibleRect();
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int k = 0; k < 8; k++)
            {
                Vector2 s = _camera.UnprojectPosition(box.GetEndpoint(k));
                x0 = Math.Min(x0, s.X); y0 = Math.Min(y0, s.Y); x1 = Math.Max(x1, s.X); y1 = Math.Max(y1, s.Y);
            }
            x0 = Math.Max(x0, 1f); y0 = Math.Max(y0, 1f); x1 = Math.Min(x1, screen.Size.X - 2f); y1 = Math.Min(y1, screen.Size.Y - 2f);
            int off = 0, picked = 0;
            for (float y = y0; y <= y1; y += 6f)
                for (float x = x0; x <= x1; x += 6f)
                {
                    var p = new Vector2(x, y);
                    Vector3 o = _camera.ProjectRayOrigin(p), d = _camera.ProjectRayNormal(p);
                    if (!box.IntersectsSegment(o, o + d * 1000f) || float.IsFinite(_views.ModelRayDistance(slot, o, d))) continue;
                    off++;
                    _sel.ClearBuilding();
                    _sel.Selection.Clear();
                    Click(p);
                    if (_sel.SelectedBuilding == slot) picked++;
                }
            _sel.ClearBuilding();
            GD.Print($"  (P3) {def.Key} at zoom {zoom}: {picked} of {off} sampled pixels in its pick box but off its model select it ({100f * picked / Math.Max(1, off):0.#}%)");
        }
    }

    // ---- (E) what is seen through a model's gaps (BUG-1681) ----

    // Enemy Heavy Infantry 1.5-7 m behind an own Keep (away from the camera): every sampled pixel of the Keep's screen rectangle
    // where the camera ray meets an enemy before any of the model's triangles is an Attack on that enemy (EnemyAt), and every
    // one where the model is met first is not (the own Keep in front). The unit pick uses the runner's interpolation, as EnemyAt.
    private async Task SeenBehindRows()
    {
        StartMatch("--players", "2", "--no-fog", "--no-combat", "--faction", "1", "malazan", "--faction", "2", "malazan", "--resources", "high");
        int keep = _data.FindBuilding("malazan_garrison_keep"), heavy = _data.FindUnit("malazan_heavy_infantry");
        int slot = Spawn(0, keep, StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-40f, 10f));
        if (!Check(slot >= 0 && _views.Models!.Has(keep), "(E) no Keep model")) { await EndMatch(); return; }
        NVector2 c = Centre(slot);
        float half = _data.Buildings[keep].FootprintHeight * MapConstants.CellSize / 2f;
        foreach (float back in new[] { 1.5f, 4f, 7f })
            for (float dx = -6f; dx <= 6.01f; dx += 2f)
                SpawnUnit(1, heavy, c + new NVector2(dx, -(half + back)));
        _views.Sync(W);
        float alpha = (float)_match.GetNode<SimRunner>("SimRunner").Alpha;
        foreach (float zoom in new[] { CameraLimits.MinZoom, CameraLimits.DefaultZoom, CameraLimits.MaxZoom })
        {
            _camera.SetZoom(zoom);
            _camera.SetFocus(c.X, c.Y);
            await Frames(2);
            int seen = 0, attacked = 0, hidden = 0, hiddenAttacked = 0, byTree = 0;
            UnitStore u = W.Units;
            ResourceStore r = W.Resources;
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
            for (float py = MathF.Floor(y0); py <= y1; py += 4f)
                for (float px = MathF.Floor(x0); px <= x1; px += 4f)
                {
                    var p = new Vector2(px, py);
                    Vector3 o = _camera.ProjectRayOrigin(p), d = _camera.ProjectRayNormal(p).Normalized();
                    int unit = UnitPicker.PickRay(u.Alive, u.PrevPosition, u.Position, u.Radius, W.Heightmap, alpha, new(o.X, o.Y, o.Z), new(d.X, d.Y, d.Z),
                        UnitViews.ExtraBodyHeight, out float unitT);
                    if (unit < 0 || u.Owner[unit] != 1) continue;
                    bool hit = _sel.EnemyAt(p, out EntityHandle target, out bool isBuilding) && !isBuilding && target.Index == unit;
                    if (_views.ModelRayDistance(slot, o, d) < unitT)
                    {
                        hidden++;
                        if (hit) hiddenAttacked++;
                    }
                    else
                    {
                        // A tree drawn in front of the enemy hides it (a click there is on the tree), as the model does.
                        ResourcePicker.PickRay(W.NavGrid, W.Data.Resources, r.Alive, r.TypeId, r.Cell, W.Heightmap, new(o.X, o.Y, o.Z), new(d.X, d.Y, d.Z),
                            PropsView.Shape, out float nodeT);
                        if (nodeT < unitT)
                        {
                            byTree++;
                            continue;
                        }
                        seen++;
                        if (hit) attacked++;
                    }
                }
            string msg = $"(E) zoom {zoom}: {seen} pixels show an enemy behind the own Keep, a right click targets it at {attacked}; {hidden} pixels where the Keep's model hides it, targeted at {hiddenAttacked}; {byTree} where a tree in front hides it";
            GD.Print($"  {msg}");
            Check(seen > 0 && attacked == seen, msg + " (a seen enemy is not the right click's target)");
            Check(hiddenAttacked == 0, msg + " (an enemy behind the model is targeted through it)");
        }
        _camera.SetZoom(CameraLimits.DefaultZoom);
        await EndMatch();
    }

    // ---- (K) the bar's ceiling ----

    private async Task BarCeilingRows()
    {
        StartMatch("--players", "2", "--no-fog", "--no-combat", "--faction", "1", "malazan", "--resources", "high");
        NVector2 near = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(0f, 30f);
        for (int type = 0; type < _data.Buildings.Length; type++)
        {
            if (!_views.Models!.Has(type)) continue;
            BuildingDef def = _data.Buildings[type];
            int slot = Spawn(0, type, near);
            near += new NVector2(16f, 0f);
            if (!Check(slot >= 0, $"(K) {def.Key} not spawned")) continue;
            _views.Sync(W);
            NVector2 c = Centre(slot);
            float ground = TerrainHeight.At(W.Heightmap, c.X, c.Y);
            float half = def.FootprintWidth * MapConstants.CellSize * BuildingViews.BarLengthShare / 2f;
            foreach (float zoom in new[] { CameraLimits.MinZoom, CameraLimits.DefaultZoom, CameraLimits.MaxZoom })
            {
                _camera.SetZoom(zoom);
                _camera.SetFocus(c.X, c.Y);
                await Frames(2);
                Rect2 screen = GetViewport().GetVisibleRect();
                float y = _views.BarY(slot), wanted = _views.BarWanted(slot), z = c.Y + _views.BarZ(slot);
                Vector2 l = _camera.UnprojectPosition(new Vector3(c.X - half, ground + y + BuildingViews.BarThickness / 2f, z));
                Vector2 r = _camera.UnprojectPosition(new Vector3(c.X + half, ground + y - BuildingViews.BarThickness / 2f, z));
                bool on = screen.HasPoint(l) && screen.HasPoint(r);
                // BUG-1680: no point along the bar (where it is drawn) is behind its own model from the camera.
                int behind = 0;
                for (int k = 0; k < 9; k++)
                {
                    var pt = new Vector3(c.X - half + 2f * half * k / 8f, ground + y, z);
                    Vector3 o = _camera.GlobalPosition, d = (pt - o).Normalized();
                    if (_views.ModelRayDistance(slot, o, d) < (pt - o).Length() - 0.05f) behind++;
                }
                GD.Print($"  (K) bar {def.Key} at zoom {zoom}, focus on its centre (ground {ground:0.00} m): {y:0.00} m (its look's {wanted:0.00}), {_views.BarZ(slot):0.00} m toward the camera, top-left {l.X:0} {l.Y:0}, bottom-right {r.X:0} {r.Y:0}, {(on ? "on" : "OFF")} screen, {behind} of 9 points behind its model");
                Check(on, $"(K) {def.Key}: bar off screen at zoom {zoom} ({l} - {r})");
                Check(behind == 0, $"(K) {def.Key}: {behind} of 9 bar points behind its own model at zoom {zoom}");
                bool lowered = y < wanted - 1e-4f;
                if (zoom > CameraLimits.MinZoom) Check(!lowered, $"(K) {def.Key}: bar lowered to {y:0.00} m at zoom {zoom} (want its look's {wanted:0.00})");
                Check(lowered || _views.BarZ(slot) == 0f, $"(K) {def.Key}: a bar not lowered is off its centre by {_views.BarZ(slot):0.00} m at zoom {zoom}");
            }
        }
        _camera.SetZoom(CameraLimits.DefaultZoom);
        await EndMatch();
    }

    // ---- (B) the Mage's bar over its staff head ----

    private async Task MageBarRows()
    {
        StartMatch("--players", "2", "--no-fog", "--no-combat", "--faction", "1", "malazan", "--resources", "high");
        UnitViews units = _match.GetNode<UnitViews>("World3D/UnitViews");
        UnitModels models = units.Models!;
        GD.Print("(B) overlay lines (m over the feet at the data scale): before = highest bone + head room; after = also over the props' top");
        for (int t = 0; t < _data.Units.Length; t++)
        {
            if (!models.Has(t)) continue;
            UnitDef def = _data.Units[t];
            float before = (models.TopBone(t) + UnitViews.ModelHeadRoom) * def.Scale, after = units.OverlayLine(t);
            float prop = UnitViews.PlayedPropTop(models, _data, t) * def.Scale, any = models.PropTop(t) * def.Scale;
            GD.Print(string.Create(CultureInfo.InvariantCulture,
                $"  (B) {def.Key}: line {before:F3} -> {after:F3} m (bar centre {before + CombatViews.BarGap:F3} -> {after + CombatViews.BarGap:F3}); highest bone {models.TopBone(t) * def.Scale:F3}, props' top in the clips it plays {(float.IsNaN(prop) ? "none" : prop.ToString("F3", CultureInfo.InvariantCulture))} (in any clip {(float.IsNaN(any) ? "none" : any.ToString("F3", CultureInfo.InvariantCulture))}, {models.PropTopClip(t)})"));
            bool propOver = float.IsFinite(prop) && prop + UnitViews.PropHeadRoom * def.Scale > before;
            if (!propOver) Check(Math.Abs(after - before) < 1e-5f, $"(B) {def.Key}: no prop over its head, but its line moved {before:F3} -> {after:F3}");
            else Check(after > prop, $"(B) {def.Key}: line {after:F3} under its props' top {prop:F3}");
        }

        int mage = _data.FindUnit("malazan_cadre_mage");
        NVector2 spot = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(0f, 20f);
        int slot = SpawnUnit(0, mage, spot);
        if (!Check(slot >= 0 && models.Has(mage), "(B) no Cadre Mage model")) { await EndMatch(); return; }
        W.Units.Hp[slot] = W.MaxHp(slot) / 2; // test seam: hurt, so its bar is drawn
        foreach (float zoom in new[] { CameraLimits.DefaultZoom, CameraLimits.MinZoom })
        {
            _camera.SetZoom(zoom);
            _camera.SetFocus(spot.X, spot.Y);
            await Frames(4);
            MeshInstance3D mesh = units.ModelMeshOf(slot)!;
            (Vector3 top, bool found) = PropTopNow(mesh, models.AtlasOf(mage)!);
            float ground = UnitViews.GroundPoint(W, slot, 1f).Y;
            float line = units.OverlayLine(mage);
            Vector3 centre = UnitViews.GroundPoint(W, slot, 1f);
            float barBottom = line + CombatViews.BarGap - CombatViews.BarThickness / 2f;
            Vector2 barScreen = _camera.UnprojectPosition(new Vector3(centre.X, ground + barBottom, centre.Z));
            Vector2 topScreen = _camera.UnprojectPosition(top);
            GD.Print(string.Create(CultureInfo.InvariantCulture,
                $"  (B) hurt malazan_cadre_mage at zoom {zoom}: staff head's top {top.Y - ground:F3} m over the ground (screen y {topScreen.Y:0}), line {line:F3} m, bar's lower edge {barBottom:F3} m (screen y {barScreen.Y:0})"));
            Check(found && line > top.Y - ground, $"(B) zoom {zoom}: the Mage's line {line:F3} m is not above its staff head's top {top.Y - ground:F3} m");
            Check(found && barScreen.Y < topScreen.Y, $"(B) zoom {zoom}: on screen the bar's lower edge (y {barScreen.Y:0}) is not above the staff head's top (y {topScreen.Y:0})");
        }
        _camera.SetZoom(CameraLimits.DefaultZoom);
        await EndMatch();
    }

    // The highest prop vertex of a skinned model mesh in the pose drawn now (world space): the skeleton's bone pose times the
    // skin's bind pose, for the vertices reading the props' tile.
    private static (Vector3 Top, bool Found) PropTopNow(MeshInstance3D mesh, UnitModels.Atlas atlas)
    {
        Skeleton3D skel = mesh.GetNode<Skeleton3D>(mesh.Skeleton);
        Skin skin = mesh.Skin;
        Godot.Collections.Array arrays = mesh.Mesh.SurfaceGetArrays(0);
        Vector3[] v = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        Vector2[] uv = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        int[] bones = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
        float[] weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
        int per = bones.Length / Math.Max(1, v.Length);
        Vector3 best = Vector3.Zero;
        bool found = false;
        for (int k = 0; k < v.Length; k++)
        {
            if (!atlas.IsProp(uv[k])) continue;
            Vector3 p = Vector3.Zero;
            for (int j = 0; j < per; j++)
            {
                float w = weights[k * per + j];
                if (w <= 0f) continue;
                int bind = bones[k * per + j];
                int bone = skin.GetBindBone(bind);
                if (bone < 0) bone = skel.FindBone(skin.GetBindName(bind));
                p += w * (skel.GlobalTransform * skel.GetBoneGlobalPose(bone) * skin.GetBindPose(bind) * v[k]);
            }
            if (!found || p.Y > best.Y) (best, found) = (p, true);
        }
        return (best, found);
    }

    // ---- (S) the Sapper's throw ----

    private async Task ThrowRows()
    {
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        match.Start(_data, LaunchOptions.Parse(new[] { "--seed", "1", "--units", "0", "--no-bases", "--no-fog", "--mute" }));
        var runner = match.GetNode<SimRunner>("SimRunner");
        runner.FixedFrameDelta = 1.0 / 60.0;
        match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
        Simulation sim = runner.Simulation!;
        UnitViews views = match.GetNode<UnitViews>("World3D/UnitViews");
        try
        {
            World w = sim.World;
            UnitStore u = w.Units;
            int sapper = _data.FindUnit("malazan_sapper"), heavy = _data.FindUnit("malazan_heavy_infantry");
            NVector2 mid = w.NavGrid.CellCenter(w.NavGrid.Width / 2, w.NavGrid.Height / 2);
            sim.Enqueue(Command.SpawnUnit(0, sapper, mid));
            sim.Enqueue(Command.SpawnUnit(1, heavy, mid + new NVector2(7f, 0f)));
            for (int f = 0; f < 10; f++) await Frames(1);
            int slot = -1, foe = -1;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (u.Alive[i] && u.TypeId[i] == sapper && u.Owner[i] == 0) slot = i;
                if (u.Alive[i] && u.Owner[i] == 1) foe = i;
            }
            if (!Check(slot >= 0 && foe >= 0 && views.AnimatorOf(slot) != null, "(S) no Sapper model or no enemy")) return;
            UnitModels models = views.Models!;
            float release = models.OneShotRelease(sapper, UnitModels.Clip.Attack);
            float length = models.ClipLength(sapper, UnitModels.Clip.Attack);
            Check(release > 0f, "(S) the Sapper's attack clip is not marked one-shot in its clip map");
            float measured = HandFastest(views, slot, length);
            GD.Print($"(S) the clip map's release {release:F2} of the {length:F2} s throw; the throwing hand is fastest at {measured:F2}");
            Check(Math.Abs(measured - release) <= 0.1f, $"(S) the release share {release:F2} is {Math.Abs(measured - release):F2} from the hand's fastest frame {measured:F2}");
            sim.Enqueue(Command.HoldPosition(1, new EntityHandle(foe, u.Generation[foe])));
            sim.Enqueue(Command.Attack(0, new EntityHandle(slot, u.Generation[slot]), new EntityHandle(foe, u.Generation[foe]), isBuilding: false));
            ProjectileStore shots = w.Projectiles;
            var seen = new bool[shots.Capacity];
            var offsets = new List<float>();
            int throws = 0, wraps = 0, attacking = 0;
            float prev = -1f, releaseAt = release * length;
            for (int f = 0; f < 60 * 20 && offsets.Count < 5 && u.Alive[foe] && u.Alive[slot]; f++)
            {
                await Frames(1);
                AnimationPlayer anim = views.AnimatorOf(slot)!;
                float pos = (float)anim.CurrentAnimationPosition;
                if (u.State[slot] == UnitState.Attacking && anim.CurrentAnimation == "attack")
                {
                    attacking++;
                    if (prev >= 0f && prev < releaseAt && pos >= releaseAt) throws++;
                    if (prev >= 0f && pos < prev - 1e-4f) wraps++;
                    prev = pos;
                }
                else prev = -1f;
                for (int n = NewShots(shots, seen); n > 0; n--) offsets.Add(pos / length - release);
            }
            string list = string.Join(' ', offsets.ConvertAll(o => o.ToString("+0.00;-0.00", CultureInfo.InvariantCulture)));
            GD.Print($"(S) {offsets.Count} shots, {throws} throws (the clip passing its release), {wraps} returns to the ready pose, {attacking} attacking frames; each shot's offset from the release (share of the clip) [{list}]");
            Check(offsets.Count == 5, $"(S) {offsets.Count} shots in 20 s, want 5");
            Check(throws == offsets.Count, $"(S) {throws} throws for {offsets.Count} shots (want one a shot)");
            foreach (float o in offsets) Check(Math.Abs(o) <= ShotTolerance, $"(S) a shot first exists {o:+0.00;-0.00} of the clip from its release (want within {ShotTolerance})");
        }
        finally
        {
            RemoveChild(match);
            match.QueueFree();
            await Frames(1);
        }
    }

    // The share of the attack clip where the right hand moves fastest, on a fresh instance of the slot's model (120 samples).
    private float HandFastest(UnitViews views, int slot, float length)
    {
        int type = W_Type(views, slot);
        var holder = new Node3D();
        AddChild(holder);
        Node3D model = views.Models!.Instantiate(type, out MeshInstance3D mesh, out AnimationPlayer anim);
        holder.AddChild(model);
        Skeleton3D skel = mesh.GetNode<Skeleton3D>(mesh.Skeleton);
        int hand = skel.FindBone("hand_r");
        anim.Play("attack", 0.0);
        const int n = 120;
        var at = new Vector3[n];
        for (int k = 0; k < n; k++)
        {
            anim.Seek(length * k / n, true);
            anim.Advance(0.0);
            at[k] = hand >= 0 ? skel.GetBoneGlobalPose(hand).Origin : Vector3.Zero;
        }
        int best = 0;
        float fastest = -1f;
        for (int k = 1; k < n; k++)
        {
            float v = (at[k] - at[k - 1]).Length();
            if (v > fastest) (fastest, best) = (v, k);
        }
        holder.Free();
        return (float)best / n;
    }

    private static int W_Type(UnitViews views, int slot) => views.Runner!.Simulation!.World.Units.TypeId[slot];

    private static int NewShots(ProjectileStore shots, bool[] seen)
    {
        ReadOnlySpan<bool> alive = shots.Alive;
        ReadOnlySpan<int> owner = shots.Owner;
        int added = 0;
        for (int k = 0; k < alive.Length; k++)
        {
            if (!alive[k]) { seen[k] = false; continue; }
            if (seen[k] || owner[k] != 0) continue;
            seen[k] = true;
            added++;
        }
        return added;
    }

    // ---- pictures ----

    private async Task Pictures()
    {
        // The Priest in the eight setup colours (a match holds four players: colours 1-4, then 5-8, one Whirlwind player each),
        // a Zealot in front of the Crimson, Cobalt and Jade Priests (ui.json colour numbers 3, 4, 5).
        int priest = _data.FindUnit("whirlwind_priest"), zealot = _data.FindUnit("whirlwind_zealot");
        foreach (int first in new[] { 1, 5 })
        {
            var flags = new List<string> { "--players", "4", "--no-fog", "--no-combat", "--resources", "high" };
            for (int p = 1; p <= 4; p++) flags.AddRange(new[] { "--faction", $"{p}", "whirlwind", "--colour", $"{p}", $"{first + p - 1}" });
            StartMatch(flags.ToArray());
            NVector2 o = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-4f, 0f);
            for (int p = 0; p < 4; p++)
            {
                SpawnUnit(p, priest, o + new NVector2(2.4f * p, 0f));
                int colour = first + p;
                if (colour is 3 or 4 or 5) SpawnUnit(p, zealot, o + new NVector2(2.4f * p, 2.6f));
            }
            foreach (float zoom in new[] { CameraLimits.DefaultZoom, CameraLimits.MinZoom })
            {
                _camera.SetZoom(zoom);
                _camera.SetFocus(o.X + 3.6f, o.Y + 1.3f);
                await Shot($"m6vh9-priest-zealot-colours-{first}-{first + 3}-{zoom}m");
            }
            await EndMatch();
        }

        // A hurt Cadre Mage at the default zoom and 20 m, a Sapper as its munition leaves, and the Keep centred at 20 m.
        StartMatch("--players", "2", "--no-fog", "--faction", "1", "malazan", "--faction", "2", "malazan", "--colour", "1", "3", "--colour", "2", "4", "--resources", "high");
        NVector2 spot = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(0f, 20f);
        int mage = SpawnUnit(0, _data.FindUnit("malazan_cadre_mage"), spot);
        if (mage >= 0) W.Units.Hp[mage] = W.MaxHp(mage) / 2;
        foreach (float zoom in new[] { CameraLimits.DefaultZoom, CameraLimits.MinZoom })
        {
            _camera.SetZoom(zoom);
            _camera.SetFocus(spot.X, spot.Y + 1f);
            await Shot($"m6vh9-hurt-mage-{zoom}m");
        }
        int keep = Spawn(0, _data.FindBuilding("malazan_garrison_keep"), spot + new NVector2(-30f, 10f));
        if (keep >= 0)
        {
            Hurt(keep);
            _views.Sync(W);
            NVector2 c = Centre(keep);
            _camera.SetZoom(CameraLimits.MinZoom);
            _camera.SetFocus(c.X, c.Y);
            await Shot("m6vh9-keep-centred-20m");
        }
        await EndMatch();

        // The Sapper throwing: the runner ticks; the picture is taken on the frame the munition first exists.
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        match.Start(_data, LaunchOptions.Parse(new[] { "--seed", "1", "--units", "0", "--no-bases", "--no-fog", "--mute", "--colour", "1", "4" }));
        var runner = match.GetNode<SimRunner>("SimRunner");
        runner.FixedFrameDelta = 1.0 / 60.0;
        var camera = match.GetNode<RtsCamera>("RtsCamera");
        camera.EdgePanEnabled = false;
        Simulation sim = runner.Simulation!;
        World w = sim.World;
        NVector2 mid = w.NavGrid.CellCenter(w.NavGrid.Width / 2, w.NavGrid.Height / 2);
        sim.Enqueue(Command.SpawnUnit(0, _data.FindUnit("malazan_sapper"), mid));
        sim.Enqueue(Command.SpawnUnit(1, _data.FindUnit("malazan_heavy_infantry"), mid + new NVector2(7f, 0f)));
        for (int f = 0; f < 10; f++) await Frames(1);
        int s = -1, foe = -1;
        for (int i = 0; i < w.Units.Capacity; i++)
        {
            if (w.Units.Alive[i] && w.Units.Owner[i] == 0) s = i;
            if (w.Units.Alive[i] && w.Units.Owner[i] == 1) foe = i;
        }
        camera.SetZoom(CameraLimits.MinZoom);
        camera.SetFocus(mid.X + 2f, mid.Y + 1f);
        if (s >= 0 && foe >= 0)
        {
            sim.Enqueue(Command.HoldPosition(1, new EntityHandle(foe, w.Units.Generation[foe])));
            sim.Enqueue(Command.Attack(0, new EntityHandle(s, w.Units.Generation[s]), new EntityHandle(foe, w.Units.Generation[foe]), isBuilding: false));
            var seen = new bool[w.Projectiles.Capacity];
            NewShots(w.Projectiles, seen);
            for (int f = 0; f < 600 && NewShots(w.Projectiles, seen) == 0; f++) await Frames(1);
            runner.ProcessMode = ProcessModeEnum.Disabled; // hold the frame the munition leaves
            await Shot("m6vh9-sapper-throw-20m");
        }
        RemoveChild(match);
        match.QueueFree();
        await Frames(1);
    }

    // ---- helpers ----

    private void StartMatch(params string[] extra)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        var args = new List<string> { "--seed", "1", "--units", "0", "--no-bases", "--mute" };
        args.AddRange(extra);
        _match.Start(_data, LaunchOptions.Parse(args.ToArray()));
        var runner = _match.GetNode<SimRunner>("SimRunner");
        runner.ProcessMode = ProcessModeEnum.Disabled;
        _sim = runner.Simulation!;
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

    // An anchor near `near` with two free, passable cells of one level round the footprint.
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
                    for (int y = cy - 2; y <= cy + def.FootprintHeight + 1 && ok; y++)
                        for (int x = cx - 2; x <= cx + def.FootprintWidth + 1 && ok; x++)
                            if (B.SlotAt(x, y) >= 0 || !g.IsPassable(x, y) || g.LevelAt(x, y) != level) ok = false;
                    if (ok) return a;
                }
        return -1;
    }

    // Four enemy Heavy Infantry strike the building until it is below its hit points, then go (its bar is drawn when hurt).
    private void Hurt(int slot)
    {
        BuildingDef def = _data.Buildings[B.TypeId[slot]];
        NVector2 c = Centre(slot);
        int heavy = _data.FindUnit("malazan_heavy_infantry");
        float r = def.FootprintHeight * MapConstants.CellSize / 2f + 1.2f;
        var attackers = new List<int>();
        for (int k = 0; k < 4; k++)
        {
            int u = SpawnUnit(1, heavy, c + new NVector2(-3f + 2f * k, r));
            if (u >= 0) attackers.Add(u);
        }
        int gen = B.Generation[slot];
        foreach (int u in attackers) _sim.Enqueue(Command.Attack(1, new EntityHandle(u, W.Units.Generation[u]), new EntityHandle(slot, gen), isBuilding: true));
        for (int t = 0; t < 2000 && B.Hp[slot] > def.Hp * 3 / 4; t++) _sim.Tick();
        foreach (int u in attackers) if (W.Units.Alive[u]) W.Units.Free(new EntityHandle(u, W.Units.Generation[u]));
        _sim.Tick();
    }

    private NVector2 Centre(int slot) => StartBase.FootprintCenter(W.NavGrid, _data.Buildings[B.TypeId[slot]], B.Cell[slot]);

    private void Click(Vector2 at)
    {
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
    }

    private async Task Shot(string name)
    {
        await Frames(6);
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image img = GetViewport().GetTexture().GetImage();
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
