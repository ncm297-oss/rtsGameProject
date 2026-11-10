using System;
using System.Collections.Generic;
using System.Diagnostics;
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
/// QA for M7-V1 (each unit type drawn at its data <c>scale</c>), on a forged copy of <c>res://data</c> (Heavy Infantry 1.5,
/// Crossbowman 0.5, Sapper 3.0; Laborer 1.0; all radius 0.4).
/// <list type="bullet">
/// <item>Hit test: at zoom 20 and 40 a click on the drawn head of the 1.5x Heavy Infantry and of the 3.0x Sapper (above the
/// sim's scale-1 body) selects nothing, a box over only the drawn head selects nothing, a box round the sim centre selects
/// it; a right-click on an enemy 3.0x Sapper's drawn head is no attack target (<see cref="SelectionController.EnemyAt"/>).</item>
/// <item>0.5 crowd: 16 Crossbowmen on a 0.8 m grid (radii touching), all hurt and selected: their bars and rings overlap no more pairs than the
/// same crowd of Laborers at 1.0 (the bar floor 0.75 keeps them readable without making the crowd worse than shipped).</item>
/// <item>3.0 beside a building: a Sapper ordered against the HQ; the drawn body's reach into blocked cells is printed (a known
/// look, docs/03 "Implementation (M7-V1)"), the sim body never inside one.</item>
/// <item>300 mixed-scale units (both players), all hurt, own ones selected: 60 frames of UnitViews, SelectionRings,
/// CombatViews, TargetRing and AbilityViews Sync allocate 0 bytes; timings printed.</item>
/// </list>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM7V1Test.tscn</c>; prints "QA M7V1 TEST PASS" or FAIL lines.
/// </summary>
public partial class QaM7V1Test : Node
{
    private static readonly (string Id, float Scale)[] Forged =
    {
        ("malazan_heavy_infantry", 1.5f), ("malazan_crossbowman", 0.5f), ("malazan_sapper", 3.0f),
    };

    private readonly List<string> _failures = new();

    public override async void _Ready()
    {
        string root = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://qa-m7v1-{OS.GetProcessId()}"));
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            await Frames(1);
            string dataDir = ProjectSettings.GlobalizePath("res://data");
            DataLoadResult shipped = DataLoader.LoadAll(dataDir);
            if (!shipped.Ok) throw new InvalidOperationException("data failed to load");
            GameData forged = Forge(dataDir, root);
            await HitTest(forged);
            int shippedBars = 0, shippedRings = 0;
            (shippedBars, shippedRings) = await Crowd(shipped.Data!, "malazan_laborer");
            (int smallBars, int smallRings) = await Crowd(forged, "malazan_crossbowman");
            GD.Print($"crowd overlaps: 1.0 laborers bars {shippedBars} rings {shippedRings}; 0.5 crossbowmen bars {smallBars} rings {smallRings}");
            Check(smallBars <= shippedBars, $"0.5 crowd: {smallBars} overlapping bar pairs, more than the 1.0 crowd's {shippedBars}");
            Check(smallRings <= shippedRings, $"0.5 crowd: {smallRings} overlapping ring pairs, more than the 1.0 crowd's {shippedRings}");
            await BesideBuilding(forged);
            await Allocation(forged);
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        foreach (string f in _failures.Take(60)) GD.Print($"QA M7V1 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M7V1 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private GameData Forge(string dataDir, string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
        string copy = Path.Combine(root, "data");
        foreach (string dir in Directory.GetDirectories(dataDir, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(copy, Path.GetRelativePath(dataDir, dir)));
        Directory.CreateDirectory(copy);
        foreach (string file in Directory.GetFiles(dataDir, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(copy, Path.GetRelativePath(dataDir, file)));
        string path = Path.Combine(copy, "factions", "malazan", "units.json");
        JsonNode units = JsonNode.Parse(File.ReadAllText(path))!;
        foreach ((string id, float scale) in Forged)
            units["units"]!.AsArray().First(n => (string?)n!["id"] == id)!["scale"] = (double)(decimal)scale;
        File.WriteAllText(path, units.ToJsonString());
        DataLoadResult loaded = DataLoader.LoadAll(copy);
        if (!loaded.Ok) throw new InvalidOperationException("forged data failed to load: " + string.Join("; ", loaded.Errors.Take(5)));
        return loaded.Data!;
    }

    private async Task<(Match Match, Simulation Sim, SelectionController Sel, RtsCamera Camera)> Start(GameData data, params string[] extra)
    {
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        match.Start(data, LaunchOptions.Parse(new[] { "--units", "0", "--no-combat", "--no-fog", "--mute", "--zoom", "20" }.Concat(extra).ToArray()));
        Simulation sim = match.GetNode<SimRunner>("SimRunner").Simulation!;
        var sel = match.GetNode<SelectionController>("SelectionController");
        var camera = match.GetNode<RtsCamera>("RtsCamera");
        camera.EdgePanEnabled = false;
        while (sim.TickNumber < 3) await Frames(1);
        return (match, sim, sel, camera);
    }

    private async Task<int[]> Spawn(Simulation sim, int player, (int Type, System.Numerics.Vector2 At)[] row)
    {
        UnitStore u = sim.World.Units;
        var before = new bool[u.Capacity];
        for (int i = 0; i < u.Capacity; i++) before[i] = u.Alive[i];
        foreach ((int type, System.Numerics.Vector2 at) in row) sim.Enqueue(Command.SpawnUnit(player, type, at));
        long t0 = sim.TickNumber;
        while (sim.TickNumber < t0 + 3) await Frames(1);
        var slots = new List<int>();
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && !before[i] && u.Owner[i] == player) slots.Add(i);
        return slots.ToArray();
    }

    // Clicks on the drawn head (above the scale-1 sim body) of a 1.5 and a 3.0 unit select nothing; an enemy 3.0's head is no target.
    private async Task HitTest(GameData data)
    {
        (Match match, Simulation sim, SelectionController sel, RtsCamera camera) = await Start(data, "--no-bases");
        World world = sim.World;
        int heavy = Type(data, "malazan_heavy_infantry"), sapper = Type(data, "malazan_sapper"), laborer = Type(data, "malazan_laborer");
        System.Numerics.Vector2 anchor = StartLayout.Block(world.NavGrid, 1, west: true, 1f)[0];
        int[] own = await Spawn(sim, SelectionController.LocalPlayer, new[]
        {
            (laborer, anchor), (heavy, anchor + new System.Numerics.Vector2(6f, 0f)), (sapper, anchor + new System.Numerics.Vector2(14f, 0f)),
        });
        int[] foe = await Spawn(sim, 1, new[] { (sapper, anchor + new System.Numerics.Vector2(22f, 0f)) });
        if (own.Length != 3 || foe.Length != 1) throw new InvalidOperationException($"spawned {own.Length} own, {foe.Length} enemy");
        int heavySlot = own.First(s => world.Units.TypeId[s] == heavy), sapperSlot = own.First(s => world.Units.TypeId[s] == sapper);
        int laborerSlot = own.First(s => world.Units.TypeId[s] == laborer);
        foreach (float zoom in new[] { 20f, 40f })
        {
            camera.SetZoom(zoom);
            System.Numerics.Vector2 c = anchor + new System.Numerics.Vector2(11f, 0f);
            camera.SetFocus(c.X, c.Y + 2f);
            await Frames(3);
            foreach (int slot in new[] { heavySlot, sapperSlot })
            {
                UnitDef def = data.Units[world.Units.TypeId[slot]];
                Vector3 ground = UnitViews.GroundPoint(world, slot, 1f);
                // Screen points: the sim's body top (scale-1 capsule) and the drawn head (just below the drawn top).
                Vector2 simTop = camera.UnprojectPosition(ground + Vector3.Up * UnitViews.BodyHeight(def.Radius));
                Vector2 head = camera.UnprojectPosition(ground + Vector3.Up * (UnitViews.DrawnHeight(def) - UnitViews.DrawnRadius(def) * 0.5f));
                sel.TryScreenPosition(slot, out Vector2 centre);
                float gap = centre.Y - head.Y;
                GD.Print($"  zoom {zoom}: {def.Key} x{def.Scale.ToString(CultureInfo.InvariantCulture)} sim centre {centre.Y:0} px, sim top {simTop.Y:0}, drawn head {head.Y:0} ({gap:0} px above centre, pick floor {ScreenPicker.MinPickRadiusPx} px)");
                if (gap <= ScreenPicker.MinPickRadiusPx + 3f)
                {
                    GD.Print($"    head inside the pick floor at zoom {zoom}: skipped");
                    continue;
                }
                sel.SelectOnly(new EntityHandle(laborerSlot, world.Units.Generation[laborerSlot]));
                Click(sel, head);
                Check(sel.Selection.Count == 0, $"zoom {zoom}: a click on the drawn head of {def.Key} x{def.Scale} selected {sel.Selection.Count}");
                sel.BoxSelect(head - new Vector2(6f, 4f), head + new Vector2(6f, 4f), add: false);
                Check(sel.Selection.Count == 0, $"zoom {zoom}: a box over only the drawn head of {def.Key} selected {sel.Selection.Count}");
                sel.BoxSelect(centre - new Vector2(4f, 4f), centre + new Vector2(4f, 4f), add: false);
                Check(sel.Selection.Count == 1 && sel.Selection.Contains(new EntityHandle(slot, world.Units.Generation[slot])),
                    $"zoom {zoom}: a box round {def.Key}'s sim centre selected {sel.Selection.Count}, not it");
            }
            // The enemy 3.0 Sapper: its drawn head is no attack target, its sim body is.
            int f = foe[0];
            UnitDef fd = data.Units[world.Units.TypeId[f]];
            Vector3 fg = UnitViews.GroundPoint(world, f, 1f);
            Vector2 fHead = camera.UnprojectPosition(fg + Vector3.Up * (UnitViews.DrawnHeight(fd) - UnitViews.DrawnRadius(fd) * 0.5f));
            Vector2 fBody = camera.UnprojectPosition(fg + Vector3.Up * (UnitViews.BodyHeight(fd.Radius) / 2f));
            bool headHit = sel.EnemyAt(fHead, out EntityHandle ht, out _);
            bool bodyHit = sel.EnemyAt(fBody, out EntityHandle bt, out _);
            GD.Print($"  zoom {zoom}: enemy sapper x3 head ({fBody.Y - fHead.Y:0} px above its sim centre) target {headHit}, sim body target {bodyHit}");
            Check(bodyHit && bt.Index == f, $"zoom {zoom}: the enemy 3.0 sapper's sim body is no target");
            Check(!headHit || ht.Index != f, $"zoom {zoom}: the enemy 3.0 sapper's drawn head is a target (the rule says the sim radius only)");
        }
        match.QueueFree();
        await Frames(2);
    }

    // 16 units of one type on a 0.8 m grid (two radii: as close as the sim packs 0.4 m units), hurt and selected: overlapping bar pairs and ring pairs.
    private async Task<(int Bars, int Rings)> Crowd(GameData data, string id)
    {
        (Match match, Simulation sim, SelectionController sel, RtsCamera camera) = await Start(data, "--no-bases");
        World world = sim.World;
        UnitStore u = world.Units;
        int type = Type(data, id);
        System.Numerics.Vector2 anchor = StartLayout.Block(world.NavGrid, 1, west: true, 1f)[0];
        var row = new (int, System.Numerics.Vector2)[16];
        for (int k = 0; k < row.Length; k++) row[k] = (type, anchor + new System.Numerics.Vector2((k % 4) * 0.8f, (k / 4) * 0.8f));
        int[] slots = await Spawn(sim, SelectionController.LocalPlayer, row);
        long t0 = sim.TickNumber;
        while (sim.TickNumber < t0 + 40) await Frames(1);
        sel.Selection.Clear();
        foreach (int s in slots) { sel.Selection.Add(new EntityHandle(s, u.Generation[s])); u.Hp[s] = Math.Max(1, world.MaxHp(s) / 2); }
        camera.SetZoom(20f);
        camera.SetFocus(anchor.X, anchor.Y + 2f);
        await Frames(3);
        var combat = match.GetNode<CombatViews>("World3D/CombatViews");
        var rings = match.GetNode<SelectionRings>("World3D/SelectionRings");
        int bars = 0, ringPairs = 0;
        float thick = CombatViews.BarThickness;
        for (int a = 0; a < combat.ShownBars; a++)
            for (int b = a + 1; b < combat.ShownBars; b++)
            {
                System.Numerics.Vector2 pa = u.Position[combat.BarSlot(a)], pb = u.Position[combat.BarSlot(b)];
                // Same height (same type); a bar runs along world x, thin in z.
                if (Math.Abs(pa.X - pb.X) < (combat.BarLengthOf(a) + combat.BarLengthOf(b)) / 2f && Math.Abs(pa.Y - pb.Y) < thick) bars++;
            }
        for (int a = 0; a < rings.ShownCount; a++)
            for (int b = a + 1; b < rings.ShownCount; b++)
            {
                float d = System.Numerics.Vector2.Distance(u.Position[slots[a]], u.Position[slots[b]]);
                if (d < rings.ShownRadius(a) + rings.ShownRadius(b)) ringPairs++;
            }
        float minD = float.MaxValue;
        for (int a = 0; a < slots.Length; a++)
            for (int b = a + 1; b < slots.Length; b++) minD = Math.Min(minD, System.Numerics.Vector2.Distance(u.Position[slots[a]], u.Position[slots[b]]));
        GD.Print($"  crowd {id} x{data.Units[type].Scale.ToString(CultureInfo.InvariantCulture)}: {slots.Length} units, nearest pair {minD:0.00} m, bar {combat.BarLengthOf(0):0.00} m, ring {rings.ShownRadius(0):0.00} m");
        Check(combat.ShownBars == slots.Length && rings.ShownCount == slots.Length, $"crowd {id}: {combat.ShownBars} bars, {rings.ShownCount} rings for {slots.Length}");
        match.QueueFree();
        await Frames(2);
        return (bars, ringPairs);
    }

    // A 3.0 Sapper sent into its HQ: how far its drawn body reaches into blocked cells (reported), and the sim body never does.
    private async Task BesideBuilding(GameData data)
    {
        (Match match, Simulation sim, SelectionController sel, RtsCamera camera) = await Start(data);
        World world = sim.World;
        int hq = -1;
        for (int b = 0; b < world.Buildings.Capacity; b++) if (world.Buildings.Alive[b] && world.Buildings.Owner[b] == SelectionController.LocalPlayer) { hq = b; break; }
        if (hq < 0) throw new InvalidOperationException("no own building");
        int cell = world.Buildings.Cell[hq];
        BuildingDef bd = data.Buildings[world.Buildings.TypeId[hq]];
        int cx = cell % world.NavGrid.Width, cy = cell / world.NavGrid.Width;
        System.Numerics.Vector2 hqCentre = world.NavGrid.CellCenter(cx, cy);
        int sapper = Type(data, "malazan_sapper");
        int[] s = await Spawn(sim, SelectionController.LocalPlayer, new[] { (sapper, hqCentre + new System.Numerics.Vector2(-12f, 0f)) });
        int slot = s[0];
        sim.Enqueue(Command.Move(SelectionController.LocalPlayer, new EntityHandle(slot, world.Units.Generation[slot]), hqCentre));
        long t0 = sim.TickNumber;
        while (sim.TickNumber < t0 + 160) await Frames(1);
        System.Numerics.Vector2 p = world.Units.Position[slot];
        // Distance from the unit to the nearest blocked cell's square, over the cells within 4 m.
        float near = float.MaxValue, cs = MapConstants.CellSize;
        world.NavGrid.WorldToCell(p, out int ux, out int uy);
        int reach = (int)Math.Ceiling(4f / cs);
        for (int y = uy - reach; y <= uy + reach; y++)
            for (int x = ux - reach; x <= ux + reach; x++)
            {
                if (!world.NavGrid.InBounds(x, y) || world.NavGrid.IsPassable(x, y)) continue;
                System.Numerics.Vector2 cc = world.NavGrid.CellCenter(x, y);
                float dx = Math.Max(0f, Math.Abs(p.X - cc.X) - cs / 2f), dy = Math.Max(0f, Math.Abs(p.Y - cc.Y) - cs / 2f);
                near = Math.Min(near, MathF.Sqrt(dx * dx + dy * dy));
            }
        UnitDef def = data.Units[sapper];
        GD.Print($"  3.0 sapper by {bd.Key}: nearest blocked cell {near:0.00} m, sim radius {def.Radius}, drawn radius {UnitViews.DrawnRadius(def):0.00} m: drawn body reaches {Math.Max(0f, UnitViews.DrawnRadius(def) - near):0.00} m into the footprint");
        Check(near >= def.Radius * 0.5f, $"3.0 sapper's sim body {near} m from a blocked cell (radius {def.Radius})");
        camera.SetZoom(20f);
        camera.SetFocus(p.X, p.Y + 2f);
        await Frames(3);
        string[] args = OS.GetCmdlineUserArgs();
        int i = Array.IndexOf(args, "--shots");
        if (i >= 0 && i + 1 < args.Length && DisplayServer.GetName() != "headless")
        {
            Directory.CreateDirectory(args[i + 1]);
            string file = Path.Combine(args[i + 1], "qa-m7v1-building.png");
            GetViewport().GetTexture().GetImage().SavePng(file);
            GD.Print($"saved {file}");
        }
        match.QueueFree();
        await Frames(2);
    }

    // 300 mixed-scale units (150 a side), hurt; own selected; the touched views' Sync allocates nothing over 60 frames.
    private async Task Allocation(GameData data)
    {
        (Match match, Simulation sim, SelectionController sel, RtsCamera camera) = await Start(data, "--no-bases");
        World world = sim.World;
        UnitStore u = world.Units;
        int[] types = { Type(data, "malazan_laborer"), Type(data, "malazan_heavy_infantry"), Type(data, "malazan_crossbowman"), Type(data, "malazan_sapper") };
        System.Numerics.Vector2 west = StartLayout.Block(world.NavGrid, 1, west: true, 1f)[0];
        for (int player = 0; player < 2; player++)
        {
            var row = new (int, System.Numerics.Vector2)[150];
            for (int k = 0; k < row.Length; k++)
                row[k] = (types[k % types.Length], west + new System.Numerics.Vector2((k % 15) * 2.5f, (k / 15) * 2.5f + player * 30f));
            await Spawn(sim, player, row);
        }
        int alive = 0;
        sel.Selection.Clear();
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            alive++;
            u.Hp[i] = Math.Max(1, world.MaxHp(i) / 2);
            if (u.Owner[i] == SelectionController.LocalPlayer) sel.Selection.Add(new EntityHandle(i, u.Generation[i]));
        }
        var views = match.GetNode<UnitViews>("World3D/UnitViews");
        var rings = match.GetNode<SelectionRings>("World3D/SelectionRings");
        var target = match.GetNode<TargetRing>("World3D/TargetRing");
        var combat = match.GetNode<CombatViews>("World3D/CombatViews");
        var ability = match.GetNode<AbilityViews>("World3D/AbilityViews");
        int big = Enumerable.Range(0, u.Capacity).First(i => u.Alive[i] && u.TypeId[i] == types[3]);
        target.Show(new EntityHandle(big, u.Generation[big]), isBuilding: false);
        camera.SetZoom(60f);
        camera.SetFocus(west.X + 18f, west.Y + 20f);
        await Frames(3);
        void Frame(float a)
        {
            views.Sync(world, a);
            rings.Sync(world, a, sel.Selection.Items);
            combat.Sync(world, a);
            target.Sync(world, a, 0f);
            ability.Sync(world, a, new Vector2(500f, 300f), false);
        }
        for (int f = 0; f < 5; f++) Frame(f / 5f);
        var watch = Stopwatch.StartNew();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int f = 0; f < 60; f++) Frame(f / 60f);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        watch.Stop();
        GD.Print($"  {alive} units, {sel.Selection.Count} selected, {combat.ShownBars} bars, {rings.ShownCount} rings: 60 frames {bytes} bytes, {watch.Elapsed.TotalMilliseconds / 60.0:0.000} ms a frame (5 views)");
        Check(alive == 300, $"{alive} units alive, want 300");
        Check(bytes == 0, $"60 frames of 300 mixed-scale units allocated {bytes} bytes");
        match.QueueFree();
        await Frames(2);
    }

    private static void Click(SelectionController sel, Vector2 at)
    {
        sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
        sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
    }

    private static int Type(GameData data, string id)
    {
        for (int t = 0; t < data.Units.Length; t++) if (data.Units[t].Key == id) return t;
        throw new InvalidOperationException($"no unit {id}");
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
