using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Map;

namespace Rts.Game.Tests;

/// <summary>
/// QA 2026-11-06-0326 (M6-V19: BUG-1092, the picked map's own counts, <c>--map-size</c> on a map file):
/// <list type="bullet">
/// <item>The one-team matrix: <c>--players 2 / 3 / 4 --map raraku</c> with every team set over the four slots (teams 0-2,
/// hidden slots included: 243 matches). Each one plays the map's 2 players as two sides (<c>IsEnemy(0, 1)</c>); a set the
/// cap left on one team is warned by <see cref="Match.OneTeamWarning"/> with every team dropped; a set LaunchOptions already
/// dropped is not warned again; a set that kept two sides keeps its teams with no warning.</item>
/// <item><c>--map-size</c> with <c>--map raraku</c>: 128 not warned, 192 / 256 warned and played at 128, junk values
/// (LaunchOptions refuses them) not warned by Match; with <c>--players 4</c> both warnings.</item>
/// <item>The setup's counts (shipped data, Raraku by id): 6 / 12 greyed, the args; Generated giving back the values; pick
/// twice in a row then Generated; a value set while greyed is dropped by Generated and not shown by the next pick; four
/// slots at their floors through a pick and back; a slot removed under the map.</item>
/// <item>A forged data copy (<c>user://</c>): <c>qa_bare</c> (Raraku with only its two start forests: a map with no forests or mines fails to load) shows 2 and passes <c>--forests 2</c>;
/// <c>qa_dense</c> (70 forests, past the generator's 64) shows 70, passes 64, and Generated puts the range back.</item>
/// </list>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6V19Test.tscn</c>; prints "QA M6-V19 TEST PASS" or
/// "QA M6-V19 TEST FAIL: ..." lines. Windowed with <c>-- --shots &lt;dir&gt;</c> it saves the setup with Raraku picked at 1152 x 648 and 1280 x 720.
/// </summary>
public partial class QaM6V19Test : Node
{
    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private UiText _ui = null!;
    private Main _main = null!;
    private MapDef _map = null!;
    private string _root = "";
    private string? _shots;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            string[] cmd = OS.GetCmdlineUserArgs();
            for (int i = 0; i + 1 < cmd.Length; i++) if (cmd[i] == "--shots") _shots = cmd[i + 1];
            await Frames(1);
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            _ui = UiText.Shared ?? throw new InvalidOperationException("ui.json failed to load");
            _map = _data.Maps.First(m => m.Key == "raraku");
            _root = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://qa-m6-v19-{OS.GetProcessId()}"));
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            Directory.CreateDirectory(Path.Combine(_root, "replays"));
            Directory.CreateDirectory(Path.Combine(_root, "saves"));
            _main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
            _main.UserArgs = new[] { Main.NoConfineFlag };
            _main.SettingsPath = $"user://qa-m6-v19-{OS.GetProcessId()}/settings.cfg";
            _main.ReplayDir = Path.Combine(_root, "replays");
            _main.SaveDir = Path.Combine(_root, "saves");
            AddChild(_main);
            await Frames(2);
            try
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                await OneTeamMatrix();
                GD.Print($"  one-team matrix: {clock.Elapsed.TotalSeconds:0.0} s");
                await MapSizeRows();
                await SetupCounts();
                await ForgedMaps();
            }
            finally
            {
                if (_main.CurrentMatch != null) _main.QuitToMenu();
            }
            try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        foreach (string f in _failures.Take(60)) GD.Print($"QA M6-V19 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M6-V19 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- BUG-1092: every --players x --team set on Raraku ----

    private async Task OneTeamMatrix()
    {
        int warned = 0, preDropped = 0, kept = 0, runs = 0;
        for (int players = 2; players <= 4; players++)
            for (int code = 0; code < 81; code++)
            {
                int[] teams = new int[4];
                for (int s = 0, c = code; s < 4; s++, c /= 3) teams[s] = c % 3;
                var flags = new List<string> { "--players", players.ToString(), "--seed", "3", "--units", "0", "--mute", "--map", "raraku" };
                for (int s = 0; s < 4; s++)
                    if (teams[s] != 0) flags.AddRange(new[] { "--team", (s + 1).ToString(), teams[s].ToString() });
                string how = string.Join(' ', flags);
                LaunchOptions asked = LaunchOptions.Parse(flags.ToArray());
                // Expected, written out independently of LaunchOptions.IsOneTeam:
                bool askedOne = teams[0] != 0 && Enumerable.Range(0, players).All(s => teams[s] == teams[0]);
                int keep = Math.Min(players, _map.Players);
                bool keptOne = !askedOne && teams[0] != 0 && Enumerable.Range(0, keep).All(s => teams[s] == teams[0]);
                Check((asked.OneTeamWarning != "") == askedOne, $"[{how}]: LaunchOptions warning '{asked.OneTeamWarning}' (want {(askedOne ? "one" : "none")})");
                _main.StartSkirmish(flags.ToArray());
                Match? match = _main.CurrentMatch;
                if (!Check(match?.Simulation != null, $"[{how}] started no match")) continue;
                runs++;
                World w = match!.Simulation!.World;
                bool enemies = true;
                for (int a = 0; a < w.Config.PlayerCount; a++)
                    for (int b = a + 1; b < w.Config.PlayerCount; b++) enemies &= w.IsEnemy(a, b) || (w.TeamOf(a) != 0 && w.TeamOf(a) == w.TeamOf(b));
                bool twoSides = w.IsEnemy(0, 1);
                Check(w.Config.PlayerCount == keep && twoSides && enemies,
                    $"[{how}]: {w.Config.PlayerCount} players (want {keep}), teams {w.TeamOf(0)},{w.TeamOf(1)}, IsEnemy(0,1) {twoSides}");
                if (keptOne)
                {
                    warned++;
                    Check(match.OneTeamWarning == LaunchOptions.OneTeamText(keep, teams[0]) && w.TeamOf(0) == 0 && w.TeamOf(1) == 0,
                        $"[{how}]: capped onto one team, warning '{match.OneTeamWarning}', teams {w.TeamOf(0)},{w.TeamOf(1)}");
                }
                else if (askedOne)
                {
                    preDropped++;
                    Check(match.OneTeamWarning == "" && w.TeamOf(0) == 0 && w.TeamOf(1) == 0,
                        $"[{how}]: LaunchOptions dropped the teams; Match warning '{match.OneTeamWarning}', teams {w.TeamOf(0)},{w.TeamOf(1)}");
                }
                else
                {
                    kept++;
                    Check(match.OneTeamWarning == "" && w.TeamOf(0) == teams[0] && w.TeamOf(1) == teams[1],
                        $"[{how}]: teams {w.TeamOf(0)},{w.TeamOf(1)} (want {teams[0]},{teams[1]}), warning '{match.OneTeamWarning}'");
                }
                Check((match.MapPlayersWarning != "") == (players > _map.Players), $"[{how}]: players warning '{match.MapPlayersWarning}'");
                await QuitMatch();
            }
        GD.Print($"  one-team matrix: {runs} matches, {warned} warned after the cap, {preDropped} dropped by LaunchOptions, {kept} kept");
        Check(runs == 243 && warned > 0 && preDropped > 0 && kept > 0, $"matrix shape: {runs} runs, {warned} / {preDropped} / {kept}");
    }

    // ---- --map-size with --map ----

    private async Task MapSizeRows()
    {
        foreach ((string size, bool warn) in new[] { ("128", false), ("192", true), ("256", true), ("100", false), ("x", false), ("-128", false) })
        {
            string[] flags = { "--seed", "4", "--units", "0", "--mute", "--map-size", size, "--map", "raraku" };
            _main.StartSkirmish(flags);
            Match? match = _main.CurrentMatch;
            if (!Check(match?.Simulation != null, $"--map-size {size} --map raraku started no match")) continue;
            World w = match!.Simulation!.World;
            Check(w.NavGrid.Width == _map.Size && (match.MapSizeWarning != "") == warn
                && (!warn || match.MapSizeWarning.Contains($"--map-size {size}", StringComparison.Ordinal) && match.MapSizeWarning.Contains($"playing {_map.Size}", StringComparison.Ordinal)),
                $"--map-size {size} --map raraku: {w.NavGrid.Width} wide, warning '{match.MapSizeWarning}' (want {(warn ? "one" : "none")})");
            await QuitMatch();
        }
        _main.StartSkirmish(new[] { "--seed", "4", "--units", "0", "--mute", "--map-size", "192", "--players", "4", "--map", "raraku" });
        Match? both = _main.CurrentMatch;
        if (Check(both?.Simulation != null, "--map-size 192 --players 4 --map raraku started no match"))
        {
            Check(both!.MapSizeWarning != "" && both.MapPlayersWarning != "" && both.OneTeamWarning == "" && both.Simulation!.World.Config.PlayerCount == 2,
                $"--map-size 192 --players 4: size '{both.MapSizeWarning}', players '{both.MapPlayersWarning}', one team '{both.OneTeamWarning}'");
            await QuitMatch();
        }
        // A generated match: --map-size is taken, nothing warned.
        _main.StartSkirmish(new[] { "--seed", "4", "--units", "0", "--mute", "--map-size", "192" });
        Match? gen = _main.CurrentMatch;
        if (Check(gen?.Simulation != null, "--map-size 192 started no match"))
        {
            Check(gen!.Simulation!.World.NavGrid.Width == 192 && gen.MapSizeWarning == "" && gen.OneTeamWarning == "",
                $"--map-size 192 generated: {gen.Simulation.World.NavGrid.Width} wide, warning '{gen.MapSizeWarning}'");
            await QuitMatch();
        }
    }

    // ---- the setup's greyed counts ----

    private async Task SetupCounts()
    {
        _main.ShowSetup();
        await Frames(2);
        SkirmishSetup setup = _main.Setup!;
        int mapMines = MinesOf(_data, _map);
        Check(_map.ForestCount == 6 && mapMines == 12, $"Raraku data: {_map.ForestCount} forests, {mapMines} gold mines (brief: 6 / 12)");

        int f0 = setup.ForestsPick, m0 = setup.MinesPick;
        Check(setup.PickMap("raraku"), "no raraku in the list");
        CheckShown(setup, 6, 12, true, "Raraku picked from defaults");
        string[] args = setup.MatchArgs()!;
        LaunchOptions parsed = LaunchOptions.Parse(args);
        Check(parsed.MapId == "raraku" && parsed.Forests == 6 && parsed.Mines == 12 && parsed.MapSize == 128, $"Raraku args [{string.Join(' ', args)}]");
        setup.PickMap("");
        CheckShown(setup, f0, m0, false, "Generated after the first pick");
        Check(setup.ForestsSpin.MaxValue == MapGenParams.MaxResourceGroups && setup.ForestsSpin.MinValue == SkirmishSetup.MinForestsFor(setup.SlotCount),
            $"Generated: forests range {setup.ForestsSpin.MinValue}-{setup.ForestsSpin.MaxValue}");

        // The player's own counts; pick, pick again (same map), Generated: the own counts, not the map's.
        setup.ForestsSpin.Value = 30;
        setup.MinesSpin.Value = 25;
        setup.PickMap("raraku");
        setup.PickMap("raraku");
        CheckShown(setup, 6, 12, true, "Raraku picked twice");
        setup.PickMap("");
        CheckShown(setup, 30, 25, false, "Generated after Raraku twice");

        // A value set while greyed (no key or click reaches it, but code could): Generated drops it, the next pick shows the map's.
        setup.PickMap("raraku");
        setup.ForestsSpin.Value = 40;
        setup.MinesSpin.Value = 3;
        setup.PickMap("");
        CheckShown(setup, 30, 25, false, "Generated after an edit while greyed");
        setup.PickMap("raraku");
        CheckShown(setup, 6, 12, true, "Raraku after an edit while greyed and Generated");
        setup.PickMap("");

        // Four slots at their floors, then the map (two slots) and Generated (four again): the floors' values back.
        for (int k = 0; k < SkirmishSetup.MaxSlots; k++) setup.AddSlot();
        int floorF = SkirmishSetup.MinForestsFor(4), floorM = (int)setup.MinesSpin.MinValue;
        setup.ForestsSpin.Value = floorF;
        setup.MinesSpin.Value = floorM;
        CheckShown(setup, floorF, floorM, false, "four slots at the floors");
        setup.PickMap("raraku");
        Check(setup.SlotCount == 2, $"Raraku shows {setup.SlotCount} slots");
        CheckShown(setup, 6, 12, true, "Raraku from four slots at the floors");
        foreach (Vector2I size in new[] { new Vector2I(1152, 648), new Vector2I(1280, 720) })
        {
            GetTree().Root.Size = size;
            await Frames(2);
            Rect2 view = setup.GetViewport().GetVisibleRect();
            Check(view.Encloses(setup.ForestsSpin.GetGlobalRect()) && view.Encloses(setup.MinesSpin.GetGlobalRect()) && view.Encloses(setup.StartButton.GetGlobalRect()),
                $"{size}: Forests {setup.ForestsSpin.GetGlobalRect()}, Gold mines {setup.MinesSpin.GetGlobalRect()}, Start {setup.StartButton.GetGlobalRect()} outside {view}");
            await Shot($"raraku-{size.X}");
        }
        GetTree().Root.Size = new Vector2I(1152, 648);
        await Frames(1);
        setup.PickMap("");
        Check(setup.SlotCount == 4 && setup.ForestsSpin.MinValue == floorF && setup.MinesSpin.MinValue == floorM,
            $"Generated: {setup.SlotCount} slots, floors {setup.ForestsSpin.MinValue} / {setup.MinesSpin.MinValue} (want {floorF} / {floorM})");
        CheckShown(setup, floorF, floorM, false, "Generated back to four slots");

        // A slot removed under the map: Generated keeps two slots, the own counts and the two-slot floors.
        setup.ForestsSpin.Value = 22;
        setup.MinesSpin.Value = 21;
        setup.PickMap("raraku");
        setup.RemoveSlot(); // a no-op at two slots
        setup.PickMap("");
        CheckShown(setup, 22, 21, false, "Generated after a remove under the map");
        Check(setup.ForestsSpin.MinValue == SkirmishSetup.MinForestsFor(setup.SlotCount), $"floor {setup.ForestsSpin.MinValue} at {setup.SlotCount} slots");
        _main.ShowMenu();
        await Frames(2);
    }

    // ---- a forged data copy: a map with no forests, a map past the generator's cap ----

    private async Task ForgedMaps()
    {
        string data = Path.Combine(_root, "data");
        CopyDir(ProjectSettings.GlobalizePath("res://data"), data);
        string rarakuPath = Path.Combine(data, "maps", "raraku.json");
        JsonNode bare = JsonNode.Parse(File.ReadAllText(rarakuPath))!;
        bare["id"] = "qa_bare";
        // The loader needs a forest beside each start (and the start mines), so the fewest a valid map holds is Raraku's
        // first two (its start forests); a map with no forests or no gold mines can't load.
        var bareForests = (JsonArray)bare["forests"]!;
        while (bareForests.Count > 2) bareForests.RemoveAt(bareForests.Count - 1);
        File.WriteAllText(Path.Combine(data, "maps", "qa_bare.json"), bare.ToJsonString());
        JsonNode dense = JsonNode.Parse(File.ReadAllText(rarakuPath))!;
        dense["id"] = "qa_dense";
        var forests = (JsonArray)dense["forests"]!;
        for (int x = 2; forests.Count < 70 && x < 126; x += 2)
            forests.Add(new JsonObject { ["x"] = x, ["y"] = 2, ["rows"] = new JsonArray("T") });
        for (int x = 2; forests.Count < 70 && x < 126; x += 2)
            forests.Add(new JsonObject { ["x"] = x, ["y"] = 125, ["rows"] = new JsonArray("T") });
        File.WriteAllText(Path.Combine(data, "maps", "qa_dense.json"), dense.ToJsonString());
        DataLoadResult forged = DataLoader.LoadAll(data);
        if (!Check(forged.Ok, $"forged data failed to load: {string.Join(" | ", forged.Errors.Take(5))}")) return;
        GameData g = forged.Data!;
        MapDef bareDef = g.Maps[g.FindMap("qa_bare")], denseDef = g.Maps[g.FindMap("qa_dense")];
        Check(bareDef.ForestCount == 2 && denseDef.ForestCount == 70, $"forged: bare {bareDef.ForestCount} forests, dense {denseDef.ForestCount}");

        var setup = new SkirmishSetup { Name = "QaSetup" };
        AddChild(setup);
        setup.Build(_ui, g);
        await Frames(2);
        setup.ForestsSpin.Value = 15;
        setup.MinesSpin.Value = 14;
        for (int k = 0; k < SkirmishSetup.MaxSlots; k++) setup.AddSlot(); // four slots: a forests floor of 4, over qa_bare's 2

        Check(setup.PickMap("qa_bare"), "no qa_bare in the forged list");
        CheckShown(setup, 2, MinesOf(g, bareDef), true, "qa_bare picked");
        LaunchOptions bareArgs = LaunchOptions.Parse(setup.MatchArgs()!);
        Check(bareArgs.Forests == 2 && bareArgs.MapId == "qa_bare", $"qa_bare args: --forests {bareArgs.Forests}, map {bareArgs.MapId}");
        setup.PickMap("");
        CheckShown(setup, 15, 14, false, "Generated after qa_bare");
        Check(setup.ForestsSpin.MinValue == SkirmishSetup.MinForestsFor(setup.SlotCount), $"Generated after qa_bare: floor {setup.ForestsSpin.MinValue}");

        Check(setup.PickMap("qa_dense"), "no qa_dense in the forged list");
        CheckShown(setup, 70, MinesOf(g, denseDef), true, "qa_dense picked");
        string[] denseRaw = setup.MatchArgs()!;
        LaunchOptions denseArgs = LaunchOptions.Parse(denseRaw);
        Check(denseArgs.Forests == MapGenParams.MaxResourceGroups && denseArgs.MapId == "qa_dense",
            $"qa_dense args [{string.Join(' ', denseRaw)}]: --forests {denseArgs.Forests}");
        // Straight from one map to another: the own counts are still the ones from before the first.
        setup.PickMap("qa_bare");
        CheckShown(setup, 2, MinesOf(g, bareDef), true, "qa_bare after qa_dense");
        setup.PickMap("qa_dense");
        setup.PickMap("");
        CheckShown(setup, 15, 14, false, "Generated after qa_dense / qa_bare / qa_dense");
        Check(setup.ForestsSpin.MaxValue == MapGenParams.MaxResourceGroups && setup.MinesSpin.MaxValue == MapGenParams.MaxResourceGroups,
            $"Generated after qa_dense: max {setup.ForestsSpin.MaxValue} / {setup.MinesSpin.MaxValue}");
        // The generator's range holds again: 70 typed is refused at 64.
        setup.ForestsSpin.Value = 70;
        Check(setup.ForestsPick == MapGenParams.MaxResourceGroups, $"Generated takes {setup.ForestsPick} forests past the cap");
        RemoveChild(setup);
        setup.QueueFree();
        await Frames(1);
    }

    private void CheckShown(SkirmishSetup setup, int forests, int mines, bool greyed, string what)
    {
        string fText = setup.ForestsSpin.GetLineEdit().Text, mText = setup.MinesSpin.GetLineEdit().Text;
        Check(setup.ForestsPick == forests && setup.MinesPick == mines && fText == forests.ToString() && mText == mines.ToString()
            && setup.ForestsSpin.Editable == !greyed && setup.MinesSpin.Editable == !greyed,
            $"{what}: Forests {setup.ForestsPick} ('{fText}'), Gold mines {setup.MinesPick} ('{mText}'), editable {setup.ForestsSpin.Editable}; want {forests} / {mines}, greyed {greyed}");
    }

    private static int MinesOf(GameData data, MapDef map) => map.Nodes.Count(n => data.Resources[n.TypeId].Resource == ResourceKind.Gold);

    private static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string f in Directory.GetFiles(from)) if (!f.EndsWith(".import", StringComparison.Ordinal)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (string d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
    }

    private async Task QuitMatch()
    {
        if (_main.BannerUp) _main.Banner!.MenuButton.EmitSignal(BaseButton.SignalName.Pressed);
        else _main.QuitToMenu();
        await Frames(2);
        Check(_main.CurrentMatch == null && !GetTree().Paused, "quit to menu");
    }

    private async Task Shot(string name)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = $"{_shots}/qa-m6-v19-{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"  screenshot {path}");
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
