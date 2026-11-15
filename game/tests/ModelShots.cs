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
/// (front row) and of player 2 (second row) beside a row of Heavy Infantry (capsules until M6-V21, models since), on a real match's ground and light.
/// Cost: <c>... -- --bench 30 [--vsync off]</c> stages 200 units a side (Laborers and Crossbowmen alternating), sends each
/// side at the other (moving, then fighting) under the default zoom, and prints the frame times (average, 99th percentile,
/// the worst 1 % averaged), draw calls, and nodes and materials per unit; with <c>--placeholder-art</c> the same run on
/// capsules; <c>--per-side N</c> stages N a side (M6-VH7). <c>--skirmish &lt;dir&gt;</c> also saves <c>models-gather-20m.png</c> /
/// <c>-40m.png</c> on a gatherer; <c>--crowd-look &lt;dir&gt;</c> saves <c>models-crowd-20m.png</c> / <c>-40m.png</c>, two
/// groups walking at each other until they fight (M6-VH7: the crowd animation's look). Prints "MODEL SHOTS DONE" and quits.
/// M6-V21: the bench mixes evenly over every type that has a model file (with <c>--placeholder-art</c> the same types as
/// capsules); <c>--types &lt;dir&gt;</c> saves, per model type, <c>type-&lt;id&gt;-40m.png</c> and <c>-20m.png</c>: the type in
/// players 1 and 2's colours beside a Laborer and a capsule type of its faction, facing the camera; <c>--lines &lt;dir&gt;</c>
/// saves <c>lines-40m.png</c> / <c>-20m.png</c>: a Malazan line (Heavy Infantry, Crossbowmen behind) fighting a Whirlwind
/// line (Raiders, Desert Archers behind), taken once the first melee has started.
/// M7-V5: <c>--teblor &lt;dir&gt;</c> (close-ups of each Teblor model type in its clips, the five in a row beside a Heavy
/// Infantry and a Laborer in two colours), <c>--teblor &lt;dir&gt; --teblor-fight</c> (Teblor against Malazan), and
/// <c>--skirmish-faction &lt;id&gt;</c> for <c>--skirmish</c> (a Teblor base gathering); <c>--bench-without &lt;faction id&gt;</c>
/// leaves that faction's model types out of the bench.
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
        string? crowd = Arg(args, "--crowd-look");
        string? typeShots = Arg(args, "--types");
        string? lines = Arg(args, "--lines");
        string? teblor = Arg(args, "--teblor");
        try
        {
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            GameData data = loaded.Data!;
            _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
            AddChild(_match);
            string vsync = Arg(args, "--vsync") ?? "on";
            // The pictures stand still (no combat); the cost run fights.
            var flags = new System.Collections.Generic.List<string> { "--seed", "1", "--units", "0", "--no-bases", "--no-fog", "--mute", "--no-hud", "--vsync", vsync };
            if (look != null || typeShots != null || (teblor != null && Array.IndexOf(args, "--teblor-fight") < 0)) flags.Add("--no-combat");
            // The real skirmish start: a Town Hall and the starting workers a side, the fog, an AI seat; Malazan for slot 1
            // unless --skirmish-faction names another (M7-V5: teblor).
            if (skirmish != null) flags = new System.Collections.Generic.List<string> { "--skirmish", "--seed", "1", "--faction", "1", Arg(args, "--skirmish-faction") ?? "malazan", "--mute" };
            _match.Start(data, LaunchOptions.Parse(flags.ToArray()));
            _sim = _match.GetNode<SimRunner>("SimRunner").Simulation!;
            _camera = _match.GetNode<RtsCamera>("RtsCamera");
            _camera.EdgePanEnabled = false;
            _views = _match.GetNode<UnitViews>("World3D/UnitViews");
            while (_sim.TickNumber < 3) await Frames(1);
            if (look != null) await Look(data, look);
            if (bench != null) await Bench(data, double.Parse(bench, CultureInfo.InvariantCulture));
            if (skirmish != null) await Skirmish(data, skirmish);
            if (crowd != null) await CrowdLook(data, crowd);
            if (typeShots != null) await TypeShots(data, typeShots);
            if (lines != null) await Lines(data, lines);
            if (teblor != null) await TeblorShots(data, teblor);
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
        // M6-V21: mixed evenly over every type with a model file (the same types as capsules with --placeholder-art).
        int[] types = ModelFileTypes(data);
        // M7-V5: --bench-without <faction id> leaves that faction's model types out (M6-V21's six types against all eleven
        // in one window: the base against the head).
        string? without = Arg(OS.GetCmdlineUserArgs(), "--bench-without");
        if (without != null) types = Array.FindAll(types, t => data.Factions[data.Units[t].Faction].Key != without);
        GD.Print($"bench: types {string.Join(' ', Array.ConvertAll(types, t => data.Units[t].Key))}");
        NVector2 c = Centre();
        // --per-side N (M6-VH7): 500 gives BUG-1321's 1,000-unit run.
        int perSide = int.Parse(Arg(OS.GetCmdlineUserArgs(), "--per-side") ?? "200", CultureInfo.InvariantCulture);
        const int columns = 20;
        for (int p = 0; p < 2; p++)
            for (int k = 0; k < perSide; k++)
            {
                float side = p == 0 ? -1f : 1f;
                Spawn(p, types[(k + p * (types.Length / 2)) % types.Length], c + new NVector2(side * (14f + (k / columns) * 1.3f), (k % columns - columns / 2) * 1.3f));
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
        // M6-VH7: the whole run's average falls as the units die (two thirds by the end); the full-crowd figure is the
        // frames while at least 90 % of them live.
        var full = new FrameTimeStats();
        ulong last = Time.GetTicksUsec(), end = last + (ulong)(seconds * 1e6);
        long drawCalls = 0, objects = 0, frames = 0;
        int attacking = 0;
        while (Time.GetTicksUsec() < end)
        {
            await Frames(1);
            ulong now = Time.GetTicksUsec();
            stats.Add((now - last) / 1000.0);
            if (u.Count * 10 >= alive * 9) full.Add((now - last) / 1000.0);
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
        double fullWorst1 = 0;
        for (int k = 0; k < 10; k++) fullWorst1 += full.Percentile(0.99 + k * 0.001);
        GD.Print($"bench: while >= 90% alive ({full.Count} frames): frame ms average {full.Average:F2} ({1000.0 / Math.Max(full.Average, 1e-3):F0} FPS), worst 1% {fullWorst1 / 10:F2}");
        GD.Print($"bench: draw calls / frame {drawCalls / Math.Max(frames, 1)}, objects / frame {objects / Math.Max(frames, 1)}");
        if (model >= 0) GD.Print($"bench: a model unit has {Nodes(_views.ViewOf(model)!)} nodes, {_views.ModelMeshOf(model)!.Mesh.GetSurfaceCount()} surfaces (materials), skeleton bones 65");
        if (capsule >= 0) GD.Print($"bench: a capsule unit has {Nodes(_views.ViewOf(capsule)!)} nodes, 1 surface");
        GD.Print($"bench: crowd animation (M6-VH7) last frame: {_views.Crowd.AdvancedLastFrame} of {_views.Crowd.EligibleLastFrame} shown players advanced ({CrowdAnimator.FullRateCount} nearest every frame, the rest 1 frame in {CrowdAnimator.StaggerFrames})");
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
        // M6-VH7: the gatherers close up, at 20 m and 40 m, under the crowd animation schedule.
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.Owner[i] != player || u.State[i] != UnitState.Gathering) continue;
            _camera.SetFocus(u.Position[i].X, u.Position[i].Y);
            break;
        }
        await Shots(dir, "models-gather");
    }

    // M6-VH7: walking and attacking figures at 20 m and 40 m (the crowd animation's look): 20 Laborers and 20 Crossbowmen a
    // side (80: more than the 48 animated every frame), 26 m apart, attack-moved at each other; the pictures once the first fights have started.
    private async Task CrowdLook(GameData data, string dir)
    {
        int[] types = { data.FindUnit("malazan_laborer"), data.FindUnit("malazan_crossbowman") };
        NVector2 c = Centre();
        for (int p = 0; p < 2; p++)
            for (int k = 0; k < 40; k++)
                Spawn(p, types[k % 2], c + new NVector2((p == 0 ? -1f : 1f) * (13f + (k / 8) * 1.4f), (k % 8 - 4) * 1.4f));
        long at = _sim.TickNumber;
        while (_sim.TickNumber < at + 4) await Frames(1);
        UnitStore u = _sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i]) _sim.Enqueue(Command.AttackMove(u.Owner[i], new EntityHandle(i, u.Generation[i]), c + new NVector2(u.Owner[i] == 0 ? 20f : -20f, 0f)));
        int attacking = 0;
        for (int t = 0; t < 20 * 20 && attacking < 4; t++)
        {
            long now = _sim.TickNumber;
            while (_sim.TickNumber == now) await Frames(1);
            attacking = 0;
            for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.State[i] == UnitState.Attacking) attacking++;
        }
        var states = new int[8];
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i]) states[(int)u.State[i]]++;
        GD.Print($"crowd look: moving {states[(int)UnitState.Moving]} attacking {states[(int)UnitState.Attacking]} idle {states[(int)UnitState.Idle]}");
        _camera.SetFocus(c.X, c.Y);
        await Shots(dir, "models-crowd");
    }

    // M6-V21: the types whose model file exists, in type order (independent of --placeholder-art).
    private static int[] ModelFileTypes(GameData data)
    {
        var list = new System.Collections.Generic.List<int>();
        for (int t = 0; t < data.Units.Length; t++) if (ResourceLoader.Exists(UnitModels.PathFor(data.Units[t].Model))) list.Add(t);
        return list.ToArray();
    }

    // M6-V21: per model type, a row facing the camera: the type for player 1 and player 2, a Laborer and the first capsule
    // type of the type's faction; pictures at the default zoom (40 m) and the closest (20 m).
    private async Task TypeShots(GameData data, string dir)
    {
        int laborer = data.FindUnit("malazan_laborer");
        UnitStore u = _sim.World.Units;
        foreach (int type in ModelFileTypes(data))
        {
            for (int i = 0; i < u.Capacity; i++) if (u.Alive[i]) u.Free(new EntityHandle(i, u.Generation[i]));
            int capsule = -1;
            for (int t = 0; t < data.Units.Length && capsule < 0; t++)
                if (data.Units[t].Faction == data.Units[type].Faction && !_views.DrawsModel(t)) capsule = t;
            NVector2 c = Centre();
            Spawn(0, type, c + new NVector2(-2.4f, -3f));
            Spawn(1, type, c + new NVector2(-0.8f, -3f));
            Spawn(0, laborer, c + new NVector2(0.8f, -3f));
            if (capsule >= 0) Spawn(0, capsule, c + new NVector2(2.4f, -3f));
            long at = _sim.TickNumber;
            while (_sim.TickNumber < at + 4) await Frames(1);
            // Everyone walks 3 m toward the camera (+y is down the screen), so each ends facing it, then stands.
            for (int i = 0; i < u.Capacity; i++)
                if (u.Alive[i]) _sim.Enqueue(Command.Move(u.Owner[i], new EntityHandle(i, u.Generation[i]), u.Position[i] + new NVector2(0f, 3f)));
            at = _sim.TickNumber;
            while (_sim.TickNumber < at + 40) await Frames(1);
            _camera.SetFocus(c.X, c.Y);
            foreach (float zoom in new[] { CameraLimits.DefaultZoom, CameraLimits.MinZoom })
            {
                _camera.SetZoom(zoom);
                await Frames(10);
                string name = $"type-{data.Units[type].Key}-{zoom:0}m.png";
                GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, name));
                GD.Print($"saved {name}: {data.Units[type].Key} p1 / p2, {data.Units[laborer].Key}, {(capsule >= 0 ? data.Units[capsule].Key : "no capsule")}");
            }
        }
    }

    // M6-V21: a Malazan line fighting a Whirlwind line: 8 Heavy Infantry with 6 Crossbowmen behind (player 1) against 8
    // Raiders with 6 Desert Archers behind (player 2), attack-moved at each other; the pictures once 4 melee units fight.
    private async Task Lines(GameData data, string dir)
    {
        int heavy = data.FindUnit("malazan_heavy_infantry"), xbow = data.FindUnit("malazan_crossbowman");
        int raider = data.FindUnit("whirlwind_raider"), archer = data.FindUnit("whirlwind_desert_archer");
        NVector2 c = Centre();
        for (int k = 0; k < 8; k++)
        {
            Spawn(0, heavy, c + new NVector2(-9f, (k - 3.5f) * 1.3f));
            Spawn(1, raider, c + new NVector2(9f, (k - 3.5f) * 1.3f));
            if (k < 6)
            {
                Spawn(0, xbow, c + new NVector2(-12f, (k - 2.5f) * 1.4f));
                Spawn(1, archer, c + new NVector2(12f, (k - 2.5f) * 1.4f));
            }
        }
        long at = _sim.TickNumber;
        while (_sim.TickNumber < at + 4) await Frames(1);
        UnitStore u = _sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i]) _sim.Enqueue(Command.AttackMove(u.Owner[i], new EntityHandle(i, u.Generation[i]), c + new NVector2(u.Owner[i] == 0 ? 20f : -20f, 0f)));
        int melee = 0;
        for (int t = 0; t < 20 * 30 && melee < 4; t++)
        {
            long now = _sim.TickNumber;
            while (_sim.TickNumber == now) await Frames(1);
            melee = 0;
            for (int i = 0; i < u.Capacity; i++)
                if (u.Alive[i] && u.State[i] == UnitState.Attacking && (u.TypeId[i] == heavy || u.TypeId[i] == raider)) melee++;
        }
        var states = new int[8];
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i]) states[(int)u.State[i]]++;
        GD.Print($"lines: moving {states[(int)UnitState.Moving]} attacking {states[(int)UnitState.Attacking]} idle {states[(int)UnitState.Idle]}, melee {melee}");
        _camera.SetFocus(c.X, c.Y);
        await Shots(dir, "lines");
    }

    // M7-V5: the Teblor figures (the shipped data, rule 56). With --no-combat (the default for this mode's match), per
    // Teblor model type: player 1 and player 2's figure facing a close camera, idle and held in each of its own clips
    // (closeup-<id>-<clip>.png); then the row (row-40m.png / -20m.png and row-40m-gray.png): every Teblor model type, a
    // Heavy Infantry and a Laborer, player 1's in front and player 2's behind, at the default and the closest zoom. With
    // --teblor-fight the match fights instead: Teblor Warriors and Breakers with Javelin Throwers and Shamans behind
    // (player 1) against Heavy Infantry with Crossbowmen behind (player 2), fight-20m.png / -40m.png once 4 melee units
    // fight, and a close camera on the melee (fight-close.png).
    private async Task TeblorShots(GameData data, string dir)
    {
        int teblor = -1;
        for (int f = 0; f < data.Factions.Length; f++) if (data.Factions[f].Key == "teblor") teblor = f;
        var types = new System.Collections.Generic.List<int>();
        foreach (int t in ModelFileTypes(data)) if (data.Units[t].Faction == teblor) types.Add(t);
        GD.Print($"teblor shots: model types {string.Join(' ', types.ConvertAll(t => data.Units[t].Key))}");
        UnitStore u = _sim.World.Units;
        NVector2 c = Centre();
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--teblor-fight") >= 0)
        {
            await TeblorFight(data, dir, types, c);
            return;
        }
        Camera3D? match = GetViewport().GetCamera3D();
        var close = new Camera3D { Fov = 40f };
        AddChild(close);
        foreach (int type in types)
        {
            ClearUnits();
            Spawn(0, type, c + new NVector2(-1.3f, -3f));
            Spawn(1, type, c + new NVector2(1.3f, -3f));
            await FaceCamera();
            int[] slots = { SlotOf(0, type), SlotOf(1, type) };
            Vector3 mid = (_views.ViewOf(slots[0])!.GlobalPosition + _views.ViewOf(slots[1])!.GlobalPosition) / 2f;
            Vector3 ground = new(mid.X, UnitViews.GroundPoint(_sim.World, slots[0], 1f).Y, mid.Z);
            close.LookAtFromPosition(ground + new Vector3(0f, 2.6f, 8.5f), ground + new Vector3(0f, 1.5f, 0f), Vector3.Up);
            close.MakeCurrent();
            string id = data.Units[type].Key;
            await Frames(10);
            Save(dir, $"closeup-{id}-idle.png");
            foreach (string clip in new[] { "walk", "attack", "gather_gold", "gather_wood", "build" })
            {
                if (!_views.Models!.HasClip(type, (UnitModels.Clip)Array.IndexOf(UnitModels.ClipNames, (StringName)clip))) continue;
                if (clip.StartsWith("gather", StringComparison.Ordinal) || clip == "build")
                    if (!data.Units[type].IsWorker) continue;
                foreach (int s in slots)
                {
                    AnimationPlayer anim = _views.AnimatorOf(s)!;
                    anim.Active = true;
                    anim.Play(clip, 0);
                    anim.Seek(anim.CurrentAnimationLength * 0.4, true);
                    anim.Active = false; // held: the crowd schedule advances nothing that is inactive
                }
                await Frames(4);
                Save(dir, $"closeup-{id}-{clip}.png");
            }
            foreach (int s in slots) { _views.AnimatorOf(s)!.Active = true; _views.AnimatorOf(s)!.Play("idle"); }
        }
        match?.MakeCurrent();
        close.QueueFree();

        ClearUnits();
        int heavy = data.FindUnit("malazan_heavy_infantry"), laborer = data.FindUnit("malazan_laborer");
        var row = new System.Collections.Generic.List<int>(types) { heavy, laborer };
        for (int p = 0; p < 2; p++)
            for (int k = 0; k < row.Count; k++)
                Spawn(p, row[k], c + new NVector2((k - (row.Count - 1) / 2f) * 2.2f, -4f + p * 3.5f));
        await FaceCamera();
        _camera.SetFocus(c.X, c.Y - 1f);
        foreach (float zoom in new[] { CameraLimits.DefaultZoom, CameraLimits.MinZoom })
        {
            _camera.SetZoom(zoom);
            await Frames(10);
            Image img = GetViewport().GetTexture().GetImage();
            img.SavePng(System.IO.Path.Combine(dir, $"row-{zoom:0}m.png"));
            GD.Print($"saved row-{zoom:0}m.png: {string.Join(' ', row.ConvertAll(t => data.Units[t].Key))}, player 1 front, player 2 behind");
            if (zoom != CameraLimits.DefaultZoom) continue;
            for (int y = 0; y < img.GetHeight(); y++)
                for (int x = 0; x < img.GetWidth(); x++)
                {
                    Color px = img.GetPixel(x, y);
                    float l = 0.2126f * px.R + 0.7152f * px.G + 0.0722f * px.B;
                    img.SetPixel(x, y, new Color(l, l, l));
                }
            img.SavePng(System.IO.Path.Combine(dir, "row-40m-gray.png"));
        }
    }

    private async Task TeblorFight(GameData data, string dir, System.Collections.Generic.List<int> types, NVector2 c)
    {
        int Find(string id) => data.FindUnit(id);
        int warrior = Find("teblor_warrior"), breaker = Find("teblor_breaker"), javelin = Find("teblor_javelin_thrower"), shaman = Find("teblor_shaman");
        int heavy = Find("malazan_heavy_infantry"), xbow = Find("malazan_crossbowman");
        for (int k = 0; k < 8; k++)
        {
            Spawn(0, k % 4 == 3 ? breaker : warrior, c + new NVector2(-9f, (k - 3.5f) * 1.6f));
            Spawn(1, heavy, c + new NVector2(9f, (k - 3.5f) * 1.3f));
            if (k < 6)
            {
                Spawn(0, k % 3 == 2 ? shaman : javelin, c + new NVector2(-12.5f, (k - 2.5f) * 1.7f));
                Spawn(1, xbow, c + new NVector2(12f, (k - 2.5f) * 1.4f));
            }
        }
        long at = _sim.TickNumber;
        while (_sim.TickNumber < at + 4) await Frames(1);
        UnitStore u = _sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i]) _sim.Enqueue(Command.AttackMove(u.Owner[i], new EntityHandle(i, u.Generation[i]), c + new NVector2(u.Owner[i] == 0 ? 20f : -20f, 0f)));
        int melee = 0;
        for (int t = 0; t < 20 * 30 && melee < 4; t++)
        {
            long now = _sim.TickNumber;
            while (_sim.TickNumber == now) await Frames(1);
            melee = 0;
            for (int i = 0; i < u.Capacity; i++)
                if (u.Alive[i] && u.State[i] == UnitState.Attacking && (u.TypeId[i] == heavy || u.TypeId[i] == warrior || u.TypeId[i] == breaker)) melee++;
        }
        var states = new int[8];
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i]) states[(int)u.State[i]]++;
        GD.Print($"teblor fight: moving {states[(int)UnitState.Moving]} attacking {states[(int)UnitState.Attacking]} idle {states[(int)UnitState.Idle]}, melee {melee}");
        _camera.SetFocus(c.X - 2f, c.Y);
        await Shots(dir, "fight");
        Camera3D? match = GetViewport().GetCamera3D();
        var close = new Camera3D { Fov = 45f };
        AddChild(close);
        Vector3 focus = Vector3.Zero;
        int n = 0;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.State[i] == UnitState.Attacking && _views.ViewOf(i) is MeshInstance3D v && (u.TypeId[i] == warrior || u.TypeId[i] == breaker)) { focus += v.GlobalPosition; n++; }
        if (n > 0)
        {
            focus /= n;
            close.LookAtFromPosition(focus + new Vector3(0f, 6f, 13f), focus, Vector3.Up);
            close.MakeCurrent();
            await Frames(6);
            Save(dir, "fight-close.png");
        }
        match?.MakeCurrent();
        close.QueueFree();
    }

    private void ClearUnits()
    {
        UnitStore u = _sim.World.Units;
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i]) u.Free(new EntityHandle(i, u.Generation[i]));
    }

    // Everyone walks 3 m toward the camera (+y is down the screen), so each ends facing it, then stands.
    private async Task FaceCamera()
    {
        UnitStore u = _sim.World.Units;
        long at = _sim.TickNumber;
        while (_sim.TickNumber < at + 4) await Frames(1);
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i]) _sim.Enqueue(Command.Move(u.Owner[i], new EntityHandle(i, u.Generation[i]), u.Position[i] + new NVector2(0f, 3f)));
        at = _sim.TickNumber;
        while (_sim.TickNumber < at + 50) await Frames(1);
    }

    private int SlotOf(int owner, int type)
    {
        UnitStore u = _sim.World.Units;
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.Owner[i] == owner && u.TypeId[i] == type) return i;
        return -1;
    }

    private void Save(string dir, string name)
    {
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, name));
        GD.Print($"saved {name}");
    }

    // One picture at 20 m and one at 40 m; the sim keeps running between them (the figures move on).
    private async Task Shots(string dir, string stem)
    {
        foreach (float zoom in new[] { CameraLimits.MinZoom, CameraLimits.DefaultZoom })
        {
            _camera.SetZoom(zoom);
            await Frames(8);
            Image img = GetViewport().GetTexture().GetImage();
            string name = $"{stem}-{zoom:0}m.png";
            img.SavePng(System.IO.Path.Combine(dir, name));
            GD.Print($"saved {name}; crowd animation: {_views.Crowd.AdvancedLastFrame} of {_views.Crowd.EligibleLastFrame} shown players advanced in the last frame");
        }
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
