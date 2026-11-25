using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using NVector2 = System.Numerics.Vector2;

namespace Rts.Game.Tests;

/// <summary>
/// M6-VH8 (BUG-1441 item 2): the overlays (hp bar, cast bar, status marks, cue words, cargo marker) sit just above the drawn
/// figure's head for a model type and above the capsule for a capsule type (<see cref="UnitViews.OverlayLines"/>).
/// </summary>
/// <remarks>
/// Headless, in the scene loop: <c>&amp; $env:GODOT --headless --path game res://tests/OverlayLineTest.tscn</c>; prints
/// "OVERLAY LINE TEST PASS" and exits 0, or each failure and exits 1. Rows, on the shipped data:
/// (1) per model type (the count is the asset manifest's entries): its highest bone over every clip its players hold, measured
///     here on a real player and skeleton (17 poses a clip, as <see cref="UnitModels.TopBone"/> samples), equals
///     <c>TopBone x scale</c> within 2 cm; M6-VH9: so do its props' highest point over the clips its data plays and
///     <see cref="UnitViews.PlayedPropTop"/>; the overlay line is above the higher of the two and at most <see cref="Margin"/>
///     over it (the gap is printed per type), above the head's crown in every pose (the head bone's highest point plus its rest distance to
///     the rest-pose top), and the hp bar's lower edge clears the figure's rest-pose top (its body without the props);
/// (2) every capsule type's line is its capsule's top, <see cref="UnitViews.DrawnHeight"/>, exactly (unchanged);
/// (3) in a real match the hp bars and the ability overlays read the same line for every type, a hurt Teblor Warrior's and a
///     hurt Heavy Infantry's bars are drawn, and a carrying Laborer's cargo marker is <see cref="UnitViews.CargoGap"/> over its line.
/// Windowed with <c>-- --shots &lt;dir&gt;</c> it saves the hurt Warrior beside the hurt Heavy Infantry at zoom 20 and 40.
/// </remarks>
public partial class OverlayLineTest : Node
{
    private const string Tag = "OVERLAY LINE TEST";

    /// <summary>The most the overlay line may sit over a model type's highest bone, in meters (the brief's "about 0.3 m").</summary>
    public const float Margin = 0.3f;

    private readonly List<string> _failures = new();
    private readonly HashSet<string> _propMaterials = new();
    private GameData _data = null!;
    private JsonNode _manifest = null!;
    private string? _shots;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        string[] args = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(args, "--shots");
        if (at >= 0 && at + 1 < args.Length) { _shots = args[at + 1]; Directory.CreateDirectory(_shots); }
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648); // headless windows are 64 x 64
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            _manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "tools", "asset-manifest.json")))!;
            foreach (JsonNode? m in _manifest["models"]!.AsArray())
                foreach (JsonNode? p in m!["props"]?.AsArray() ?? new JsonArray())
                    if ((string)p!["material"]! != UnitModels.TeamMaterialName) _propMaterials.Add((string)p["material"]!);
            UnitViews.UseModels = true;
            await ModelTypes();
            await InAMatch();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures) GD.Print($"{Tag} FAIL: {f}");
        if (_failures.Count == 0) GD.Print($"{Tag} PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- (1) and (2): every unit type's line ----

    private async Task ModelTypes()
    {
        var views = new UnitViews();
        AddChild(views);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        double sampled = UnitModels.TopBoneMs;
        views.Bind(_data, 4, new uint[] { 0xC03030, 0x3060D0 });
        double first = clock.Elapsed.TotalMilliseconds, firstSampling = UnitModels.TopBoneMs - sampled;
        clock.Restart();
        sampled = UnitModels.TopBoneMs;
        views.Bind(_data, 4, new uint[] { 0xC03030, 0x3060D0 });
        GD.Print($"(1) Bind: {first:0} ms the first time in the run ({firstSampling:0} ms of it the highest bones), {clock.Elapsed.TotalMilliseconds:0} ms again ({UnitModels.TopBoneMs - sampled:0.0} ms)");
        int manifestModels = _manifest["models"]!.AsArray().Count;
        Check(views.Models != null && views.Models.ModelTypes == manifestModels, $"(1) {views.Models?.ModelTypes} model types, the manifest has {manifestModels}");
        int models = 0, capsules = 0;
        float worstGap = 0f;
        string worstType = "";
        for (int type = 0; type < _data.Units.Length; type++)
        {
            UnitDef def = _data.Units[type];
            float line = views.OverlayLine(type);
            if (!views.DrawsModel(type))
            {
                capsules++;
                Check(line == UnitViews.DrawnHeight(def), $"(2) capsule {def.Key}: line {line}, want its capsule's top {UnitViews.DrawnHeight(def)}");
                continue;
            }
            models++;
            (float measured, string bone, string clip, float head, float restHead, float propMeasured) = await MeasuredTop(views, type);
            float computed = views.Models!.TopBone(type) * def.Scale;
            // M6-VH9 (BUG-1600 item 3): a prop over the head in a clip the type plays (the Mage's staff head) sets the line.
            float propComputed = UnitViews.PlayedPropTop(views.Models, _data, type) * def.Scale;
            float under = float.IsFinite(propComputed) ? Math.Max(measured, propMeasured) : measured;
            float gap = line - under;
            float restTop = RestTop(views, type);
            // The crown in any pose: the head bone's highest point plus the rest pose's head bone to crown.
            float crown = head + (restTop - restHead);
            // The hp bar's lower edge at the nearest zooms (its thickness and gap don't grow below CombatViews.BarFullZoom).
            float barBottom = line + CombatViews.BarGap - CombatViews.BarThickness / 2f;
            GD.Print(string.Create(CultureInfo.InvariantCulture,
                $"(1) {def.Key} (scale {def.Scale}): highest bone {bone} in {clip} at {measured:F3} m (TopBone x scale {computed:F3}), line {line:F3} m, gap {gap:F3} m"
                + $" (was {UnitViews.DrawnHeight(def):F2} m, gap {UnitViews.DrawnHeight(def) - measured:F2}); head's crown at most {crown:F3} m, line {line - crown:F3} m over it; rest top {restTop:F3} m, hp bar's lower edge {barBottom - restTop:F3} m over it"));
            Check(MathF.Abs(measured - computed) < 0.02f, $"(1) {def.Key}: measured highest bone {measured:F3} m, TopBone x scale {computed:F3} m");
            if (float.IsFinite(propComputed))
            {
                GD.Print(string.Create(CultureInfo.InvariantCulture, $"(1) {def.Key}: props' top over the clips it plays {propMeasured:F3} m measured, {propComputed:F3} m computed"));
                Check(MathF.Abs(propMeasured - propComputed) < 0.02f, $"(1) {def.Key}: measured props' top {propMeasured:F3} m, PlayedPropTop x scale {propComputed:F3} m");
            }
            Check(gap > 0f && gap <= Margin, $"(1) {def.Key}: line {line:F3} m is {gap:F3} m over the highest bone or prop {under:F3} m (want above it, at most {Margin} m)");
            Check(barBottom > restTop, $"(1) {def.Key}: the hp bar's lower edge {barBottom:F3} m is inside the figure (rest top {restTop:F3} m)");
            Check(head > 0f && line > crown, $"(1) {def.Key}: line {line:F3} m under the head's crown {crown:F3} m in some clip");
            if (gap > worstGap) (worstGap, worstType) = (gap, def.Key);
        }
        GD.Print($"(1) {models} model types, (2) {capsules} capsule types; the largest gap {worstGap:F3} m ({worstType}), margin {Margin} m");
        views.Free();
    }

    // The highest counted bone over 17 poses of every clip the type's players hold, on a real player and skeleton (blend off,
    // so each pose is the clip's own), in meters above the feet at the data scale.
    // Also the head bone's highest point over those poses and its rest height ("Head", QaM7V5Test's name; 0 when absent), and
    // (M6-VH9) the props' highest skinned vertex over the poses of the clips the type plays (NaN without props).
    private async Task<(float Top, string Bone, string Clip, float Head, float RestHead, float Prop)> MeasuredTop(UnitViews views, int type)
    {
        UnitDef def = _data.Units[type];
        var holder = new Node3D();
        AddChild(holder);
        Node3D model = views.Models!.Instantiate(type, out MeshInstance3D mesh, out AnimationPlayer anim);
        holder.AddChild(model);
        model.Scale = new Vector3(def.Scale, def.Scale, def.Scale);
        anim.PlaybackDefaultBlendTime = 0.0;
        Skeleton3D skel = mesh.GetNode<Skeleton3D>(mesh.Skeleton);
        (float top, string bone, string clip) = (float.MinValue, "", "");
        int headBone = skel.FindBone("Head");
        float head = 0f, restHead = headBone >= 0 ? (skel.GlobalTransform * skel.GetBoneGlobalRest(headBone)).Origin.Y : 0f;
        float prop = float.NaN;
        UnitModels.Atlas? atlas = views.Models.AtlasOf(type);
        foreach (StringName name in anim.GetAnimationList())
        {
            int c = Array.IndexOf(UnitModels.ClipNames, name);
            bool played = c >= 0 && Plays(def, (UnitModels.Clip)c);
            anim.Play(name);
            double len = anim.CurrentAnimationLength;
            for (int k = 0; k <= UnitModels.TopBoneSamples; k++)
            {
                anim.Seek(len * k / UnitModels.TopBoneSamples, true);
                await Frame(); // the skeleton applies the pose at the frame's end
                for (int b = 0; b < skel.GetBoneCount(); b++)
                {
                    string n = skel.GetBoneName(b), lower = n.ToLowerInvariant();
                    if (lower.Contains("ik") || lower == "root" || skel.GetBoneParent(b) < 0) continue;
                    float y = (skel.GlobalTransform * skel.GetBoneGlobalPose(b)).Origin.Y;
                    if (y > top) (top, bone, clip) = (y, n, name.ToString());
                    if (b == headBone) head = Math.Max(head, y);
                }
                if (played && atlas != null)
                {
                    float y = PropTopNow(mesh, skel, atlas);
                    if (float.IsNaN(prop) || y > prop) prop = y;
                }
            }
        }
        holder.Free();
        return (top, bone, clip, head, restHead, prop);
    }

    // The clips a type's data has it play (UnitViews.PlayedPropTop's rule): idle, walk, attack; a shown cast; a worker's.
    private bool Plays(UnitDef def, UnitModels.Clip clip)
    {
        bool casts = false;
        foreach (int a in def.Abilities) casts |= _data.Abilities[a].CastTicks > 1;
        return clip switch
        {
            UnitModels.Clip.Idle or UnitModels.Clip.Walk or UnitModels.Clip.Attack => true,
            UnitModels.Clip.Cast => casts,
            _ => def.IsWorker,
        };
    }

    // The highest prop vertex (the props' atlas tile) in the skeleton's current pose, world y; NaN without props.
    private static float PropTopNow(MeshInstance3D mesh, Skeleton3D skel, UnitModels.Atlas atlas)
    {
        Skin skin = mesh.Skin;
        Godot.Collections.Array arrays = mesh.Mesh.SurfaceGetArrays(0);
        Vector3[] v = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        Vector2[] uv = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        int[] bones = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
        float[] weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
        int per = bones.Length / Math.Max(1, v.Length);
        float top = float.NaN;
        for (int k = 0; k < v.Length; k++)
        {
            if (!atlas.IsProp(uv[k])) continue;
            Vector3 p = Vector3.Zero;
            for (int j = 0; j < per; j++)
            {
                float w = weights[k * per + j];
                if (w <= 0f) continue;
                int bind = bones[k * per + j];
                int bone = skin.GetBindBone(bind);
                if (bone < 0) bone = skel.FindBone(skin.GetBindName(bind));
                p += w * (skel.GlobalTransform * skel.GetBoneGlobalPose(bone) * skin.GetBindPose(bind) * v[k]);
            }
            if (float.IsNaN(top) || p.Y > top) top = p.Y;
        }
        return top;
    }

    // The figure's rest-pose top at the data scale, without the props' own surfaces (TeblorModelTest's box).
    private float RestTop(UnitViews views, int type)
    {
        UnitDef def = _data.Units[type];
        var holder = new Node3D();
        AddChild(holder);
        Node3D model = views.Models!.Instantiate(type, out MeshInstance3D mesh, out AnimationPlayer _);
        holder.AddChild(model);
        model.Scale = new Vector3(def.Scale, def.Scale, def.Scale);
        Mesh m = mesh.Mesh;
        Transform3D x = mesh.GlobalTransform;
        float top = float.MinValue;
        UnitModels.Atlas? atlas = views.Models!.AtlasOf(type);
        for (int s = 0; s < m.GetSurfaceCount(); s++)
        {
            if (m.SurfaceGetMaterial(s) is BaseMaterial3D mat && _propMaterials.Contains(mat.ResourceName)) continue;
            // M6-V24: on the one surface of an atlas model the props' own faces read the props' tile.
            Godot.Collections.Array arrays = m.SurfaceGetArrays(s);
            Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            Vector2[] uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            for (int k = 0; k < vertices.Length; k++)
                if (atlas == null || k >= uvs.Length || !atlas.IsProp(uvs[k])) top = Math.Max(top, (x * vertices[k]).Y);
        }
        holder.Free();
        return top;
    }

    // ---- (3) in a real match ----

    private async Task InAMatch()
    {
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        try
        {
            match.Start(_data, LaunchOptions.Parse(new[] { "--seed", "1", "--units", "0", "--mute" }));
            var runner = match.GetNode<SimRunner>("SimRunner");
            runner.ProcessMode = ProcessModeEnum.Disabled;
            Simulation sim = runner.Simulation!;
            World w = sim.World;
            UnitStore u = w.Units;
            var units = match.GetNode<UnitViews>("World3D/UnitViews");
            var combat = match.GetNode<CombatViews>("World3D/CombatViews");
            var abilities = match.GetNode<AbilityViews>("World3D/AbilityViews");
            var camera = match.GetNode<RtsCamera>("RtsCamera");
            camera.EdgePanEnabled = false;
            sim.Tick();
            sim.Tick();
            int mismatched = 0;
            for (int t = 0; t < _data.Units.Length; t++)
                if (combat.BarLineOf(t) != units.OverlayLine(t) || abilities.OverlayLineOf(t) != units.OverlayLine(t)) mismatched++;
            Check(mismatched == 0, $"(3) {mismatched} types whose hp bar or ability overlays don't read the unit views' line");

            // A hurt Teblor Warrior beside a hurt Heavy Infantry, and a Laborer carrying gold, on open ground by player 0's hall.
            int warrior = _data.FindUnit("teblor_warrior"), heavy = _data.FindUnit("malazan_heavy_infantry");
            int laborer = StartBase.UnitOfSlot(_data, w.FactionOf(0), UnitSlot.Worker);
            NVector2 hall = HallCenter(w);
            var middle = new NVector2(w.NavGrid.Width, w.NavGrid.Height) * MapConstants.CellSize / 2f;
            NVector2 away = NVector2.Normalize(middle - hall), side = new(-away.Y, away.X);
            NVector2 spot = Passable(w, hall + away * 16f);
            EntityHandle hw = Spawn(sim, 0, warrior, Passable(w, spot - side * 1.5f));
            EntityHandle hh = Spawn(sim, 0, heavy, Passable(w, spot + side * 1.5f));
            EntityHandle lb = Spawn(sim, 0, laborer, Passable(w, spot + away * 3f));
            u.Hp[hw.Index] = w.MaxHp(hw.Index) / 2; // test seam: hurt, not killed
            u.Hp[hh.Index] = w.MaxHp(hh.Index) / 2;
            u.Cargo[lb.Index] = 5;
            u.CargoKind[lb.Index] = ResourceKind.Gold;
            camera.SetZoom(CameraLimits.MinZoom);
            camera.SetFocus(spot.X, spot.Y + 2f);
            await Frame();
            await Frame();
            int barW = -1, barH = -1;
            for (int k = 0; k < combat.ShownBars; k++)
            {
                if (combat.BarSlot(k) == hw.Index) barW = k;
                if (combat.BarSlot(k) == hh.Index) barH = k;
            }
            Check(barW >= 0 && barH >= 0, $"(3) hurt Warrior's bar {barW}, hurt Heavy Infantry's bar {barH} (of {combat.ShownBars})");
            float lineW = units.OverlayLine(warrior), lineH = units.OverlayLine(heavy);
            GD.Print(string.Create(CultureInfo.InvariantCulture,
                $"(3) hurt Teblor Warrior: bar centre {lineW + CombatViews.BarGap:F2} m (line {lineW:F2}, was {UnitViews.DrawnHeight(_data.Units[warrior]) + CombatViews.BarGap:F2}); hurt Heavy Infantry: {lineH + CombatViews.BarGap:F2} m (line {lineH:F2}, was {UnitViews.DrawnHeight(_data.Units[heavy]) + CombatViews.BarGap:F2})"));
            MeshInstance3D? cargo = units.MarkerOf(lb.Index);
            float ground = UnitViews.GroundPoint(w, lb.Index, 1f).Y;
            float wantCargo = units.OverlayLine(laborer) + UnitViews.CargoGap + UnitViews.CargoSize / 2f;
            Check(cargo != null && cargo.Visible && MathF.Abs(cargo.GlobalPosition.Y - ground - wantCargo) < 1e-3f,
                $"(3) the Laborer's cargo marker at {(cargo == null ? "none" : (cargo.GlobalPosition.Y - ground).ToString("F3", CultureInfo.InvariantCulture))} m, want {wantCargo:F3} m (line + gap + half its size)");
            GD.Print(string.Create(CultureInfo.InvariantCulture, $"(3) carrying Laborer: cargo marker centre {wantCargo:F3} m over the ground (line {units.OverlayLine(laborer):F3} m)"));
            if (_shots != null && DisplayServer.GetName() != "headless")
            {
                await Shot("hurt-warrior-beside-heavy-infantry-zoom20");
                camera.SetZoom(CameraLimits.DefaultZoom);
                camera.SetFocus(spot.X, spot.Y + 4f);
                await Shot("hurt-warrior-beside-heavy-infantry-zoom40");
            }
        }
        finally
        {
            RemoveChild(match);
            match.QueueFree();
            await Frame();
        }
    }

    private static EntityHandle Spawn(Simulation sim, int owner, int type, NVector2 at)
    {
        UnitStore u = sim.World.Units;
        var alive = new bool[u.Capacity];
        for (int i = 0; i < u.Capacity; i++) alive[i] = u.Alive[i];
        sim.Enqueue(Command.SpawnUnit(owner, type, at));
        sim.Tick();
        sim.Tick();
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && !alive[i]) return new EntityHandle(i, u.Generation[i]);
        throw new InvalidOperationException($"spawn of type {type} at {at} refused");
    }

    private static NVector2 HallCenter(World w)
    {
        BuildingStore b = w.Buildings;
        for (int k = 0; k < b.Capacity; k++)
            if (b.Alive[k] && b.Owner[k] == 0 && w.Data.Buildings[b.TypeId[k]].Slot == BuildingSlot.TownHall)
                return StartBase.FootprintCenter(w.NavGrid, w.Data.Buildings[b.TypeId[k]], b.Cell[k]);
        throw new InvalidOperationException("player 0 has no hall");
    }

    private static NVector2 Passable(World w, NVector2 p)
    {
        NavGrid g = w.NavGrid;
        int x = Math.Clamp((int)(p.X / MapConstants.CellSize), 1, g.Width - 2), y = Math.Clamp((int)(p.Y / MapConstants.CellSize), 1, g.Height - 2);
        int cell = Rts.Sim.Pathfinding.FlowField.NearestPassable(g, y * g.Width + x);
        if (cell < 0) throw new InvalidOperationException($"nothing passable near {p}");
        return g.CellCenter(cell % g.Width, cell / g.Width);
    }

    private async Task Shot(string name)
    {
        for (int i = 0; i < 4; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = $"{_shots}/{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"screenshot {path}");
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
