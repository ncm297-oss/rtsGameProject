using System;
using System.Globalization;
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
/// QA M6-V24 (windowed, not in the scene loop): before / after pictures of every model type in four team colours, a dark
/// and a very light one among them, so a base build and the merged-surface build can be diffed pixel by pixel.
/// </summary>
/// <remarks>
/// <c>&amp; $env:GODOT --path game --resolution 1600x900 res://tests/QaM6V24Shot.tscn -- --out &lt;dir&gt;</c>. Per model type
/// (every type whose model file exists), four figures of player 1 in a row, each drawn with a copy of its type's team
/// material whose <c>team_colour</c> is one of <see cref="Colours"/> (red, near-black navy, green, near-white), held at idle's
/// first frame: <c>q-&lt;id&gt;-front.png</c> / <c>-back.png</c> (close camera), <c>-close&lt;n&gt;.png</c> (each figure from
/// 2.4 m: seams and bleeding), <c>-20m</c> / <c>-40m</c> / <c>-60m.png</c> (the match camera) and <c>-far.png</c> (a 120 m
/// camera: the textures' smallest mip levels a figure reads). The scene only draws; it works on any build that has
/// <c>UnitModels.TeamMaterial</c> / <c>TeamSurface</c> (the base, 95186a7, too). Prints "QA M6V24 SHOT DONE".
/// </remarks>
public partial class QaM6V24Shot : Node
{
    private static readonly uint[] Colours = { 0xC03030, 0x1A1A33, 0x30A040, 0xF4F4EE };

    private Match _match = null!;
    private Simulation _sim = null!;
    private RtsCamera _camera = null!;
    private UnitViews _views = null!;

    public override async void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(args, "--out");
        string dir = at >= 0 && at + 1 < args.Length ? args[at + 1] : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "qa-m6v24-shot");
        int onlyAt = Array.IndexOf(args, "--only");
        string? only = onlyAt >= 0 && onlyAt + 1 < args.Length ? args[onlyAt + 1] : null;
        try
        {
            System.IO.Directory.CreateDirectory(dir);
            GameData data = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data")).Data!;
            _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
            AddChild(_match);
            _match.Start(data, LaunchOptions.Parse(new[] { "--seed", "1", "--units", "0", "--no-bases", "--no-fog", "--mute", "--no-hud", "--no-combat" }));
            _sim = _match.GetNode<SimRunner>("SimRunner").Simulation!;
            _camera = _match.GetNode<RtsCamera>("RtsCamera");
            _camera.EdgePanEnabled = false;
            _views = _match.GetNode<UnitViews>("World3D/UnitViews");
            while (_sim.TickNumber < 3) await Frames(1);
            await Shots(data, dir, only);
        }
        catch (Exception ex)
        {
            GD.Print($"QA M6V24 SHOT exception: {ex}");
        }
        GD.Print("QA M6V24 SHOT DONE");
        GetTree().Quit();
    }

    private async Task Shots(GameData data, string dir, string? only)
    {
        Camera3D? match = GetViewport().GetCamera3D();
        var close = new Camera3D { Fov = 40f };
        AddChild(close);
        NVector2 c = Centre();
        UnitStore u = _sim.World.Units;
        for (int type = 0; type < data.Units.Length; type++)
        {
            if (!ResourceLoader.Exists(UnitModels.PathFor(data.Units[type].Model))) continue;
            string id = data.Units[type].Key;
            if (only != null && id != only) continue;
            float k = Math.Max(1f, data.Units[type].Scale);
            for (int i = 0; i < u.Capacity; i++) if (u.Alive[i]) u.Free(new EntityHandle(i, u.Generation[i]));
            for (int f = 0; f < Colours.Length; f++) _sim.Enqueue(Command.SpawnUnit(0, type, c + new NVector2((f - 1.5f) * 1.9f * k, -3f)));
            long t0 = _sim.TickNumber;
            while (_sim.TickNumber < t0 + 4) await Frames(1);
            for (int i = 0; i < u.Capacity; i++)
                if (u.Alive[i]) _sim.Enqueue(Command.Move(0, new EntityHandle(i, u.Generation[i]), u.Position[i] + new NVector2(0f, 3f)));
            t0 = _sim.TickNumber;
            while (_sim.TickNumber < t0 + 50) await Frames(1);
            // The figures left to right (by x), each with its own colour.
            var slots = new System.Collections.Generic.List<int>();
            for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.TypeId[i] == type) slots.Add(i);
            slots.Sort((a, b) => u.Position[a].X.CompareTo(u.Position[b].X));
            if (slots.Count != Colours.Length || _views.Models == null) { GD.Print($"{id}: {slots.Count} figures, skipped"); continue; }
            int surface = _views.Models.TeamSurface(type);
            for (int f = 0; f < slots.Count; f++)
            {
                int s = slots[f];
                var mat = (ShaderMaterial)_views.Models.TeamMaterial(type, 0)!.Duplicate();
                mat.SetShaderParameter("team_colour", UnitViews.ColorFromRgb(Colours[f]));
                _views.ModelMeshOf(s)!.SetSurfaceOverrideMaterial(surface, mat);
                AnimationPlayer anim = _views.AnimatorOf(s)!;
                anim.Active = true;
                anim.Play("idle", 0);
                anim.Seek(0.0, true);
                anim.Active = false;
            }
            Vector3 first = _views.ViewOf(slots[0])!.GlobalPosition, last = _views.ViewOf(slots[^1])!.GlobalPosition;
            Vector3 mid = (first + last) / 2f;
            Vector3 ground = new(mid.X, UnitViews.GroundPoint(_sim.World, slots[0], 1f).Y, mid.Z);
            foreach ((string side, float z) in new[] { ("front", 1f), ("back", -1f) })
            {
                close.LookAtFromPosition(ground + new Vector3(0f, 2.2f * k, 9f * k * z), ground + new Vector3(0f, 1.0f * k, 0f), Vector3.Up);
                close.MakeCurrent();
                await Frames(6);
                Save(dir, $"q-{id}-{side}.png");
            }
            for (int f = 0; f < slots.Count; f++)
            {
                Vector3 p = _views.ViewOf(slots[f])!.GlobalPosition;
                Vector3 g = new(p.X, ground.Y, p.Z);
                close.LookAtFromPosition(g + new Vector3(0.8f * k, 1.6f * k, 2.4f * k), g + new Vector3(0f, 1.0f * k, 0f), Vector3.Up);
                await Frames(6);
                Save(dir, $"q-{id}-close{f}.png");
            }
            close.LookAtFromPosition(ground + new Vector3(0f, 85f, 85f), ground + new Vector3(0f, 1.0f * k, 0f), Vector3.Up);
            await Frames(6);
            Save(dir, $"q-{id}-far.png");
            match?.MakeCurrent();
            _camera.SetFocus(mid.X, mid.Z);
            foreach (float zoom in new[] { CameraLimits.MinZoom, CameraLimits.DefaultZoom, CameraLimits.MaxZoom })
            {
                _camera.SetZoom(zoom);
                await Frames(6);
                Save(dir, $"q-{id}-{zoom.ToString("0", CultureInfo.InvariantCulture)}m.png");
            }
        }
        match?.MakeCurrent();
        close.QueueFree();
    }

    // A flat, passable patch (as ModelShots.Centre).
    private NVector2 Centre()
    {
        World w = _sim.World;
        Rts.Sim.Map.Heightmap h = w.Heightmap;
        const int bw = 24, bh = 16;
        for (int y0 = 0; y0 + bh < h.Height; y0 += 2)
            for (int x0 = 0; x0 + bw < h.Width; x0 += 2)
            {
                int level = h.LevelAt(x0, y0);
                bool ok = true;
                for (int y = y0; y < y0 + bh && ok; y++)
                    for (int x = x0; x < x0 + bw && ok; x++)
                        ok = h.LevelAt(x, y) == level && !h.IsRamp(x, y) && w.NavGrid.IsPassable(x, y);
                if (ok) return new NVector2(x0 + bw / 2f, y0 + bh / 2f) * Rts.Sim.Map.MapConstants.CellSize;
            }
        return new NVector2(h.Width, h.Height) * (Rts.Sim.Map.MapConstants.CellSize / 2f);
    }

    private void Save(string dir, string name) => GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, name));

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
