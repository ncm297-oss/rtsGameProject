using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// M7-V1 (docs/02 "Combat / Stats": the visual scale may be larger than the collision radius): each unit type is drawn at its
/// data <c>scale</c> times today's capsule height, and what is sized from the body follows it, but the hit test does not.
/// Since M7-V3c (BUG-1260) the width is the collision radius alone: <c>scale</c> is the height only, so the rings and the corpse
/// disc are multiples of the radius.
/// <list type="bullet">
/// <item>Shipped data: every type's mesh is <c>radius</c> by <c>BodyHeight(radius) x scale</c>, and every type at scale 1
/// is exactly today's capsule (<c>radius</c> by <c>2 radius + 1</c>).</item>
/// <item>Forged data (a copy of <c>res://data</c> under <c>user://</c> with Heavy Infantry 1.5, Crossbowman 0.5, Sapper 3.0 and the
/// catapult 1.5; Laborer stays 1.0, all four infantry radius 0.4): in a real <c>Match</c> each is drawn at its scale in height
/// and at its radius in width beside a Laborer, feet on the ground; the selection ring, the target ring and the corpse disc
/// follow the radius, the hp bar's length (never under <see cref="CombatViews.BarMinScale"/>) and height the scale; a click
/// just outside the sim's pick radius (beside the body: off it; above the centre of the 1.5x catapult or the 3.0x Sapper: on
/// its drawn upper body) selects nothing (a click inside it selects the unit), and a box over the drawn upper body but not
/// the sim centre selects nothing; the per-frame view update of the mixed scales allocates nothing.</item>
/// <item>Shot heights (M7-V2, a second copy with the Desert Archer at 3.0): a 0.5 Crossbowman and a 3.0 Desert Archer shoot each
/// other; the Crossbowman's bolt leaves at 0.6 m and its hit flash on the Archer is at 3.6 m, the Archer's arrow leaves at 3.6 m
/// and its flash on the Crossbowman is at 0.6 m (<see cref="ProjectileViews.HeightFor"/>, 1.2 m x scale), and in between a shot
/// is between its two ends.</item>
/// </list>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/ScaleViewTest.tscn</c>; prints "SCALE VIEW TEST PASS" or
/// "SCALE VIEW TEST FAIL: ..." lines. Windowed with <c>-- --shots &lt;dir&gt;</c> it saves <c>scale-row.png</c> (the row at zoom
/// 20: 1.0, 1.5, 1.0, 0.5, 1.0, 3.0, 1.5 left to right, all selected and hurt, the target ring on the 3.0, corpses of a 1.0, a
/// 1.5 and a 3.0 in front) and <c>scale-row-close.png</c> (its middle at twice the size).
/// </summary>
public partial class ScaleViewTest : Node
{
    // The forged scales, by unit id (all Malazan; Laborer stays 1.0 as the reference).
    private static readonly (string Id, float Scale)[] Forged =
    {
        ("malazan_heavy_infantry", 1.5f), ("malazan_crossbowman", 0.5f), ("malazan_sapper", 3.0f), ("malazan_catapult", 1.5f),
    };

    private readonly List<string> _failures = new();
    private string? _shots;
    private SelectionController _sel = null!;
    private RtsCamera _camera = null!;

    public override async void _Ready()
    {
        string root = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://scale-view-{OS.GetProcessId()}"));
        // M6-V20: this scene pins capsule geometry (the Laborer is its reference body); a scaled model is ModelViewTest's.
        UnitViews.UseModels = false;
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
            GameData forged = Forge(dataDir, root);
            await Staged(forged);
            await ShotHeights(root);
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        Input.ActionRelease("select_add");
        foreach (string f in _failures.Take(60)) GD.Print($"SCALE VIEW TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("SCALE VIEW TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // Criterion 3: shipped data draws every type at radius by BodyHeight(radius) x scale (M7-V3c); a scale-1 type is today's capsule.
    private void ShippedRows(GameData data)
    {
        var views = new UnitViews();
        AddChild(views);
        views.Bind(data, 4);
        int ones = 0;
        for (int t = 0; t < data.Units.Length; t++)
        {
            UnitDef def = data.Units[t];
            CapsuleMesh? mesh = views.MeshOf(t);
            if (mesh == null)
            {
                _failures.Add($"shipped {def.Key}: no mesh");
                continue;
            }
            Check(mesh.Radius == def.Radius && mesh.Height == UnitViews.BodyHeight(def.Radius) * def.Scale,
                $"shipped {def.Key} (scale {def.Scale}): mesh {mesh.Radius} x {mesh.Height}, want {def.Radius} x {UnitViews.BodyHeight(def.Radius) * def.Scale}");
            if (def.Scale != 1f) continue;
            ones++;
            // Today's formula, written out: radius, and twice it plus the 1 m extra body.
            Check(mesh.Radius == def.Radius && mesh.Height == 2f * def.Radius + 1f,
                $"shipped {def.Key}: mesh {mesh.Radius} x {mesh.Height}, today's capsule is {def.Radius} x {2f * def.Radius + 1f}");
        }
        GD.Print($"shipped: {data.Units.Length} unit types, {ones} at scale 1 (today's capsule), {data.Units.Length - ones} scaled");
        views.QueueFree();
    }

    // A copy of the data with the Forged scales written into the Malazan units.json.
    private GameData Forge(string dataDir, string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
        string copy = Path.Combine(root, "data");
        Directory.CreateDirectory(copy);
        foreach (string dir in Directory.GetDirectories(dataDir, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(copy, Path.GetRelativePath(dataDir, dir)));
        foreach (string file in Directory.GetFiles(dataDir, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(copy, Path.GetRelativePath(dataDir, file)));
        string path = Path.Combine(copy, "factions", "malazan", "units.json");
        JsonNode units = JsonNode.Parse(File.ReadAllText(path)) ?? throw new InvalidOperationException($"{path} is empty");
        foreach ((string id, float scale) in Forged)
        {
            JsonNode unit = units["units"]!.AsArray().First(n => (string?)n!["id"] == id)!;
            unit["scale"] = (double)(decimal)scale;
        }
        File.WriteAllText(path, units.ToJsonString());
        DataLoadResult loaded = DataLoader.LoadAll(copy);
        if (!loaded.Ok) throw new InvalidOperationException("forged data failed to load: " + string.Join("; ", loaded.Errors.Take(5)));
        GameData forged = loaded.Data!;
        foreach ((string id, float scale) in Forged)
            Check(Type(forged, id) is int t && forged.Units[t].Scale == scale, $"forged {id}: scale not {scale}");
        Check(forged.Units[Type(forged, "malazan_laborer")!.Value].Scale == 1f, "forged laborer not at scale 1");
        return forged;
    }

    private async Task Staged(GameData data)
    {
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        match.Start(data, LaunchOptions.Parse(new[] { "--units", "0", "--no-bases", "--no-combat", "--no-fog", "--mute", "--zoom", "20" }));
        var runner = match.GetNode<SimRunner>("SimRunner");
        Simulation sim = runner.Simulation!;
        World world = sim.World;
        UnitStore u = world.Units;
        _sel = match.GetNode<SelectionController>("SelectionController");
        _camera = match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        var views = match.GetNode<UnitViews>("World3D/UnitViews");
        var rings = match.GetNode<SelectionRings>("World3D/SelectionRings");
        var target = match.GetNode<TargetRing>("World3D/TargetRing");
        var combat = match.GetNode<CombatViews>("World3D/CombatViews");
        while (sim.TickNumber < 3) await Frames(1);

        int laborer = Type(data, "malazan_laborer")!.Value, heavy = Type(data, "malazan_heavy_infantry")!.Value,
            crossbow = Type(data, "malazan_crossbowman")!.Value, sapper = Type(data, "malazan_sapper")!.Value,
            catapult = Type(data, "malazan_catapult")!.Value;
        // Left to right, far enough apart that no drawn body or pick circle reaches a neighbour's.
        (int Type, float X)[] row =
        {
            (laborer, 0f), (heavy, 3.5f), (laborer, 7f), (crossbow, 10f), (laborer, 13f), (sapper, 18.5f), (catapult, 25f),
        };
        System.Numerics.Vector2 anchor = StartLayout.Block(world.NavGrid, 1, west: true, 1f)[0];
        foreach ((int type, float x) in row) sim.Enqueue(Command.SpawnUnit(SelectionController.LocalPlayer, type, anchor + new System.Numerics.Vector2(x, 0f)));
        long spawnTick = sim.TickNumber;
        while (sim.TickNumber < spawnTick + 3) await Frames(1);
        var slots = new int[row.Length];
        Array.Fill(slots, -1);
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            for (int k = 0; k < row.Length; k++)
            {
                if (slots[k] >= 0 || u.TypeId[i] != row[k].Type) continue;
                if (System.Numerics.Vector2.Distance(u.Position[i], anchor + new System.Numerics.Vector2(row[k].X, 0f)) > 1.5f) continue;
                slots[k] = i;
                break;
            }
        }
        if (slots.Any(s => s < 0)) throw new InvalidOperationException($"spawned row incomplete: slots [{string.Join(", ", slots)}]");
        System.Numerics.Vector2 centre = anchor + new System.Numerics.Vector2(12.5f, 0f);
        _camera.SetZoom(20f);
        _camera.SetFocus(centre.X, centre.Y + 2f);
        await Frames(2);

        // Criterion 1: the height at the scale and the width at the radius (M7-V3c), against the Laborer (same radius) beside it; feet on the ground.
        var reference = (CapsuleMesh)views.ViewOf(slots[0])!.Mesh;
        Check(reference.Radius == 0.4f && reference.Height == UnitViews.BodyHeight(0.4f), $"laborer capsule {reference.Radius} x {reference.Height}");
        for (int k = 0; k < row.Length; k++)
        {
            int slot = slots[k];
            UnitDef def = data.Units[row[k].Type];
            MeshInstance3D view = views.ViewOf(slot)!;
            var mesh = (CapsuleMesh)view.Mesh;
            float wantR = def.Radius, wantH = UnitViews.BodyHeight(def.Radius) * def.Scale;
            GD.Print($"  {def.Key} scale {def.Scale.ToString(CultureInfo.InvariantCulture)}: capsule {mesh.Radius:0.###} x {mesh.Height:0.###} (radius {def.Radius})");
            Check(Mathf.Abs(mesh.Radius - wantR) < 1e-5f && Mathf.Abs(mesh.Height - wantH) < 1e-5f,
                $"{def.Key}: capsule {mesh.Radius} x {mesh.Height}, want {wantR} x {wantH}");
            if (def.Radius == reference.Radius)
                Check(Mathf.Abs(mesh.Radius / reference.Radius - 1f) < 1e-5f && Mathf.Abs(mesh.Height / reference.Height - def.Scale) < 1e-5f,
                    $"{def.Key}: {mesh.Radius / reference.Radius}x as wide and {mesh.Height / reference.Height}x as tall as the laborer, want 1x and {def.Scale}x");
            Vector3 ground = UnitViews.GroundPoint(world, slot, (float)runner.Alpha);
            Check(Mathf.Abs(view.Position.Y - mesh.Height / 2f - ground.Y) < 1e-3f, $"{def.Key}: feet at {view.Position.Y - mesh.Height / 2f}, ground {ground.Y}");
        }

        // Criterion 2: rings, bars, corpses.
        _sel.Selection.Clear();
        foreach (int s in slots) _sel.Selection.Add(new EntityHandle(s, u.Generation[s]));
        for (int k = 0; k < slots.Length; k++) u.Hp[slots[k]] = Math.Max(1, world.MaxHp(slots[k]) / 2);
        int sapperSlot = slots[5];
        target.Show(new EntityHandle(sapperSlot, u.Generation[sapperSlot]), isBuilding: false);
        System.Numerics.Vector2 front = anchor + new System.Numerics.Vector2(0f, 6f);
        DeathEvent[] deaths =
        {
            new(new EntityHandle(0, 0), false, laborer, SelectionController.LocalPlayer, 1, front),
            new(new EntityHandle(0, 0), false, heavy, SelectionController.LocalPlayer, 1, front + new System.Numerics.Vector2(3.5f, 0f)),
            new(new EntityHandle(0, 0), false, sapper, SelectionController.LocalPlayer, 1, front + new System.Numerics.Vector2(18.5f, 0f)),
        };
        combat.AddDeaths(world, deaths, sim.TickNumber);
        await Frames(2);
        Check(rings.ShownCount == slots.Length, $"{rings.ShownCount} selection rings for {slots.Length} selected");
        for (int k = 0; k < slots.Length && k < rings.ShownCount; k++)
        {
            UnitDef def = data.Units[row[k].Type];
            float want = def.Radius * SelectionRings.RadiusScale;
            Check(Mathf.Abs(rings.ShownRadius(k) - want) < 1e-5f, $"{def.Key}: selection ring {rings.ShownRadius(k)}, want {want}");
        }
        float targetR = target.Ring.Transform.Basis.X.Length();
        UnitDef sapperDef = data.Units[sapper];
        Check(target.Shown && Mathf.Abs(targetR - sapperDef.Radius * TargetRing.UnitScale) < 1e-4f,
            $"target ring on the 3.0 sapper: shown {target.Shown}, radius {targetR}, want {sapperDef.Radius * TargetRing.UnitScale}");
        Check(combat.ShownBars == slots.Length, $"{combat.ShownBars} hp bars for {slots.Length} hurt units");
        for (int b = 0; b < combat.ShownBars; b++)
        {
            int slot = combat.BarSlot(b);
            UnitDef def = data.Units[u.TypeId[slot]];
            float want = CombatViews.BarLength * Math.Max(def.Scale, CombatViews.BarMinScale);
            GD.Print($"  {def.Key} scale {def.Scale.ToString(CultureInfo.InvariantCulture)}: hp bar {combat.BarLengthOf(b):0.###} m");
            Check(Mathf.Abs(combat.BarLengthOf(b) - want) < 1e-5f, $"{def.Key}: hp bar {combat.BarLengthOf(b)} m, want {want}");
        }
        Check(CombatViews.BarScaleFor(0.5f) == CombatViews.BarMinScale && CombatViews.BarScaleFor(3f) == 3f && CombatViews.BarScaleFor(1f) == 1f,
            "bar share for 0.5 / 1 / 3 not 0.75 / 1 / 3");
        int corpses = 0;
        for (int m = 0; m < combat.Markers.Capacity; m++)
        {
            if (combat.MarkerShown(m) != 1) continue;
            corpses++;
            UnitDef def = data.Units[combat.Markers.Type[m]];
            float r = combat.MarkerTransform(m).Basis.X.Length();
            Check(Mathf.Abs(r - def.Radius) < 1e-5f, $"{def.Key}: corpse disc {r}, want {def.Radius}");
        }
        Check(corpses == deaths.Length, $"{corpses} corpses drawn for {deaths.Length} deaths");

        if (_shots != null && DisplayServer.GetName() != "headless") await Shots();

        // Criterion 5: the steady frame of the mixed scales allocates nothing (views, rings, bars, target ring).
        views.Sync(world, 0.5f);
        rings.Sync(world, 0.5f, _sel.Selection.Items);
        combat.Sync(world, 0.5f);
        target.Sync(world, 0.5f, 0f);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int f = 0; f < 20; f++)
        {
            views.Sync(world, f / 20f);
            rings.Sync(world, f / 20f, _sel.Selection.Items);
            combat.Sync(world, f / 20f);
            target.Sync(world, f / 20f, 0f);
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(bytes == 0, $"20 frames of mixed-scale views, rings, bars and target ring allocated {bytes} bytes");

        // Criterion 4: the hit test is the sim's radius. Outside it on the drawn body: nothing; inside: the unit.
        // Since M7-V3c the body is as wide as the pick circle, so the drawn body outside it is above the centre: the 1.5 catapult's
        // drawn top is close above its sim centre, the 3.0 Sapper's far; both rows above run.
        HitRows(world, slots[6], data.Units[row[6].Type], above: true);
        HitRows(world, slots[5], data.Units[row[5].Type], above: true);
        match.QueueFree();
        await Frames(1);
    }

    // Criterion 4 (M7-V2): a 0.5 Crossbowman and a 3.0 Desert Archer 10 m apart shoot each other; each shot leaves at its
    // shooter's 1.2 m x scale and flashes at its victim's.
    private async Task ShotHeights(string root)
    {
        string copy = Path.Combine(root, "data");
        string path = Path.Combine(copy, "factions", "whirlwind", "units.json");
        JsonNode units = JsonNode.Parse(File.ReadAllText(path)) ?? throw new InvalidOperationException($"{path} is empty");
        units["units"]!.AsArray().First(n => (string?)n!["id"] == "whirlwind_desert_archer")!["scale"] = 3.0;
        File.WriteAllText(path, units.ToJsonString());
        DataLoadResult loaded = DataLoader.LoadAll(copy);
        if (!loaded.Ok) throw new InvalidOperationException("forged data failed to load: " + string.Join("; ", loaded.Errors.Take(5)));
        GameData data = loaded.Data!;
        int crossbow = Type(data, "malazan_crossbowman")!.Value, archer = Type(data, "whirlwind_desert_archer")!.Value;
        Check(data.Units[crossbow].Scale == 0.5f && data.Units[archer].Scale == 3f, "shot-height copy: scales not 0.5 / 3.0");

        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        match.Start(data, LaunchOptions.Parse(new[] { "--seed", "1", "--units", "0", "--no-bases", "--no-fog", "--mute" }));
        var runner = match.GetNode<SimRunner>("SimRunner");
        runner.ProcessMode = ProcessModeEnum.Disabled; // ticked here
        Simulation sim = runner.Simulation!;
        World world = sim.World;
        var shots = match.GetNode<ProjectileViews>("World3D/ProjectileViews");
        System.Numerics.Vector2 anchor = StartLayout.Block(world.NavGrid, 1, west: true, 1f)[0];
        sim.Enqueue(Command.SpawnUnit(0, crossbow, anchor));
        sim.Enqueue(Command.SpawnUnit(1, archer, anchor + new System.Numerics.Vector2(10f, 0f)));
        float small = ProjectileViews.HeightFor(0.5f), big = ProjectileViews.HeightFor(3f);
        Check(Mathf.Abs(small - 0.6f) < 1e-6f && Mathf.Abs(big - 3.6f) < 1e-6f && ProjectileViews.HeightFor(1f) == ProjectileViews.LaunchHeight,
            $"HeightFor: 0.5 -> {small}, 3.0 -> {big}, 1.0 -> {ProjectileViews.HeightFor(1f)}");
        // Per owner (0 the Crossbowman, 1 the Archer): the launch height seen, the flash height seen, mid-flight outside its ends.
        var launch = new float[] { -1f, -1f };
        var flash = new float[] { -1f, -1f };
        int outside = 0, drawn = 0;
        ProjectileStore p = world.Projectiles;
        for (int t = 0; t < 600 && (launch[0] < 0f || launch[1] < 0f || flash[0] < 0f || flash[1] < 0f); t++)
        {
            sim.Tick();
            shots.CollectTick(world);
            shots.Sync(world, 0f); // alpha 0 draws a shot at PrevPosition: its launch point on the tick it was fired
            for (int k = 0; k < shots.Shown; k++)
            {
                if (shots.DrawnIsLob(k)) continue;
                int slot = shots.DrawnSlot(k), owner = p.Owner[slot];
                Vector3 at = shots.DrawnAt(k);
                float h = at.Y - TerrainHeight.At(world.Heightmap, at.X, at.Z);
                float from = owner == 0 ? small : big, to = owner == 0 ? big : small;
                drawn++;
                if (h < Math.Min(from, to) - 1e-4f || h > Math.Max(from, to) + 1e-4f) outside++;
                if (p.PrevPosition[slot] == shots.Tracker.Launch[slot] && (uint)owner < 2u && launch[owner] < 0f) launch[owner] = h;
            }
            for (int m = 0; m < shots.Marks.Capacity; m++)
            {
                if (!shots.Marks.Active[m] || shots.Marks.Kind[m] != ImpactMarkKind.Flash) continue;
                int owner = shots.Marks.Owner[m];
                System.Numerics.Vector2 mp = shots.Marks.Position[m];
                float h = shots.MarkTransform(m).Origin.Y - TerrainHeight.At(world.Heightmap, mp.X, mp.Y);
                if ((uint)owner < 2u && flash[owner] < 0f) flash[owner] = h;
            }
        }
        GD.Print($"  shot heights: 0.5 crossbowman leaves at {launch[0]:0.###} m, flashes on the 3.0 archer at {flash[0]:0.###} m; " +
            $"the archer leaves at {launch[1]:0.###} m, flashes on the crossbowman at {flash[1]:0.###} m; {drawn} shot draws, {outside} outside their ends");
        Check(Mathf.Abs(launch[0] - small) < 1e-3f, $"the 0.5 crossbowman's bolt left at {launch[0]} m, want {small}");
        Check(Mathf.Abs(launch[1] - big) < 1e-3f, $"the 3.0 archer's arrow left at {launch[1]} m, want {big}");
        Check(Mathf.Abs(flash[0] - big) < 1e-3f, $"the crossbowman's hit flash on the 3.0 archer at {flash[0]} m, want {big}");
        Check(Mathf.Abs(flash[1] - small) < 1e-3f, $"the archer's hit flash on the 0.5 crossbowman at {flash[1]} m, want {small}");
        Check(drawn > 0 && outside == 0, $"{outside} of {drawn} shot draws outside their two ends' heights");
        match.QueueFree();
        await Frames(1);
    }

    private void HitRows(World world, int slot, UnitDef def, bool above)
    {
        if (!_sel.TryScreenPosition(slot, out Vector2 at)) throw new InvalidOperationException($"{def.Key} off screen");
        // Pixels per meter at the body centre, as the picker's Project() works it out.
        Vector3 c = UnitViews.GroundPoint(world, slot, 1f) + new Vector3(0f, UnitViews.BodyHeight(def.Radius) / 2f, 0f);
        float pxPerM = _camera.UnprojectPosition(c + Vector3.Right).X - _camera.UnprojectPosition(c).X;
        float pickPx = Math.Max(def.Radius * pxPerM, ScreenPicker.MinPickRadiusPx), drawnPx = UnitViews.DrawnRadius(def) * pxPerM;
        float outside = pickPx + 3f;
        GD.Print($"  {def.Key} scale {def.Scale.ToString(CultureInfo.InvariantCulture)}: pick radius {pickPx:0.#} px, drawn radius {drawnPx:0.#} px, click at +{outside:0.#} px");
        // M7-V3c: the drawn width is the sim's radius, so beside the centre the pick circle and the body end together.
        Check(Mathf.Abs(drawnPx - def.Radius * pxPerM) < 1e-3f, $"{def.Key}: drawn radius {drawnPx} px is not the sim radius's {def.Radius * pxPerM} px");
        // How far the drawn body's top (the centre of its upper cap) is above the sim centre on screen.
        Vector3 ground = UnitViews.GroundPoint(world, slot, 1f);
        float topPx = at.Y - _camera.UnprojectPosition(ground + Vector3.Up * (UnitViews.DrawnHeight(def) - UnitViews.DrawnRadius(def))).Y;
        var handle = new EntityHandle(slot, world.Units.Generation[slot]);

        // Beside the centre, then above it (a tall drawn body reaches far over the sim's).
        foreach (Vector2 dir in above ? new[] { Vector2.Right, Vector2.Up } : new[] { Vector2.Right })
        {
            if (dir == Vector2.Up && outside >= topPx - 2f)
            {
                _failures.Add($"{def.Key}: the drawn top is only {topPx:0.#} px above the centre: the row above proves nothing");
                continue;
            }
            _sel.SelectOnly(new EntityHandle(OtherSlot(world, slot), world.Units.Generation[OtherSlot(world, slot)]));
            Click(at + dir * outside);
            Check(_sel.Selection.Count == 0, $"{def.Key}: a click {outside:0.#} px {(dir == Vector2.Right ? "right of" : "above")} the centre (outside the sim's {pickPx:0.#} px; drawn radius {drawnPx:0.#} px, top {topPx:0.#} px up) selected {_sel.Selection.Count}");
        }
        Click(at + Vector2.Right * (pickPx - 3f));
        Check(_sel.Selection.Count == 1 && _sel.Selection.Contains(handle), $"{def.Key}: a click {pickPx - 3f:0.#} px right of the centre selected {_sel.Selection.Count}, not the unit");

        // A box beside the centre past the pick circle, and (above) a box over the drawn upper body that leaves out the sim centre,
        // select nothing; one over the centre the unit.
        _sel.BoxSelect(at + new Vector2(outside, -drawnPx), at + new Vector2(outside + drawnPx + 4f, drawnPx), add: false);
        Check(_sel.Selection.Count == 0, $"{def.Key}: a box beside the body, outside the pick circle, selected {_sel.Selection.Count}");
        if (above && outside < topPx - 2f)
        {
            _sel.BoxSelect(at + new Vector2(-drawnPx, -topPx), at + new Vector2(drawnPx, -outside), add: false);
            Check(_sel.Selection.Count == 0, $"{def.Key}: a box over the drawn upper body but not the centre selected {_sel.Selection.Count}");
        }
        _sel.BoxSelect(at - new Vector2(4f, 4f), at + new Vector2(4f, 4f), add: false);
        Check(_sel.Selection.Count == 1 && _sel.Selection.Contains(handle), $"{def.Key}: a box round the centre selected {_sel.Selection.Count}, not the unit");
    }

    // Another live own unit, to have something selected that a miss must clear.
    private static int OtherSlot(World world, int slot)
    {
        for (int i = 0; i < world.Units.Capacity; i++)
            if (i != slot && world.Units.Alive[i] && world.Units.Owner[i] == SelectionController.LocalPlayer) return i;
        throw new InvalidOperationException("no other own unit");
    }

    private void Click(Vector2 at)
    {
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
    }

    // Windowed with `-- --shots <dir>`: the row at zoom 20, then its middle closer.
    private async Task Shots()
    {
        Directory.CreateDirectory(_shots!);
        await Frames(4);
        Image row = GetViewport().GetTexture().GetImage();
        string a = Path.Combine(_shots!, "scale-row.png");
        Error ea = row.SavePng(a);
        int w = row.GetWidth(), h = row.GetHeight();
        Image close = row.GetRegion(new Rect2I(w / 4, h / 4, w / 2, h / 2));
        close.Resize(w, h, Image.Interpolation.Nearest);
        string b = Path.Combine(_shots!, "scale-row-close.png");
        Error eb = close.SavePng(b);
        GD.Print($"saved {a} ({ea}), {b} ({eb})");
    }

    private static int? Type(GameData data, string id)
    {
        for (int t = 0; t < data.Units.Length; t++) if (data.Units[t].Key == id) return t;
        return null;
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
    }
}
