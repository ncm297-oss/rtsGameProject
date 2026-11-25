using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// M7-V4: a building's data <c>scale</c> is its drawn height (<see cref="BuildingViews.DrawnHeight"/> =
/// <see cref="BuildingViews.BoxHeight"/> x scale); the box keeps the footprint's width and depth (the unit rule of M7-V3c).
/// <list type="bullet">
/// <item>Forged data (a copy of <c>res://data</c> under <c>user://</c> with the Billet at 1.25 and the Barracks at 2.0; the Garrison
/// Keep stays 1.0), both players Malazan, fog on, in a real <c>Match</c>: each finished box is the footprint by the drawn height,
/// its base on the ground and its top at ground + scale x 3 m; its bar back sits <c>BarHeight - BoxHeight</c> over the drawn
/// top, and so does a hit-point bar on the building hurt by real crossbow bolts; a Barracks site rises from
/// <c>SiteMinHeight</c> of 6 m to exactly 6 m with its progress bar over its full top on every frame; the placement ghost of each
/// type is the same box, its reason label over the drawn top; an enemy Billet and Barracks scouted and left are drawn as ghosts
/// of the same height; 20 frames of the scaled views during the site's rise allocate nothing.</item>
/// <item>The hit test follows the drawn height (M6-VH9, BUG-1630; it kept the 3 m box until then): a click on the 2.0
/// Barracks just above 3 m, where the old box wasn't, selects the Barracks; so does a click on its lower half. The
/// footprint, outline, ring and rally point read the footprint, not the height.</item>
/// <item>Shipped data: every type's drawn height is <c>3 m x scale</c>; a Teblor base (Longhouse, Hut, War Lodge) beside a Malazan
/// one at the default zoom draws each box at its type's height (1.0 today; 1.25 once the data track's D31 rows land).</item>
/// </list>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/BuildingScaleViewTest.tscn</c>; prints
/// "BUILDING SCALE VIEW TEST PASS" or FAIL lines. Windowed with <c>-- --shots &lt;dir&gt; --placeholder-art</c> it saves
/// <c>building-scale-row.png</c> (1.0, 1.25, 2.0 and the finished 2.0 site, left to right, at the default zoom) and
/// <c>teblor-beside-malazan.png</c>.
/// </summary>
public partial class BuildingScaleViewTest : Node
{
    private const string Tag = "BUILDING SCALE VIEW TEST";
    private const float Eps = 1e-4f;

    // The forged scales, by building id (Malazan; the Garrison Keep stays 1.0 as the reference).
    private static readonly (string Id, float Scale)[] Forged = { ("malazan_billet", 1.25f), ("malazan_barracks", 2.0f) };

    private readonly List<string> _failures = new();
    private string? _shots;
    private Match _match = null!;
    private Simulation _sim = null!;
    private BuildingViews _views = null!;
    private RtsCamera _camera = null!;
    private SelectionController _sel = null!;

    private World W => _sim.World;
    private BuildingStore B => _sim.World.Buildings;

    public override async void _Ready()
    {
        string root = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://building-scale-view-{OS.GetProcessId()}"));
        UnitViews.UseModels = false; // no unit is read here; capsules keep the scene light
        BuildingViews.UseModels = false; // M6-V26: the rows pin each type's box and its scale (models: BuildingModelTest)
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            string[] args = OS.GetCmdlineUserArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--shots") _shots = args[i + 1];
            await Frames(1);
            string dataDir = ProjectSettings.GlobalizePath("res://data");
            DataLoadResult loaded = DataLoader.LoadAll(dataDir);
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            ShippedRows(loaded.Data!);
            await ForgedRows(Forge(dataDir, root));
            await TeblorBesideMalazan(loaded.Data!);
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        foreach (string f in _failures.Take(60)) GD.Print($"{Tag} FAIL: {f}");
        if (_failures.Count == 0) GD.Print($"{Tag} PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // The rule on whatever the shipped data says: 3 m x scale, and the bar offset kept; a scale-1 type exactly the old box.
    private void ShippedRows(GameData data)
    {
        int ones = 0;
        for (int t = 0; t < data.Buildings.Length; t++)
        {
            BuildingDef def = data.Buildings[t];
            float h = BuildingViews.DrawnHeight(def), bar = BuildingViews.BarHeightOf(def);
            Check(h == BuildingViews.BoxHeight * def.Scale && Math.Abs(bar - h - (BuildingViews.BarHeight - BuildingViews.BoxHeight)) < Eps,
                $"shipped {def.Key} (scale {def.Scale}): drawn {h}, bar {bar}");
            if (def.Scale != 1f) continue;
            ones++;
            Check(h == BuildingViews.BoxHeight && bar == BuildingViews.BarHeight, $"shipped {def.Key}: scale 1 drawn {h} / bar {bar}, not the old 3 / 3.6");
        }
        GD.Print($"shipped: {data.Buildings.Length} building types, {ones} at scale 1, {data.Buildings.Length - ones} scaled");
    }

    private GameData Forge(string dataDir, string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
        string copy = Path.Combine(root, "data");
        Directory.CreateDirectory(copy);
        foreach (string dir in Directory.GetDirectories(dataDir, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(copy, Path.GetRelativePath(dataDir, dir)));
        foreach (string file in Directory.GetFiles(dataDir, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(copy, Path.GetRelativePath(dataDir, file)));
        string path = Path.Combine(copy, "factions", "malazan", "buildings.json");
        JsonNode doc = JsonNode.Parse(File.ReadAllText(path)) ?? throw new InvalidOperationException($"{path} is empty");
        foreach ((string id, float scale) in Forged)
            doc["buildings"]!.AsArray().First(n => (string?)n!["id"] == id)!["scale"] = (double)(decimal)scale;
        File.WriteAllText(path, doc.ToJsonString());
        DataLoadResult loaded = DataLoader.LoadAll(copy);
        if (!loaded.Ok) throw new InvalidOperationException("forged data failed to load: " + string.Join("; ", loaded.Errors.Take(5)));
        GameData forged = loaded.Data!;
        foreach ((string id, float scale) in Forged)
            Check(forged.FindBuilding(id) is int t && t >= 0 && forged.Buildings[t].Scale == scale, $"forged {id}: scale not {scale}");
        Check(forged.Buildings[forged.FindBuilding("malazan_garrison_keep")].Scale == 1f, "forged keep not at scale 1");
        return forged;
    }

    private void StartMatch(GameData data, params string[] extra)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        var args = new List<string> { "--seed", "1", "--units", "0", "--no-bases", "--mute" };
        args.AddRange(extra);
        _match.Start(data, LaunchOptions.Parse(args.ToArray()));
        var runner = _match.GetNode<SimRunner>("SimRunner");
        runner.ProcessMode = ProcessModeEnum.Disabled; // ticked here
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

    private async Task ForgedRows(GameData data)
    {
        StartMatch(data, "--faction", "1", "malazan", "--faction", "2", "malazan", "--zoom", CameraLimits.DefaultZoom.ToString(CultureInfo.InvariantCulture));
        int keep = data.FindBuilding("malazan_garrison_keep"), billet = data.FindBuilding("malazan_billet"), barracks = data.FindBuilding("malazan_barracks");
        int laborer = data.FindUnit("malazan_laborer"), crossbow = data.FindUnit("malazan_crossbowman");
        System.Numerics.Vector2 west = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0];
        int keepSlot = Spawn(0, keep, west + new System.Numerics.Vector2(-10f, 0f));
        int billetSlot = Spawn(0, billet, west + new System.Numerics.Vector2(-18f, 0f));
        int barracksSlot = Spawn(0, barracks, west + new System.Numerics.Vector2(-26f, 0f));
        if (keepSlot < 0 || billetSlot < 0 || barracksSlot < 0) throw new InvalidOperationException($"row not spawned: {keepSlot} {billetSlot} {barracksSlot}");
        System.Numerics.Vector2 mid = Centre(billetSlot);
        _camera.SetFocus(mid.X - 4f, mid.Y);
        await Frames(2);

        // Criterion 2: the finished boxes.
        foreach (int slot in new[] { keepSlot, billetSlot, barracksSlot }) FinishedBox(slot, "finished");

        // Criterion 2: a Barracks site rises to 6 m; criterion 3: its progress bar over the drawn top on every frame;
        // criterion 6: 20 frames of the rise allocate nothing.
        SiteRise(data, barracks, laborer, west + new System.Numerics.Vector2(-36f, 0f));

        // Criterion 2: the placement ghost.
        PlacementGhost(data, new[] { keep, billet, barracks }, keepSlot);
        await GhostLabel(barracks, keepSlot);

        // Criterion 5 (M7-V4), changed by M6-VH9: the hit test follows the drawn height, not the 3 m box.
        HitRows(barracksSlot, keepSlot);

        // Criterion 3: hit-point bars on the hurt 1.0, 1.25 and 2.0 buildings.
        await HurtBars(crossbow, new[] { keepSlot, billetSlot, barracksSlot });

        // Criterion 2: the remembered ghosts of an enemy Billet and Barracks.
        RememberedGhosts(data, billet, barracks, laborer, west);

        if (_shots != null && DisplayServer.GetName() != "headless")
        {
            _camera.SetFocus(mid.X - 4f, mid.Y);
            await Shot("building-scale-row");
        }
        await EndMatch();
    }

    // The box of a finished building: footprint wide and deep, DrawnHeight tall, base on the ground; the bar back over the top.
    private void FinishedBox(int slot, string what)
    {
        BuildingDef def = W.Data.Buildings[B.TypeId[slot]];
        Node3D root = _views.ViewOf(slot)!;
        MeshInstance3D box = _views.BoxOf(slot);
        var mesh = (BoxMesh)box.Mesh;
        System.Numerics.Vector2 c = Centre(slot);
        float ground = TerrainHeight.At(W.Heightmap, c.X, c.Y), want = BuildingViews.BoxHeight * def.Scale;
        float top = root.Position.Y + box.Position.Y + mesh.Size.Y * box.Scale.Y / 2f;
        float bottom = root.Position.Y + box.Position.Y - mesh.Size.Y * box.Scale.Y / 2f;
        GD.Print($"  {what} {def.Key} scale {def.Scale.ToString(CultureInfo.InvariantCulture)}: box {mesh.Size.X} x {mesh.Size.Y * box.Scale.Y} x {mesh.Size.Z}, top {top - ground:0.###} m over the ground");
        Check(_views.IsShown(slot) && root.Visible && !_views.ShownAsSite(slot), $"{what} {def.Key}: not shown as a finished building");
        Check(mesh.Size.X == def.FootprintWidth * MapConstants.CellSize && mesh.Size.Z == def.FootprintHeight * MapConstants.CellSize,
            $"{what} {def.Key}: box {mesh.Size.X} x {mesh.Size.Z}, footprint {def.FootprintWidth * MapConstants.CellSize} x {def.FootprintHeight * MapConstants.CellSize}");
        Check(Math.Abs(mesh.Size.Y - want) < Eps && Math.Abs(box.Scale.Y - 1f) < Eps && box.Scale.X == 1f && box.Scale.Z == 1f,
            $"{what} {def.Key}: box height {mesh.Size.Y} x {box.Scale}, want {want}");
        Check(Math.Abs(root.Position.Y - ground) < 1e-3f && Math.Abs(bottom - ground) < 1e-3f && Math.Abs(top - (ground + want)) < 1e-3f,
            $"{what} {def.Key}: root {root.Position.Y}, bottom {bottom}, top {top}, ground {ground}, want top {ground + want}");
        Check(Math.Abs(root.Position.X - c.X) < 1e-3f && Math.Abs(root.Position.Z - c.Y) < 1e-3f, $"{what} {def.Key}: not on its footprint centre");
        float barY = root.GetNode<MeshInstance3D>("BarBack").Position.Y;
        Check(Math.Abs(barY - want - (BuildingViews.BarHeight - BuildingViews.BoxHeight)) < Eps,
            $"{what} {def.Key}: bar back {barY} m, drawn top {want} m, want {BuildingViews.BarHeight - BuildingViews.BoxHeight} m over it");
    }

    private void SiteRise(GameData data, int type, int laborer, System.Numerics.Vector2 near)
    {
        BuildingDef def = data.Buildings[type];
        float h = BuildingViews.DrawnHeight(def);
        int anchor = OpenAnchor(def, near);
        if (!Check(anchor >= 0, "site: no open anchor")) return;
        System.Numerics.Vector2 at = PlacementGhost_AnchorPoint(anchor);
        System.Numerics.Vector2 c = StartBase.FootprintCenter(W.NavGrid, def, anchor);
        _sim.Enqueue(Command.SpawnUnit(0, laborer, c + new System.Numerics.Vector2(0f, def.FootprintHeight * MapConstants.CellSize / 2f + 0.6f))); // in reach
        _sim.Tick();
        _sim.Tick(); // a command applies at the start of the tick after the one it was queued for
        int worker = -1;
        for (int i = 0; i < W.Units.Capacity; i++)
            if (W.Units.Alive[i] && W.Units.Owner[i] == 0 && W.Units.TypeId[i] == laborer) worker = i;
        if (!Check(worker >= 0, "site: no laborer")) return;
        _sim.Enqueue(Command.Build(0, new EntityHandle(worker, W.Units.Generation[worker]), type, at));
        int site = -1;
        for (int t = 0; t < 200 && site < 0; t++)
        {
            _sim.Tick();
            site = B.SlotAt(anchor % W.NavGrid.Width, anchor / W.NavGrid.Width);
        }
        if (!Check(site >= 0 && B.UnderConstruction[site], $"site: none at anchor {anchor} (wood {W.Wood[0]})")) return;
        int gen = B.Generation[site];
        float lowest = float.MaxValue, highest = 0f, lastRise = 0f;
        int frames = 0, bad = 0, barBad = 0;
        long bytes = 0;
        int measured = 0;
        for (int t = 0; t < 4000 && B.Alive[site] && B.Generation[site] == gen && B.UnderConstruction[site]; t++)
        {
            _sim.Tick();
            if (t % 8 != 0) continue;
            long before = GC.GetAllocatedBytesForCurrentThread();
            _views.Sync(W);
            long after = GC.GetAllocatedBytesForCurrentThread();
            if (frames > 2 && measured < 20)
            {
                bytes += after - before;
                measured++;
            }
            frames++;
            MeshInstance3D box = _views.BoxOf(site);
            float top = box.Position.Y + ((BoxMesh)box.Mesh).Size.Y * box.Scale.Y / 2f;
            BuildingBarKind bar = BuildingBars.Of(B, W.Data.Buildings, site, out float fill);
            float rise = Math.Max(BuildingViews.SiteMinHeight, fill);
            lastRise = rise;
            if (Math.Abs(top - h * rise) > 1e-3f || !_views.ShownAsSite(site)) bad++;
            float barY = _views.BarFillOf(site).Position.Y;
            if (bar == BuildingBarKind.Progress && fill > 0f && barY - BuildingViews.BarThickness / 2f <= h) barBad++;
            lowest = Math.Min(lowest, top);
            highest = Math.Max(highest, top);
        }
        _views.Sync(W);
        Check(B.Alive[site] && B.Generation[site] == gen && !B.UnderConstruction[site], $"site: not finished after the loop (progress rise {lastRise})");
        GD.Print($"  site {def.Key} scale {def.Scale.ToString(CultureInfo.InvariantCulture)}: {frames} frames, top from {lowest:0.###} to {highest:0.###} m, finished {h} m; 20 frames {bytes} bytes");
        Check(bad == 0, $"site: {bad} of {frames} frames with the box top not {h} m x the rise");
        Check(barBad == 0, $"site: {barBad} frames with the progress bar inside the drawn box");
        Check(Math.Abs(lowest - h * BuildingViews.SiteMinHeight) < 0.05f * h && highest <= h + 1e-3f, $"site: rose {lowest} to {highest}, want from {h * BuildingViews.SiteMinHeight} to at most {h}");
        Check(measured == 20 && bytes == 0, $"site: {measured} measured syncs of the scaled rise allocated {bytes} bytes");
        FinishedBox(site, "site finished");
    }

    private void PlacementGhost(GameData data, int[] types, int keepSlot)
    {
        var ghost = _match.GetNode<BuildGhost>("World3D/BuildGhost");
        foreach (int type in types)
        {
            BuildingDef def = data.Buildings[type];
            ghost.Begin(type);
            var mesh = (BoxMesh)ghost.Box.Mesh;
            float h = BuildingViews.DrawnHeight(def);
            Check(mesh.Size.X == def.FootprintWidth * MapConstants.CellSize && mesh.Size.Z == def.FootprintHeight * MapConstants.CellSize
                && Math.Abs(mesh.Size.Y - h) < Eps && Math.Abs(ghost.Box.Position.Y - h / 2f) < Eps && ghost.Box.Scale == Vector3.One,
                $"placement ghost {def.Key}: box {mesh.Size} at {ghost.Box.Position.Y}, want height {h} with its base at 0");
            ghost.End();
        }
    }

    // The reason label of a blocked placement sits over the drawn top of the type hovered.
    private async Task GhostLabel(int type, int keepSlot)
    {
        var ghost = _match.GetNode<BuildGhost>("World3D/BuildGhost");
        float h = BuildingViews.DrawnHeight(W.Data.Buildings[type]);
        System.Numerics.Vector2 c = Centre(keepSlot);
        _camera.SetFocus(c.X, c.Y);
        await Frames(1);
        ghost.Begin(type);
        ghost.ScreenOverride = _camera.UnprojectPosition(new Vector3(c.X, TerrainHeight.At(W.Heightmap, c.X, c.Y), c.Y));
        await Frames(2);
        float px = ghost.ReasonLabel.PixelSize;
        float bottom = ghost.ReasonLabel.Position.Y - 0.5f * BuildGhost.ReasonFontSize * px;
        Check(ghost.ReasonLabel.Visible && bottom > h, $"placement ghost label over the keep: visible {ghost.ReasonLabel.Visible}, lower edge {bottom} m, drawn top {h} m");
        ghost.ScreenOverride = null;
        ghost.End();
    }

    // A click above the old 3 m top on the 2.0 Barracks's drawn box selects the Barracks (M6-VH9: picking by drawn height); a low
    // click too.
    private void HitRows(int barracksSlot, int keepSlot)
    {
        System.Numerics.Vector2 c = Centre(barracksSlot);
        _camera.SetFocus(c.X, c.Y);
        MeshInstance3D box = _views.BoxOf(barracksSlot);
        Aabb drawn = box.GlobalTransform * box.GetAabb();
        float ground = TerrainHeight.At(W.Heightmap, c.X, c.Y);
        // Screen bounds of the drawn box.
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (int k = 0; k < 8; k++)
        {
            Vector2 p = _camera.UnprojectPosition(drawn.GetEndpoint(k));
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X); minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
        }
        Vector2 best = new(-1f, -1f);
        float bestY = float.MaxValue;
        for (float y = minY; y <= maxY; y += 1f)
            for (float x = minX; x <= maxX; x += 2f)
            {
                var px = new Vector2(x, y);
                Vector3 o = _camera.ProjectRayOrigin(px), d = _camera.ProjectRayNormal(px);
                if (RayHit(drawn, o, d) is not Vector3 hit) continue;
                if (hit.Y < ground + BuildingViews.BoxHeight + 0.05f || hit.Y >= bestY) continue;
                // Only where the old 3 m box is not behind the pixel: there the old hit test gave something else.
                if (RayHit(new Aabb(drawn.Position, new Vector3(drawn.Size.X, BuildingViews.BoxHeight, drawn.Size.Z)), o, d) != null) continue;
                best = px;
                bestY = hit.Y;
            }
        if (!Check(best.X >= 0f, "hit test: no pixel on the drawn 2.0 box above 3 m outside the old box")) return;
        int before = _sel.PickBuilding(best);
        _sel.SelectBuilding(keepSlot);
        Click(best);
        int after = _sel.SelectedBuilding;
        GD.Print($"  hit test: a click on the 2.0 Barracks's drawn box {bestY - ground:0.###} m up (old top 3 m) picks {before}, selected {after}");
        Check(after == barracksSlot && before == barracksSlot, $"hit test: a click {bestY - ground} m up the drawn 2.0 box selected building {after} (pick {before}), not the Barracks {barracksSlot}");
        // Low on the box (1 m up its near face), inside the old box too: the Barracks.
        Vector2 low = _camera.UnprojectPosition(new Vector3(c.X, ground + 1f, c.Y));
        _sel.SelectBuilding(keepSlot);
        Click(low);
        Check(_sel.SelectedBuilding == barracksSlot, $"hit test: a click 1 m up the Barracks selected {_sel.SelectedBuilding}, not the Barracks {barracksSlot}");
        _sel.ClearBuilding();
    }

    // The first point where the ray from o along d enters the box (slab method), or null.
    private static Vector3? RayHit(Aabb box, Vector3 o, Vector3 d)
    {
        float near = 0f, far = float.MaxValue;
        for (int a = 0; a < 3; a++)
        {
            float lo = box.Position[a], hi = box.End[a];
            if (Math.Abs(d[a]) < 1e-9f)
            {
                if (o[a] < lo || o[a] > hi) return null;
                continue;
            }
            float t1 = (lo - o[a]) / d[a], t2 = (hi - o[a]) / d[a];
            near = Math.Max(near, Math.Min(t1, t2));
            far = Math.Min(far, Math.Max(t1, t2));
            if (near > far) return null;
        }
        return o + d * near;
    }

    private async Task HurtBars(int crossbow, int[] targets)
    {
        var shooters = new List<(int Unit, int Target)>();
        foreach (int slot in targets)
        {
            System.Numerics.Vector2 c = Centre(slot);
            _sim.Enqueue(Command.SpawnUnit(1, crossbow, c + new System.Numerics.Vector2(0f, 10f)));
        }
        _sim.Tick();
        _sim.Tick();
        var used = new HashSet<int>();
        foreach (int slot in targets)
        {
            System.Numerics.Vector2 c = Centre(slot);
            for (int i = 0; i < W.Units.Capacity; i++)
            {
                if (!W.Units.Alive[i] || W.Units.Owner[i] != 1 || used.Contains(i)) continue;
                if (System.Numerics.Vector2.Distance(W.Units.Position[i], c + new System.Numerics.Vector2(0f, 10f)) > 1.5f) continue;
                used.Add(i);
                shooters.Add((i, slot));
                _sim.Enqueue(Command.Attack(1, new EntityHandle(i, W.Units.Generation[i]), new EntityHandle(slot, B.Generation[slot]), isBuilding: true));
                break;
            }
        }
        if (!Check(shooters.Count == targets.Length, $"hurt bars: {shooters.Count} shooters for {targets.Length} buildings")) return;
        for (int t = 0; t < 1200 && targets.Any(s => B.Hp[s] >= W.Data.Buildings[B.TypeId[s]].Hp); t++) _sim.Tick();
        foreach ((int unit, int _) in shooters) _sim.Enqueue(Command.Stop(1, new EntityHandle(unit, W.Units.Generation[unit])));
        _sim.Tick();
        _views.Sync(W);
        await Frames(1);
        foreach (int slot in targets)
        {
            BuildingDef def = W.Data.Buildings[B.TypeId[slot]];
            float h = BuildingViews.DrawnHeight(def);
            BuildingBarKind bar = _views.ShownBar(slot, out float fill);
            MeshInstance3D f = _views.BarFillOf(slot);
            GD.Print($"  hurt {def.Key} scale {def.Scale.ToString(CultureInfo.InvariantCulture)}: hp {B.Hp[slot]} / {def.Hp}, bar {bar} at {f.Position.Y:0.###} m, top {h} m");
            Check(bar == BuildingBarKind.HitPoints && f.Visible && fill > 0f && fill < 1f, $"hurt {def.Key}: bar {bar} visible {f.Visible} fill {fill}");
            Check(Math.Abs(f.Position.Y - h - (BuildingViews.BarHeight - BuildingViews.BoxHeight)) < Eps && f.Position.Y - BuildingViews.BarThickness / 2f > h,
                $"hurt {def.Key}: hp bar at {f.Position.Y} m, drawn top {h} m");
        }
    }

    private void RememberedGhosts(GameData data, int billet, int barracks, int laborer, System.Numerics.Vector2 west)
    {
        System.Numerics.Vector2 far = west + new System.Numerics.Vector2(50f, 0f);
        int eBillet = Spawn(1, billet, far);
        int eBarracks = Spawn(1, barracks, far + new System.Numerics.Vector2(8f, 0f));
        if (!Check(eBillet >= 0 && eBarracks >= 0, $"ghosts: enemy row not spawned ({eBillet}, {eBarracks})")) return;
        System.Numerics.Vector2 between = (Centre(eBillet) + Centre(eBarracks)) / 2f + new System.Numerics.Vector2(0f, 7f);
        _sim.Enqueue(Command.SpawnUnit(0, laborer, between));
        _sim.Tick();
        _sim.Tick();
        int scout = -1;
        for (int i = 0; i < W.Units.Capacity; i++)
            if (W.Units.Alive[i] && W.Units.Owner[i] == 0 && System.Numerics.Vector2.Distance(W.Units.Position[i], between) < 1.5f) scout = i;
        if (!Check(scout >= 0, "ghosts: no scout")) return;
        for (int t = 0; t < 100 && !(W.Fog.CanSeeBuilding(0, eBillet) && W.Fog.CanSeeBuilding(0, eBarracks)); t++) _sim.Tick();
        Check(W.Fog.CanSeeBuilding(0, eBillet) && W.Fog.CanSeeBuilding(0, eBarracks), "ghosts: the scout never saw the enemy row");
        _views.Sync(W);
        _sim.Enqueue(Command.Move(0, new EntityHandle(scout, W.Units.Generation[scout]), west + new System.Numerics.Vector2(0f, 10f)));
        for (int t = 0; t < 1200 && !(_views.IsGhostShown(eBillet) && _views.IsGhostShown(eBarracks)); t++)
        {
            _sim.Tick();
            _views.Sync(W);
        }
        foreach (int slot in new[] { eBillet, eBarracks })
        {
            BuildingDef def = W.Data.Buildings[B.TypeId[slot]];
            if (!Check(_views.IsGhostShown(slot), $"ghosts: {def.Key} not drawn as a ghost")) continue;
            MeshInstance3D g = _views.GhostBoxOf(slot)!;
            var mesh = (BoxMesh)g.Mesh;
            System.Numerics.Vector2 c = Centre(slot);
            float ground = TerrainHeight.At(W.Heightmap, c.X, c.Y), h = BuildingViews.DrawnHeight(def);
            float top = g.Position.Y + mesh.Size.Y * g.Scale.Y / 2f, bottom = g.Position.Y - mesh.Size.Y * g.Scale.Y / 2f;
            GD.Print($"  ghost {def.Key} scale {def.Scale.ToString(CultureInfo.InvariantCulture)}: box {mesh.Size.X} x {mesh.Size.Y * g.Scale.Y} x {mesh.Size.Z}, top {top - ground:0.###} m");
            Check(!_views.GhostShownAsSite(slot) && mesh.Size.X == def.FootprintWidth * MapConstants.CellSize && mesh.Size.Z == def.FootprintHeight * MapConstants.CellSize
                && Math.Abs(bottom - ground) < 1e-3f && Math.Abs(top - ground - h) < 1e-3f,
                $"ghosts: {def.Key} ghost {mesh.Size} x {g.Scale.Y} from {bottom} to {top}, ground {ground}, want {h} tall");
        }
        // The ghosts' steady frames allocate nothing either.
        _views.Sync(W);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int f = 0; f < 20; f++) _views.Sync(W);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(bytes == 0, $"ghosts: 20 syncs with scaled ghosts and boxes allocated {bytes} bytes");
    }

    // Criterion 7 (and the shipped rule): a Teblor base beside a Malazan one, each box its type's drawn height.
    private async Task TeblorBesideMalazan(GameData data)
    {
        int teblor = data.FindFaction("teblor"), malazan = data.FindFaction("malazan");
        if (!Check(teblor >= 0 && malazan >= 0, "teblor row: factions missing")) return;
        StartMatch(data, "--faction", "1", "teblor", "--faction", "2", "malazan", "--no-fog");
        System.Numerics.Vector2 west = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0];
        BuildingSlot[] kinds = { BuildingSlot.TownHall, BuildingSlot.House, BuildingSlot.InfantryHall };
        var slots = new List<int>();
        float x = -4f;
        foreach ((int player, int faction) in new[] { (0, teblor), (1, malazan) })
        {
            foreach (BuildingSlot kind in kinds)
            {
                int type = StartBase.BuildingOfSlot(data, faction, kind);
                BuildingDef def = data.Buildings[type];
                int slot = Spawn(player, type, west + new System.Numerics.Vector2(x - def.FootprintWidth * MapConstants.CellSize, 0f));
                x -= def.FootprintWidth * MapConstants.CellSize + 3f;
                if (Check(slot >= 0, $"teblor row: {def.Key} not spawned")) slots.Add(slot);
            }
            x -= 4f;
        }
        await Frames(2);
        foreach (int slot in slots) FinishedBox(slot, "base row");
        if (_shots != null && DisplayServer.GetName() != "headless" && slots.Count > 0)
        {
            System.Numerics.Vector2 a = Centre(slots[0]), b = Centre(slots[^1]);
            _camera.SetZoom(CameraLimits.DefaultZoom);
            _camera.SetFocus((a.X + b.X) / 2f, a.Y);
            await Shot("teblor-beside-malazan");
        }
        await EndMatch();
    }

    // A building of `type` for `player` with its anchor at the first open spot near `near` (2 m steps outward); its slot or -1.
    private int Spawn(int player, int type, System.Numerics.Vector2 near)
    {
        BuildingDef def = W.Data.Buildings[type];
        int anchor = OpenAnchor(def, near);
        if (anchor < 0) return -1;
        _sim.Enqueue(Command.SpawnBuilding(player, type, PlacementGhost_AnchorPoint(anchor)));
        _sim.Tick();
        _sim.Tick(); // a command applies at the start of the tick after the one it was queued for
        int slot = B.SlotAt(anchor % W.NavGrid.Width, anchor / W.NavGrid.Width);
        return slot >= 0 && B.Cell[slot] == anchor && B.TypeId[slot] == type ? slot : -1;
    }

    private int OpenAnchor(BuildingDef def, System.Numerics.Vector2 near)
    {
        int type = W.Data.FindBuilding(def.Key);
        for (int r = 0; r <= 6; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    System.Numerics.Vector2 p = near + new System.Numerics.Vector2(dx, dy) * MapConstants.CellSize;
                    if (!W.NavGrid.WorldToCell(p, out int cx, out int cy)) continue;
                    int a = cy * W.NavGrid.Width + cx;
                    if (cx < 1 || cy < 1 || cx + def.FootprintWidth >= W.NavGrid.Width - 1 || cy + def.FootprintHeight >= W.NavGrid.Height - 1) continue;
                    if (!B.Fits(type, a) || !FitsWithMargin(def, cx, cy)) continue;
                    return a;
                }
        return -1;
    }

    // One free cell round the footprint, so neighbours never touch and units can pass.
    private bool FitsWithMargin(BuildingDef def, int cx, int cy)
    {
        for (int y = cy - 1; y <= cy + def.FootprintHeight; y++)
            for (int x = cx - 1; x <= cx + def.FootprintWidth; x++)
                if (B.SlotAt(x, y) >= 0 || !W.NavGrid.IsPassable(x, y)) return false;
        return true;
    }

    private System.Numerics.Vector2 PlacementGhost_AnchorPoint(int anchor) => Rts.Sim.ViewApi.PlacementGhost.AnchorPoint(W.NavGrid, anchor);

    private System.Numerics.Vector2 Centre(int slot) => StartBase.FootprintCenter(W.NavGrid, W.Data.Buildings[B.TypeId[slot]], B.Cell[slot]);

    private void Click(Vector2 at)
    {
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
    }

    private async Task Shot(string name)
    {
        Directory.CreateDirectory(_shots!);
        await Frames(4);
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = Path.Combine(_shots!, name + ".png");
        Error e = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"screenshot {path} ({e})");
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
