using System;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using NVector2 = System.Numerics.Vector2;

namespace Rts.Game.Tests;

/// <summary>QA for M6-V20 (windowed, not in the scene loop): fixed-frame pictures of Laborers and Crossbowmen for pixel comparisons.</summary>
/// <remarks>
/// <c>&amp; $env:GODOT --path game --resolution 1600x900 res://tests/QaM6V20Shots.tscn -- --out &lt;dir&gt; [--players 4] [--placeholder-art]</c>.
/// The sim is ticked by this scene (the runner and the unit views' own process are off) and the unit views are synced at
/// alpha 1 with no delta, so the picture is the same every run: it can be compared byte for byte across builds (the M6-V20
/// rule: <c>--placeholder-art</c> draws exactly what the base drew). Uses only APIs that predate M6-V20, so the same file runs
/// on the base. Saves <c>qa-40m.png</c> and <c>qa-60m.png</c>, prints "QA M6-V20 SHOTS DONE" and quits.
/// </remarks>
public partial class QaM6V20Shots : Node
{
    public override async void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(args, "--out");
        string dir = at >= 0 && at + 1 < args.Length ? args[at + 1] : OS.GetUserDataDir();
        int pAt = Array.IndexOf(args, "--players");
        int players = pAt >= 0 && pAt + 1 < args.Length ? int.Parse(args[pAt + 1]) : 2;
        try
        {
            GameData data = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data")).Data!;
            var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
            AddChild(match);
            match.Start(data, LaunchOptions.Parse(new[] { "--seed", "1", "--units", "0", "--no-bases", "--no-fog", "--mute", "--no-hud", "--no-combat", "--players", players.ToString() }));
            var runner = match.GetNode<SimRunner>("SimRunner");
            runner.ProcessMode = ProcessModeEnum.Disabled;
            var views = match.GetNode<UnitViews>("World3D/UnitViews");
            views.ProcessMode = ProcessModeEnum.Disabled;
            var camera = match.GetNode<RtsCamera>("RtsCamera");
            camera.EdgePanEnabled = false;
            Simulation sim = runner.Simulation!;
            World w = sim.World;
            int laborer = data.FindUnit("malazan_laborer"), xbow = data.FindUnit("malazan_crossbowman"), heavy = data.FindUnit("malazan_heavy_infantry");
            NVector2 c;
            int center = Rts.Sim.Pathfinding.FlowField.NearestPassable(w.NavGrid, w.NavGrid.Height / 2 * w.NavGrid.Width + w.NavGrid.Width / 2);
            c = w.NavGrid.CellCenter(center % w.NavGrid.Width, center / w.NavGrid.Width);
            for (int p = 0; p < players; p++)
            {
                for (int k = 0; k < 6; k++)
                {
                    sim.Enqueue(Command.SpawnUnit(p, laborer, c + new NVector2(-9f + k * 1.4f, -6f + p * 3f)));
                    sim.Enqueue(Command.SpawnUnit(p, xbow, c + new NVector2(1f + k * 1.4f, -6f + p * 3f)));
                }
                sim.Enqueue(Command.SpawnUnit(p, heavy, c + new NVector2(10f, -6f + p * 3f)));
            }
            for (int i = 0; i < 4; i++) sim.Tick();
            views.Sync(w, 1f);
            camera.SetFocus(c.X, c.Y - 2f);
            foreach (float zoom in new[] { 40f, 60f })
            {
                camera.SetZoom(zoom);
                views.Sync(w, 1f);
                for (int i = 0; i < 4; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                string path = System.IO.Path.Combine(dir, $"qa-{zoom:0}m.png");
                GetViewport().GetTexture().GetImage().SavePng(path);
                GD.Print($"saved {path}");
            }
        }
        catch (Exception ex)
        {
            GD.Print($"QA M6-V20 SHOTS FAIL: {ex}");
        }
        GD.Print("QA M6-V20 SHOTS DONE");
        GetTree().Quit();
    }
}
