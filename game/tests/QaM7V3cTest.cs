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
/// QA M7-V3c (session 2026-11-12-0546): the drawn width is the collision radius (BUG-1260), attacked.
/// <list type="bullet">
/// <item>Forged corners inside the loader's limits (radius 0.4-1.0, scale 0.5-3.0): Heavy Infantry radius 0.9 scale 0.5,
/// Crossbowman radius 1.0 scale 0.5, Sapper radius 0.4 scale 3.0, Catapult radius 1.0 scale 3.0. Each type's capsule must be
/// exactly <c>radius</c> wide and <see cref="UnitViews.DrawnHeight"/> tall (Godot's <see cref="CapsuleMesh"/> clamps the
/// radius to half the height, BUG-1290). In a match: feet on the ground, selection ring 1.35x, corpse disc 1x radius, hp bar
/// length the zoom's bar x max(scale, 0.75). A radius of 0.3 is refused by the loader (outside the format).</item>
/// <item>Thirty shipped Warriors ordered to one point: over 160 ticks, no pair is ever drawn overlapping more than the sim
/// itself overlaps them.</item>
/// <item>A real Warrior: a click 3 px inside its drawn edge (beside the pick centre) selects it, 3 px outside selects nothing;
/// a box over only the drawn edge selects nothing.</item>
/// <item>A War-dog (scale 1) beside a Warrior: the dog is its own 0.4 m capsule, scale-1 tall; drawn apart.</item>
/// </list>
/// Headless: prints "QA M7-V3C TEST PASS" or FAIL lines.
/// </summary>
public partial class QaM7V3cTest : Node
{
    private static readonly (string Id, double Radius, double Scale)[] Corners =
    {
        ("malazan_heavy_infantry", 0.9, 0.5), ("malazan_crossbowman", 1.0, 0.5), ("malazan_sapper", 0.4, 3.0), ("malazan_catapult", 1.0, 3.0),
    };

    private readonly List<string> _failures = new();
    private string _root = "";

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _root = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://qa-m7-v3c-{OS.GetProcessId()}"));
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            await Frames(1);
            string dataDir = ProjectSettings.GlobalizePath("res://data");
            Check(!LoadForged(dataDir, Path.Combine(_root, "tiny"), new[] { ("malazan_heavy_infantry", 0.3, 3.0) }).Ok,
                "a unit radius of 0.3 loaded (outside DataLimits.MinUnitRadius)");
            DataLoadResult forged = LoadForged(dataDir, Path.Combine(_root, "corners"), Corners);
            if (!Check(forged.Ok, "the corner copy did not load: " + string.Join("; ", forged.Errors.Take(3)))) throw new InvalidOperationException("no forge");
            await CornerRows(forged.Data!);
            DataLoadResult shipped = DataLoader.LoadAll(dataDir);
            if (!shipped.Ok) throw new InvalidOperationException("shipped data failed to load");
            await GiantRows(shipped.Data!);
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        foreach (string f in _failures.Take(60)) GD.Print($"QA M7-V3C TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M7-V3C TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- forged corners of the format ----

    private async Task CornerRows(GameData data)
    {
        var bound = new UnitViews();
        AddChild(bound);
        bound.Bind(data, 4);
        foreach ((string id, _, _) in Corners)
        {
            int t = Type(data, id);
            UnitDef def = data.Units[t];
            CapsuleMesh mesh = bound.MeshOf(t)!;
            GD.Print($"  {id} radius {def.Radius.ToString(CultureInfo.InvariantCulture)} scale {def.Scale.ToString(CultureInfo.InvariantCulture)}: capsule {mesh.Radius:0.###} x {mesh.Height:0.###} (DrawnRadius {UnitViews.DrawnRadius(def):0.###}, DrawnHeight {UnitViews.DrawnHeight(def):0.###})");
            Check(Mathf.Abs(mesh.Radius - def.Radius) < 1e-5f, $"{id} (radius {def.Radius}, scale {def.Scale}): capsule radius {mesh.Radius}, want the collision radius {def.Radius} (BUG-1290)");
            Check(Mathf.Abs(mesh.Height - UnitViews.DrawnHeight(def)) < 1e-5f, $"{id}: capsule height {mesh.Height}, want {UnitViews.DrawnHeight(def)}");
        }
        bound.QueueFree();

        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        try
        {
            match.Start(data, LaunchOptions.Parse(new[] { "--seed", "1", "--units", "0", "--no-bases", "--no-combat", "--no-fog", "--mute" }));
            var runner = match.GetNode<SimRunner>("SimRunner");
            runner.ProcessMode = ProcessModeEnum.Disabled;
            Simulation sim = runner.Simulation!;
            World w = sim.World;
            UnitStore u = w.Units;
            var views = match.GetNode<UnitViews>("World3D/UnitViews");
            var rings = match.GetNode<SelectionRings>("World3D/SelectionRings");
            var combat = match.GetNode<CombatViews>("World3D/CombatViews");
            var sel = match.GetNode<SelectionController>("SelectionController");
            match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
            Tick(sim, 2);
            System.Numerics.Vector2 anchor = StartLayout.Block(w.NavGrid, 1, west: true, 1f)[0];
            var slots = new List<int>();
            for (int k = 0; k < Corners.Length; k++)
            {
                int s = Spawn(sim, 0, Type(data, Corners[k].Id), anchor + new System.Numerics.Vector2(k * 5f, 0f));
                if (!Check(s >= 0, $"{Corners[k].Id} did not spawn")) return;
                slots.Add(s);
            }
            sel.SelectOnly(new EntityHandle(slots[0], u.Generation[slots[0]]));
            for (int k = 1; k < slots.Count; k++) sel.Selection.Add(new EntityHandle(slots[k], u.Generation[slots[k]]));
            foreach (int s in slots) u.Hp[s] = Math.Max(1, w.MaxHp(s) / 2);
            await Frames(2);
            views.Sync(w, 1f);
            rings.Sync(w, 1f, sel.Selection.Items);
            combat.Sync(w, 1f);
            for (int k = 0; k < slots.Count; k++)
            {
                int s = slots[k];
                UnitDef def = data.Units[u.TypeId[s]];
                MeshInstance3D view = views.ViewOf(s)!;
                var mesh = (CapsuleMesh)view.Mesh;
                Vector3 ground = UnitViews.GroundPoint(w, s, 1f);
                Check(Mathf.Abs(view.Position.Y - mesh.Height / 2f - ground.Y) < 1e-3f, $"{def.Key}: feet at {view.Position.Y - mesh.Height / 2f}, ground {ground.Y}");
                Check(Mathf.Abs(rings.ShownRadius(k) - def.Radius * SelectionRings.RadiusScale) < 1e-5f, $"{def.Key}: selection ring {rings.ShownRadius(k)}, want {def.Radius * SelectionRings.RadiusScale}");
            }
            int bars = 0;
            for (int b = 0; b < combat.ShownBars; b++)
            {
                int s = combat.BarSlot(b);
                if (!slots.Contains(s)) continue;
                bars++;
                UnitDef def = data.Units[u.TypeId[s]];
                float want = combat.BarLengthNow * CombatViews.BarScaleFor(def.Scale); // the zoom's length times the scale's share
                Check(Mathf.Abs(combat.BarLengthOf(b) - want) < 1e-4f, $"{def.Key}: bar length {combat.BarLengthOf(b)}, want {want}");
            }
            Check(bars == slots.Count, $"{bars} bars for {slots.Count} hurt corner units");
            var deaths = new DeathEvent[Corners.Length];
            for (int k = 0; k < deaths.Length; k++)
                deaths[k] = new DeathEvent(new EntityHandle(0, 0), false, Type(data, Corners[k].Id), 0, 1, anchor + new System.Numerics.Vector2(k * 5f, 8f));
            combat.AddDeaths(w, deaths, sim.TickNumber);
            combat.Sync(w, 1f);
            await Frames(2);
            combat.Sync(w, 1f);
            int discs = 0;
            for (int m = 0; m < combat.Markers.Capacity; m++)
            {
                if (combat.MarkerShown(m) != 1) continue;
                discs++;
                UnitDef def = data.Units[combat.Markers.Type[m]];
                float r = combat.MarkerTransform(m).Basis.X.Length();
                Check(Mathf.Abs(r - def.Radius) < 1e-5f, $"{def.Key}: corpse disc {r}, want {def.Radius}");
            }
            Check(discs == deaths.Length, $"{discs} corpse discs for {deaths.Length} deaths");
        }
        finally
        {
            RemoveChild(match);
            match.QueueFree();
            await Frames(2);
        }
    }

    // ---- the shipped giants ----

    private async Task GiantRows(GameData data)
    {
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        try
        {
            match.Start(data, LaunchOptions.Parse(new[] { "--seed", "3", "--units", "0", "--no-bases", "--no-combat", "--no-fog", "--mute", "--faction", "1", "teblor" }));
            var runner = match.GetNode<SimRunner>("SimRunner");
            runner.ProcessMode = ProcessModeEnum.Disabled;
            Simulation sim = runner.Simulation!;
            World w = sim.World;
            UnitStore u = w.Units;
            var views = match.GetNode<UnitViews>("World3D/UnitViews");
            var sel = match.GetNode<SelectionController>("SelectionController");
            var camera = match.GetNode<RtsCamera>("RtsCamera");
            camera.EdgePanEnabled = false;
            Tick(sim, 2);
            int warrior = Type(data, "teblor_warrior"), dog = Type(data, "teblor_war_dog");
            System.Numerics.Vector2 anchor = StartLayout.Block(w.NavGrid, 1, west: true, 1f)[0];

            // Thirty giants, a 6 x 5 block 2.5 m apart, all ordered to one point beside it.
            var giants = new List<int>();
            for (int k = 0; k < 30; k++)
            {
                int s = Spawn(sim, 0, warrior, anchor + new System.Numerics.Vector2(k % 6 * 2.5f, k / 6 * 2.5f));
                if (s >= 0) giants.Add(s);
            }
            Check(giants.Count == 30, $"{giants.Count} of 30 giants spawned");
            System.Numerics.Vector2 point = anchor + new System.Numerics.Vector2(6f, 14f);
            foreach (int s in giants) sim.Enqueue(Command.Move(0, new EntityHandle(s, u.Generation[s]), point));
            float worst = 0f, worstSimOverlap = 0f;
            int simOverlapPairs = 0, frames = 0;
            for (int t = 0; t < 160; t++)
            {
                sim.Tick();
                foreach (float alpha in new[] { 0.5f, 1f })
                {
                    views.Sync(w, alpha);
                    frames++;
                    for (int a = 0; a < giants.Count; a++)
                        for (int c = a + 1; c < giants.Count; c++)
                        {
                            int i = giants[a], j = giants[c];
                            Vector3 pi = views.ViewOf(i)!.Position, pj = views.ViewOf(j)!.Position;
                            float drawn = new Vector2(pi.X - pj.X, pi.Z - pj.Z).Length();
                            float ri = ((CapsuleMesh)views.ViewOf(i)!.Mesh).Radius, rj = ((CapsuleMesh)views.ViewOf(j)!.Mesh).Radius;
                            // The sim's overlap at the same alpha: its two interpolated centres against the two collision radii.
                            System.Numerics.Vector2 si = System.Numerics.Vector2.Lerp(u.PrevPosition[i], u.Position[i], alpha);
                            System.Numerics.Vector2 sj = System.Numerics.Vector2.Lerp(u.PrevPosition[j], u.Position[j], alpha);
                            float simOver = u.Radius[i] + u.Radius[j] - System.Numerics.Vector2.Distance(si, sj);
                            float drawnOver = ri + rj - drawn;
                            if (alpha == 1f && simOver > 1e-3f) simOverlapPairs++;
                            worstSimOverlap = Math.Max(worstSimOverlap, simOver);
                            worst = Math.Max(worst, drawnOver - Math.Max(simOver, 0f));
                        }
                }
            }
            GD.Print($"  30 giants to one point, 160 ticks, {frames} syncs: drawn overlap beyond the sim's at most {worst:0.####} m; the sim's own largest overlap {worstSimOverlap:0.###} m ({simOverlapPairs} pair-ticks overlapping)");
            Check(worst < 1e-3f, $"30 giants: a pair drawn {worst} m more overlapped than the sim keeps them");

            // A lone Warrior far from the pack for the click / box rows, and a War-dog beside a second Warrior.
            System.Numerics.Vector2 lone = anchor + new System.Numerics.Vector2(30f, 0f);
            int giant = Spawn(sim, 0, warrior, lone);
            int other = Spawn(sim, 0, warrior, lone + new System.Numerics.Vector2(0f, 12f));
            int hound = Spawn(sim, 0, dog, lone + new System.Numerics.Vector2(1.2f, 12f));
            if (!Check(giant >= 0 && other >= 0 && hound >= 0, "lone warrior / pair did not spawn")) return;
            Tick(sim, 30);
            camera.SetZoom(20f);
            camera.SetFocus(u.Position[giant].X, u.Position[giant].Y + 2f);
            await Frames(3);
            views.Sync(w, 1f);
            UnitDef gd = data.Units[warrior];
            if (Check(sel.TryScreenPosition(giant, out Vector2 at), "the lone giant is off screen"))
            {
                Vector3 c = UnitViews.GroundPoint(w, giant, 1f) + new Vector3(0f, UnitViews.BodyHeight(gd.Radius) / 2f, 0f);
                float pxPerM = camera.UnprojectPosition(c + Vector3.Right).X - camera.UnprojectPosition(c).X;
                float drawnPx = ((CapsuleMesh)views.ViewOf(giant)!.Mesh).Radius * pxPerM;
                var handle = new EntityHandle(giant, u.Generation[giant]);
                var otherH = new EntityHandle(other, u.Generation[other]);
                GD.Print($"  lone giant: drawn radius {drawnPx:0.#} px at the pick centre ({pxPerM:0.#} px/m)");
                sel.SelectOnly(otherH);
                Click(sel, at + Vector2.Right * (drawnPx - 3f));
                Check(sel.Selection.Count == 1 && sel.Selection.Contains(handle), $"a click 3 px inside the giant's drawn edge selected {sel.Selection.Count}, not the giant");
                sel.SelectOnly(otherH);
                Click(sel, at + Vector2.Right * (drawnPx + 3f));
                Check(sel.Selection.Count == 0, $"a click 3 px outside the giant's drawn edge selected {sel.Selection.Count}");
                sel.BoxSelect(at + new Vector2(drawnPx - 4f, -6f), at + new Vector2(drawnPx + 4f, 6f), add: false);
                Check(sel.Selection.Count == 0, $"a box over only the giant's drawn edge selected {sel.Selection.Count}");
                sel.BoxSelect(at - new Vector2(4f, 4f), at + new Vector2(4f, 4f), add: false);
                Check(sel.Selection.Count == 1 && sel.Selection.Contains(handle), $"a box round the giant's centre selected {sel.Selection.Count}");
            }

            UnitDef dd = data.Units[dog];
            var dm = (CapsuleMesh)views.ViewOf(hound)!.Mesh;
            var wm = (CapsuleMesh)views.ViewOf(other)!.Mesh;
            float apart = new Vector2(views.ViewOf(hound)!.Position.X - views.ViewOf(other)!.Position.X, views.ViewOf(hound)!.Position.Z - views.ViewOf(other)!.Position.Z).Length();
            float simApart = System.Numerics.Vector2.Distance(u.Position[hound], u.Position[other]);
            GD.Print($"  War-dog {2f * dm.Radius:0.###} x {dm.Height:0.###} m beside a Warrior {2f * wm.Radius:0.###} x {wm.Height:0.###} m: centres {apart:0.###} m drawn, {simApart:0.###} m in the sim, radii {dd.Radius + gd.Radius:0.###} m");
            Check(dd.Scale == 1f && dm.Radius == dd.Radius && dm.Height == UnitViews.BodyHeight(dd.Radius), $"war-dog capsule {dm.Radius} x {dm.Height}");
            Check(wm.Height > dm.Height * 2f, $"the giant {wm.Height} m is not over twice the dog's {dm.Height} m");
            Check(Mathf.Abs(apart - simApart) < 1e-3f, $"dog / giant drawn {apart} m apart, sim {simApart} m");
            Check(simApart < dd.Radius + gd.Radius - 1e-3f || apart >= dm.Radius + wm.Radius - 1e-3f, $"dog and giant kept apart by the sim but drawn overlapping");
        }
        finally
        {
            RemoveChild(match);
            match.QueueFree();
            await Frames(2);
        }
    }

    // ---- helpers ----

    private static DataLoadResult LoadForged(string dataDir, string root, (string Id, double Radius, double Scale)[] rows)
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
        foreach ((string id, double radius, double scale) in rows)
        {
            JsonNode unit = units["units"]!.AsArray().First(n => (string?)n!["id"] == id)!;
            unit["radius"] = radius;
            unit["scale"] = scale;
        }
        File.WriteAllText(path, units.ToJsonString());
        return DataLoader.LoadAll(copy);
    }

    private static int Spawn(Simulation sim, int player, int type, System.Numerics.Vector2 at)
    {
        UnitStore u = sim.World.Units;
        var before = new bool[u.Capacity];
        for (int i = 0; i < u.Capacity; i++) before[i] = u.Alive[i];
        sim.Enqueue(Command.SpawnUnit(player, type, at));
        Tick(sim, 2);
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && !before[i] && u.Owner[i] == player && u.TypeId[i] == type) return i;
        return -1;
    }

    private static int Type(GameData data, string id)
    {
        for (int t = 0; t < data.Units.Length; t++) if (data.Units[t].Key == id) return t;
        throw new InvalidOperationException($"no unit {id}");
    }

    private static void Tick(Simulation sim, int n)
    {
        for (int i = 0; i < n; i++) sim.Tick();
    }

    private static void Click(SelectionController sel, Vector2 at)
    {
        sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
        sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private bool Check(bool ok, string message)
    {
        if (!ok && _failures.Count < 200) _failures.Add(message);
        return ok;
    }
}
