using System;
using System.Collections.Generic;
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

/// <summary>QA for M7-V5 (the five Teblor figures at the data scale): what TeblorModelTest and ModelViewTest do not reach.</summary>
/// <remarks>
/// Headless, in the scene loop. Run: <c>&amp; $env:GODOT --headless --path game res://tests/QaM7V5Test.tscn</c>; prints
/// "QA M7-V5 TEST PASS" and exits 0, or each failure and exits 1. Windowed with <c>-- --shots &lt;dir&gt;</c> it also saves
/// pictures. The Teblor model types are found on the shipped data (Teblor faction, model file loads). Rows:
/// (A) per Teblor model type and clip, animated (skeleton bones, 9 samples a clip, props excluded): the body's highest bone
///     stays under <see cref="UnitViews.DrawnHeight"/> (the line every overlay - hp bar, cast bar, status marks, cue words,
///     cargo marker - is drawn above), and the lowest foot bone stays near the ground (no figure floats);
/// (B) forged copies whose Warrior and Gatherer have scale 1 and 2: the figure follows (base x scale within 2 %, uniform,
///     feet on the ground), the radius, the selection ring and the overlays' line do not shrink under the figure;
/// (C) a Teblor match (both seats Teblor, no AI, fog on): 100 Gatherers on one gold mine for 1,500 ticks, every 10 ticks
///     each shown figure stands on the terrain under it (origin within 1 mm), plays a clip, no NaN;
/// (D) one figure of each Teblor model type on a ramp and at a cliff's lip (then ordered into the cliff): origin on the
///     terrain; the ground's spread under the feet is reported;
/// (E) fog: an enemy Warrior walks out of sight and back: hidden iff the fog hides it, its player inactive while hidden;
/// (F) real frames at game speeds 0.5 / 1 / 2 and paused, a Teblor fight: ticks follow the speed, paused advances neither
///     ticks nor clips; each death leaves a corpse disc at the data radius and hides the figure.
/// </remarks>
public partial class QaM7V5Test : Node
{
    private const string Tag = "QA M7-V5 TEST";
    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private JsonNode _manifest = null!;
    private readonly HashSet<string> _propMaterials = new();
    private readonly List<int> _types = new();
    private string _scratch = "";
    private string? _shots;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _scratch = Path.Combine(Path.GetTempPath(), $"rts-qa-m7v5-{OS.GetProcessId()}");
        string[] args = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(args, "--shots");
        if (at >= 0 && at + 1 < args.Length) { _shots = args[at + 1]; Directory.CreateDirectory(_shots); }
        try
        {
            string dataDir = ProjectSettings.GlobalizePath("res://data");
            DataLoadResult loaded = DataLoader.LoadAll(dataDir);
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            _manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "tools", "asset-manifest.json")))!;
            foreach (JsonNode? m in _manifest["models"]!.AsArray())
                foreach (JsonNode? p in m!["props"]?.AsArray() ?? new JsonArray())
                    if ((string)p!["material"]! != UnitModels.TeamMaterialName) _propMaterials.Add((string)p["material"]!);
            UnitViews.UseModels = true;
            int teblor = -1;
            for (int f = 0; f < _data.Factions.Length; f++) if (_data.Factions[f].Key == "teblor") teblor = f;
            var probe = new UnitModels(_data, new uint[] { 0xC03030 });
            for (int t = 0; t < _data.Units.Length; t++) if (_data.Units[t].Faction == teblor && probe.Has(t)) _types.Add(t);
            GD.Print($"teblor model types: {string.Join(' ', _types.ConvertAll(t => _data.Units[t].Key))}");
            Check(_types.Count == 5, $"{_types.Count} Teblor model types, want the five of M7-V5");

            await AnimatedBounds();
            await ForgedScales(dataDir);
            await TeblorMatch();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        try { if (Directory.Exists(_scratch)) Directory.Delete(_scratch, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        foreach (string f in _failures) GD.Print($"{Tag} FAIL: {f}");
        if (_failures.Count == 0) GD.Print($"{Tag} PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- (A) animated bounds: the overlays' line over the head, feet near the ground, every clip ----------------------------
    private async Task AnimatedBounds()
    {
        var views = new UnitViews();
        AddChild(views);
        views.Bind(_data, 4, new uint[] { 0xC03030, 0x3060D0 });
        foreach (int type in _types)
        {
            UnitDef def = _data.Units[type];
            var holder = new Node3D();
            AddChild(holder);
            Node3D model = views.Models!.Instantiate(type, out MeshInstance3D mesh, out AnimationPlayer anim);
            holder.AddChild(model);
            model.Scale = new Vector3(def.Scale, def.Scale, def.Scale);
            Skeleton3D? skel = mesh.GetNodeOrNull<Skeleton3D>(mesh.Skeleton);
            if (!Check(skel != null, $"(A) {def.Key}: no skeleton")) { holder.Free(); continue; }
            float line = UnitViews.DrawnHeight(def);
            var rows = new List<string>();
            foreach (StringName clip in UnitModels.ClipNames)
            {
                if (!anim.HasAnimation(clip)) continue;
                anim.Play(clip);
                float len = (float)anim.CurrentAnimationLength;
                float top = float.MinValue, foot = float.MaxValue, footMax = float.MinValue, headLow = float.MaxValue;
                string topBone = "";
                for (int k = 0; k <= 8; k++)
                {
                    // Manual callback mode (UnitViews' crowd schedule): only Advance applies a pose.
                    anim.Advance(k == 0 ? 0.0 : len / 8.0);
                    await Frames(1); // the skeleton applies the pose at the frame's end
                    float lowFoot = float.MaxValue;
                    for (int b = 0; b < skel!.GetBoneCount(); b++)
                    {
                        string name = skel.GetBoneName(b);
                        string lower = name.ToLowerInvariant();
                        if (lower.Contains("ik") || lower == "root" || skel.GetBoneParent(b) < 0) continue;
                        float y = (skel.GlobalTransform * skel.GetBoneGlobalPose(b)).Origin.Y;
                        if (y > top) (top, topBone) = (y, name);
                        if (name == "Head") headLow = Math.Min(headLow, y);
                        if (lower.Contains("foot") || lower.Contains("ball") || lower.Contains("toe")) lowFoot = Math.Min(lowFoot, y);
                    }
                    foot = Math.Min(foot, lowFoot);
                    footMax = Math.Max(footMax, lowFoot);
                }
                // The head bone is the skull's base; the crown is about 0.2 m over it at 1x.
                float crown = top + 0.2f * def.Scale;
                rows.Add($"{clip}: top bone {topBone} {top:F2} m (crown ~{crown:F2}, head down to {headLow:F2}), lowest foot {foot:F2}..{footMax:F2} m");
                Check(crown < line, $"(A) {def.Key} {clip}: the head reaches ~{crown:F2} m, over the overlays' line {line:F2} m (bars drawn inside the figure)");
                // A foot bone sits a few cm over the sole; a figure whose lowest foot is high in every sample floats.
                Check(footMax < 0.35f * def.Scale && foot > -0.15f, $"(A) {def.Key} {clip}: lowest foot bone {foot:F2}..{footMax:F2} m over the ground (floating or sunk)");
            }
            GD.Print($"(A) {def.Key} (scale {def.Scale}, overlays at {line:F2} m): {string.Join("; ", rows)}");
            holder.Free();
        }
        views.Free();
    }

    // ---- (B) forged scales 1 and 2 ---------------------------------------------------------------------------------------
    private async Task ForgedScales(string dataDir)
    {
        foreach (float scale in new[] { 1f, 2f })
        {
            string copy = Path.Combine(_scratch, $"data-{scale}");
            foreach (string dir in Directory.GetDirectories(dataDir, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(copy, Path.GetRelativePath(dataDir, dir)));
            foreach (string file in Directory.GetFiles(dataDir, "*", SearchOption.AllDirectories))
                File.Copy(file, Path.Combine(copy, Path.GetRelativePath(dataDir, file)), true);
            string path = Path.Combine(copy, "factions", "teblor", "units.json");
            JsonNode units = JsonNode.Parse(File.ReadAllText(path))!;
            int edited = 0;
            foreach (JsonNode? unit in units["units"]!.AsArray())
                if ((string?)unit!["id"] is "teblor_warrior" or "teblor_gatherer") { unit["scale"] = scale; edited++; }
            File.WriteAllText(path, units.ToJsonString());
            Check(edited == 2, $"(B) forged {scale}: {edited} rows edited");
            DataLoadResult loaded = DataLoader.LoadAll(copy);
            if (!Check(loaded.Ok, $"(B) forged {scale}: copy failed to load")) continue;
            GameData forged = loaded.Data!;

            var views = new UnitViews();
            AddChild(views);
            views.Bind(forged, 16, new uint[] { 0xC03030, 0x3060D0 });
            var rings = new SelectionRings();
            AddChild(rings);
            rings.Init(16);
            var sim = new Simulation(new SimConfig(5, 2, 16, 4096) { Data = forged, Combat = false });
            NVector2[] west = StartLayout.Block(sim.World.NavGrid, 4, true, 1f);
            int k = 0;
            var ids = new[] { "teblor_warrior", "teblor_gatherer" };
            foreach (string id in ids) sim.Enqueue(Command.SpawnUnit(0, forged.FindUnit(id), west[k++ * 2]));
            sim.Tick();
            sim.Tick();
            views.Sync(sim.World, 1f);
            var sel = new List<EntityHandle>();
            foreach (string id in ids)
            {
                int type = forged.FindUnit(id);
                UnitDef def = forged.Units[type];
                int slot = Slot(sim.World.Units, 0, type);
                if (!Check(slot >= 0 && views.ModelMeshOf(slot) != null, $"(B) forged {scale}: {id} not a model")) continue;
                sel.Add(new EntityHandle(slot, sim.World.Units.Generation[slot]));
                float baseH = BaseHeight(views, type);
                Aabb box = FigureBox(views.ModelMeshOf(slot)!, views.Models!.AtlasOf(type));
                float ground = UnitViews.GroundPoint(sim.World, slot, 1f).Y;
                Vector3 s = views.ModelOf(slot)!.Scale;
                float line = UnitViews.DrawnHeight(def);
                GD.Print($"(B) forged scale {scale}: {id} drawn {box.Size.Y:F3} m (base {baseH:F3} x {scale} = {baseH * scale:F3}), feet {box.Position.Y - ground:F3}, model scale {s}, radius {UnitViews.DrawnRadius(sim.World, slot)}, overlays at {line:F2} m");
                Check(Mathf.Abs(box.Size.Y - baseH * scale) <= 0.02f * baseH * scale, $"(B) forged {scale}: {id} drawn {box.Size.Y:F3}, want {baseH * scale:F3}");
                Check(s.IsEqualApprox(new Vector3(scale, scale, scale)), $"(B) forged {scale}: {id} model scale {s}");
                Check(Mathf.Abs(box.Position.Y - ground) < 0.05f, $"(B) forged {scale}: {id} feet {box.Position.Y - ground:F3} m off the ground");
                Check(UnitViews.DrawnRadius(sim.World, slot) == def.Radius && def.Radius == _data.Units[_data.FindUnit(id)].Radius, $"(B) forged {scale}: {id} radius moved");
                Check(line > box.Size.Y, $"(B) forged {scale}: {id} overlays at {line:F2} m, inside the {box.Size.Y:F2} m figure");
            }
            rings.Sync(sim.World, 1f, sel.ToArray());
            for (int r = 0; r < rings.ShownCount && r < sel.Count; r++)
            {
                UnitDef def = forged.Units[sim.World.Units.TypeId[sel[r].Index]];
                Check(Mathf.Abs(rings.ShownRadius(r) - def.Radius * SelectionRings.RadiusScale) < 1e-5f,
                    $"(B) forged {scale}: {def.Key} ring {rings.ShownRadius(r)}, want {def.Radius} x {SelectionRings.RadiusScale} (the ring followed the figure)");
            }
            Check(rings.ShownCount == sel.Count, $"(B) forged {scale}: {rings.ShownCount} rings for {sel.Count}");
            views.QueueFree();
            rings.QueueFree();
            await Frames(1);
        }
    }

    // ---- (C)-(F) a Teblor match ---------------------------------------------------------------------------------------------
    private Match _match = null!;
    private Simulation _sim = null!;
    private SimRunner _runner = null!;
    private UnitViews _views = null!;
    private RtsCamera _camera = null!;
    private World W => _sim.World;
    private UnitStore U => _sim.World.Units;

    private async Task TeblorMatch()
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        try
        {
            _match.Start(_data, LaunchOptions.Parse(new[] { "--seed", "3", "--units", "0", "--mute", "--faction", "1", "teblor", "--faction", "2", "teblor", "--ai", "human" }));
            _runner = _match.GetNode<SimRunner>("SimRunner");
            _runner.ProcessMode = ProcessModeEnum.Disabled;
            _sim = _runner.Simulation!;
            _views = _match.GetNode<UnitViews>("World3D/UnitViews");
            _camera = _match.GetNode<RtsCamera>("RtsCamera");
            _camera.EdgePanEnabled = false;
            Tick(2);
            await Frames(2);
            await Gatherers();
            await Terrain();
            await FogRow();
            await Speeds();
        }
        finally
        {
            RemoveChild(_match);
            _match.QueueFree();
            await Frames(2);
        }
    }

    private async Task Gatherers()
    {
        int gatherer = _data.FindUnit("teblor_gatherer");
        StartBasePlan plan = _match.Bases!;
        NVector2 hall = StartBase.FootprintCenter(W.NavGrid, _data.Buildings[plan.HallType[0]], plan.HallAnchor[0]);
        int mine = NearestNode(hall, ResourceKind.Gold);
        if (!Check(mine >= 0, "(C) no gold mine")) return;
        NVector2 mineC = NodeCenter(mine);
        // 100 Gatherers on open cells round the hall.
        int placed = 0;
        W.NavGrid.WorldToCell(hall, out int hx, out int hy);
        for (int r = 3; r < 30 && placed < 100; r++)
            for (int dy = -r; dy <= r && placed < 100; dy++)
                for (int dx = -r; dx <= r && placed < 100; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r || !StartLayout.IsOpen(W.NavGrid, hx + dx, hy + dy)) continue;
                    _sim.Enqueue(Command.SpawnUnit(0, gatherer, W.NavGrid.CellCenter(hx + dx, hy + dy)));
                    placed++;
                }
        Tick(2);
        var mine100 = new List<int>();
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && U.Owner[i] == 0 && U.TypeId[i] == gatherer) mine100.Add(i);
        Check(mine100.Count >= 100, $"(C) {mine100.Count} Gatherers spawned, want 100");
        foreach (int i in mine100) _sim.Enqueue(Command.Gather(0, new EntityHandle(i, U.Generation[i]), mineC));
        int checks = 0, off = 0, noClip = 0, nan = 0, gathering = 0, maxGathering = 0;
        float worst = 0f;
        string first = "";
        for (int t = 0; t < 1500; t++)
        {
            _sim.Tick();
            if (t % 10 != 0) continue;
            _views.Sync(W, 1f);
            gathering = 0;
            foreach (int i in mine100)
            {
                if (!U.Alive[i] || !_views.IsShown(i)) continue;
                if (U.State[i] == UnitState.Gathering) gathering++;
                Node3D? model = _views.ModelOf(i);
                if (model == null) { noClip++; continue; }
                Vector3 o = model.GlobalPosition;
                if (!float.IsFinite(o.X) || !float.IsFinite(o.Y) || !float.IsFinite(o.Z)) { nan++; continue; }
                float ground = TerrainHeight.At(W.Heightmap, U.Position[i].X, U.Position[i].Y);
                float d = Math.Abs(o.Y - ground);
                worst = Math.Max(worst, d);
                checks++;
                if (d > 1e-3f && off++ == 0) first = $"tick {t} slot {i} ({U.State[i]}) origin {o.Y:F3}, ground {ground:F3}";
                AnimationPlayer? a = _views.AnimatorOf(i);
                if (a == null || !a.IsPlaying() || a.CurrentAnimation == "") noClip++;
            }
            maxGathering = Math.Max(maxGathering, gathering);
            if (t == 600 && _shots != null)
            {
                _camera.SetFocus(mineC.X, mineC.Y);
                await Shot("gatherers-100-default-zoom");
                _camera.SetZoom(20f);
                await Shot("gatherers-100-20m");
                _camera.SetZoom(RtsCameraDefaultZoom());
            }
        }
        GD.Print($"(C) 100 Gatherers on one mine, 1,500 ticks: {checks} figure checks, {off} off the terrain (worst {worst:F4} m; first: {first}), {noClip} without a clip, {nan} NaN; up to {maxGathering} Gathering at once; gold {W.Gold[0]}");
        Check(off == 0 && nan == 0, $"(C) {off} figures off the terrain, {nan} NaN; first: {first}");
        Check(noClip == 0, $"(C) {noClip} figure samples without a playing clip");
        Check(maxGathering > 0, "(C) no Gatherer ever gathered");
        // Clear them away for the next rows.
        foreach (int i in mine100) if (U.Alive[i]) U.Free(new EntityHandle(i, U.Generation[i]));
        Tick(1);
        _views.Sync(W, 1f);
    }

    private float _defaultZoom = -1f;
    private float RtsCameraDefaultZoom() => _defaultZoom;

    private async Task Terrain()
    {
        // The passable cells beside a ramp and at a cliff's lip (a passable cell with a Cliff cell next to it, the higher side).
        NavGrid g = W.NavGrid;
        int rampCell = -1, lipCell = -1, lipTo = -1, lipCliff = -1;
        float lipDrop = 0f;
        for (int y = 2; y < g.Height - 2; y++)
            for (int x = 2; x < g.Width - 2; x++)
            {
                if (!g.IsPassable(x, y)) continue;
                NavFlags here = g.FlagsAt(x, y);
                if (rampCell < 0 && (here & NavFlags.Ramp) != 0) rampCell = y * g.Width + x;
                if ((here & NavFlags.Ramp) != 0) continue;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), ny = y + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if ((g.FlagsAt(nx, ny) & NavFlags.Cliff) == 0) continue;
                    // The lower ground beyond the cliff cell.
                    int fx = nx + (nx - x), fy = ny + (ny - y);
                    NVector2 a = g.CellCenter(x, y), b = g.CellCenter(fx, fy);
                    float drop = TerrainHeight.At(W.Heightmap, a.X, a.Y) - TerrainHeight.At(W.Heightmap, b.X, b.Y);
                    if (drop > lipDrop) (lipDrop, lipCell, lipTo, lipCliff) = (drop, y * g.Width + x, fy * g.Width + fx, ny * g.Width + nx);
                }
            }
        Check(rampCell >= 0 && lipCell >= 0, $"(D) ramp cell {rampCell}, lip cell {lipCell}");
        if (rampCell < 0 || lipCell < 0) return;
        GD.Print($"(D) ramp cell ({rampCell % g.Width}, {rampCell / g.Width}); cliff lip ({lipCell % g.Width}, {lipCell / g.Width}) dropping {lipDrop:F2} m");
        var spawned = new List<(int type, string where)>();
        NVector2 ramp = g.CellCenter(rampCell % g.Width, rampCell / g.Width), lip = g.CellCenter(lipCell % g.Width, lipCell / g.Width);
        NVector2 beyond = g.CellCenter(lipCliff % g.Width, lipCliff / g.Width); // the cliff cell itself: blocked, so they stop at the lip
        for (int k = 0; k < _types.Count; k++)
        {
            _sim.Enqueue(Command.SpawnUnit(0, _types[k], ramp + new NVector2((k - 2) * 1.6f, 0f)));
            _sim.Enqueue(Command.SpawnUnit(0, _types[k], lip + new NVector2(0f, (k - 2) * 1.6f)));
        }
        Tick(2);
        var lipUnits = new List<int>();
        var all = new List<int>();
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && U.Owner[i] == 0 && _types.Contains(U.TypeId[i]) && NVector2.Distance(U.Position[i], ramp) < 12f || U.Alive[i] && U.Owner[i] == 0 && _types.Contains(U.TypeId[i]) && NVector2.Distance(U.Position[i], lip) < 12f)
            {
                all.Add(i);
                if (NVector2.Distance(U.Position[i], lip) < NVector2.Distance(U.Position[i], ramp)) lipUnits.Add(i);
            }
        // The lip group walks into the cliff: they stop at its edge.
        foreach (int i in lipUnits) _sim.Enqueue(Command.Move(0, new EntityHandle(i, U.Generation[i]), beyond));
        if (_shots != null)
        {
            _views.Sync(W, 1f);
            _camera.SetFocus(ramp.X, ramp.Y);
            _camera.SetZoom(20f);
            await Shot("ramp-20m");
        }
        float worstOrigin = 0f, worstSpread = 0f, worstHang = 0f, worstBury = 0f;
        string spreadAt = "", hangAt = "", buryAt = "";
        for (int t = 0; t < 200; t++)
        {
            _sim.Tick();
            if (t % 5 != 0) continue;
            _views.Sync(W, 1f);
            foreach (int i in all)
            {
                if (!U.Alive[i] || _views.ModelOf(i) is not Node3D model) continue;
                NVector2 p = U.Position[i];
                float ground = TerrainHeight.At(W.Heightmap, p.X, p.Y);
                worstOrigin = Math.Max(worstOrigin, Math.Abs(model.GlobalPosition.Y - ground));
                // The feet span about 0.3 m x scale from the origin: the ground's spread under them.
                float r = 0.3f * _data.Units[U.TypeId[i]].Scale, lo = ground, hi = ground;
                for (int a = 0; a < 8; a++)
                {
                    float ang = a * Mathf.Tau / 8f;
                    float h = TerrainHeight.At(W.Heightmap, p.X + r * Mathf.Cos(ang), p.Y + r * Mathf.Sin(ang));
                    lo = Math.Min(lo, h);
                    hi = Math.Max(hi, h);
                }
                float spread = Math.Max(ground - lo, hi - ground);
                string who = $"{_data.Units[U.TypeId[i]].Key} at ({p.X:F2}, {p.Y:F2}) {(lipUnits.Contains(i) ? "lip" : "ramp")}, state {U.State[i]}, tick {t}";
                if (ground - lo > worstHang) (worstHang, hangAt) = (ground - lo, who);
                if (hi - ground > worstBury) (worstBury, buryAt) = (hi - ground, who);
                if (spread > worstSpread) (worstSpread, spreadAt) = (spread, $"{_data.Units[U.TypeId[i]].Key} at ({p.X:F1}, {p.Y:F1}) {(lipUnits.Contains(i) ? "lip" : "ramp")}, state {U.State[i]}");
            }
        }
        GD.Print($"(D) {all.Count} figures on the ramp and the lip over 200 ticks: origin off the terrain by at most {worstOrigin:F4} m; ground spread under the feet at most {worstSpread:F3} m ({spreadAt}); ground up to {worstHang:F3} m under the origin within a foot's reach ({hangAt}), up to {worstBury:F3} m over it ({buryAt})");
        Check(worstOrigin < 1e-3f, $"(D) a figure's origin {worstOrigin:F4} m off the terrain");
        if (_shots != null)
        {
            _views.Sync(W, 1f);
            _camera.SetFocus(lip.X, lip.Y);
            _camera.SetZoom(20f);
            await Shot("cliff-lip-20m");
            _camera.SetZoom(_defaultZoom);
            await Shot("cliff-lip-default-zoom");
        }

        // (E') the overlays: a hurt, selected Warrior of the lip group: its hp bar over its head (picture; the line is row A's).
        int warrior = _data.FindUnit("teblor_warrior");
        foreach (int i in all)
        {
            if (!U.Alive[i] || U.TypeId[i] != warrior) continue;
            U.Hp[i] = W.MaxHp(i) / 2;
            var sel = _match.GetNode<SelectionController>("SelectionController");
            sel.SelectOnly(new EntityHandle(i, U.Generation[i]));
            var combat = _match.GetNode<CombatViews>("World3D/CombatViews");
            _views.Sync(W, 1f);
            combat.Sync(W, 1f);
            bool bar = false;
            for (int k = 0; k < combat.ShownBars; k++) bar |= combat.BarSlot(k) == i;
            Check(bar, "(E') a hurt Teblor Warrior shows no hp bar");
            if (_shots != null)
            {
                _camera.SetFocus(U.Position[i].X, U.Position[i].Y);
                _camera.SetZoom(14f);
                await Shot("hurt-warrior-bar-14m");
                _camera.SetZoom(_defaultZoom);
                await Shot("hurt-warrior-bar-default-zoom");
            }
            break;
        }
        foreach (int i in all) if (U.Alive[i]) U.Free(new EntityHandle(i, U.Generation[i]));
        Tick(1);
        _views.Sync(W, 1f);
    }

    private async Task FogRow()
    {
        if (!Check(_views.Fog != null, "(E) the match has no fog")) return;
        StartBasePlan plan = _match.Bases!;
        NVector2 hall = StartBase.FootprintCenter(W.NavGrid, _data.Buildings[plan.HallType[0]], plan.HallAnchor[0]);
        NVector2 enemyHall = StartBase.FootprintCenter(W.NavGrid, _data.Buildings[plan.HallType[1]], plan.HallAnchor[1]);
        int warrior = _data.FindUnit("teblor_warrior");
        NVector2 start = OpenNear(hall + NVector2.Normalize(enemyHall - hall) * 8f);
        _sim.Enqueue(Command.SpawnUnit(1, warrior, start));
        Tick(2);
        int slot = -1;
        for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && U.Owner[i] == 1 && U.TypeId[i] == warrior && NVector2.Distance(U.Position[i], start) < 4f) slot = i;
        if (!Check(slot >= 0, "(E) enemy warrior not spawned")) return;
        var h = new EntityHandle(slot, U.Generation[slot]);
        _sim.Enqueue(Command.Move(1, h, enemyHall));
        int shownFrames = 0, hiddenFrames = 0, bad = 0, flips = 0;
        bool last = true;
        string first = "";
        bool shotOut = false;
        for (int t = 0; t < 2400 && U.Alive[slot]; t++)
        {
            if (t == 900) _sim.Enqueue(Command.Move(1, h, start)); // and back into sight
            _sim.Tick();
            _views.Sync(W, 1f);
            bool seen = _views.Fog!.Refreshed(W)!.ShowsUnit(slot);
            bool shown = _views.IsShown(slot) && _views.ViewOf(slot)!.Visible;
            bool active = _views.AnimatorOf(slot)?.Active ?? false;
            if (seen) shownFrames++; else hiddenFrames++;
            if (seen != last) flips++;
            last = seen;
            if ((seen != shown || seen != active) && bad++ == 0) first = $"tick {t}: fog shows {seen}, drawn {shown}, player active {active}";
            if (!seen && !shotOut && _shots != null)
            {
                shotOut = true;
                _camera.SetFocus(U.Position[slot].X, U.Position[slot].Y);
                await Shot("fog-giant-just-left-sight");
            }
        }
        GD.Print($"(E) enemy Warrior out of sight and back: {shownFrames} ticks seen, {hiddenFrames} hidden, {flips} changes, {bad} mismatches (first: {first})");
        Check(hiddenFrames > 0 && shownFrames > 0 && flips >= 2, $"(E) the Warrior never left and came back into sight ({shownFrames} / {hiddenFrames} / {flips})");
        Check(bad == 0, $"(E) {bad} ticks where the figure disagrees with the fog; first: {first}");
        if (U.Alive[slot]) U.Free(h);
        Tick(1);
        _views.Sync(W, 1f);
    }

    private async Task Speeds()
    {
        StartBasePlan plan = _match.Bases!;
        NVector2 hall = StartBase.FootprintCenter(W.NavGrid, _data.Buildings[plan.HallType[0]], plan.HallAnchor[0]);
        NVector2 enemyHall = StartBase.FootprintCenter(W.NavGrid, _data.Buildings[plan.HallType[1]], plan.HallAnchor[1]);
        // Mid-map, far from both halls (the Breakers knock buildings down: a hall nearby ended the match early).
        NVector2 field = OpenNear((hall + enemyHall) * 0.5f);
        int warrior = _data.FindUnit("teblor_warrior"), breaker = _data.FindUnit("teblor_breaker"), javelin = _data.FindUnit("teblor_javelin_thrower");
        for (int k = 0; k < 6; k++)
        {
            _sim.Enqueue(Command.SpawnUnit(0, k % 2 == 0 ? warrior : javelin, OpenNear(field + new NVector2(-3f, (k - 3) * 1.8f))));
            _sim.Enqueue(Command.SpawnUnit(1, k % 2 == 0 ? breaker : warrior, OpenNear(field + new NVector2(3f, (k - 3) * 1.8f))));
        }
        Tick(2);
        for (int i = 0; i < U.Capacity; i++)
            if (U.Alive[i] && NVector2.Distance(U.Position[i], field) < 10f && _types.Contains(U.TypeId[i]))
                _sim.Enqueue(Command.AttackMove(U.Owner[i], new EntityHandle(i, U.Generation[i]), U.Owner[i] == 0 ? field + new NVector2(8f, 0f) : field - new NVector2(8f, 0f)));
        var combat = _match.GetNode<CombatViews>("World3D/CombatViews");
        _camera.SetFocus(field.X, field.Y);
        _runner.FixedFrameDelta = 1.0 / 60.0;
        _runner.ProcessMode = ProcessModeEnum.Inherit;
        _match.ProcessMode = ProcessModeEnum.Pausable;
        int deathsSeen = 0;
        foreach (double speed in new[] { 0.5, 1.0, 2.0 })
        {
            _runner.GameSpeed = speed;
            long t0 = _sim.TickNumber;
            await Frames(120);
            long ticks = _sim.TickNumber - t0;
            double want = 120.0 / 60.0 * 20.0 * speed;
            GD.Print($"(F) speed {speed}: {ticks} ticks over 120 frames of 1/60 s (want ~{want:F0})");
            Check(Math.Abs(ticks - want) <= Math.Max(2, want * 0.1), $"(F) speed {speed}: {ticks} ticks, want ~{want:F0}");
            if (_shots != null) await Shot($"fight-speed-{speed}");
        }
        // Paused: nothing moves.
        GetTree().Paused = true;
        long pt = _sim.TickNumber;
        var pos = new Dictionary<int, double>();
        for (int i = 0; i < U.Capacity; i++) if (_views.AnimatorOf(i) is AnimationPlayer a && _views.IsShown(i)) pos[i] = a.CurrentAnimationPosition;
        await Frames(30);
        int moved = 0;
        foreach (KeyValuePair<int, double> kv in pos) if (_views.AnimatorOf(kv.Key) is AnimationPlayer a && a.CurrentAnimationPosition != kv.Value) moved++;
        GD.Print($"(F) paused 30 frames: {_sim.TickNumber - pt} ticks, {moved} of {pos.Count} clips moved");
        Check(_sim.TickNumber == pt && moved == 0, $"(F) paused: {_sim.TickNumber - pt} ticks, {moved} clips moved");
        if (_shots != null) await Shot("fight-paused");
        GetTree().Paused = false;
        // Run the fight out at 2x; then every corpse disc is at its type's data radius, and the dead are hidden.
        _runner.GameSpeed = 2.0;
        int corpses = 0, wrong = 0, hiddenDead = 0, shownDead = 0, frames = 0;
        bool shotCorpses = false;
        for (int f = 0; f < 1800; f++)
        {
            await Frames(1);
            frames++;
            int alive = 0, now = 0;
            for (int i = 0; i < U.Capacity; i++) if (U.Alive[i] && _types.Contains(U.TypeId[i]) && NVector2.Distance(U.Position[i], field) < 30f) alive++;
            for (int m = 0; m < combat.Markers.Capacity; m++)
            {
                if (combat.MarkerShown(m) != 1) continue;
                int type = combat.Markers.Type[m];
                if (!_types.Contains(type)) continue;
                now++;
                float disc = combat.MarkerTransform(m).Basis.X.Length();
                if (Mathf.Abs(disc - _data.Units[type].Radius) > 1e-4f) wrong++;
            }
            corpses = Math.Max(corpses, now);
            for (int i = 0; i < U.Capacity; i++)
                if (!U.Alive[i] && _views.ViewOf(i) is MeshInstance3D v && U.Generation[i] > 0) { if (v.Visible) shownDead++; else hiddenDead++; }
            if (now >= 3 && !shotCorpses && _shots != null) { shotCorpses = true; await Shot("fight-corpses"); }
            if (alive <= 3) break;
        }
        GD.Print($"(F) fight ran {frames} frames at 2x");
        GD.Print($"(F) after the fight: {corpses} Teblor corpse discs, {wrong} not at the data radius; dead slots {hiddenDead} hidden, {shownDead} drawn");
        Check(corpses > 0 && wrong == 0, $"(F) {corpses} corpses, {wrong} off the data radius");
        Check(shownDead == 0, $"(F) {shownDead} dead slots still drawn");
        if (_shots != null) await Shot("fight-after-corpses");
        _runner.ProcessMode = ProcessModeEnum.Disabled;
        deathsSeen = corpses;
        _ = deathsSeen;
    }

    // ---- helpers ------------------------------------------------------------------------------------------------------------
    private async Task Shot(string name)
    {
        if (_defaultZoom < 0) _defaultZoom = 40f;
        await Frames(6);
        for (int i = 0; i < 2; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = Path.Combine(_shots!, name + ".png");
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"shot {path}");
    }

    private float BaseHeight(UnitViews views, int type)
    {
        var holder = new Node3D();
        AddChild(holder);
        Node3D model = views.Models!.Instantiate(type, out MeshInstance3D mesh, out AnimationPlayer _);
        holder.AddChild(model);
        float h = FigureBox(mesh, views.Models!.AtlasOf(type)).Size.Y;
        holder.Free();
        return h;
    }

    // M6-V24: on the one surface of an atlas model the props' own faces are the vertices reading the props' tile (a team
    // prop reads the team tile and counts, as it did on the team surface); the surface check stays for a pre-atlas file.
    private Aabb FigureBox(MeshInstance3D mi, UnitModels.Atlas? atlas)
    {
        Mesh mesh = mi.Mesh;
        Transform3D x = mi.GlobalTransform;
        Aabb box = default;
        bool any = false;
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            if (mesh.SurfaceGetMaterial(s) is BaseMaterial3D mat && _propMaterials.Contains(mat.ResourceName)) continue;
            Godot.Collections.Array arrays = mesh.SurfaceGetArrays(s);
            Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            Vector2[] uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            for (int k = 0; k < vertices.Length; k++)
            {
                if (atlas != null && k < uvs.Length && atlas.IsProp(uvs[k])) continue;
                Vector3 p = x * vertices[k];
                box = any ? box.Expand(p) : new Aabb(p, Vector3.Zero);
                any = true;
            }
        }
        return box;
    }

    private int NearestNode(NVector2 from, ResourceKind kind)
    {
        int best = -1;
        float bestD = float.PositiveInfinity;
        for (int i = 0; i < W.Resources.Capacity; i++)
        {
            if (!W.Resources.Alive[i] || _data.Resources[W.Resources.TypeId[i]].Resource != kind) continue;
            float d = NVector2.Distance(NodeCenter(i), from);
            if (d < bestD) (best, bestD) = (i, d);
        }
        return best;
    }

    private NVector2 NodeCenter(int node)
    {
        ResourceDef d = _data.Resources[W.Resources.TypeId[node]];
        int c = W.Resources.Cell[node], w = W.NavGrid.Width;
        return new((c % w + d.FootprintWidth * 0.5f) * MapConstants.CellSize, (c / w + d.FootprintHeight * 0.5f) * MapConstants.CellSize);
    }

    private NVector2 OpenNear(NVector2 p)
    {
        NavGrid g = W.NavGrid;
        g.WorldToCell(p, out int px, out int py);
        for (int r = 0; r < 20; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                    if (StartLayout.IsOpen(g, px + dx, py + dy)) return g.CellCenter(px + dx, py + dy);
        throw new InvalidOperationException("no open ground");
    }

    private static int Slot(UnitStore u, int owner, int type)
    {
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == owner && u.TypeId[i] == type) return i;
        return -1;
    }

    private void Tick(int n)
    {
        for (int i = 0; i < n; i++) _sim.Tick();
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private bool Check(bool ok, string why)
    {
        if (!ok) _failures.Add(why);
        return ok;
    }
}
