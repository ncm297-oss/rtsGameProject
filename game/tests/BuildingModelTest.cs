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
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using NVector2 = System.Numerics.Vector2;

namespace Rts.Game.Tests;

/// <summary>
/// M6-V26: the first building models (the Garrison Keep and the Barracks): the files, the fit, the site-to-model switch, the
/// remembered model under the fog, the bar, the click, team colour, and the box fallbacks.
/// </summary>
/// <remarks>
/// Headless, in the scene loop: <c>&amp; $env:GODOT --headless --path game res://tests/BuildingModelTest.tscn</c>; prints
/// "BUILDING MODEL TEST PASS" or FAIL lines. Rows, for every entry of <c>tools/asset-manifest.json</c>'s <c>buildings</c> (the
/// count read from the file):
/// <list type="bullet">
/// <item>Files (criterion 2): the type is found by its id at the manifest's destination; one surface, the <c>team</c>
/// material, one atlas of at most 1024 x 1024, at most the manifest's and docs/04's triangle cap; the mask (team tile alpha
/// 0.5, every other 1) read from the imported texture; every face's three UVs on one texel of one tile, the triangles per tile
/// pinned to the first build.</item>
/// <item>Team colour (criterion 6): in every colour of the setup's table (eight, Crimson and Bone included), each player's
/// material draws a team texel in exactly that colour at the team shade, and every other tile's texel the same for every
/// colour; the remembered material is the same darkened as a ghost box.</item>
/// <item>In a real match, three players of the building's faction, fog on (criteria 3-5): each finished model stands on its footprint's ground
/// with every part inside the footprint (at most 0.25 m over an edge is the brief's bound; the fit keeps
/// <see cref="BuildingModels.FitMargin"/> inside), its box hidden; a site of each type is the rising box and is the model on the
/// first frame after the tick it is finished; the bar is over the model's highest point (a hurt building's too); a click on
/// the footprint selects it; a type without a model file stays a box; a cancelled site and a destroyed finished building
/// leave nothing drawn; an enemy Keep and Barracks scouted and left are drawn as their models darkened, the ghost box hidden;
/// one razed by a third player out of sight stays remembered until the scout sees the spot again, then goes; 20 frames with
/// models and remembered models allocate nothing.</item>
/// <item>Fallbacks (criterion 9): Git LFS pointer files in place of the models, missing files and models off each give
/// boxes, a pointer one warning line a file and no <c>ERROR</c> line (the loop reads the log).</item>
/// </list>
/// M6-V28 (six types): a type that needs Age II (the Watchtower) is built by player 2 once it has researched it, seen
/// through a player-0 Billet beside it (player 0's stock can't pay Age II and every site); the remembered row sends a scout
/// to each enemy building and frees them once seen.
/// M6-V30 (the Teblor models): the match rows run once per faction the manifest's buildings belong to, every seat of that
/// faction, with its own worker (builder and scout), line unit (the razing party), house (the watcher) and halls (Age II),
/// all found by the data's slots, so no faction's unit or building is named here.
/// Windowed with <c>-- --shots &lt;dir&gt;</c> it also saves the pictures of criterion 7 (default zoom and close up, two and
/// four player colours, a Laborer and a Heavy Infantry beside each building, a site, and the remembered models under the fog).
/// </remarks>
public partial class BuildingModelTest : Node
{
    private const string Tag = "BUILDING MODEL TEST";

    // docs/04's building budget, and the brief's largest texture.
    private const int BuildingTriangleBudget = 5000, MaxTexture = 1024;

    // The brief's bound: no part beyond the footprint's edge by more than this.
    private const float EdgeSlack = 0.25f;

    // The atlas alpha of the team tile and of every other tile (build_building.py ALPHA_*).
    private const int AlphaTeam = 128, AlphaPlain = 255;

    // Triangles per atlas tile of the first build (build_building.py's result): a later import that moves a face between
    // the team and another colour fails here.
    private static readonly Dictionary<string, Dictionary<string, int>> TileTriangles = new()
    {
        ["malazan_garrison_keep"] = new() { ["team"] = 420, ["Stone"] = 1026, ["Stone_Light"] = 2360, ["Walls"] = 52, ["Wood"] = 524, ["Wood_Light"] = 58, ["Gold"] = 200, ["Metal_Light"] = 162, ["White"] = 138 },
        ["malazan_barracks"] = new() { ["team"] = 908, ["Stone"] = 154, ["Stone_Light"] = 18, ["Walls"] = 52, ["Wood"] = 468, ["Wood_Light"] = 58, ["Gold"] = 200, ["Metal_Light"] = 162, ["White"] = 138 },
        // M6-V28.
        ["malazan_billet"] = new() { ["team"] = 806, ["Stone"] = 14, ["Walls"] = 6, ["Wood"] = 438, ["Wood_Light"] = 205 },
        ["malazan_depot"] = new() { ["team"] = 699, ["Stone"] = 12, ["Stone_Light"] = 438, ["Walls"] = 12, ["Wood"] = 2222, ["Wood_Light"] = 958, ["Metal_Light"] = 623 },
        ["malazan_crossbow_range"] = new() { ["team"] = 759, ["Stone"] = 422, ["Stone_Light"] = 816, ["Wood"] = 1001, ["Wood_Light"] = 311, ["White"] = 893, ["Red"] = 112 },
        ["malazan_watchtower"] = new() { ["team"] = 594, ["Stone"] = 557, ["Stone_Light"] = 723, ["Wood"] = 336, ["Wood_Light"] = 211 },
        // M6-V30: the Teblor buildings (their team tile holds the roof faces the manifest's teamFaces rules pick, plus any Main).
        ["teblor_longhouse"] = new() { ["team"] = 158, ["Stone"] = 38, ["Wood"] = 865, ["Wood_Light"] = 22 },
        ["teblor_hut"] = new() { ["team"] = 76, ["Stone"] = 16, ["Wood"] = 276 },
        ["teblor_woodcutters_lodge"] = new() { ["team"] = 65, ["Wood"] = 1234, ["Wood_Light"] = 911 },
        ["teblor_war_lodge"] = new() { ["team"] = 272, ["Stone"] = 248, ["Stone_Light"] = 64, ["Wood"] = 777, ["Wood_Light"] = 76, ["Gold"] = 176, ["Metal_Light"] = 108, ["White"] = 158 },
        ["teblor_javelin_ground"] = new() { ["team"] = 286, ["Wood"] = 1218, ["Wood_Light"] = 319, ["White"] = 770, ["Red"] = 84 },
        ["teblor_stone_cairn"] = new() { ["team"] = 59, ["Wood"] = 336, ["Wood_Light"] = 37 },
        ["teblor_bloodwood_carver"] = new() { ["team"] = 528, ["Wood"] = 1147, ["Wood_Light"] = 720, ["Yellow"] = 160 },
        ["teblor_shamans_circle"] = new() { ["team"] = 536, ["Wood"] = 2256, ["Wood_Light"] = 44 },
    };

    private readonly List<string> _failures = new();
    private string? _shots;
    private string _scratch = "";
    private JsonArray _entries = null!;
    private Match _match = null!;
    private Simulation _sim = null!;
    private BuildingViews _views = null!;
    private RtsCamera _camera = null!;
    private SelectionController _sel = null!;

    private World W => _sim.World;
    private BuildingStore B => _sim.World.Buildings;

    public override async void _Ready()
    {
        _scratch = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://building-model-test-{OS.GetProcessId()}"));
        string[] args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--shots") _shots = args[i + 1];
        if (_shots != null && DisplayServer.GetName() == "headless") _shots = null;
        // Figures only for the pictures (a Laborer and a Heavy Infantry for scale); capsules keep the headless rows light.
        UnitViews.UseModels = _shots != null;
        BuildingViews.UseModels = true;
        BuildingModels.ModelRoot = "res://assets/models/";
        try
        {
            GetTree().Root.Size = new Vector2I(1600, 900);
            await Frames(1);
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            GameData data = loaded.Data!;
            JsonNode manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "tools", "asset-manifest.json")))!;
            _entries = manifest["buildings"]!.AsArray();
            Check(_entries.Count > 0, "the manifest lists no building");
            FileRows(data);
            foreach (int faction in ManifestFactions(data)) await MatchRows(data, faction);
            FallbackRows(data);
            if (_shots != null)
                foreach (int faction in ManifestFactions(data)) await Pictures(data, faction);
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        UnitViews.UseModels = true;
        BuildingViews.UseModels = true;
        BuildingModels.ModelRoot = "res://assets/models/";
        try { if (Directory.Exists(_scratch)) Directory.Delete(_scratch, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        foreach (string f in _failures) GD.Print($"{Tag} FAIL: {f}");
        if (_failures.Count == 0) GD.Print($"{Tag} PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // The setup's colour table (eight colours, Crimson and Bone among them).
    private static uint[] TableColours()
    {
        UiText ui = UiText.Shared ?? throw new InvalidOperationException("ui.json did not load");
        var rgb = new uint[UiText.ColourCount];
        for (int c = 0; c < rgb.Length; c++) rgb[c] = ui.ColourRgb(c);
        return rgb;
    }

    // Criteria 2 and 6: each manifest building's file, mask, faces, and its materials in every table colour.
    private void FileRows(GameData data)
    {
        uint[] colours = TableColours();
        var models = new BuildingModels(data, colours);
        Check(models.ModelTypes == _entries.Count, $"{models.ModelTypes} building types drawn as models, the manifest lists {_entries.Count}");
        foreach (JsonNode? e in _entries)
        {
            string id = (string)e!["id"]!;
            int type = data.FindBuilding(id);
            if (!Check(type >= 0 && models.Has(type), $"{id}: not drawn as a model")) continue;
            BuildingDef def = data.Buildings[type];
            string want = "res://" + ((string)e["destination"]!)["game/".Length..];
            Check(BuildingModels.PathFor(data, def) == want, $"{id}: found at {BuildingModels.PathFor(data, def)}, the manifest writes {want}");

            Mesh mesh = models.MeshOf(type)!;
            var material = mesh.GetSurfaceCount() == 1 ? mesh.SurfaceGetMaterial(0) as BaseMaterial3D : null;
            UnitModels.Atlas atlas = models.AtlasOf(type)!;
            Texture2D texture = models.TextureOf(type)!;
            Godot.Collections.Array arrays = mesh.SurfaceGetArrays(0);
            int[] index = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            Vector2[] uv = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            int tris = index.Length / 3, cap = (int)e["maxTriangles"]!;
            int tile = (int)e["tileSize"]!;
            GD.Print($"{id}: {mesh.GetSurfaceCount()} surface, material '{material?.ResourceName}', {tris} triangles (cap {cap}), atlas {atlas.Grid} of {tile} ({texture.GetWidth()} x {texture.GetHeight()})");
            Check(mesh.GetSurfaceCount() == 1 && material?.ResourceName == UnitModels.TeamMaterialName && material.AlbedoTexture == texture,
                $"{id}: {mesh.GetSurfaceCount()} surfaces, material '{material?.ResourceName}'; want one surface, one 'team' material on the atlas");
            Check(tris > 0 && tris <= cap && tris <= BuildingTriangleBudget, $"{id}: {tris} triangles, over the manifest's {cap} or docs/04's {BuildingTriangleBudget}");
            Check(texture.GetWidth() <= MaxTexture && texture.GetHeight() <= MaxTexture && texture.GetWidth() == atlas.Grid.X * tile && texture.GetHeight() == atlas.Grid.Y * tile,
                $"{id}: texture {texture.GetWidth()} x {texture.GetHeight()}, want the {atlas.Grid} grid of {tile} and at most {MaxTexture}");

            // The mask, every texel of the imported texture.
            Image image = texture.GetImage();
            if (image.IsCompressed()) image.Decompress();
            image.Convert(Image.Format.Rgba8);
            byte[] px = image.GetData();
            int w = image.GetWidth();
            foreach (KeyValuePair<string, Vector2I> t in atlas.Tiles)
            {
                int wantA = t.Key == "team" ? AlphaTeam : AlphaPlain, lo = 255, hi = 0;
                for (int y = t.Value.Y * tile; y < (t.Value.Y + 1) * tile; y++)
                    for (int x = t.Value.X * tile; x < (t.Value.X + 1) * tile; x++)
                    {
                        int a = px[(y * w + x) * 4 + 3];
                        lo = Math.Min(lo, a);
                        hi = Math.Max(hi, a);
                    }
                Check(Math.Abs(lo - wantA) <= 1 && Math.Abs(hi - wantA) <= 1, $"{id}: tile {t.Key} alpha {lo}-{hi}, want {wantA} on every texel");
            }

            // The faces: one texel each, counted per tile against the first build.
            var counts = new Dictionary<string, int>();
            int spread = 0;
            for (int k = 0; k + 2 < index.Length; k += 3)
            {
                Vector2 a = uv[index[k]], b = uv[index[k + 1]], c = uv[index[k + 2]];
                if (a.DistanceTo(b) > 1e-5f || a.DistanceTo(c) > 1e-5f) spread++;
                string name = atlas.TileAt(a);
                counts[name] = counts.GetValueOrDefault(name) + 1;
            }
            var line = new List<string>();
            foreach (KeyValuePair<string, int> kv in counts) line.Add($"{kv.Key} {kv.Value}");
            GD.Print($"{id}: triangles per tile [{string.Join(", ", line)}]");
            Check(spread == 0, $"{id}: {spread} triangles read more than one texel");
            if (Check(TileTriangles.TryGetValue(id, out Dictionary<string, int>? pins), $"{id}: no pin of its triangles per tile"))
            {
                foreach (KeyValuePair<string, int> pin in pins!)
                    Check(counts.GetValueOrDefault(pin.Key) == pin.Value, $"{id}: {counts.GetValueOrDefault(pin.Key)} triangles on tile {pin.Key}, the first build had {pin.Value}");
                foreach (string have in counts.Keys) Check(pins.ContainsKey(have), $"{id}: triangles on a tile '{have}' the first build had none on");
            }

            // Team colour: the shader's rule on a texel of every tile, per colour of the table.
            ColourRows(models, type, id, atlas, px, w, tile, colours);
        }
    }

    private void ColourRows(BuildingModels models, int type, string id, UnitModels.Atlas atlas, byte[] px, int w, int tile, uint[] colours)
    {
        float ghost = new Color(FogView.ExploredBrightness, FogView.ExploredBrightness, FogView.ExploredBrightness).SrgbToLinear().R;
        var plain = new Dictionary<string, Color>();
        int teamOk = 0;
        for (int c = 0; c < colours.Length; c++)
        {
            Color player = UnitViews.ColorFromRgb(colours[c]);
            ShaderMaterial? team = models.TeamMaterial(type, c), dark = models.GhostMaterial(type, c);
            bool mats = team != null && dark != null
                        && team.GetShaderParameter("team_colour").AsColor() == player && dark.GetShaderParameter("team_colour").AsColor() == player
                        && team.GetShaderParameter("use_atlas").AsBool() && team.GetShaderParameter("atlas_grid").AsVector2() == new Vector2(atlas.Grid.X, atlas.Grid.Y)
                        && Math.Abs(team.GetShaderParameter("brightness").AsSingle() - 1f) < 1e-6f
                        && Math.Abs(dark.GetShaderParameter("brightness").AsSingle() - ghost) < 1e-6f
                        && team.GetShaderParameter("albedo_texture").As<Texture2D>() == models.TextureOf(type);
            Check(mats, $"{id} colour {c}: its team and remembered materials are not the atlas in {player.ToHtml(false)} at brightness 1 / {ghost}");
            foreach (KeyValuePair<string, Vector2I> t in atlas.Tiles)
            {
                int x = t.Value.X * tile + tile / 2, y = t.Value.Y * tile + tile / 2, o = (y * w + x) * 4;
                var texel = new Color(px[o] / 255f, px[o + 1] / 255f, px[o + 2] / 255f, px[o + 3] / 255f);
                Color drawn = Shade(texel, player);
                if (t.Key == "team")
                {
                    // Exactly the player colour at the team shade (linear): every channel in the player's proportion.
                    float shade = Mathf.Clamp(texel.SrgbToLinear().R * 1.25f, 0f, 1f);
                    Color lin = player.SrgbToLinear(), got = drawn.SrgbToLinear();
                    bool ok = shade > 0.5f && Math.Abs(got.R - lin.R * shade) < 0.01f && Math.Abs(got.G - lin.G * shade) < 0.01f && Math.Abs(got.B - lin.B * shade) < 0.01f;
                    if (Check(ok, $"{id} colour {c} ({player.ToHtml(false)}): a team texel {texel.ToHtml()} draws {drawn.ToHtml(false)}, not the player colour at shade {shade:0.###}")) teamOk++;
                }
                else if (plain.TryGetValue(t.Key, out Color first)) Check(drawn == first, $"{id} colour {c}: tile {t.Key} draws {drawn.ToHtml(false)}, colour 0 drew {first.ToHtml(false)}");
                else
                {
                    plain[t.Key] = drawn;
                    Check(drawn.ToHtml(false) != player.ToHtml(false), $"{id}: tile {t.Key} draws the player colour");
                }
            }
        }
        GD.Print($"{id}: team texels take each of {colours.Length} colours ({teamOk} ok); {plain.Count} other tiles draw the same in every colour");
    }

    // team_colour.gdshader's albedo rule, in sRGB (AtlasModelTest's copy).
    private static Color Shade(Color texel, Color player)
    {
        if (texel.A > 0.625f) return new Color(texel.R, texel.G, texel.B);
        float shade = Mathf.Clamp(texel.SrgbToLinear().R * 1.25f, 0f, 1f);
        Color lin = player.SrgbToLinear();
        return new Color(lin.R * shade, lin.G * shade, lin.B * shade).LinearToSrgb();
    }

    // The model types, by the manifest's ids, in its order (M6-V30: only faction `faction`'s when it is 0 or more).
    private int[] ModelTypes(GameData data, int faction = -1)
    {
        var types = new List<int>();
        foreach (JsonNode? e in _entries)
        {
            int t = data.FindBuilding((string)e!["id"]!);
            if (faction < 0 || (t >= 0 && data.Buildings[t].Faction == faction)) types.Add(t);
        }
        return types.ToArray();
    }

    // M6-V30: the factions the manifest's buildings belong to, in the order of their first entry.
    private List<int> ManifestFactions(GameData data)
    {
        var list = new List<int>();
        foreach (int t in ModelTypes(data))
            if (t >= 0 && !list.Contains(data.Buildings[t].Faction)) list.Add(data.Buildings[t].Faction);
        return list;
    }

    // M6-V30: faction `faction`'s first unit / building of a slot (its worker, its line unit, its house, its halls).
    private static int UnitOf(GameData data, int faction, UnitSlot slot)
    {
        for (int u = 0; u < data.Units.Length; u++) if (data.Units[u].Faction == faction && data.Units[u].Slot == slot) return u;
        return -1;
    }

    private static int BuildingOf(GameData data, int faction, BuildingSlot slot)
    {
        for (int b = 0; b < data.Buildings.Length; b++) if (data.Buildings[b].Faction == faction && data.Buildings[b].Slot == slot) return b;
        return -1;
    }

    // The faction of the match the rows run in (M6-V30), and the pictures' name prefix.
    private int _faction;
    private string _prefix = "";

    private async Task MatchRows(GameData data, int faction)
    {
        _faction = faction;
        string key = data.Factions[faction].Key;
        _prefix = faction == ManifestFactions(data)[0] ? "" : key + "-";
        GD.Print($"match rows: {key}");
        StartMatch(data, "--players", "3", "--faction", "1", key, "--faction", "2", key, "--faction", "3", key, "--resources", "high",
            "--zoom", CameraLimits.DefaultZoom.ToString(CultureInfo.InvariantCulture));
        if (_views.Models is not BuildingModels models)
        {
            Check(false, "the match's building views have no models");
            return;
        }
        int[] types = ModelTypes(data, faction);
        int boxType = -1;
        for (int t = 0; t < data.Buildings.Length && boxType < 0; t++) if (data.Buildings[t].Faction == faction && !models.Has(t)) boxType = t;
        NVector2 west = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0];

        // Finished models: the fit, the ground, the bar, the click (criteria 3 and 5).
        var finished = new List<int>();
        float x = -6f;
        foreach (int type in types)
        {
            int slot = Spawn(0, type, west + new NVector2(x, 0f));
            x -= data.Buildings[type].FootprintWidth * MapConstants.CellSize + 4f;
            if (Check(slot >= 0, $"{data.Buildings[type].Key}: not spawned")) finished.Add(slot);
        }
        int boxSlot = boxType >= 0 ? Spawn(0, boxType, west + new NVector2(x, 0f)) : -1;
        _views.Sync(W);
        await Frames(1);
        foreach (int slot in finished) await FitRow(slot);
        if (Check(boxSlot >= 0, $"no {key} type without a model to spawn"))
            Check(_views.IsShown(boxSlot) && !_views.ShownAsModel(boxSlot) && _views.BoxOf(boxSlot).Visible && _views.ModelOf(boxSlot) == null,
                $"{data.Buildings[boxType].Key} (no model file): not its box");

        // Criterion 4: a site of each model type rises as the box and is the model from the frame after the tick it finishes.
        // M6-V28: a type that needs Age II (the Watchtower) is built by player 2, which researches it first: player 0's stock
        // can't pay Age II and every site of the six (a player-0 Laborer beside the site sees it for the view).
        foreach (int type in types)
        {
            int pl = Aged(data, type) ? 2 : 0;
            if (pl != 0) AgeTwo(data, pl, west + new NVector2(-30f, 110f));
            SiteRow(data, type, west + new NVector2(0f, 26f + 12f * Array.IndexOf(types, type)), pl);
        }

        // A hurt model's hit-point bar over its top (criterion 5), and a destroyed one leaves nothing drawn (criterion 4).
        await HurtAndDestroy(data, finished);

        // Criterion 4: remembered models under the fog, and one razed out of sight. M6-VH9 (BUG-1632 item 1): the allocation
        // row runs inside it, while the remembered models are drawn.
        await Remembered(data, types, west);
        await EndMatch();
    }

    // M6-V28: player 0 sees another player's site through a building of its own east of it (a building's sight, and no
    // unit to start a fight with the builders).
    private void Watch(GameData data, BuildingDef def, NVector2 c) =>
        Check(Spawn(0, BuildingOf(data, _faction, BuildingSlot.House), c + new NVector2(def.FootprintWidth * MapConstants.CellSize / 2f + 4f, 0f)) >= 0,
            $"{def.Key}: no watcher house beside {c}");

    // M6-V30: how far outside a footprint's front edge a builder is spawned: 0.8 m for a 0.4 m worker (the Malazan rows as
    // before), more for a wider one (a 0.75 m Teblor Gatherer at 0.8 m stood in the footprint: "UnitInTheWay").
    private float Clear(GameData data) => 0.8f + Math.Max(0f, data.Units[UnitOf(data, _faction, UnitSlot.Worker)].Radius - 0.4f);

    private static bool Aged(GameData data, int type) => data.Buildings[type].RequiresTechs.Contains(data.FindTech("age_ii"));

    // M6-V28: player `player` gets a town hall, an infantry hall and a ranged hall near `near` (spawned) and researches Age II
    // at the hall, ticking until it has it (Age II needs two kinds of hall). M6-V30: the match's faction's.
    private void AgeTwo(GameData data, int player, NVector2 near)
    {
        int age = data.FindTech("age_ii");
        if (age < 0 || W.HasTech(player, age)) return;
        int hall = Spawn(player, BuildingOf(data, _faction, BuildingSlot.TownHall), near);
        Check(Spawn(player, BuildingOf(data, _faction, BuildingSlot.InfantryHall), near + new NVector2(14f, 0f)) >= 0, "Age II: no infantry hall");
        Check(Spawn(player, BuildingOf(data, _faction, BuildingSlot.RangedHall), near + new NVector2(26f, 0f)) >= 0, "Age II: no ranged hall");
        if (!Check(hall >= 0, "Age II: no town hall to research it at")) return;
        _sim.Enqueue(Command.Research(player, Centre(hall), age));
        for (int t = 0; t < 4000 && !W.HasTech(player, age); t++) _sim.Tick();
        Check(W.HasTech(player, age), $"Age II not researched by player {player} (gold {W.Gold[player]}, wood {W.Wood[player]})");
    }

    private int OwnUnitsNear(int player, NVector2 at, float r)
    {
        int n = 0;
        for (int i = 0; i < W.Units.Capacity; i++)
            if (W.Units.Alive[i] && W.Units.Owner[i] == player && NVector2.Distance(W.Units.Position[i], at) < r) n++;
        return n;
    }

    private int CountShownModels()
    {
        int n = 0;
        for (int i = 0; i < B.Capacity; i++) if (_views.ShownAsModel(i)) n++;
        return n;
    }

    private int CountGhostModels()
    {
        int n = 0;
        for (int i = 0; i < B.Capacity; i++) if (_views.GhostShownAsModel(i)) n++;
        return n;
    }

    // The footprint of slot `slot` in world meters: x0, x1, z0, z1.
    private (float X0, float X1, float Z0, float Z1) Footprint(int def, int anchor)
    {
        BuildingDef d = W.Data.Buildings[def];
        float x0 = anchor % W.NavGrid.Width * MapConstants.CellSize, z0 = anchor / W.NavGrid.Width * MapConstants.CellSize;
        return (x0, x0 + d.FootprintWidth * MapConstants.CellSize, z0, z0 + d.FootprintHeight * MapConstants.CellSize);
    }

    // A finished model: inside its footprint, on the ground, box hidden; the bar over its top; a click on the footprint selects it.
    private async Task FitRow(int slot)
    {
        BuildingDef def = W.Data.Buildings[B.TypeId[slot]];
        MeshInstance3D? m = _views.ModelOf(slot);
        if (!Check(_views.ShownAsModel(slot) && m != null && m.Visible && !_views.BoxOf(slot).Visible && !_views.ShownAsSite(slot),
                $"{def.Key}: finished, not drawn as its model alone")) return;
        Check(m!.MaterialOverride == _views.Models!.TeamMaterial(B.TypeId[slot], B.Owner[slot]), $"{def.Key}: not in its owner's team material");
        Aabb box = m.GlobalTransform * m.Mesh.GetAabb();
        (float x0, float x1, float z0, float z1) = Footprint(B.TypeId[slot], B.Cell[slot]);
        NVector2 c = Centre(slot);
        float ground = TerrainHeight.At(W.Heightmap, c.X, c.Y);
        float over = Math.Max(Math.Max(x0 - box.Position.X, box.End.X - x1), Math.Max(z0 - box.Position.Z, box.End.Z - z1));
        GD.Print($"  {def.Key}: footprint {x1 - x0} x {z1 - z0} m, model {box.Size.X:0.###} x {box.Size.Z:0.###} x {box.Size.Y:0.###} m, "
                 + $"nearest edge gap {-over:0.###} m, base {box.Position.Y - ground:0.####} m over the ground, bar at {_views.BarY(slot):0.###} m");
        Check(over <= EdgeSlack, $"{def.Key}: a part {over} m beyond its footprint's edge (bound {EdgeSlack})");
        Check(over <= -BuildingModels.FitMargin + 1e-3f, $"{def.Key}: the fit leaves {-over} m to the nearest edge, want {BuildingModels.FitMargin}");
        Check(Math.Abs(box.Position.Y - ground) < 0.01f, $"{def.Key}: base {box.Position.Y} m, ground {ground} m");
        Check(Math.Abs((box.Position.X + box.End.X) / 2f - c.X) < 0.01f && Math.Abs((box.Position.Z + box.End.Z) / 2f - c.Y) < 0.01f, $"{def.Key}: plan not centred on its footprint");
        float top = box.End.Y - ground;
        Check(Math.Abs(top - _views.Models.Top(B.TypeId[slot])) < 1e-3f, $"{def.Key}: drawn top {top}, the models say {_views.Models.Top(B.TypeId[slot])}");
        Check(_views.BarY(slot) - BuildingViews.BarThickness / 2f > top, $"{def.Key}: bar at {_views.BarY(slot)} m, model top {top} m");
        Check(Math.Abs(_views.ViewOf(slot)!.GetNode<MeshInstance3D>("BarBack").Position.Y - _views.BarY(slot)) < 1e-4f, $"{def.Key}: bar back not at the bar height");

        // The click: the footprint's centre 1 m up, inside the unchanged 3 m hit box.
        _camera.SetFocus(c.X, c.Y);
        await Frames(1);
        _sel.ClearBuilding();
        Click(_camera.UnprojectPosition(new Vector3(c.X, ground + 1f, c.Y)));
        Check(_sel.SelectedBuilding == slot, $"{def.Key}: a click on its footprint selected {_sel.SelectedBuilding}");
        _sel.ClearBuilding();
    }

    private void SiteRow(GameData data, int type, NVector2 near, int pl)
    {
        BuildingDef def = data.Buildings[type];
        int laborer = UnitOf(data, _faction, UnitSlot.Worker);
        int anchor = OpenAnchor(def, near, clearOfUnits: true);
        if (!Check(anchor >= 0, $"{def.Key} site: no open anchor")) return;
        NVector2 c = StartBase.FootprintCenter(W.NavGrid, def, anchor);
        if (pl != 0) Watch(data, def, c);
        var workers = new List<int>();
        for (int k = 0; k < 4; k++) _sim.Enqueue(Command.SpawnUnit(pl, laborer, c + new NVector2(-3f + 2f * k, def.FootprintHeight * MapConstants.CellSize / 2f + Clear(data))));
        _sim.Tick();
        _sim.Tick();
        for (int i = 0; i < W.Units.Capacity; i++)
            if (W.Units.Alive[i] && W.Units.Owner[i] == pl && W.Units.TypeId[i] == laborer && W.Units.State[i] == UnitState.Idle
                && NVector2.Distance(W.Units.Position[i], c) < def.FootprintHeight * MapConstants.CellSize) workers.Add(i);
        if (!Check(workers.Count > 0, $"{def.Key} site: no laborer")) return;
        // M6-V28: the Laborers' sight explores the footprint first (a Build needs explored ground; player 2 has none here).
        for (int t = 0; t < 10; t++) _sim.Tick();
        NVector2 at = PlacementGhost.AnchorPoint(W.NavGrid, anchor);
        foreach (int u in workers) _sim.Enqueue(Command.Build(pl, new EntityHandle(u, W.Units.Generation[u]), type, at));
        int site = -1;
        for (int t = 0; t < 200 && site < 0; t++)
        {
            _sim.Tick();
            site = B.SlotAt(anchor % W.NavGrid.Width, anchor / W.NavGrid.Width);
        }
        W.CanPlace(pl, type, anchor, out PlacementError placeWhy);
        if (!Check(site >= 0 && B.UnderConstruction[site], $"{def.Key} site: none at anchor {anchor} (gold {W.Gold[pl]}, wood {W.Wood[pl]}, placement {placeWhy}, {workers.Count} workers)")) return;
        int gen = B.Generation[site], frames = 0, bad = 0, switchedAfter = -1;
        _views.Sync(W);
        for (int t = 0; t < 6000 && B.Alive[site] && B.Generation[site] == gen; t++)
        {
            bool wasSite = B.UnderConstruction[site];
            _sim.Tick();
            _views.Sync(W);
            frames++;
            if (B.UnderConstruction[site])
            {
                MeshInstance3D? m = _views.ModelOf(site);
                if (!_views.ShownAsSite(site) || _views.ShownAsModel(site) || !_views.BoxOf(site).Visible || (m != null && m.Visible)
                    || _views.BarY(site) != BuildingViews.BarHeightOf(def)) bad++;
                continue;
            }
            if (wasSite) switchedAfter = frames;
            break;
        }
        MeshInstance3D? model = _views.ModelOf(site);
        GD.Print($"  {def.Key} site: {frames} ticks as a box, then the model ({(switchedAfter > 0 ? "on the frame after the finishing tick" : "never")}); bar {_views.BarY(site):0.###} m");
        Check(bad == 0, $"{def.Key} site: {bad} frames not drawn as the rising box with its bar over the box");
        Check(switchedAfter > 0 && _views.ShownAsModel(site) && model != null && model.Visible && !_views.BoxOf(site).Visible && !_views.ShownAsSite(site),
            $"{def.Key} site: not the model on the frame after it finished");
        Check(model != null && _views.BarY(site) - BuildingViews.BarThickness / 2f > _views.Models!.Top(type), $"{def.Key} site finished: the bar not over the model's top");

        // Another site of the type, cancelled: nothing left drawn. South of the builders, in ground they have explored.
        int anchor2 = OpenAnchor(def, c + new NVector2(0f, def.FootprintHeight * MapConstants.CellSize + 6f), clearOfUnits: true);
        if (!Check(anchor2 >= 0, $"{def.Key} cancel: no open anchor")) return;
        NVector2 at2 = PlacementGhost.AnchorPoint(W.NavGrid, anchor2);
        if (pl != 0) Watch(data, def, StartBase.FootprintCenter(W.NavGrid, def, anchor2));
        // A Laborer beside it explores the ground (a Build needs every footprint cell explored).
        _sim.Enqueue(Command.SpawnUnit(pl, laborer, StartBase.FootprintCenter(W.NavGrid, def, anchor2) + new NVector2(0f, def.FootprintHeight * MapConstants.CellSize / 2f + Clear(data))));
        // Through a fog update (every 4th tick): a Build onto unexplored ground is refused at once.
        for (int t = 0; t < 6; t++) _sim.Tick();
        W.CanPlace(pl, type, anchor2, out PlacementError why);
        _sim.Enqueue(Command.Build(pl, new EntityHandle(workers[0], W.Units.Generation[workers[0]]), type, at2));
        int site2 = -1;
        for (int t = 0; t < 400 && site2 < 0; t++)
        {
            _sim.Tick();
            site2 = B.SlotAt(anchor2 % W.NavGrid.Width, anchor2 / W.NavGrid.Width);
        }
        if (!Check(site2 >= 0, $"{def.Key} cancel: no second site (placement {why}, gold {W.Gold[pl]}, wood {W.Wood[pl]})")) return;
        _views.Sync(W);
        Check(_views.IsShown(site2) && _views.ShownAsSite(site2), $"{def.Key} cancel: the second site not drawn");
        _sim.Enqueue(Command.Cancel(pl, StartBase.FootprintCenter(W.NavGrid, def, anchor2)));
        for (int t = 0; t < 4 && B.Alive[site2]; t++) _sim.Tick();
        _views.Sync(W);
        Check(!B.Alive[site2] && !_views.IsShown(site2) && !_views.ViewOf(site2)!.Visible, $"{def.Key} cancel: the cancelled site still drawn (alive {B.Alive[site2]})");
    }

    // Player 2's Heavy Infantry hurt then raze each finished model; the bar over the top while hurt, nothing drawn after.
    private async Task HurtAndDestroy(GameData data, List<int> slots)
    {
        int heavy = UnitOf(data, _faction, UnitSlot.Line);
        foreach (int slot in slots)
        {
            BuildingDef def = W.Data.Buildings[B.TypeId[slot]];
            int gen = B.Generation[slot];
            NVector2 c = Centre(slot);
            float r = def.FootprintHeight * MapConstants.CellSize / 2f + 1.2f;
            for (int k = 0; k < 16; k++) _sim.Enqueue(Command.SpawnUnit(1, heavy, c + new NVector2(-6f + 0.8f * k, k % 2 == 0 ? r : -r)));
            _sim.Tick();
            _sim.Tick();
            var attackers = new List<int>();
            for (int i = 0; i < W.Units.Capacity; i++)
                if (W.Units.Alive[i] && W.Units.Owner[i] == 1 && W.Units.TypeId[i] == heavy && NVector2.Distance(W.Units.Position[i], c) < r + 6f) attackers.Add(i);
            foreach (int u in attackers) _sim.Enqueue(Command.Attack(1, new EntityHandle(u, W.Units.Generation[u]), new EntityHandle(slot, gen), isBuilding: true));
            bool hurtSeen = false;
            for (int t = 0; t < 8000 && B.Alive[slot] && B.Generation[slot] == gen; t++)
            {
                _sim.Tick();
                if (hurtSeen || B.Hp[slot] >= def.Hp) continue;
                _views.Sync(W);
                BuildingBarKind bar = _views.ShownBar(slot, out float fill);
                float top = _views.Models!.Top(B.TypeId[slot]);
                MeshInstance3D f = _views.BarFillOf(slot);
                Check(bar == BuildingBarKind.HitPoints && f.Visible && fill < 1f && f.Position.Y - BuildingViews.BarThickness / 2f > top,
                    $"{def.Key} hurt: bar {bar} visible {f.Visible} at {f.Position.Y} m, model top {top} m");
                GD.Print($"  {def.Key} hurt: hp {B.Hp[slot]} / {def.Hp}, hit-point bar at {f.Position.Y:0.###} m over a {top:0.###} m model");
                hurtSeen = true;
            }
            _views.Sync(W);
            await Frames(1);
            Check(hurtSeen, $"{def.Key}: never hurt");
            Check(!(B.Alive[slot] && B.Generation[slot] == gen), $"{def.Key}: not destroyed");
            MeshInstance3D? m = _views.ModelOf(slot);
            Check(!_views.IsShown(slot) && !_views.ViewOf(slot)!.Visible && !_views.ShownAsModel(slot), $"{def.Key} destroyed: still drawn");
            // Staging (a test write between ticks): the attackers go, so they don't fight the next rows' scouts.
            foreach (int u in attackers) if (W.Units.Alive[u]) W.Units.Free(new EntityHandle(u, W.Units.Generation[u]));
            _sim.Tick();
        }
    }

    // An enemy (player 1) Keep and Barracks scouted and left: remembered as their darkened models; one razed by player 2 out of
    // player 0's sight stays remembered until the scout sees the spot again.
    private async Task Remembered(GameData data, int[] types, NVector2 west)
    {
        NVector2 far = west + new NVector2(60f, 0f);
        var enemy = new List<int>();
        float dx = 0f;
        foreach (int type in types)
        {
            int slot = Spawn(1, type, far + new NVector2(dx, 0f));
            dx += data.Buildings[type].FootprintWidth * MapConstants.CellSize + 4f;
            if (Check(slot >= 0, $"{data.Buildings[type].Key}: enemy not spawned")) enemy.Add(slot);
        }
        if (enemy.Count == 0) return;
        // M6-V28: one scout in front of each (the row of six is wider than one Laborer's sight; the enemy Watchtower may shoot
        // its scout, whose death leaves the spot out of sight just as walking away does).
        int laborer = UnitOf(data, _faction, UnitSlot.Worker);
        var aliveBefore = new bool[W.Units.Capacity];
        for (int i = 0; i < aliveBefore.Length; i++) aliveBefore[i] = W.Units.Alive[i];
        foreach (int s in enemy) _sim.Enqueue(Command.SpawnUnit(0, laborer, Centre(s) + new NVector2(0f, W.Data.Buildings[B.TypeId[s]].FootprintHeight * MapConstants.CellSize / 2f + 5f)));
        _sim.Tick();
        _sim.Tick();
        var scouts = new List<int>();
        for (int i = 0; i < W.Units.Capacity; i++)
            if (W.Units.Alive[i] && !aliveBefore[i] && W.Units.Owner[i] == 0 && W.Units.TypeId[i] == laborer) scouts.Add(i);
        if (!Check(scouts.Count == enemy.Count, $"remembered: {scouts.Count} scouts for {enemy.Count} buildings")) return;
        for (int t = 0; t < 100 && !enemy.TrueForAll(s => W.Fog.CanSeeBuilding(0, s)); t++) _sim.Tick();
        _views.Sync(W);
        foreach (int s in enemy) Check(_views.ShownAsModel(s), $"{W.Data.Buildings[B.TypeId[s]].Key}: the scouted enemy building not drawn as its model");
        // Staging between ticks: the scouts go, the spot falls out of sight (QaM6V26Test's way; walking six away took long).
        foreach (int scout in scouts)
            if (W.Units.Alive[scout]) W.Units.Free(new EntityHandle(scout, W.Units.Generation[scout]));
        for (int t = 0; t < 1500 && !enemy.TrueForAll(s => _views.IsGhostShown(s)); t++)
        {
            _sim.Tick();
            _views.Sync(W);
        }
        foreach (int s in enemy)
        {
            BuildingDef def = W.Data.Buildings[B.TypeId[s]];
            MeshInstance3D? g = _views.GhostModelOf(s);
            if (!Check(_views.GhostShownAsModel(s) && g != null && g.Visible && !_views.GhostBoxOf(s)!.Visible && !_views.IsShown(s),
                    $"{def.Key}: not remembered as its model alone (ghost {_views.IsGhostShown(s)}, as model {_views.GhostShownAsModel(s)}, "
                    + $"seen now {W.Fog.CanSeeBuilding(0, s)}, own units within 20 m {OwnUnitsNear(0, Centre(s), 20f)})")) continue;
            Check(g!.MaterialOverride == _views.Models!.GhostMaterial(B.TypeId[s], 1), $"{def.Key}: the remembered model not in the darkened material");
            Aabb box = g.GlobalTransform * g.Mesh.GetAabb();
            (float x0, float x1, float z0, float z1) = Footprint(B.TypeId[s], B.Cell[s]);
            NVector2 c = Centre(s);
            float ground = TerrainHeight.At(W.Heightmap, c.X, c.Y);
            bool inside = box.Position.X >= x0 - EdgeSlack && box.End.X <= x1 + EdgeSlack && box.Position.Z >= z0 - EdgeSlack && box.End.Z <= z1 + EdgeSlack;
            Check(inside && Math.Abs(box.Position.Y - ground) < 0.01f, $"{def.Key}: the remembered model not on its footprint's ground ({box})");
            GD.Print($"  {def.Key}: remembered as its model, darkened, top {box.End.Y - ground:0.###} m");
        }
        if (_shots != null)
        {
            _camera.SetZoom(CameraLimits.DefaultZoom);
            NVector2 m = (Centre(enemy[0]) + Centre(enemy[^1])) / 2f;
            _camera.SetFocus(m.X, m.Y + 6f);
            await Shot("remembered-under-fog-40m");
        }

        // 20 frames with models and remembered models drawn allocate nothing (M6-VH9, BUG-1632 item 1: measured here, while the
        // remembered models are drawn; it ran after they were gone).
        _views.Sync(W);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int f = 0; f < 20; f++) _views.Sync(W);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        int ghosts = CountGhostModels();
        GD.Print($"20 syncs with {CountShownModels()} models and {ghosts} remembered models drawn: {bytes} bytes");
        Check(ghosts > 0, "the allocation row drew no remembered model");
        Check(bytes == 0, $"20 syncs with models and {ghosts} remembered models allocated {bytes} bytes");

        // Player 2 razes the last one out of player 0's sight: still remembered, until the scout sees the spot again.
        int target = enemy[^1], tgen = B.Generation[target];
        BuildingDef tdef = W.Data.Buildings[B.TypeId[target]];
        NVector2 tc = Centre(target);
        int heavy = UnitOf(data, _faction, UnitSlot.Line);
        float rr = tdef.FootprintHeight * MapConstants.CellSize / 2f + 1.2f;
        for (int k = 0; k < 16; k++) _sim.Enqueue(Command.SpawnUnit(2, heavy, tc + new NVector2(-6f + 0.8f * k, k % 2 == 0 ? rr : -rr)));
        _sim.Tick();
        _sim.Tick();
        var raiders = new List<int>();
        for (int i = 0; i < W.Units.Capacity; i++)
            if (W.Units.Alive[i] && W.Units.Owner[i] == 2 && NVector2.Distance(W.Units.Position[i], tc) < rr + 6f) raiders.Add(i);
        foreach (int u in raiders) _sim.Enqueue(Command.Attack(2, new EntityHandle(u, W.Units.Generation[u]), new EntityHandle(target, tgen), isBuilding: true));
        for (int t = 0; t < 8000 && B.Alive[target] && B.Generation[target] == tgen; t++) _sim.Tick();
        _views.Sync(W);
        bool razed = !(B.Alive[target] && B.Generation[target] == tgen);
        Check(razed && !W.Fog.CanSeeBuilding(0, target), $"{tdef.Key}: not razed out of sight (razed {razed})");
        Check(_views.GhostShownAsModel(target) && _views.GhostModelOf(target)!.Visible, $"{tdef.Key}: razed out of sight, the remembered model went before the spot was seen");
        // Staging: the raiders go; a fresh scout of player 0 at the spot sees it empty.
        foreach (int u in raiders) if (W.Units.Alive[u]) W.Units.Free(new EntityHandle(u, W.Units.Generation[u]));
        _sim.Enqueue(Command.SpawnUnit(0, laborer, tc + new NVector2(0f, 3f)));
        for (int t = 0; t < 200 && _views.IsGhostShown(target); t++)
        {
            _sim.Tick();
            _views.Sync(W);
        }
        MeshInstance3D? gone = _views.GhostModelOf(target);
        Check(!_views.IsGhostShown(target) && gone != null && !gone.Visible && !_views.GhostBoxOf(target)!.Visible,
            $"{tdef.Key}: seen razed, the remembered model still drawn");
        GD.Print($"  {tdef.Key}: razed out of sight stayed remembered, gone once the spot was seen");
        // The scout back where the other one is seen live again: the live model, its remembered model hidden.
        int other = enemy[0];
        _sim.Enqueue(Command.SpawnUnit(0, laborer, Centre(other) + new NVector2(0f, data.Buildings[B.TypeId[other]].FootprintHeight * MapConstants.CellSize / 2f + 1.5f)));
        for (int t = 0; t < 200 && !W.Fog.CanSeeBuilding(0, other); t++) _sim.Tick();
        _sim.Tick();
        _views.Sync(W);
        Check(_views.ShownAsModel(other) && !_views.GhostShownAsModel(other) && !(_views.GhostModelOf(other)?.Visible ?? false),
            $"{W.Data.Buildings[B.TypeId[other]].Key}: seen again, not the live model alone");
    }

    // Criterion 9: LFS pointers, missing files and models off give boxes; a pointer one warning, no ERROR.
    private void FallbackRows(GameData data)
    {
        const string pointer = "version https://git-lfs.github.com/spec/v1\noid sha256:4d7a214614ab2935c943f9e0ff69d22eadbb8f32b1258daaa5e2ca24d17e2393\nsize 348712\n";
        uint[] colours = { 0xC03030, 0x3060D0 };
        int written = 0;
        foreach (JsonNode? e in _entries)
        {
            int type = data.FindBuilding((string)e!["id"]!);
            BuildingDef def = data.Buildings[type];
            string dir = Path.Combine(_scratch, "pointers", data.Factions[def.Faction].Key, def.Key);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, def.Key + ".glb"), pointer);
            File.WriteAllText(Path.Combine(dir, def.Key + "_atlas.png"), pointer);
            written++;
        }
        int warnings = BuildingModels.Warnings;
        BuildingModels.ModelRoot = Path.Combine(_scratch, "pointers").Replace('\\', '/') + "/";
        var pointers = new BuildingModels(data, colours);
        Check(pointers.ModelTypes == 0 && BuildingModels.Warnings - warnings == written,
            $"pointers: {pointers.ModelTypes} model types, {BuildingModels.Warnings - warnings} warnings for {written} pointer files");
        Check(BoxesDrawn(data, colours), "pointers: a finished model type not drawn as its box");
        BuildingModels.ModelRoot = Path.Combine(_scratch, "missing").Replace('\\', '/') + "/";
        var missing = new BuildingModels(data, colours);
        Check(missing.ModelTypes == 0, $"missing files: {missing.ModelTypes} model types");
        BuildingModels.ModelRoot = "res://assets/models/";
        BuildingViews.UseModels = false;
        Check(BoxesDrawn(data, colours), "models off: a finished model type not drawn as its box");
        BuildingViews.UseModels = true;
        GD.Print($"fallbacks: {written} pointer files give {pointers.ModelTypes} models and {BuildingModels.Warnings - warnings} warnings; missing files and models off give boxes");
    }

    // A fresh view over a fresh sim with one finished building of each manifest type: every one drawn as its box.
    private bool BoxesDrawn(GameData data, uint[] colours)
    {
        var sim = new Simulation(new SimConfig(1, 2, 8, 4096) { Data = data, Combat = false });
        int[] types = ModelTypes(data);
        NVector2 p = new(40f, 40f);
        foreach (int type in types)
        {
            sim.Enqueue(Command.SpawnBuilding(0, type, p));
            p += new NVector2(14f, 0f);
        }
        sim.Tick();
        sim.Tick();
        var views = new BuildingViews();
        AddChild(views);
        views.Bind(data, sim.World.Buildings.Capacity, colours);
        views.Sync(sim.World);
        bool ok = BuildingViews.UseModels == (views.Models != null);
        int drawn = 0;
        for (int i = 0; i < sim.World.Buildings.Capacity; i++)
        {
            if (!sim.World.Buildings.Alive[i]) continue;
            drawn++;
            ok &= views.IsShown(i) && !views.ShownAsModel(i) && views.BoxOf(i).Visible && views.ModelOf(i) == null;
        }
        ok &= drawn == types.Length;
        RemoveChild(views);
        views.QueueFree();
        return ok;
    }

    // Criterion 7's pictures (windowed): no fog, four players. M6-V30: once per faction, four seats of it; a faction after
    // the first prefixes its pictures' names with its id.
    private async Task Pictures(GameData data, int faction)
    {
        _faction = faction;
        string key = data.Factions[faction].Key;
        _prefix = faction == ManifestFactions(data)[0] ? "" : key + "-";
        StartMatch(data, "--players", "4", "--no-fog", "--no-combat", "--resources", "high", "--faction", "1", key, "--faction", "2", key,
            "--faction", "3", key, "--faction", "4", key);
        _match.GetNode<SimRunner>("SimRunner").ProcessMode = ProcessModeEnum.Inherit; // figures animate
        int[] types = ModelTypes(data, faction);
        int laborer = UnitOf(data, _faction, UnitSlot.Worker), heavy = UnitOf(data, _faction, UnitSlot.Line);
        NVector2 o = StartLayout.Block(W.NavGrid, 1, west: true, 1f)[0] + new NVector2(-40f, -16f);
        // Rows: per type, players 1 and 2 side by side, a Laborer and a Heavy Infantry before each building.
        var centres = new List<NVector2>();
        float z = 0f;
        foreach (int type in types)
        {
            BuildingDef def = data.Buildings[type];
            float x = 0f;
            for (int p = 0; p < 2; p++)
            {
                int slot = Spawn(p, type, o + new NVector2(x, z));
                if (slot < 0) continue;
                NVector2 c = Centre(slot);
                centres.Add(c);
                float front = def.FootprintHeight * MapConstants.CellSize / 2f + 1.6f;
                _sim.Enqueue(Command.SpawnUnit(p, laborer, c + new NVector2(-1.2f, front)));
                _sim.Enqueue(Command.SpawnUnit(p, heavy, c + new NVector2(1.2f, front)));
                x += def.FootprintWidth * MapConstants.CellSize + 6f;
            }
            z += data.Buildings[type].FootprintHeight * MapConstants.CellSize + 7f;
        }
        // Everyone faces the camera with a short step toward it.
        for (int t = 0; t < 2; t++) _sim.Tick();
        for (int i = 0; i < W.Units.Capacity; i++)
            if (W.Units.Alive[i]) _sim.Enqueue(Command.Move(W.Units.Owner[i], new EntityHandle(i, W.Units.Generation[i]), W.Units.Position[i] + new NVector2(0f, 0.6f)));
        for (int t = 0; t < 40; t++) _sim.Tick();
        NVector2 mid = new((centres[0].X + centres[^1].X) / 2f + 6f, (centres[0].Y + centres[^1].Y) / 2f);
        _camera.SetZoom(CameraLimits.DefaultZoom);
        _camera.SetFocus(mid.X, mid.Y + 2f);
        await Shot("buildings-40m");
        await Shot("buildings-40m-gray", gray: true);
        _camera.SetZoom(CameraLimits.MinZoom);
        foreach ((NVector2 c, int k) in Indexed(centres))
        {
            _camera.SetFocus(c.X, c.Y + 3f);
            await Shot($"close-{k}-20m");
        }
        _camera.SetZoom(CameraLimits.MaxZoom);
        _camera.SetFocus(mid.X, mid.Y);
        await Shot("buildings-60m");
        // A site of each type to the right of the rows (player 1's, a Laborer at each, part-way up), beside a finished one.
        // M6-V28: the Watchtower's site needs Age II, which player 0's stock can't pay beside every site: player 2 builds it.
        var sites = new List<NVector2>();
        float sz = 0f;
        foreach (int type in types)
        {
            BuildingDef def = data.Buildings[type];
            int anchor = OpenAnchor(def, o + new NVector2(32f, sz));
            sz += def.FootprintHeight * MapConstants.CellSize + 7f;
            if (anchor < 0) continue;
            int pl = Aged(data, type) ? 2 : 0;
            if (pl != 0) AgeTwo(data, pl, o + new NVector2(70f, 0f));
            NVector2 c = StartBase.FootprintCenter(W.NavGrid, def, anchor), at = c + new NVector2(0f, def.FootprintHeight * MapConstants.CellSize / 2f + Clear(data));
            _sim.Enqueue(Command.SpawnUnit(pl, laborer, at));
            _sim.Tick();
            _sim.Tick();
            int builder = -1;
            for (int i = 0; i < W.Units.Capacity; i++)
                if (W.Units.Alive[i] && W.Units.Owner[i] == pl && W.Units.TypeId[i] == laborer && W.Units.State[i] == UnitState.Idle
                    && NVector2.Distance(W.Units.Position[i], at) < 3f) builder = i;
            if (builder >= 0) _sim.Enqueue(Command.Build(pl, new EntityHandle(builder, W.Units.Generation[builder]), type, PlacementGhost.AnchorPoint(W.NavGrid, anchor)));
            // M6-VH9 (BUG-1632 item 2): the Laborer walks to the 8 x 8 Keep's edge before the site exists; 40 ticks were too few.
            for (int t = 0; t < 400 && B.SlotAt(anchor % W.NavGrid.Width, anchor / W.NavGrid.Width) < 0; t++) _sim.Tick();
            W.CanPlace(pl, type, anchor, out PlacementError why);
            int site = B.SlotAt(anchor % W.NavGrid.Width, anchor / W.NavGrid.Width);
            GD.Print($"picture site {def.Key}: builder {builder}, site {site} (placement now {why}){(site < 0 ? " - skipped, no site in the picture" : "")}");
            Check(site >= 0, $"picture: no {def.Key} site was placed");
            sites.Add(c);
        }
        for (int t = 0; t < 300; t++) _sim.Tick();
        if (sites.Count > 0)
        {
            _camera.SetZoom(CameraLimits.DefaultZoom);
            _camera.SetFocus((sites[0].X + sites[^1].X) / 2f - 8f, (sites[0].Y + sites[^1].Y) / 2f + 2f);
            await Shot("sites-40m");
        }
        // Four colours: one row per type, players 1-4.
        z += 6f;
        var four = new List<NVector2>();
        foreach (int type in types)
        {
            float x = 0f;
            for (int p = 0; p < 4; p++)
            {
                int slot = Spawn(p, type, o + new NVector2(x, z));
                if (slot >= 0) four.Add(Centre(slot));
                x += data.Buildings[type].FootprintWidth * MapConstants.CellSize + 3f;
            }
            z += data.Buildings[type].FootprintHeight * MapConstants.CellSize + 5f;
        }
        if (four.Count > 0)
        {
            _camera.SetZoom(CameraLimits.DefaultZoom);
            _camera.SetFocus((four[0].X + four[^1].X) / 2f + 8f, (four[0].Y + four[^1].Y) / 2f);
            await Shot("four-colours-40m");
        }
        await EndMatch();
    }

    private static IEnumerable<(T, int)> Indexed<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++) yield return (list[i], i);
    }

    private void StartMatch(GameData data, params string[] extra)
    {
        _match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(_match);
        var args = new List<string> { "--seed", "1", "--units", "0", "--no-bases", "--mute" };
        args.AddRange(extra);
        _match.Start(data, LaunchOptions.Parse(args.ToArray()));
        var runner = _match.GetNode<SimRunner>("SimRunner");
        runner.ProcessMode = ProcessModeEnum.Disabled; // ticked here
        _sim = runner.Simulation!;
        _views = _match.GetNode<BuildingViews>("World3D/BuildingViews");
        _camera = _match.GetNode<RtsCamera>("RtsCamera");
        _camera.EdgePanEnabled = false;
        _sel = _match.GetNode<SelectionController>("SelectionController");
        _sim.Tick();
        _sim.Tick();
    }

    private async Task EndMatch()
    {
        RemoveChild(_match);
        _match.QueueFree();
        await Frames(1);
    }

    // A building of `type` for `player` with its anchor at the first open spot near `near`; its slot or -1.
    private int Spawn(int player, int type, NVector2 near)
    {
        BuildingDef def = W.Data.Buildings[type];
        int anchor = OpenAnchor(def, near);
        if (anchor < 0) return -1;
        _sim.Enqueue(Command.SpawnBuilding(player, type, PlacementGhost.AnchorPoint(W.NavGrid, anchor)));
        _sim.Tick();
        _sim.Tick(); // a command applies at the start of the tick after the one it was queued for
        int slot = B.SlotAt(anchor % W.NavGrid.Width, anchor / W.NavGrid.Width);
        return slot >= 0 && B.Cell[slot] == anchor && B.TypeId[slot] == type ? slot : -1;
    }

    // M6-V30: `clearOfUnits` also skips an anchor with a live unit on or within 1.5 m of the footprint (a site row's builders
    // and explorers stay where they stood; a 0.75 m Teblor Gatherer left by the row before made the next site "UnitInTheWay").
    private int OpenAnchor(BuildingDef def, NVector2 near, bool clearOfUnits = false)
    {
        int type = W.Data.FindBuilding(def.Key);
        for (int r = 0; r <= 8; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    NVector2 p = near + new NVector2(dx, dy) * MapConstants.CellSize;
                    if (!W.NavGrid.WorldToCell(p, out int cx, out int cy)) continue;
                    int a = cy * W.NavGrid.Width + cx;
                    if (cx < 1 || cy < 1 || cx + def.FootprintWidth >= W.NavGrid.Width - 1 || cy + def.FootprintHeight >= W.NavGrid.Height - 1) continue;
                    if (!B.Fits(type, a) || !FitsWithMargin(def, cx, cy)) continue;
                    if (clearOfUnits && UnitNear(cx * MapConstants.CellSize, cy * MapConstants.CellSize,
                            (cx + def.FootprintWidth) * MapConstants.CellSize, (cy + def.FootprintHeight) * MapConstants.CellSize)) continue;
                    return a;
                }
        return -1;
    }

    private bool UnitNear(float x0, float z0, float x1, float z1)
    {
        for (int i = 0; i < W.Units.Capacity; i++)
        {
            if (!W.Units.Alive[i]) continue;
            NVector2 p = W.Units.Position[i];
            if (p.X > x0 - 1.5f && p.X < x1 + 1.5f && p.Y > z0 - 1.5f && p.Y < z1 + 1.5f) return true;
        }
        return false;
    }

    // One free cell round the footprint, so neighbours never touch and units can pass.
    private bool FitsWithMargin(BuildingDef def, int cx, int cy)
    {
        for (int y = cy - 1; y <= cy + def.FootprintHeight; y++)
            for (int x = cx - 1; x <= cx + def.FootprintWidth; x++)
                if (B.SlotAt(x, y) >= 0 || !W.NavGrid.IsPassable(x, y)) return false;
        return true;
    }

    private NVector2 Centre(int slot) => StartBase.FootprintCenter(W.NavGrid, W.Data.Buildings[B.TypeId[slot]], B.Cell[slot]);

    private void Click(Vector2 at)
    {
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
        _sel._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
    }

    private async Task Shot(string name, bool gray = false)
    {
        Directory.CreateDirectory(_shots!);
        await Frames(6);
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image img = GetViewport().GetTexture().GetImage();
        if (gray)
            for (int y = 0; y < img.GetHeight(); y++)
                for (int x = 0; x < img.GetWidth(); x++)
                {
                    Color px = img.GetPixel(x, y);
                    float l = 0.2126f * px.R + 0.7152f * px.G + 0.0722f * px.B;
                    img.SetPixel(x, y, new Color(l, l, l));
                }
        string path = Path.Combine(_shots!, _prefix + name + ".png");
        Error e = img.SavePng(path);
        GD.Print($"screenshot {path} ({e})");
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private bool Check(bool ok, string message)
    {
        if (!ok) _failures.Add(message);
        return ok;
    }
}
