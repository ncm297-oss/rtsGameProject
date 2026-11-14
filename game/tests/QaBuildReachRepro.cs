using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// QA check of the M7-V4 developer's open sim question (not in the scene loop: its name doesn't end in "Test"; 48 matches).
/// Finding: the "stuck" Laborer was dev-spawned inside an impassable cell (a 2x2 blocked patch two rows south of the site),
/// so it never moved; every Laborer spawned on passable ground finishes. Blocked spawns are printed as notes, not failures. Shipped data, seed 1, no bases, standard resources raised to high: a lone Laborer
/// spawned a little out of build reach (5.6 / 6 / 7 m from a 3x3 Barracks site's centre, in each of the four directions)
/// is ordered to Build it; every site must finish within 4000 ticks. Prints the stuck worker's state, position and goal
/// for its first ticks. Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaBuildReachRepro.tscn</c>; prints
/// "QA BUILD REACH REPRO PASS" or FAIL lines.
/// </summary>
public partial class QaBuildReachRepro : Node
{
    private const string Tag = "QA BUILD REACH REPRO";
    private static readonly MethodInfo DamageMethod = typeof(BuildingStore).GetMethod("Damage", BindingFlags.Instance | BindingFlags.NonPublic)!;

    public override void _Ready()
    {
        var failures = new List<string>();
        try
        {
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            GameData data = loaded.Data!;
            int barracks = data.FindBuilding("malazan_barracks"), laborer = data.FindUnit("malazan_laborer");
            foreach (int seed in new[] { 1, 6 })
                foreach (bool open in new[] { false, true })
                    foreach (float dist in new[] { 5.6f, 6f, 7f })
                        foreach (System.Numerics.Vector2 dir in new[] { new System.Numerics.Vector2(0, 1), new System.Numerics.Vector2(0, -1), new System.Numerics.Vector2(1, 0), new System.Numerics.Vector2(-1, 0) })
                            RunOne(data, barracks, laborer, seed, open, dist, dir, failures);
        }
        catch (Exception ex)
        {
            failures.Add($"exception: {ex}");
        }
        foreach (string f in failures) GD.Print($"{Tag} FAIL: {f}");
        if (failures.Count == 0) GD.Print($"{Tag} PASS");
        SceneExit.Quit(this, failures.Count == 0 ? 0 : 1);
    }

    // One fresh match: a Barracks site at the anchor near west + (-36, 0) (`open`: 5 clear cells round it, no ramp; else the
    // M7-V4 developer's rule, one free passable cell round it), a lone Laborer `dist` m from its centre along `dir`.
    private void RunOne(GameData data, int barracks, int laborer, int seed, bool open, float dist, System.Numerics.Vector2 dir, List<string> failures)
    {
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        try
        {
            match.Start(data, LaunchOptions.Parse(new[] { "--seed", seed.ToString(), "--units", "0", "--no-bases", "--mute", "--resources", "high" }));
            var runner = match.GetNode<SimRunner>("SimRunner");
            runner.ProcessMode = ProcessModeEnum.Disabled;
            Simulation sim = runner.Simulation!;
            World w = sim.World;
            NavGrid g = w.NavGrid;
            System.Numerics.Vector2 west = StartLayout.Block(g, 1, west: true, 1f)[0];
            sim.Enqueue(Command.SpawnUnit(0, laborer, west + new System.Numerics.Vector2(0f, -40f))); // never defeated
            sim.Enqueue(Command.SpawnUnit(1, laborer, StartLayout.Block(g, 1, west: false, 1f)[0]));
            sim.Tick();
            sim.Tick();
            BuildingDef def = data.Buildings[barracks];
            int anchor = OpenAnchor(w, barracks, west + new System.Numerics.Vector2(-36f, 0f), open);
            string where = $"seed {seed} {(open ? "open" : "dev")} anchor {anchor}";
            if (anchor < 0) { failures.Add($"{where}: no anchor"); return; }
            System.Numerics.Vector2 c = StartBase.FootprintCenter(g, def, anchor);
            System.Numerics.Vector2 spot = c + dir * dist;
            int worker = SpawnAt(sim, laborer, spot);
            if (worker < 0) { failures.Add($"{where}: no worker at {dir * dist}"); return; }
            for (int t = 0; t < 4; t++) sim.Tick(); // the worker's sight explores the ground first
            sim.Enqueue(Command.Build(0, new EntityHandle(worker, w.Units.Generation[worker]), barracks, Rts.Sim.ViewApi.PlacementGhost.AnchorPoint(g, anchor)));
            sim.Tick();
            sim.Tick();
            int site = w.Buildings.SlotAt(anchor % g.Width, anchor / g.Width);
            if (site < 0) { failures.Add($"{where}: no site (wood {w.Wood[0]})"); return; }
            var trace = new List<string>();
            int k = 0;
            for (; k < 4000 && w.Buildings.Alive[site] && w.Buildings.UnderConstruction[site]; k++)
            {
                if (k < 45 && k % 5 == 0)
                    trace.Add($"t{k}: {w.Units.State[worker]} pos {w.Units.Position[worker] - c} goal {w.Units.Goal[worker] - c} goalCell {w.Units.GoalCell[worker]} stuck {w.Units.StuckTicks[worker]}");
                sim.Tick();
            }
            bool ok = w.Buildings.Alive[site] && !w.Buildings.UnderConstruction[site];
            GD.Print($"{where}: laborer at {dir * dist} from the centre ({dist - 3f:0.0} m from the edge): {(ok ? $"finished in {k} ticks" : $"STUCK after {k} ticks, {w.Units.State[worker]} at {w.Units.Position[worker] - c}, wood work {w.Buildings.UnderConstruction[site]}")}");
            bool blockedSpawn = g.WorldToCell(spot, out int sx, out int sy) && !g.IsPassable(sx, sy);
            if (!ok && blockedSpawn)
                GD.Print($"    note: the dev spawn put the laborer inside impassable cell ({sx}, {sy}); it can't path out (a test-setup artifact, not a build-reach defect)");
            if (!ok && !blockedSpawn)
            {
                failures.Add($"{where}: a laborer {dist - 3f:0.0} m from the {Side(dir)} edge never finished the site ({k} ticks, {w.Units.State[worker]}, still at {w.Units.Position[worker] - c})");
                foreach (string s in trace) GD.Print($"    {s}");
                int ax = anchor % g.Width, ay = anchor / g.Width;
                for (int y = ay - 2; y <= ay + 5; y++)
                {
                    var row = new System.Text.StringBuilder();
                    for (int x = ax - 2; x <= ax + 5; x++)
                        row.Append(w.Buildings.SlotAt(x, y) >= 0 ? 'B' : !g.IsPassable(x, y) ? '#' : (g.FlagsAt(x, y) & NavFlags.Ramp) != 0 ? 'r' : '.');
                    GD.Print($"    row {y}: {row}");
                }
            }
        }
        finally
        {
            RemoveChild(match);
            match.QueueFree();
        }
    }

    private static string Side(System.Numerics.Vector2 d) => d.Y > 0 ? "+y (south on screen)" : d.Y < 0 ? "-y" : d.X > 0 ? "+x" : "-x";

    private static int SpawnAt(Simulation sim, int type, System.Numerics.Vector2 spot)
    {
        sim.Enqueue(Command.SpawnUnit(0, type, spot));
        sim.Tick();
        sim.Tick();
        World w = sim.World;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 0 && w.Units.TypeId[i] == type && System.Numerics.Vector2.Distance(w.Units.Position[i], spot) < 0.8f) return i;
        return -1;
    }

    private static int OpenAnchor(World w, int type, System.Numerics.Vector2 near, bool wide)
    {
        NavGrid g = w.NavGrid;
        BuildingDef def = w.Data.Buildings[type];
        for (int r = 0; r <= 8; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    if (!g.WorldToCell(near + new System.Numerics.Vector2(dx, dy) * MapConstants.CellSize, out int cx, out int cy)) continue;
                    if (cx < 6 || cy < 6 || cx + def.FootprintWidth + 6 >= g.Width || cy + def.FootprintHeight + 6 >= g.Height) continue;
                    if (!wide && !w.Buildings.Fits(type, cy * g.Width + cx)) continue;
                    bool open = true;
                    int m = wide ? 5 : 1;
                    for (int y = cy - m; y <= cy + def.FootprintHeight + m - 1 && open; y++)
                        for (int x = cx - m; x <= cx + def.FootprintWidth + m - 1 && open; x++)
                            open = g.IsPassable(x, y) && (!wide || (g.FlagsAt(x, y) & NavFlags.Ramp) == 0) && w.Buildings.SlotAt(x, y) < 0;
                    int a = cy * g.Width + cx;
                    if (open && w.Buildings.Fits(type, a)) return a;
                }
        return -1;
    }
}
