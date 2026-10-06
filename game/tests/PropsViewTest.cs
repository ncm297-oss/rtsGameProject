using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>M2-3b: <c>--forests</c> / <c>--mines</c> parsing, and the real Match scene's <see cref="PropsView"/> and minimap resource layer on the default 12 / 8 map and two overrides.</summary>
/// <remarks>
/// Headless. Run: <c>&amp; $env:GODOT --headless --path game res://tests/PropsViewTest.tscn</c>; prints
/// "PROPS VIEW TEST PASS" and exits 0, or prints each failure and exits 1. Felling a node needs the
/// sim's internal <c>Take</c>, so depletion is covered by the xUnit rows (PropLayoutTests,
/// MinimapRasterResourceTests), not here.
/// </remarks>
public partial class PropsViewTest : Node
{
    private readonly List<string> _failures = new();
    private GameData _data = null!;

    public override async void _Ready()
    {
        try
        {
            await Run();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures) GD.Print($"PROPS VIEW TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("PROPS VIEW TEST PASS");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    private async Task Run()
    {
        GetTree().Root.Size = new Vector2I(1152, 648); // headless windows are 64 x 64
        await Frame();
        DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
        if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
        _data = loaded.Data!;

        LaunchOptionsRows();
        await MatchWith(Array.Empty<string>(), LaunchOptions.DefaultForests, LaunchOptions.DefaultMines, deep: true);
        await MatchWith(new[] { "--forests", "3", "--mines", "2" }, 3, 2, deep: false);
        await MatchWith(new[] { "--forests", "0", "--mines", "0" }, 0, 0, deep: false);
    }

    // LaunchOptionsTests rows: defaults, overrides, the 0 and 64 bounds, refused values and missing values.
    private void LaunchOptionsRows()
    {
        (string Args, int Forests, int Mines)[] rows =
        {
            ("", 12, 8), ("--forests 3 --mines 2", 3, 2), ("--forests 0 --mines 0", 0, 0), ("--forests 64 --mines 64", 64, 64),
            ("--forests 65 --mines -1", 12, 8), ("--forests x --mines 2.5", 12, 8), ("--forests --mines 4", 12, 4), ("--mines", 12, 8),
        };
        foreach ((string args, int f, int m) in rows)
        {
            LaunchOptions o = LaunchOptions.Parse(args.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            Check(o.Forests == f && o.Mines == m, $"'{args}': forests {o.Forests}, mines {o.Mines}; want {f} / {m}");
        }
    }

    private async Task MatchWith(string[] flags, int forests, int mines, bool deep)
    {
        var args = new List<string> { "--units", "20" };
        args.AddRange(flags);
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        match.Start(_data, LaunchOptions.Parse(args.ToArray()));
        Simulation sim = match.GetNode<SimRunner>("SimRunner").Simulation!;
        World w = sim.World;
        ResourceStore r = w.Resources;
        var props = match.GetNode<PropsView>("World3D/PropsView");
        var mini = match.GetNode<Minimap>("Hud/Minimap");
        await Frame();
        await Frame();
        string what = $"[{string.Join(' ', flags)}]";

        Check(w.Config.Map.Forests == forests && w.Config.Map.GoldMines == mines,
            $"{what}: map asked for {w.Config.Map.Forests} forests, {w.Config.Map.GoldMines} mines");
        ResourcePlacement placed = w.ResourcePlacement;
        GD.Print($"{what}: placed forests {placed.Forests} trees {placed.Trees} mines {placed.Mines}");
        Check(placed.Forests <= forests && placed.Mines <= mines && (forests == 0 || placed.Forests > 0) && (mines == 0 || placed.Mines > 0),
            $"{what}: placed {placed}");

        // One MultiMesh per type, its drawn count the store's live count of that type.
        int tree = _data.FindResource("tree"), mine = _data.FindResource("gold_mine");
        for (int t = 0; t < _data.Resources.Length; t++)
        {
            int alive = 0;
            for (int i = 0; i < r.Capacity; i++) if (r.Alive[i] && r.TypeId[i] == t) alive++;
            Check(props.ShownCount(t) == alive, $"{what}: type {_data.Resources[t].Key} shows {props.ShownCount(t)}, store has {alive}");
            Check(props.InstanceOf(t).Multimesh.Mesh != null, $"{what}: type {t} has no mesh");
        }
        Check(props.ShownCount(tree) == placed.Trees && props.ShownCount(mine) == placed.Mines,
            $"{what}: shown {props.ShownCount(tree)} trees / {props.ShownCount(mine)} mines, placed {placed.Trees} / {placed.Mines}");
        Check(props.Uploads == 1, $"{what}: {props.Uploads} uploads over the first frames, expected 1");

        if (deep)
        {
            // Meshes fill their footprints: a mine's block is the footprint, a tree stays inside its cell.
            Aabb mineBox = props.InstanceOf(mine).Multimesh.Mesh.GetAabb();
            ResourceDef md = _data.Resources[mine], td = _data.Resources[tree];
            Check(Mathf.IsEqualApprox(mineBox.Size.X, md.FootprintWidth * MapConstants.CellSize) && Mathf.IsEqualApprox(mineBox.Size.Z, md.FootprintHeight * MapConstants.CellSize)
                && mineBox.Position.Y >= -1e-4f, $"mine mesh bounds {mineBox}");
            Aabb treeBox = props.InstanceOf(tree).Multimesh.Mesh.GetAabb();
            Check(treeBox.Size.X <= td.FootprintWidth * MapConstants.CellSize && treeBox.Size.Z <= td.FootprintHeight * MapConstants.CellSize
                && Mathf.IsEqualApprox(treeBox.End.Y, PropsView.TreeHeight) && treeBox.Position.Y >= -1e-4f, $"tree mesh bounds {treeBox}");

            // Minimap: a mine's cells are gold and a tree's dark green on the resource layer.
            await RefreshMinimap(mini);
            int ma = FirstSlot(r, mine), ta = FirstSlot(r, tree);
            CheckPixel(mini, r.Cell[ma], MinimapRaster.GoldRgb, "mine");
            CheckPixel(mini, r.Cell[ta], MinimapRaster.WoodRgb, "tree");
            Check(mini.Raster!.ResourceDraws == 1, $"minimap resource layer drawn {mini.Raster.ResourceDraws} times, expected 1");

            // Steady frames: no relist, no upload, 0 bytes.
            props.Sync(w);
            var watch = new Stopwatch();
            long before = GC.GetAllocatedBytesForCurrentThread();
            watch.Start();
            for (int i = 0; i < 1000; i++) props.Sync(w);
            watch.Stop();
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            GD.Print($"PropsView steady Sync: {watch.Elapsed.TotalMilliseconds / 1000 * 1000:F2} us per frame, {bytes} bytes over 1000 frames");
            Check(bytes == 0, $"steady Sync allocated {bytes} bytes");
            Check(props.Uploads == 1 && props.Layout!.Rebuilds == 1, $"steady frames relisted: uploads {props.Uploads}, rebuilds {props.Layout!.Rebuilds}");
        }
        RemoveChild(match);
        match.QueueFree();
        await Frame();
    }

    private static int FirstSlot(ResourceStore r, int type)
    {
        for (int i = 0; i < r.Capacity; i++) if (r.Alive[i] && r.TypeId[i] == type) return i;
        throw new InvalidOperationException($"no live node of type {type}");
    }

    private async Task RefreshMinimap(Minimap mini)
    {
        int start = mini.Refreshes;
        while (mini.Refreshes == start) await Frame();
    }

    private void CheckPixel(Minimap mini, int cell, uint rgb, string what)
    {
        int w = mini.Raster!.Width;
        Color got = mini.ResourcesImage.GetPixel(cell % w, cell / w);
        Color want = UnitViews.ColorFromRgb(rgb);
        Check(Mathf.Abs(got.R - want.R) < 0.01f && Mathf.Abs(got.G - want.G) < 0.01f && Mathf.Abs(got.B - want.B) < 0.01f && got.A > 0.99f,
            $"minimap {what} pixel ({cell % w}, {cell / w}) is {got}, want {want}");
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private void Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
    }
}
