using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;

namespace Rts.Game.Tests;

/// <summary>QA (M2-H2): the 10 s bench's march across the map on seed 1 and two seeds whose (0.85 W, 0.5 H) cell is blocked, the real Match's start blocks at <c>--units 1000</c>, and PropsView's per-type uploads after a felled tree and a felled mine.</summary>
/// <remarks>
/// Headless. Run: <c>&amp; $env:GODOT --headless --path game res://tests/QaH2Test.tscn</c>; prints
/// "QA M2-H2 TEST PASS" and exits 0, or prints each failure and exits 1. Scan the log for ERROR too.
/// Optional user args: <c>-- --seeds 1,6,31</c> to pick the bench seeds (the default).
/// </remarks>
public partial class QaH2Test : Node
{
    private readonly List<string> _failures = new();
    private GameData _data = null!;

    public override async void _Ready()
    {
        try
        {
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            // 1 = QaM27's seed; 6 = (0.85 W, 0.5 H) is a forest cell; 31 = it is a cliff cell. Seed 21 (also a cliff
            // cell) moves the centre the least of the seeds measured (19.1 m; BUG-0104): `-- --seeds 1,6,21`.
            string seeds = "1,6,31";
            string[] args = OS.GetCmdlineUserArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--seeds") seeds = args[i + 1];
            foreach (string s in seeds.Split(',')) await BenchAcross(s.Trim());
            foreach (string s in new[] { "1", "13", "40" }) await Blocks1000(s);
            await PropsFell();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures) GD.Print($"QA M2-H2 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M2-H2 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // The 10 s bench's march: the centre shift depends on the seed's terrain and on the idle enemy block in the path
    // (19.1-26.2 m over seeds 1, 6, 7, 21, 23, 31, 43). Seed 1, QaM27's, is held to its 20 m; any other seed to 15 m,
    // the family floor (the least measured, seed 21's 19.1 m, less ~20 %): the row proves the army marches, not a pace
    // (BUG-0104; docs/03 "Implementation (M2-7)").
    private const float Seed1MinShift = 20f, FamilyMinShift = 15f;

    private async Task BenchAcross(string seed)
    {
        Match match = StartMatch("--bench", "10", "--mute", "--speed", "1", "--seed", seed, "--no-combat"); // combat off: a world without fights, as in M2 (M4-V1, BUG-0147)
        BenchRunner bench = match.Bench!;
        bench.QuitOnFinish = false;
        Simulation sim = match.GetNode<SimRunner>("SimRunner").Simulation!;
        var watch = Stopwatch.StartNew();
        while (!bench.Running && watch.Elapsed.TotalSeconds < 20) await Frame();
        UnitStore u = sim.World.Units;
        System.Numerics.Vector2 c0 = Centre(u);
        float maxShift = 0;
        while (!bench.Finished && watch.Elapsed.TotalSeconds < 40)
        {
            await Frame();
            maxShift = Math.Max(maxShift, System.Numerics.Vector2.Distance(c0, Centre(u)));
        }
        NavGrid g = sim.World.NavGrid;
        float w = g.Width * MapConstants.CellSize, h = g.Height * MapConstants.CellSize;
        int fx = (int)(0.85f * w / MapConstants.CellSize), fy = (int)(0.5f * h / MapConstants.CellSize);
        float reach = System.Numerics.Vector2.Distance(bench.AcrossFrom, bench.AcrossTarget);
        GD.Print($"QA M2-H2 NOTE: seed {seed}: far cell ({fx}, {fy}) flags {g.FlagsAt(fx, fy)}; {bench.AcrossOrders} across orders; " +
            $"target {bench.AcrossTarget} {reach:F1} m from {bench.AcrossFrom}; centre moved at most {maxShift:F1} m; {bench.Line}");
        Check(bench.Finished, $"seed {seed}: 10 s bench never finished");
        Check(bench.AcrossOrders >= 1, $"seed {seed}: no across order");
        Check(reach >= 100f, $"seed {seed}: target only {reach:F1} m away");
        float minShift = seed == "1" ? Seed1MinShift : FamilyMinShift;
        Check(maxShift >= minShift, $"seed {seed}: centre moved only {maxShift:F1} m (want >= {minShift} m)");
        Check(g.WorldToCell(bench.AcrossTarget, out int tx, out int ty) && g.IsPassable(tx, ty), $"seed {seed}: target {bench.AcrossTarget} not passable");
        RemoveChild(match);
        match.QueueFree();
        await Frame();
        await Frame();
    }

    private static System.Numerics.Vector2 Centre(UnitStore u)
    {
        var sum = System.Numerics.Vector2.Zero;
        int n = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.Owner[i] != SelectionController.LocalPlayer) continue;
            sum += u.Position[i];
            n++;
        }
        return n > 0 ? sum / n : sum;
    }

    // The real Match at --units 1000: 1,000 per side spawn, each on an open cell (passable, not a ramp, no
    // Blocked cell among its 8 neighbours), each side on one level.
    private async Task Blocks1000(string seed)
    {
        Match match = StartMatch("--units", "1000", "--mute", "--seed", seed, "--no-bases", "--no-combat"); // armies only, no fights, as in M2 (M3-V1, M4-V1 BUG-0147)
        Simulation sim = match.GetNode<SimRunner>("SimRunner").Simulation!;
        var watch = Stopwatch.StartNew();
        while (sim.TickNumber < 2 && watch.Elapsed.TotalSeconds < 20) await Frame();
        UnitStore u = sim.World.Units;
        NavGrid g = sim.World.NavGrid;
        var count = new int[2];
        var level = new[] { -1, -1 };
        int notOpen = 0, offLevel = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            int o = u.Owner[i];
            count[o]++;
            g.WorldToCell(u.Position[i], out int x, out int y);
            bool open = g.IsPassable(x, y) && (g.FlagsAt(x, y) & NavFlags.Ramp) == 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if ((g.FlagsAt(x + dx, y + dy) & NavFlags.Blocked) != 0) open = false;
            if (!open) notOpen++;
            if (level[o] < 0) level[o] = g.LevelAt(x, y);
            else if (g.LevelAt(x, y) != level[o]) offLevel++;
        }
        GD.Print($"QA M2-H2 NOTE: seed {seed} --units 1000 at tick {sim.TickNumber}: {count[0]} / {count[1]} units, levels {level[0]} / {level[1]}, {notOpen} not open, {offLevel} off level");
        Check(count[0] == 1000 && count[1] == 1000, $"seed {seed}: {count[0]} / {count[1]} units at --units 1000");
        Check(notOpen == 0 && offLevel == 0, $"seed {seed}: {notOpen} units on non-open cells, {offLevel} off their block's level");
        RemoveChild(match);
        match.QueueFree();
        await Frame();
        await Frame();
    }

    // BUG-0086 in the real scene: a felled tree re-uploads only the trees; a felled mine only the mines.
    private async Task PropsFell()
    {
        // Armies only, no fights, as in M2 (M3-V1, M4-V1 BUG-0147); no fog, since the staged fells are wherever the first
        // slots stand and a fell out of sight keeps the last-seen node without a relist (M4-VH2, BUG-0281: FogViewTest's row).
        Match match = StartMatch("--units", "10", "--mute", "--no-bases", "--no-combat", "--no-fog");
        Simulation sim = match.GetNode<SimRunner>("SimRunner").Simulation!;
        var props = match.GetNode<PropsView>("World3D/PropsView");
        for (int i = 0; i < 10; i++) await Frame();
        ResourceStore r = sim.World.Resources;
        int tree = _data.FindResource("tree"), mine = _data.FindResource("gold_mine");
        int t0 = props.UploadsOf(tree), m0 = props.UploadsOf(mine), all0 = props.Uploads;
        MethodInfo take = typeof(ResourceStore).GetMethod("Take", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach ((int type, string what) in new[] { (tree, "tree"), (mine, "mine") })
        {
            int slot = -1;
            for (int i = 0; i < r.Capacity && slot < 0; i++) if (r.Alive[i] && r.TypeId[i] == type) slot = i;
            take.Invoke(r, new object[] { r.HandleOf(slot), int.MaxValue });
            for (int i = 0; i < 5; i++) await Frame();
            GD.Print($"QA M2-H2 NOTE: after felling a {what}: uploads trees {props.UploadsOf(tree)}, mines {props.UploadsOf(mine)}, any {props.Uploads}; shown {props.ShownCount(tree)} / {props.ShownCount(mine)}");
        }
        Check(props.UploadsOf(tree) == t0 + 1, $"tree uploads {t0} -> {props.UploadsOf(tree)}, want +1 (one tree fell, then a mine)");
        Check(props.UploadsOf(mine) == m0 + 1, $"mine uploads {m0} -> {props.UploadsOf(mine)}, want +1");
        Check(props.Uploads == all0 + 2, $"relist uploads {all0} -> {props.Uploads}, want +2");
        RemoveChild(match);
        match.QueueFree();
        await Frame();
    }

    private Match StartMatch(params string[] args)
    {
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        match.Start(_data, LaunchOptions.Parse(args));
        return match;
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private void Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
    }
}
