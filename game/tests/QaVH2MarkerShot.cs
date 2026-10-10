using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using NVector2 = System.Numerics.Vector2;

namespace Rts.Game.Tests;

/// <summary>QA (M4-VH2, BUG-0371 a) status-marker readability capture: own and enemy units at the map centre with Slowed, Burning, or both, shot at zooms 20 (the minimum), 30 and 60.</summary>
/// <remarks>
/// Windowed only. Run: <c>&amp; $env:GODOT --path game res://tests/QaVH2MarkerShot.tscn -- --out C:/tmp/qa-markers</c>.
/// Writes <c>markers-zoomN.png</c> per zoom into the folder. A look tool for QA, not a pass/fail test.
/// </remarks>
public partial class QaVH2MarkerShot : Node
{
    private static readonly MethodInfo ApplyStatus = typeof(Rts.Sim.Abilities.StatusStore).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private string _out = "user://qa-markers";

    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--out") _out = args[i + 1];
        _ = Run();
    }

    private async Task Run()
    {
        if (DisplayServer.GetName() == "headless")
        {
            GD.Print("QaVH2MarkerShot: capture unavailable in headless mode.");
            SceneExit.Quit(this, 0);
            return;
        }
        DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
        GameData data = loaded.Data!;
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        match.Start(data, LaunchOptions.Parse(new[] { "--units", "0", "--no-bases", "--no-combat", "--mute", "--seed", "1" }));
        var runner = match.GetNode<SimRunner>("SimRunner");
        runner.ProcessMode = ProcessModeEnum.Disabled;
        Simulation sim = runner.Simulation!;
        World w = sim.World;
        var g = w.NavGrid;
        int center = Rts.Sim.Pathfinding.FlowField.NearestPassable(g, g.Height / 2 * g.Width + g.Width / 2);
        NVector2 c = g.CellCenter(center % g.Width, center / g.Width);
        int slowed = data.FindStatus("slowed"), burning = data.FindStatus("burning");
        var spawns = new List<(int player, int type, NVector2 at)>();
        string[] own = { "malazan_heavy_infantry", "malazan_laborer", "malazan_cadre_mage", "malazan_crossbowman" };
        for (int k = 0; k < own.Length; k++) spawns.Add((0, data.FindUnit(own[k]), c + new NVector2(-6f + 3f * k, -2f)));
        for (int k = 0; k < 4; k++) spawns.Add((1, data.FindUnit("whirlwind_raider"), c + new NVector2(-6f + 3f * k, 3f)));
        foreach (var s in spawns) sim.Enqueue(Command.SpawnUnit(s.player, s.type, s.at));
        for (int t = 0; t < 8; t++) sim.Tick();
        UnitStore u = w.Units;
        int n = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            ApplyStatus.Invoke(u.Statuses, new object[] { i, slowed, 0.3f, 2400, 0 });
            if (n % 2 == 1) ApplyStatus.Invoke(u.Statuses, new object[] { i, burning, 1f, 2400, 0 });
            n++;
        }
        var camera = match.GetNode<RtsCamera>("RtsCamera");
        camera.EdgePanEnabled = false;
        camera.SetFocus(c.X, c.Y);
        DirAccess.MakeDirRecursiveAbsolute(_out);
        foreach (float zoom in new[] { 20f, 30f, 60f })
        {
            camera.SetZoom(zoom);
            for (int i = 0; i < 6; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            string path = $"{_out}/markers-zoom{zoom:0}.png";
            Error e = GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print($"QaVH2MarkerShot: {n} units with statuses, zoom {camera.Zoom}, saved {path} ({e})");
        }
        SceneExit.Quit(this, 0);
    }
}
