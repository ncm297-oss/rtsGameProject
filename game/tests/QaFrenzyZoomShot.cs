using System;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;

namespace Rts.Game.Tests;

/// <summary>
/// QA, for looking (M4-V6d): Burning, Slowed, Blinded and Frenzied markers side by side on a Heavy Infantry and a Raider,
/// one pair on the lowest ground level and one on the highest, shot at zoom 20, 30 and 60 (six PNGs:
/// <c>qa-frenzy-z{zoom}-level{n}.png</c>), so the Frenzied colour can be judged against the other three at RTS zoom.
/// </summary>
/// <remarks>
/// Windowed: <c>&amp; $env:GODOT --path game res://tests/QaFrenzyZoomShot.tscn -- --out-dir C:\temp\shots [--seed N]</c>.
/// Headless it prints that capture is unavailable and quits 0. The statuses are written into the store by reflection
/// (test staging only; nothing ticks after it).
/// </remarks>
public partial class QaFrenzyZoomShot : Node
{
    private static readonly MethodInfo ApplyStatus = typeof(Rts.Sim.Abilities.StatusStore).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Instance)!;

    public override async void _Ready()
    {
        if (DisplayServer.GetName() == "headless")
        {
            GD.Print("QaFrenzyZoomShot: capture unavailable in headless mode.");
            SceneExit.Quit(this, 0);
            return;
        }
        string outDir = "user://", seed = "1";
        string[] args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--out-dir") outDir = args[i + 1];
            if (args[i] == "--seed") seed = args[i + 1];
        }
        GetTree().Root.Size = new Vector2I(1152, 648);
        GameData data = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data")).Data!;
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        match.Start(data, LaunchOptions.Parse(new[] { "--seed", seed, "--units", "0", "--no-bases", "--no-combat", "--mute", "--zoom", "30" }));
        SimRunner runner = match.GetNode<SimRunner>("SimRunner");
        runner.ProcessMode = ProcessModeEnum.Disabled;
        Simulation sim = runner.Simulation!;
        RtsCamera camera = match.GetNode<RtsCamera>("RtsCamera");
        camera.EdgePanEnabled = false;
        NavGrid g = sim.World.NavGrid;
        int heavy = data.FindUnit("malazan_heavy_infantry"), raider = data.FindUnit("whirlwind_raider");
        int[] statuses = { data.FindStatus("burning"), data.FindStatus("slowed"), data.FindStatus("blinded"), data.FindStatus("frenzied") };
        float[] magnitudes = { 1f, 0.3f, 0f, 0.5f };

        // A passable cell on the lowest and on the highest level, each with its neighbours 3 cells east passable.
        int lo = -1, hi = -1, loLevel = int.MaxValue, hiLevel = int.MinValue;
        for (int y = 8; y < g.Height - 8; y++)
            for (int x = 8; x < g.Width - 12; x++)
            {
                bool ok = true;
                for (int dx = -2; dx <= 4 && ok; dx++) for (int dy = -2; dy <= 2 && ok; dy++) ok = g.IsPassable(x + dx, y + dy) && g.LevelAt(x + dx, y + dy) == g.LevelAt(x, y);
                if (!ok) continue;
                int lv = g.LevelAt(x, y);
                if (lv < loLevel) (lo, loLevel) = (y * g.Width + x, lv);
                if (lv > hiLevel) (hi, hiLevel) = (y * g.Width + x, lv);
            }
        var spots = new[] { (lo, loLevel), (hi, hiLevel) };
        foreach ((int cell, int _) in spots)
        {
            System.Numerics.Vector2 c = g.CellCenter(cell % g.Width, cell / g.Width);
            sim.Enqueue(Command.SpawnUnit(0, heavy, c));
            sim.Enqueue(Command.SpawnUnit(1, raider, c + new System.Numerics.Vector2(2.5f, 0f)));
        }
        for (int t = 0; t < 8; t++) sim.Tick();
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i])
                for (int k = 0; k < 4; k++) ApplyStatus.Invoke(u.Statuses, new object[] { i, statuses[k], magnitudes[k], 2400, 0 });
        foreach (float zoom in new[] { 20f, 30f, 60f })
            foreach ((int cell, int level) in spots)
            {
                System.Numerics.Vector2 c = g.CellCenter(cell % g.Width, cell / g.Width);
                camera.SetZoom(zoom);
                camera.SetFocus(c.X + 1.25f, c.Y);
                for (int f = 0; f < 4; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                for (int f = 0; f < 3; f++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                string path = System.IO.Path.Combine(ProjectSettings.GlobalizePath(outDir), string.Create(CultureInfo.InvariantCulture, $"qa-frenzy-z{zoom:0}-level{level}.png"));
                GetViewport().GetTexture().GetImage().SavePng(path);
                GD.Print($"QaFrenzyZoomShot: zoom {zoom} (camera {camera.Zoom}) level {level} at {c}: {path}");
            }
        GD.Print("QaFrenzyZoomShot: done");
        SceneExit.Quit(this, 0);
    }
}
