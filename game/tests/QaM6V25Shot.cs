using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using NVector2 = System.Numerics.Vector2;

namespace Rts.Game.Tests;

/// <summary>
/// QA M6-V25 pictures (windowed, not in the scene loop): the four new figures beside a reference figure of their faction, for
/// four players in the setup's Crimson, Cobalt, Jade and Bone (ui.json colours 3, 4, 5, 8), each hurt to half hp so its bar
/// shows over its head; at the default zoom and the closest, plus a grey copy of the default.
/// </summary>
/// <remarks>
/// <c>&amp; $env:GODOT --path game --resolution 1600x900 res://tests/QaM6V25Shot.tscn -- --shots &lt;dir&gt;</c>. Rows top to bottom:
/// players 1-4; columns: Cadre Mage, Sapper, Heavy Infantry, Priest, Zealot, Raider. Prints "QA M6V25 SHOT DONE".
/// </remarks>
public partial class QaM6V25Shot : Node
{
    private static readonly string[] Types =
    {
        "malazan_cadre_mage", "malazan_sapper", "malazan_heavy_infantry", "whirlwind_priest", "whirlwind_zealot", "whirlwind_raider",
    };

    public override async void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(args, "--shots");
        string dir = at >= 0 && at + 1 < args.Length ? args[at + 1] : System.IO.Path.GetTempPath();
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            GameData data = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data")).Data!;
            var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
            AddChild(match);
            match.Start(data, LaunchOptions.Parse(new[]
            {
                "--seed", "1", "--units", "0", "--no-bases", "--no-fog", "--mute", "--no-hud", "--no-combat", "--players", "4",
                "--colour", "1", "3", "--colour", "2", "4", "--colour", "3", "5", "--colour", "4", "8",
            }));
            Simulation sim = match.GetNode<SimRunner>("SimRunner").Simulation!;
            var camera = match.GetNode<RtsCamera>("RtsCamera");
            camera.EdgePanEnabled = false;
            while (sim.TickNumber < 3) await Frames(1);
            GD.Print($"player colours: {string.Join(' ', Array.ConvertAll(match.PlayerRgb, c => c.ToString("X6")))}");
            World w = sim.World;
            NVector2 c = Centre(w);
            for (int p = 0; p < 4; p++)
                for (int k = 0; k < Types.Length; k++)
                    sim.Enqueue(Command.SpawnUnit(p, data.FindUnit(Types[k]), c + new NVector2((k - (Types.Length - 1) / 2f) * 2.4f, -8f + p * 3.2f)));
            long t0 = sim.TickNumber;
            while (sim.TickNumber < t0 + 4) await Frames(1);
            UnitStore u = w.Units;
            for (int i = 0; i < u.Capacity; i++)
                if (u.Alive[i]) sim.Enqueue(Command.Move(u.Owner[i], new EntityHandle(i, u.Generation[i]), u.Position[i] + new NVector2(0f, 3f)));
            t0 = sim.TickNumber;
            while (sim.TickNumber < t0 + 50) await Frames(1);
            // Test seam: half hp, so every bar shows (the view reads hp only).
            // Above half, so the Zealots' below-half Frenzy doesn't add its mark; the hit flash this sets off fades first.
            for (int i = 0; i < u.Capacity; i++) if (u.Alive[i]) u.Hp[i] = Math.Max(1, u.Hp[i] * 3 / 5);
            await Frames(120);
            camera.MakeCurrent();
            camera.SetFocus(c.X, c.Y - 1.5f);
            foreach (float zoom in new[] { CameraLimits.DefaultZoom, CameraLimits.MinZoom })
            {
                camera.SetZoom(zoom);
                await Frames(12);
                Image img = GetViewport().GetTexture().GetImage();
                img.SavePng(System.IO.Path.Combine(dir, $"qa-m6v25-colours-{zoom:0}m.png"));
                GD.Print($"saved qa-m6v25-colours-{zoom:0}m.png");
                if (zoom != CameraLimits.DefaultZoom) continue;
                for (int y = 0; y < img.GetHeight(); y++)
                    for (int x = 0; x < img.GetWidth(); x++)
                    {
                        Color px = img.GetPixel(x, y);
                        float l = 0.2126f * px.R + 0.7152f * px.G + 0.0722f * px.B;
                        img.SetPixel(x, y, new Color(l, l, l));
                    }
                img.SavePng(System.IO.Path.Combine(dir, "qa-m6v25-colours-40m-gray.png"));
            }
        }
        catch (Exception ex)
        {
            GD.Print($"QA M6V25 SHOT FAIL: {ex}");
        }
        GD.Print("QA M6V25 SHOT DONE");
        GetTree().Quit();
    }

    // The open, level ground nearest the map's centre (ModelShots' rule): a 24 x 16 cell box of one level, no ramp, passable.
    private static NVector2 Centre(World w)
    {
        Rts.Sim.Map.Heightmap h = w.Heightmap;
        const int bw = 24, bh = 16;
        int best = int.MaxValue;
        NVector2 found = new NVector2(h.Width, h.Height) * (Rts.Sim.Map.MapConstants.CellSize / 2f);
        for (int y0 = 0; y0 + bh < h.Height; y0 += 2)
            for (int x0 = 0; x0 + bw < h.Width; x0 += 2)
            {
                int level = h.LevelAt(x0, y0);
                bool ok = true;
                for (int y = y0; y < y0 + bh && ok; y++)
                    for (int x = x0; x < x0 + bw && ok; x++)
                        ok = h.LevelAt(x, y) == level && !h.IsRamp(x, y) && w.NavGrid.IsPassable(x, y);
                int d = Math.Abs(x0 + bw / 2 - h.Width / 2) + Math.Abs(y0 + bh / 2 - h.Height / 2);
                if (!ok || d >= best) continue;
                best = d;
                found = new NVector2(x0 + bw / 2f, y0 + bh / 2f) * Rts.Sim.Map.MapConstants.CellSize;
            }
        return found;
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
