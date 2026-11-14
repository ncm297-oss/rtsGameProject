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

/// <summary>M6-V20's windowed pictures and cost run for the unit models (not in the scene loop: it needs a window).</summary>
/// <remarks>
/// Pictures: <c>&amp; $env:GODOT --path game --resolution 1600x900 res://tests/ModelShots.tscn -- --look &lt;dir&gt;</c> saves
/// <c>models-40m.png</c>, <c>models-60m.png</c> and <c>models-40m-gray.png</c>: ten Laborers and ten Crossbowmen of player 1
/// (front row) and of player 2 (second row) beside a row of Heavy Infantry capsules, on a real match's ground and light.
/// Cost: <c>... -- --bench 30 [--vsync off]</c> stages 200 units a side (Laborers and Crossbowmen alternating), sends each
/// side at the other (moving, then fighting) under the default zoom, and prints the frame times (average, 99th percentile,
/// the worst 1 % averaged), draw calls, and nodes and materials per unit; with <c>--placeholder-art</c> the same run on
/// capsules. Prints "MODEL SHOTS DONE" and quits.
/// </remarks>
public partial class ModelShots : Node
{
    private Match _match = null!;
    private Simulation _sim = null!;
    private RtsCamera _camera = null!;
    private UnitViews _views = null!;

    public override async void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        string? look = Arg(args, "--look");
        string? bench = Arg(args, "--bench");
        string? skirmish = Arg(args, "--skirmish");
        try
        {
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            GameData data = loaded.Data!;
            _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
            AddChild(_match);
            string vsync = Arg(args, "--vsync") ?? "on";
            // The pictures stand still (no combat); the cost run fights.
            var flags = new System.Collections.Generic.List<string> { "--seed", "1", "--units", "0", "--no-bases", "--no-fog", "--mute", "--no-hud", "--vsync", vsync };
            if (look != null) flags.Add("--no-combat");
            // The real skirmish start: a Town Hall and the starting workers a side, the fog, an AI seat; Malazan for slot 1.
            if (skirmish != null) flags = new System.Collections.Generic.List<string> { "--skirmish", "--seed", "1", "--faction", "1", "malazan", "--mute" };
            _match.Start(data, LaunchOptions.Parse(flags.ToArray()));
            _sim = _match.GetNode<SimRunner>("SimRunner").Simulation!;
            _camera = _match.GetNode<RtsCamera>("RtsCamera");
            _camera.EdgePanEnabled = false;
            _views = _match.GetNode<UnitViews>("World3D/UnitViews");
            while (_sim.TickNumber < 3) await Frames(1);
            if (look != null) await Look(data, look);
            if (bench != null) await Bench(data, double.Parse(bench, CultureInfo.InvariantCulture));
            if (skirmish != null) await Skirmish(data, skirmish);
        }
        catch (Exception ex)
        {
            GD.Print($"MODEL SHOTS FAIL: {ex}");
        }
        GD.Print("MODEL SHOTS DONE");
        GetTree().Quit();
    }

    private async Task Look(GameData data, string dir)
    {
        int laborer = data.FindUnit("malazan_laborer"), xbow = data.FindUnit("malazan_crossbowman"), heavy = data.FindUnit("malazan_heavy_infantry");
        NVector2 c = Centre() + new NVector2(0f, -4f);
        for (int p = 0; p < 2; p++)
        {
            for (int k = 0; k < 10; k++)
            {
                Spawn(p, laborer, c + new NVector2(-16f + k * 1.4f, p * 4f));
                Spawn(p, xbow, c + new NVector2(1f + k * 1.4f, p * 4f));
            }
            for (int k = 0; k < 5; k++) Spawn(p, heavy, c + new NVector2(-16f + k * 2f + p * 10f, 8f));
        }
        long at = _sim.TickNumber;
        while (_sim.TickNumber < at + 4) await Frames(1);
        // Everyone turns to face the camera (+y is down the screen) with a step toward it, then stands.
        for (int i = 0; i < _sim.World.Units.Capacity; i++)
            if (_sim.World.Units.Alive[i]) _sim.Enqueue(Command.Move(_sim.World.Units.Owner[i], new EntityHandle(i, _sim.World.Units.Generation[i]), _sim.World.Units.Position[i] + new NVector2(0f, 0.6f)));
        at = _sim.TickNumber;
        while (_sim.TickNumber < at + 40) await Frames(1);
        _camera.SetFocus(c.X, c.Y + 4f);
        foreach (float zoom in new[] { CameraLimits.DefaultZoom, CameraLimits.MaxZoom, CameraLimits.MinZoom })
        {
            _camera.SetZoom(zoom);
            await Frames(10);
            Image img = GetViewport().GetTexture().GetImage();
            string name = $"models-{zoom:0}m";
            img.SavePng(System.IO.Path.Combine(dir, name + ".png"));
            GD.Print($"saved {name}.png ({img.GetWidth()} x {img.GetHeight()})");
            if (zoom != CameraLimits.DefaultZoom) continue;
            for (int y = 0; y < img.GetHeight(); y++)
                for (int x = 0; x < img.GetWidth(); x++)
                {
                    Color px = img.GetPixel(x, y);
                    float l = 0.2126f * px.R + 0.7152f * px.G + 0.0722f * px.B;
                    img.SetPixel(x, y, new Color(l, l, l));
                }
            img.SavePng(System.IO.Path.Combine(dir, name + "-gray.png"));
        }
    }

    private async Task Bench(GameData data, double seconds)
    {
        int[] types = { data.FindUnit("malazan_laborer"), data.FindUnit("malazan_crossbowman") };
        NVector2 c = Centre();
        const int perSide = 200, columns = 20;
        for (int p = 0; p < 2; p++)
            for (int k = 0; k < perSide; k++)
            {
                float side = p == 0 ? -1f : 1f;
                Spawn(p, types[k % 2], c + new NVector2(side * (14f + (k / columns) * 1.3f), (k % columns - columns / 2) * 1.3f));
            }
        long at = _sim.TickNumber;
        while (_sim.TickNumber < at + 4) await Frames(1);
        UnitStore u = _sim.World.Units;
        int alive = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            alive++;
            _sim.Enqueue(Command.AttackMove(u.Owner[i], new EntityHandle(i, u.Generation[i]), c + new NVector2(u.Owner[i] == 0 ? 30f : -30f, 0f)));
        }
        _camera.SetZoom(CameraLimits.DefaultZoom);
        _camera.SetFocus(c.X, c.Y);
        await Frames(30);
        // Cost split (measure only): --no-anim stops every model's AnimationPlayer (skinning of a still pose stays), and
        // --no-model-shadows takes the models out of the shadow pass.
        string[] args = OS.GetCmdlineUserArgs();
        bool noAnim = Array.IndexOf(args, "--no-anim") >= 0, noShadows = Array.IndexOf(args, "--no-model-shadows") >= 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (noAnim && _views.AnimatorOf(i) is AnimationPlayer a) a.Active = false;
            if (noShadows && _views.ModelMeshOf(i) is MeshInstance3D m) m.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
        if (noAnim || noShadows) GD.Print($"bench: split{(noAnim ? " --no-anim" : "")}{(noShadows ? " --no-model-shadows" : "")}");
        var stats = new FrameTimeStats();
        ulong last = Time.GetTicksUsec(), end = last + (ulong)(seconds * 1e6);
        long drawCalls = 0, objects = 0, frames = 0;
        int attacking = 0;
        while (Time.GetTicksUsec() < end)
        {
            await Frames(1);
            ulong now = Time.GetTicksUsec();
            stats.Add((now - last) / 1000.0);
            last = now;
            drawCalls += (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
            objects += (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame);
            frames++;
            for (int i = 0; i < u.Capacity && attacking == 0; i++) if (u.Alive[i] && u.State[i] == UnitState.Attacking) attacking = (int)frames;
        }
        // The worst 1 %: the mean of the slowest frames, from the histogram's percentile walk.
        double worst1 = 0;
        for (int k = 0; k < 10; k++) worst1 += stats.Percentile(0.99 + k * 0.001);
        worst1 /= 10;
        int model = -1, capsule = -1;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || _views.ViewOf(i) == null) continue;
            if (_views.ModelOf(i) != null && model < 0) model = i;
            if (_views.ModelOf(i) == null && capsule < 0) capsule = i;
        }
        int still = 0;
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i]) still++;
        GD.Print($"bench: models {(UnitViews.UseModels ? "on" : "off (--placeholder-art)")}, {alive} units ({still} alive at the end), {frames} frames in {seconds} s, first attack at frame {attacking}");
        GD.Print($"bench: frame ms average {stats.Average:F2} ({1000.0 / Math.Max(stats.Average, 1e-3):F0} FPS), p99 {stats.Percentile(0.99):F2}, worst 1% {worst1:F2}, worst {stats.Worst:F2}");
        GD.Print($"bench: draw calls / frame {drawCalls / Math.Max(frames, 1)}, objects / frame {objects / Math.Max(frames, 1)}");
        if (model >= 0) GD.Print($"bench: a model unit has {Nodes(_views.ViewOf(model)!)} nodes, {_views.ModelMeshOf(model)!.Mesh.GetSurfaceCount()} surfaces (materials), skeleton bones 65");
        if (capsule >= 0) GD.Print($"bench: a capsule unit has {Nodes(_views.ViewOf(capsule)!)} nodes, 1 surface");
        GD.Print($"bench: unit views {_views.NodeCount} slot nodes, {_views.ModelNodeCount} model instances, team materials {_views.Models?.TeamMaterialCount ?? 0}");
    }

    // The local player's starting workers sent to work as a player would: the first half to the nearest gold mine, the
    // rest to the nearest tree; then one picture of the base under the match camera, gathering under way.
    private async Task Skirmish(GameData data, string dir)
    {
        World w = _sim.World;
        UnitStore u = w.Units;
        ResourceStore r = w.Resources;
        int player = SelectionController.LocalPlayer, workers = 0, total = 0;
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.Owner[i] == player) total++;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.Owner[i] != player) continue;
            ResourceKind want = workers++ < (total + 1) / 2 ? ResourceKind.Gold : ResourceKind.Wood;
            NVector2 best = default;
            float bestD = float.MaxValue;
            for (int k = 0; k < r.Capacity; k++)
            {
                if (!r.Alive[k] || data.Resources[r.TypeId[k]].Resource != want) continue;
                var at = new NVector2(r.Cell[k] % w.Heightmap.Width + 0.5f, r.Cell[k] / w.Heightmap.Width + 0.5f) * Rts.Sim.Map.MapConstants.CellSize;
                float d = NVector2.DistanceSquared(at, u.Position[i]);
                if (d < bestD) (bestD, best) = (d, at);
            }
            if (bestD < float.MaxValue) _sim.Enqueue(Command.Gather(player, new EntityHandle(i, u.Generation[i]), best));
        }
        long start = _sim.TickNumber;
        while (_sim.TickNumber < start + 20 * 20) await Frames(1);
        var states = new int[8];
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.Owner[i] == player) states[(int)u.State[i]]++;
        GD.Print($"skirmish: {workers} workers sent; states idle {states[0]} moving {states[1]} gathering {states[2]} returning {states[3]}");
        Image img = GetViewport().GetTexture().GetImage();
        img.SavePng(System.IO.Path.Combine(dir, "models-skirmish.png"));
        GD.Print("saved models-skirmish.png");
    }

    private static int Nodes(Node n)
    {
        int count = 1;
        foreach (Node child in n.GetChildren()) count += Nodes(child);
        return count;
    }

    // The open, level ground nearest the map's centre: a 24 x 16 cell box (48 x 32 m) of one level, no ramp, all passable.
    private NVector2 Centre()
    {
        World w = _sim.World;
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

    private void Spawn(int player, int type, NVector2 at) => _sim.Enqueue(Command.SpawnUnit(player, type, at));

    private static string? Arg(string[] args, string flag)
    {
        int i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
