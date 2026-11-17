using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
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
/// M6-V24: every unit model is one surface with one material drawing one atlas, and the atlas alpha marks exactly the faces
/// that were team-coloured before (criteria 1, 3 and 7 of the brief).
/// </summary>
/// <remarks>
/// Headless, in the scene loop: <c>&amp; $env:GODOT --headless --path game res://tests/AtlasModelTest.tscn</c>; prints
/// "ATLAS MODEL TEST PASS". Rows, per entry of <c>tools/asset-manifest.json</c> (the count read from the file): (1) the
/// shipped file is one mesh, one surface, one material named <c>team</c>, with the atlas layout in its extras and a texture of
/// whole square tiles; (2) the mask: every texel of the team tile has alpha 0.5, of the props tile 0.75, of every other tile
/// 1 (no texel transparent), read from the imported (VRAM-compressed) texture; (3) the faces: every triangle's three UVs are
/// in one tile, and each tile holds exactly as many triangles as the same role's surface held in the base files (95186a7,
/// the table below): no face gained or lost the team colour, the Heavy Infantry's shield (12 triangles at one team texel) is
/// on the team tile, every prop face of its own colour on the props tile at one texel; (4) the materials: the players' team
/// materials of a type share the atlas and differ only in the player colour, and a team texel drawn by the shader's rule
/// takes each player's colour while a skin and a prop texel come out the same for both; (5) fallbacks: Git LFS pointers in
/// place of every model file and atlas give capsules, and models off gives capsules (no ERROR line: the loop reads the log).
/// Windowed with <c>-- --render &lt;dir&gt;</c> (not in the loop): each model type drawn in two player colours from one held
/// pose; the pixels that change between the colours are the team pixels; they take each player's hue, and the rest of the
/// figure (skin, props, outfit) is the same in both pictures (the pictures are saved).
/// </remarks>
public partial class AtlasModelTest : Node
{
    private const string Tag = "ATLAS MODEL TEST";

    // Triangles per role surface in the base's files (95186a7, measured with a probe over the imported meshes; the props'
    // own colours summed under "props"). The shield's 12 triangles are in the Heavy Infantry's team count.
    private static readonly Dictionary<string, Dictionary<string, int>> BaseTriangles = new()
    {
        ["malazan_laborer"] = new() { ["team"] = 799, ["outfit"] = 873, ["skin"] = 426, ["head"] = 499 },
        ["malazan_crossbowman"] = new() { ["team"] = 1130, ["outfit"] = 1060, ["skin"] = 140, ["head"] = 349, ["props"] = 48 },
        ["malazan_heavy_infantry"] = new() { ["team"] = 792, ["outfit"] = 1147, ["skin"] = 132, ["head"] = 400, ["props"] = 36 },
        ["whirlwind_camp_follower"] = new() { ["team"] = 799, ["outfit"] = 873, ["skin"] = 426, ["head"] = 499 },
        ["whirlwind_raider"] = new() { ["team"] = 950, ["outfit"] = 1060, ["skin"] = 140, ["head"] = 349, ["props"] = 48 },
        ["whirlwind_desert_archer"] = new() { ["team"] = 1130, ["outfit"] = 1060, ["skin"] = 140, ["head"] = 349, ["props"] = 48 },
        ["teblor_gatherer"] = new() { ["team"] = 700, ["outfit"] = 742, ["skin"] = 358, ["head"] = 450, ["hair"] = 450, ["props"] = 24 },
        ["teblor_warrior"] = new() { ["team"] = 780, ["outfit"] = 967, ["skin"] = 132, ["head"] = 400, ["hair"] = 450, ["props"] = 36 },
        ["teblor_javelin_thrower"] = new() { ["team"] = 600, ["outfit"] = 967, ["skin"] = 132, ["head"] = 400, ["hair"] = 450, ["props"] = 60 },
        ["teblor_shaman"] = new() { ["team"] = 700, ["outfit"] = 742, ["skin"] = 358, ["head"] = 450, ["hair"] = 450, ["props"] = 36 },
        ["teblor_breaker"] = new() { ["team"] = 780, ["outfit"] = 1093, ["skin"] = 106, ["head"] = 400, ["hair"] = 399, ["props"] = 24 },
    };

    // The base's team triangles drawn at one texel (a team prop): the Heavy Infantry's shield box.
    private static readonly Dictionary<string, int> BaseTeamPropTriangles = new() { ["malazan_heavy_infantry"] = 12 };

    // The atlas alpha of each kind of tile (tools/art/build_unit.py ALPHA_*; team_colour.gdshader's thresholds 0.625 / 0.875).
    private const int AlphaTeam = 128, AlphaProp = 191, AlphaPlain = 255;

    private static readonly uint[] Colours = { 0xC03030, 0x3060D0 };

    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private JsonNode _manifest = null!;
    private string _scratch = "";

    public override async void _Ready()
    {
        _scratch = Path.Combine(Path.GetTempPath(), $"rts-atlas-model-{OS.GetProcessId()}");
        string[] args = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(args, "--render");
        string? render = at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        try
        {
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            _manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "tools", "asset-manifest.json")))!;
            UnitViews.UseModels = true;
            var models = new UnitModels(_data, Colours);
            int entries = _manifest["models"]!.AsArray().Count;
            int checkedTypes = 0;
            foreach (JsonNode? m in _manifest["models"]!.AsArray())
                if (ModelRows(models, m!)) checkedTypes++;
            GD.Print($"{checkedTypes} of {entries} manifest model types: one surface, one material, the atlas and its mask as in the base");
            Check(checkedTypes == entries, $"{checkedTypes} of the manifest's {entries} model types checked");
            FallbackRows();
            if (render != null && DisplayServer.GetName() != "headless") await RenderRows(render);
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        UnitViews.UseModels = true;
        UnitModels.ModelRoot = "res://assets/models/";
        try { if (Directory.Exists(_scratch)) Directory.Delete(_scratch, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        foreach (string f in _failures) GD.Print($"{Tag} FAIL: {f}");
        if (_failures.Count == 0) GD.Print($"{Tag} PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // Rows (1)-(4) for one manifest entry; false when its type isn't drawn as a model at all.
    private bool ModelRows(UnitModels models, JsonNode m)
    {
        string id = (string)m["id"]!;
        int type = _data.FindUnit(id);
        if (!Check(type >= 0 && models.Has(type), $"{id}: not drawn as a model")) return false;
        int tile = (int)m["textureSize"]!;

        // (1) One mesh, one surface, one material named team; the atlas layout; whole square tiles.
        Node probe = models.Instantiate(type, out MeshInstance3D mesh, out AnimationPlayer _);
        int meshes = probe.FindChildren("*", "MeshInstance3D", true, false).Count;
        Mesh arrayMesh = mesh.Mesh;
        var material = arrayMesh.GetSurfaceCount() == 1 ? arrayMesh.SurfaceGetMaterial(0) as BaseMaterial3D : null;
        UnitModels.Atlas? atlas = models.AtlasOf(type);
        Texture2D? texture = material?.AlbedoTexture;
        Vector2I size = texture == null ? Vector2I.Zero : new Vector2I(texture.GetWidth(), texture.GetHeight());
        GD.Print($"{id}: {meshes} mesh, {arrayMesh.GetSurfaceCount()} surface, material '{material?.ResourceName}', atlas {atlas?.Grid} of {tile} ({size.X} x {size.Y})");
        bool one = Check(meshes == 1 && arrayMesh.GetSurfaceCount() == 1 && material?.ResourceName == UnitModels.TeamMaterialName && models.TeamSurface(type) == 0,
            $"{id}: {meshes} meshes, {arrayMesh.GetSurfaceCount()} surfaces, material '{material?.ResourceName}', team surface {models.TeamSurface(type)}; want one surface, one 'team' material");
        if (!one || atlas == null || texture == null)
        {
            Check(atlas != null && texture != null, $"{id}: no atlas layout or texture");
            probe.Free();
            return false;
        }
        Check(size == atlas.Grid * tile, $"{id}: texture {size}, want the {atlas.Grid} grid of {tile} texel tiles");

        // (2) The mask: each tile's alpha, every texel, from the imported texture.
        Image image = texture.GetImage();
        if (image.IsCompressed()) image.Decompress();
        image.Convert(Image.Format.Rgba8);
        byte[] px = image.GetData();
        int w = image.GetWidth();
        var tileOf = new string[atlas.Grid.X, atlas.Grid.Y];
        foreach (KeyValuePair<string, Vector2I> t in atlas.Tiles) tileOf[t.Value.X, t.Value.Y] = t.Key;
        var alphaLine = new List<string>();
        for (int c = 0; c < atlas.Grid.X; c++)
            for (int r = 0; r < atlas.Grid.Y; r++)
            {
                string name = tileOf[c, r] ?? "(unused)";
                int want = name == "team" ? AlphaTeam : name == UnitModels.PropsTile ? AlphaProp : AlphaPlain;
                int lo = 255, hi = 0;
                for (int y = r * tile; y < (r + 1) * tile; y++)
                    for (int x = c * tile; x < (c + 1) * tile; x++)
                    {
                        int a = px[(y * w + x) * 4 + 3];
                        lo = Math.Min(lo, a);
                        hi = Math.Max(hi, a);
                    }
                alphaLine.Add($"{name} {lo}-{hi}");
                Check(Math.Abs(lo - want) <= 1 && Math.Abs(hi - want) <= 1, $"{id}: tile {name} has alpha {lo}-{hi}, want {want} on every texel");
            }
        GD.Print($"{id}: mask (alpha per tile) [{string.Join(", ", alphaLine)}]");

        // (3) The faces: one tile each, counted per tile against the base's surfaces.
        Godot.Collections.Array arrays = arrayMesh.SurfaceGetArrays(0);
        int[] index = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        Vector2[] uv = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        var counts = new Dictionary<string, int>();
        int straddle = 0, teamPoint = 0, propSpread = 0;
        for (int k = 0; k + 2 < index.Length; k += 3)
        {
            Vector2 a = uv[index[k]], b = uv[index[k + 1]], c = uv[index[k + 2]];
            string ta = atlas.TileAt(a);
            if (ta != atlas.TileAt(b) || ta != atlas.TileAt(c)) straddle++;
            counts[ta] = counts.GetValueOrDefault(ta) + 1;
            bool point = a == b && a == c;
            if (ta == "team" && point) teamPoint++;
            if (ta == UnitModels.PropsTile && !point) propSpread++;
        }
        var line = new List<string>();
        foreach (KeyValuePair<string, int> kv in counts) line.Add($"{kv.Key} {kv.Value}");
        GD.Print($"{id}: triangles per tile [{string.Join(", ", line)}], team prop triangles {teamPoint}");
        Check(straddle == 0, $"{id}: {straddle} triangles straddle two tiles");
        Check(propSpread == 0, $"{id}: {propSpread} prop triangles read more than one texel");
        if (Check(BaseTriangles.TryGetValue(id, out Dictionary<string, int>? baseCounts), $"{id}: no base pin for its triangles per role"))
        {
            foreach (KeyValuePair<string, int> want in baseCounts!)
                Check(counts.GetValueOrDefault(want.Key) == want.Value, $"{id}: {counts.GetValueOrDefault(want.Key)} triangles on the {want.Key} tile, the base's {want.Key} surface had {want.Value}");
            foreach (KeyValuePair<string, int> have in counts)
                Check(baseCounts.ContainsKey(have.Key), $"{id}: {have.Value} triangles on a tile '{have.Key}' the base had no surface for");
        }
        Check(teamPoint == BaseTeamPropTriangles.GetValueOrDefault(id), $"{id}: {teamPoint} team triangles at one texel (a team prop), the base had {BaseTeamPropTriangles.GetValueOrDefault(id)}");

        // (4) The materials and the shader's rule on one texel of each kind (team, skin, a prop).
        ShaderMaterial? first = models.TeamMaterial(type, 0), second = models.TeamMaterial(type, 1);
        bool shared = first != null && second != null
                      && first.GetShaderParameter("albedo_texture").As<Texture2D>() == texture
                      && second.GetShaderParameter("albedo_texture").As<Texture2D>() == texture
                      && first.GetShaderParameter("use_atlas").AsBool() && second.GetShaderParameter("use_atlas").AsBool()
                      && first.GetShaderParameter("atlas_grid").AsVector2() == new Vector2(atlas.Grid.X, atlas.Grid.Y)
                      && second.GetShaderParameter("atlas_grid").AsVector2() == first.GetShaderParameter("atlas_grid").AsVector2();
        Check(shared, $"{id}: the players' materials don't share the atlas, its grid and use_atlas");
        Check(first?.GetShaderParameter("team_colour").AsColor() == UnitViews.ColorFromRgb(Colours[0]) && second?.GetShaderParameter("team_colour").AsColor() == UnitViews.ColorFromRgb(Colours[1]),
            $"{id}: the players' team colours are not their colours");
        foreach (string kind in new[] { "team", "skin", UnitModels.PropsTile })
        {
            int k = Array.FindIndex(index, v => atlas.TileAt(uv[v]) == kind);
            if (k < 0) { Check(kind == UnitModels.PropsTile && !baseCounts!.ContainsKey(kind), $"{id}: no {kind} face"); continue; }
            Vector2 p = uv[index[k]];
            int ix = Math.Clamp((int)(p.X * w), 0, w - 1), iy = Math.Clamp((int)(p.Y * image.GetHeight()), 0, image.GetHeight() - 1);
            int o = (iy * w + ix) * 4;
            var texel = new Color(px[o] / 255f, px[o + 1] / 255f, px[o + 2] / 255f, px[o + 3] / 255f);
            Color a = Shade(texel, UnitViews.ColorFromRgb(Colours[0])), b = Shade(texel, UnitViews.ColorFromRgb(Colours[1]));
            GD.Print($"{id}: a {kind} texel {texel.ToHtml()} draws {a.ToHtml(false)} for player 1, {b.ToHtml(false)} for player 2");
            if (kind == "team") Check(a.R > a.B && b.B > b.R, $"{id}: a team texel draws {a.ToHtml(false)} / {b.ToHtml(false)}, not each player's hue");
            else Check(a == b, $"{id}: a {kind} texel draws {a.ToHtml(false)} / {b.ToHtml(false)}, want the same for both players");
        }
        probe.Free();
        return true;
    }

    // team_colour.gdshader's albedo rule, in sRGB: a team texel (alpha at most 0.625) is its red channel times the gain times
    // the player colour; any other texel is itself.
    private static Color Shade(Color texel, Color player)
    {
        if (texel.A > 0.625f) return new Color(texel.R, texel.G, texel.B);
        float shade = Mathf.Clamp(texel.SrgbToLinear().R * 1.25f, 0f, 1f);
        Color lin = player.SrgbToLinear();
        return new Color(lin.R * shade, lin.G * shade, lin.B * shade).LinearToSrgb();
    }

    // (5) Fallbacks: every model file and every atlas an LFS pointer gives capsules; models off gives capsules.
    private void FallbackRows()
    {
        const string pointer = "version https://git-lfs.github.com/spec/v1\noid sha256:4d7a214614ab2935c943f9e0ff69d22eadbb8f32b1258daaa5e2ca24d17e2393\nsize 875488\n";
        int written = 0;
        for (int t = 0; t < _data.Units.Length; t++)
        {
            string model = _data.Units[t].Model;
            if (!File.Exists(ProjectSettings.GlobalizePath(UnitModels.PathFor(model)))) continue;
            string leaf = model[(model.LastIndexOf('/') + 1)..];
            string dir = Path.Combine(_scratch, "models", model);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, leaf + ".glb"), pointer);
            File.WriteAllText(Path.Combine(dir, leaf + "_atlas.png"), pointer);
            written++;
        }
        UnitModels.ModelRoot = Path.Combine(_scratch, "models").Replace('\\', '/') + "/";
        var pointed = new UnitViews();
        AddChild(pointed);
        pointed.Bind(_data, 8, Colours);
        int drawn = 0;
        for (int t = 0; t < _data.Units.Length; t++) if (pointed.DrawsModel(t)) drawn++;
        UnitModels.ModelRoot = "res://assets/models/";
        pointed.QueueFree();
        GD.Print($"LFS pointers for {written} model files and their atlases: {drawn} types draw a model");
        Check(written == _manifest["models"]!.AsArray().Count && drawn == 0, $"LFS pointers: {written} written, {drawn} types still draw a model");

        UnitViews.UseModels = false;
        var off = new UnitViews();
        AddChild(off);
        off.Bind(_data, 8, Colours);
        Check(off.Models == null, "models off: a model was loaded");
        off.QueueFree();
        UnitViews.UseModels = true;
        GD.Print("models off: every type its capsule");
    }

    // Windowed: per model type, one figure held in one pose, drawn in each player's colour; compare the two pictures.
    private async Task RenderRows(string dir)
    {
        Directory.CreateDirectory(dir);
        GetTree().Root.Size = new Vector2I(1152, 648);
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        match.Start(_data, LaunchOptions.Parse(new[] { "--seed", "1", "--units", "0", "--no-bases", "--no-combat", "--no-fog", "--mute", "--no-hud" }));
        var runner = match.GetNode<SimRunner>("SimRunner");
        runner.ProcessMode = ProcessModeEnum.Disabled; // ticked here
        Simulation sim = runner.Simulation!;
        UnitStore u = sim.World.Units;
        var views = match.GetNode<UnitViews>("World3D/UnitViews");
        var camera = new Camera3D { Fov = 30f };
        AddChild(camera);
        NVector2 mid = sim.World.NavGrid.CellCenter(sim.World.NavGrid.Width / 2, sim.World.NavGrid.Height / 2);
        foreach (JsonNode? m in _manifest["models"]!.AsArray())
        {
            string id = (string)m!["id"]!;
            int type = _data.FindUnit(id);
            for (int i = 0; i < u.Capacity; i++) if (u.Alive[i]) u.Free(new EntityHandle(i, u.Generation[i]));
            sim.Enqueue(Command.SpawnUnit(0, type, mid));
            sim.Tick();
            sim.Tick();
            int slot = -1;
            for (int i = 0; i < u.Capacity && slot < 0; i++) if (u.Alive[i] && u.TypeId[i] == type) slot = i;
            views.Sync(sim.World, 1f);
            await Frames(2);
            if (!Check(slot >= 0 && views.AnimatorOf(slot) != null && views.ModelMeshOf(slot) != null, $"(render) {id}: no model")) continue;
            AnimationPlayer anim = views.AnimatorOf(slot)!;
            anim.Play("idle", 0);
            anim.Seek(0.0, true);
            anim.Active = false;
            float scale = _data.Units[type].Scale;
            Vector3 ground = views.ModelOf(slot)!.GlobalPosition;
            Vector3 facing = views.ViewOf(slot)!.GlobalTransform.Basis.Z.Normalized(); // the figure looks along -Z: the camera stands in front
            camera.LookAtFromPosition(ground - facing * 6.5f * scale + new Vector3(0f, 1.6f * scale, 0f), ground + new Vector3(0f, 0.95f * scale, 0f), Vector3.Up);
            camera.MakeCurrent();
            MeshInstance3D mesh = views.ModelMeshOf(slot)!;
            var shots = new Image[3];
            for (int p = 0; p < 2; p++)
            {
                mesh.SetSurfaceOverrideMaterial(views.Models!.TeamSurface(type), views.Models.TeamMaterial(type, p));
                await Frames(8);
                for (int f = 0; f < 2; f++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                shots[p] = GetViewport().GetTexture().GetImage();
                shots[p].SavePng(Path.Combine(dir, $"atlas-{id}-p{p + 1}.png"));
            }
            views.ViewOf(slot)!.Visible = false; // the ground alone, to tell the figure's pixels from it
            await Frames(8);
            for (int f = 0; f < 2; f++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            shots[2] = GetViewport().GetTexture().GetImage();
            views.ViewOf(slot)!.Visible = true;
            Compare(id, shots[0], shots[1], shots[2], views.Models!.TeamMaterial(type, 0)!.GetShaderParameter("team_colour").AsColor(),
                views.Models.TeamMaterial(type, 1)!.GetShaderParameter("team_colour").AsColor());
        }
        RemoveChild(match);
        match.QueueFree();
        camera.QueueFree();
        await Frames(1);
    }

    // The figure's pixels are those that differ from the ground alone. Those that also differ between the two colours are the
    // team's and take each player's hue (nearer in direction to its own player's colour than to the other's); the rest
    // (skin, hair, outfit, props) are the same in both pictures.
    private void Compare(string id, Image a, Image b, Image ground, Color colourA, Color colourB)
    {
        int w = a.GetWidth(), h = a.GetHeight();
        int figure = 0, changed = 0, hueOk = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color ca = a.GetPixel(x, y), cb = b.GetPixel(x, y), cg = ground.GetPixel(x, y);
                if (Far(ca, cg) || Far(cb, cg)) figure++;
                if (!Far(ca, cb)) continue;
                changed++;
                if (Cos(ca, colourA) > Cos(ca, colourB) && Cos(cb, colourB) > Cos(cb, colourA)) hueOk++;
            }
        GD.Print($"(render) {id} in {colourA.ToHtml(false)} / {colourB.ToHtml(false)}: {figure} figure pixels, {changed} change between the two colours ({hueOk} in each player's hue), {figure - changed} the same in both");
        Check(changed > 500 && figure - changed > 500, $"(render) {id}: {changed} team pixels, {figure - changed} others");
        Check(hueOk >= changed * 0.95, $"(render) {id}: {changed - hueOk} of {changed} changed pixels not in the players' hues");
    }

    private static float Cos(Color a, Color b)
    {
        float dot = a.R * b.R + a.G * b.G + a.B * b.B;
        return dot / Mathf.Max(1e-6f, Mathf.Sqrt((a.R * a.R + a.G * a.G + a.B * a.B) * (b.R * b.R + b.G * b.G + b.B * b.B)));
    }

    private static bool Far(Color a, Color b) => Mathf.Abs(a.R - b.R) + Mathf.Abs(a.G - b.G) + Mathf.Abs(a.B - b.B) >= 0.02f;

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private bool Check(bool ok, string why)
    {
        if (!ok) _failures.Add(why);
        return ok;
    }
}
