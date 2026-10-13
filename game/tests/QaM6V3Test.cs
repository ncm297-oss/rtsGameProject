using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Game.Tests;

/// <summary>
/// QA attacks on M6-V3 (session 2026-10-13-0146). The menu's opening on 6 seeds x 3 map sizes through the real setup (each
/// player one Town Hall and the starting workers, no other unit, <c>UnitsPerPlayer</c> 0, the camera on the local hall; whether
/// each hall sees a gold mine is printed, not failed: the sim's start locations fix it after the merge); ten quit cycles from
/// that lean match (nodes flat, no orphans, objects and resources flat); <c>--skirmish</c> against the setup's defaults
/// (BUG-0630: the AI seat differs, a printed probe); <c>--colour</c> junk and repeated sets through the real <see cref="Match"/>
/// (PlayerRgb, unit and building materials, the match ticks); colour flags are hash-neutral; a mirror match draws two colours in
/// unit bodies, buildings and both players' minimap dots; a press on the minimap held into a pause (Esc twice) with the release
/// before or after the resume, and a middle drag and a box press held alongside it: the camera moves 0 m after.
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6V3Test.tscn</c>; prints "QA M6V3 TEST PASS" or "QA M6V3 TEST FAIL: ..." lines.</remarks>
public partial class QaM6V3Test : Node
{
    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private UiText _ui = null!;
    private int _mal, _whirl;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            await Frames(1);
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            _ui = UiText.Shared ?? throw new InvalidOperationException("ui.json failed to load");
            _mal = _data.FindFaction("malazan");
            _whirl = _data.FindFaction("whirlwind");
            SizeFormatVariants();
            ColourTableRobustness();
            SkirmishEqualsMenuDefaults();
            await ColourFlagsThroughMatch();
            await ColourFlagsHashNeutral();
            await MirrorMatchColours();
            await MenuOpenings();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        foreach (string f in _failures.Take(60)) GD.Print($"QA M6V3 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M6V3 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- ui.json ----

    private string Json() => System.IO.File.ReadAllText(UiText.DefaultPath);

    // BUG-0602 item 1 and its neighbours: refused (one error naming the key) vs accepted.
    private void SizeFormatVariants()
    {
        foreach (string bad in new[] { "{{0}}", "{{0}}x", "Size {{0}} cells", "{{0}} {{0}} {{0}}" })
            Check(ParseWith(r => r["setup"]!["map_size_item"] = bad, out List<string> e) == null && e.Count == 1 && e[0].Contains("setup.map_size_item"),
                $"map_size_item '{bad}': [{string.Join("; ", e)}]");
        // ("{0,5}" or "{0:D3}" are refused too: the rule wants the literal "{0}"; a wording note, not a bug.)
        foreach (string good in new[] { "{0}", "{{{0}}}", "{0} ({{0}})" })
        {
            UiText? ui = ParseWith(r => r["setup"]!["map_size_item"] = good, out List<string> e);
            Check(ui != null && ui.MapSizeItem(192).Contains("192"), $"map_size_item '{good}' refused or wrong: '{ui?.MapSizeItem(192)}' [{string.Join("; ", e)}]");
        }
    }

    // Colour entries of the wrong JSON kind are one error naming the entry or field, never a crash or a silent default.
    private void ColourTableRobustness()
    {
        var rows = new (string What, Action<System.Text.Json.Nodes.JsonObject> Edit, string At)[]
        {
            ("rgb a number", r => r["setup"]!["colour_4"]!["rgb"] = 0x123456, "setup.colour_4.rgb"),
            ("rgb empty", r => r["setup"]!["colour_4"]!["rgb"] = "", "setup.colour_4.rgb"),
            ("rgb null", r => r["setup"]!["colour_4"]!["rgb"] = null, "setup.colour_4.rgb"),
            ("displayName a number", r => r["setup"]!["colour_5"]!["displayName"] = 5, "setup.colour_5.displayName"),
            ("displayName empty", r => r["setup"]!["colour_5"]!["displayName"] = "", "setup.colour_5.displayName"),
            ("entry a string", r => r["setup"]!["colour_6"] = "#112233", "setup.colour_6"),
            ("entry an array", r => r["setup"]!["colour_6"] = new System.Text.Json.Nodes.JsonArray(), "setup.colour_6"),
            ("rgb 0x form", r => r["setup"]!["colour_7"]!["rgb"] = "0x1234", "setup.colour_7.rgb"),
            ("rgb full-width digits", r => r["setup"]!["colour_7"]!["rgb"] = "#１２３４５６", "setup.colour_7.rgb"),
        };
        foreach ((string what, Action<System.Text.Json.Nodes.JsonObject> edit, string at) in rows)
        {
            UiText? ui = ParseWith(edit, out List<string> e);
            Check(ui == null && e.Count == 1 && e[0].Contains(at), $"colour table, {what}: loaded {ui != null}, errors [{string.Join("; ", e)}]");
        }
        // Lower case and an extra colour_9 key load (extra keys are accepted, as everywhere in ui.json).
        UiText? lower = ParseWith(r => { r["setup"]!["colour_1"]!["rgb"] = "#a3192b"; r["setup"]!["colour_9"] = "x"; }, out List<string> le);
        Check(lower != null && lower.ColourRgb(0) == 0xA3192Bu, $"lower-case rgb / an extra colour_9: [{string.Join("; ", le)}]");
        // Two table entries with the same rgb: probe (the loader does not check; the shipped table is checked by ShellTest).
        UiText? dup = ParseWith(r => r["setup"]!["colour_2"]!["rgb"] = (string)r["setup"]!["colour_1"]!["rgb"]!, out _);
        GD.Print($"  PROBE colour table: colours 1 and 2 with the same rgb {(dup != null ? "load clean (a default match would draw both sides alike)" : "refused")}");
    }

    private UiText? ParseWith(Action<System.Text.Json.Nodes.JsonObject> edit, out List<string> errors)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(Json())!.AsObject();
        edit(root);
        errors = new List<string>();
        return UiText.Parse(root.ToJsonString(), errors);
    }

    // ---- flags ----

    // BUG-0602 item 2 / criterion 2: --skirmish must be the menu's Start with nothing changed.
    private void SkirmishEqualsMenuDefaults()
    {
        var setup = new SkirmishSetup();
        AddChild(setup);
        setup.Build(_ui, _data);
        string[] menuArgs = setup.MatchArgs()!;
        RemoveChild(setup);
        setup.QueueFree();
        LaunchOptions menu = LaunchOptions.Parse(menuArgs), skirmish = LaunchOptions.Parse(new[] { "--skirmish" });
        // An absent --seed / --map-size is the runner's default, seed 1 and the 128 map (the smoke's "Match started:" line).
        ulong menuSeed = menu.Seed ?? 1, skSeed = skirmish.Seed ?? 1;
        int menuSize = menu.MapSize ?? LaunchOptions.MapSizes[0], skSize = skirmish.MapSize ?? LaunchOptions.MapSizes[0];
        Check(menuSeed == skSeed && menuSize == skSize && menu.UnitsPerPlayer == skirmish.UnitsPerPlayer && menu.Workers == skirmish.Workers,
            $"--skirmish vs menu defaults: seed {skirmish.Seed}/{menu.Seed}, map {skirmish.MapSize}/{menu.MapSize}, units {skirmish.UnitsPerPlayer}/{menu.UnitsPerPlayer}");
        for (int s = 0; s < 2; s++)
        {
            int menuFaction = menu.FactionId(s) is string id ? _data.FindFaction(id) : s % _data.Factions.Length;
            int skFaction = skirmish.FactionId(s) is string id2 ? _data.FindFaction(id2) : s % _data.Factions.Length;
            Check(menuFaction == skFaction && (menu.ColourIndex(s) ?? s) == (skirmish.ColourIndex(s) ?? s),
                $"--skirmish vs menu defaults, slot {s}: faction {skFaction}/{menuFaction}, colour {skirmish.ColourIndex(s) ?? s}/{menu.ColourIndex(s) ?? s}");
        }
        // BUG-0630 (S2, open): printed until fixed; flip to Check(menu.Ai == skirmish.Ai, ...) when it is.
        if (menu.Ai != skirmish.Ai)
            GD.Print($"  KNOWN BUG-0630 probe: --skirmish slot 2 is {(skirmish.Ai is AiDifficulty a ? $"AI {a}" : "human")}, the menu's Start {(menu.Ai is AiDifficulty b ? $"AI {b}" : "human")} (args '{string.Join(' ', menuArgs)}')");
    }

    // --colour through the real Match: junk keeps the default (slot s, colour s), repeated sets keep the last good value.
    private async Task ColourFlagsThroughMatch()
    {
        var rows = new (string[] Args, int C0, int C1)[]
        {
            (new[] { "--colour", "1", "9" }, 0, 1),
            (new[] { "--colour", "3", "4" }, 0, 1),
            (new[] { "--colour", "1", "-1" }, 0, 1),
            (new[] { "--colour", "1", "١" }, 0, 1), // Arabic-Indic digit one
            (new[] { "--colour", "１", "4" }, 0, 1), // full-width one
            (new[] { "--colour", "1", " 4" }, 0, 1),
            (new[] { "--colour", "1", "" }, 0, 1),
            (new[] { "--colour", "", "4" }, 0, 1),
            (new[] { "--colour", "2" }, 0, 1),
            (new[] { "--colour", "1", "4", "--colour", "1", "junk" }, 3, 1),
            (new[] { "--colour", "1", "4", "--colour", "1", "7", "--colour", "2", "2" }, 6, 1),
            (new[] { "--colour", "2", "1", "--colour", "1", "1" }, 0, 0),
            (new[] { "--colour", "2", "8", "--colour", "2", "--seed", "3" }, 0, 7),
        };
        foreach ((string[] args, int c0, int c1) in rows)
        {
            string how = string.Join(' ', args.Select(a => $"'{a}'"));
            (Match match, SimRunner runner) = StartMatch(args.Concat(new[] { "--units", "4", "--mute", "--no-fog" }).ToArray());
            uint[] want = { _ui.ColourRgb(c0), _ui.ColourRgb(c1) };
            Check(match.PlayerRgb.Length == 2 && match.PlayerRgb[0] == want[0] && match.PlayerRgb[1] == want[1],
                $"{how}: PlayerRgb [{string.Join(", ", match.PlayerRgb.Select(c => c.ToString("X6")))}], want {want[0]:X6}, {want[1]:X6}");
            Simulation sim = runner.Simulation!;
            runner.GameSpeed = 8.0;
            await UntilTick(sim, 20);
            await Frames(2);
            BodiesAndBuildings(match, sim, want, how);
            await EndMatch(match);
        }
    }

    private void BodiesAndBuildings(Match match, Simulation sim, uint[] want, string how)
    {
        var units = match.GetNode<UnitViews>("World3D/UnitViews");
        var buildings = match.GetNode<BuildingViews>("World3D/BuildingViews");
        UnitStore u = sim.World.Units;
        int seen = 0, wrong = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || (uint)u.Owner[i] > 1 || !units.IsShown(i)) continue;
            seen++;
            if (units.ViewOf(i)?.MaterialOverride is not StandardMaterial3D m || !m.AlbedoColor.IsEqualApprox(UnitViews.ColorFromRgb(want[u.Owner[i]]))) wrong++;
        }
        Check(seen > 0 && wrong == 0, $"{how}: {seen} unit bodies shown, {wrong} not their player's colour");
        for (int p = 0; p < 2; p++)
            Check(buildings.PlayerMaterial(p).AlbedoColor.IsEqualApprox(UnitViews.ColorFromRgb(want[p])), $"{how}: player {p} building colour {buildings.PlayerMaterial(p).AlbedoColor}");
    }

    // Colours are view only: the same match with and without colour flags hashes alike tick for tick.
    private async Task ColourFlagsHashNeutral()
    {
        ulong[] a = await Hashes(new[] { "--seed", "11", "--units", "30", "--mute" });
        ulong[] b = await Hashes(new[] { "--seed", "11", "--units", "30", "--mute", "--colour", "1", "5", "--colour", "2", "5" });
        Check(a.Length == b.Length && a.SequenceEqual(b), $"colour flags moved the state hash: first difference at tick {Enumerable.Range(0, Math.Min(a.Length, b.Length)).FirstOrDefault(i => a[i] != b[i]) + 1}");
    }

    private async Task<ulong[]> Hashes(string[] args)
    {
        (Match match, SimRunner runner) = StartMatch(args);
        runner.ProcessMode = ProcessModeEnum.Disabled; // tick by hand: no wall clock in the comparison
        Simulation sim = runner.Simulation!;
        var hashes = new ulong[60];
        for (int t = 0; t < hashes.Length; t++)
        {
            sim.Tick();
            hashes[t] = sim.StateHash();
        }
        await EndMatch(match);
        return hashes;
    }

    // A mirror (Whirlwind v Whirlwind): two colours in bodies, buildings and both players' minimap dots (fog off, so enemy dots draw).
    private async Task MirrorMatchColours()
    {
        (Match match, SimRunner runner) = StartMatch(new[] { "--faction", "1", "whirlwind", "--faction", "2", "whirlwind", "--units", "20", "--mute", "--no-fog" });
        Simulation sim = runner.Simulation!;
        await UntilTick(sim, 4);
        await Frames(2);
        uint[] want = { _ui.ColourRgb(0), _ui.ColourRgb(1) };
        Check(want[0] != want[1], "the default colours 1 and 2 are alike");
        BodiesAndBuildings(match, sim, want, "mirror");
        var mini = match.GetNode<Minimap>("Hud/Minimap");
        mini.Refresh(sim);
        UnitStore u = sim.World.Units;
        var dots = new int[2];
        var wrong = new int[2];
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || (uint)u.Owner[i] > 1 || !mini.Raster!.TryPixelOf(u.Position[i], out int x, out int y)) continue;
            Color got = mini.DotsImage.GetPixel(x, y), c = UnitViews.ColorFromRgb(want[u.Owner[i]]);
            dots[u.Owner[i]]++;
            if (Mathf.Abs(got.R - c.R) > 0.01f || Mathf.Abs(got.G - c.G) > 0.01f || Mathf.Abs(got.B - c.B) > 0.01f) wrong[u.Owner[i]]++;
        }
        Check(dots[0] > 0 && dots[1] > 0 && wrong[0] == 0 && wrong[1] == 0,
            $"mirror minimap dots: p0 {dots[0]} ({wrong[0]} wrong), p1 {dots[1]} ({wrong[1]} wrong)");
        GD.Print($"  mirror: dots p0 {dots[0]} p1 {dots[1]}, colours {want[0]:X6} / {want[1]:X6}");
        await EndMatch(match);
    }

    // ---- the menu ----

    private async Task MenuOpenings()
    {
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.UserArgs = Array.Empty<string>(); // no match flag: the menu shows
        AddChild(main);
        await Frames(2);
        Check(main.Menu is { Visible: true }, "the menu did not show");
        MainMenu menu = main.Menu!;
        SkirmishSetup setup = main.Setup!;
        int sizes = setup.MapSizeOption.ItemCount;
        ulong[] seeds = { 1, 2, 3, 7, 42, 9001 };
        int sawMine = 0, halls = 0;
        foreach (ulong seed in seeds)
        {
            for (int s = 0; s < sizes; s++)
            {
                await ClickControl(menu.SkirmishButton);
                setup.SeedEdit.Text = seed.ToString(System.Globalization.CultureInfo.InvariantCulture);
                setup.SeedEdit.EmitSignal(LineEdit.SignalName.TextChanged, setup.SeedEdit.Text);
                setup.MapSizeOption.Select(s);
                await ClickControl(setup.StartButton);
                await Frames(2);
                string how = $"seed {seed}, map {setup.MapSize}";
                Match? match = main.CurrentMatch;
                Check(match != null, $"{how}: no match");
                if (match == null) continue;
                Simulation sim = match.GetNode<SimRunner>("SimRunner").Simulation!;
                match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
                await UntilTick(sim, 9); // the base is in on tick 1, the fog updated by tick 8; a worker can't train in 0.45 s
                Opening(match, sim, how, ref sawMine, ref halls);
                await QuitToMenu(main);
            }
        }
        GD.Print($"  menu openings: {halls} halls planned of {seeds.Length * sizes * 2}, {sawMine} see a gold mine at the start");

        // Ten quit cycles from the lean match (the last picks: seed 9001, the largest map).
        var rows = new List<(int Nodes, double Objects, double Orphans, double Resources, long Managed)>();
        for (int cycle = 0; cycle < 10; cycle++)
        {
            await ClickControl(menu.SkirmishButton);
            await ClickControl(setup.StartButton);
            await Frames(2);
            if (main.CurrentMatch == null) { Check(false, $"cycle {cycle}: no match"); continue; }
            SimRunner r = main.CurrentMatch.GetNode<SimRunner>("SimRunner");
            r.GameSpeed = 8.0;
            await UntilTick(r.Simulation!, 120);
            if (cycle == 0) await HeldIntoPause(main);
            await QuitToMenu(main);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Frames(3);
            rows.Add((GetTree().GetNodeCount(), Performance.GetMonitor(Performance.Monitor.ObjectCount),
                Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount), Performance.GetMonitor(Performance.Monitor.ObjectResourceCount),
                GC.GetTotalMemory(true)));
        }
        for (int i = 0; i < rows.Count; i++)
            GD.Print($"  lean cycle {i}: nodes {rows[i].Nodes}, objects {rows[i].Objects}, orphans {rows[i].Orphans}, resources {rows[i].Resources}, managed {rows[i].Managed / 1048576.0:0.0} MB");
        if (rows.Count == 10)
        {
            Check(rows.All(r => r.Nodes == rows[0].Nodes), $"node count not flat: {string.Join(",", rows.Select(r => r.Nodes))}");
            Check(rows.All(r => r.Orphans == 0), $"orphans: {string.Join(",", rows.Select(r => r.Orphans))}");
            double obj = rows[9].Objects - rows[2].Objects, res = rows[9].Resources - rows[2].Resources;
            double managedMb = (rows[9].Managed - rows[2].Managed) / 1048576.0;
            GD.Print($"  lean cycles growth 2 -> 9: objects {obj}, resources {res}, managed {managedMb:0.00} MB");
            Check(obj <= 50 && res <= 20, $"objects grow {obj}, resources {res} over 7 lean cycles");
            Check(managedMb < 8.0, $"managed memory grows {managedMb:0.00} MB over 7 lean cycles");
        }
        RemoveChild(main);
        main.QueueFree();
        await Frames(2);
    }

    private void Opening(Match match, Simulation sim, string how, ref int sawMine, ref int halls)
    {
        World w = sim.World;
        Check(match.UnitsPerPlayer == 0 && match.WorkersPerPlayer == _data.Rules.StartingWorkers, $"{how}: {match.UnitsPerPlayer} units, {match.WorkersPerPlayer} workers a player");
        int[] workers = new int[2], others = new int[2], hallCount = new int[2], buildings = new int[2];
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            if ((uint)u.Owner[i] > 1) { Check(false, $"{how}: a unit of player {u.Owner[i]}"); continue; }
            if (_data.Units[u.TypeId[i]].Slot == UnitSlot.Worker) workers[u.Owner[i]]++;
            else others[u.Owner[i]]++;
        }
        BuildingStore b = w.Buildings;
        for (int k = 0; k < b.Capacity; k++)
        {
            if (!b.Alive[k] || (uint)b.Owner[k] > 1) continue;
            buildings[b.Owner[k]]++;
            if (_data.Buildings[b.TypeId[k]].Slot == BuildingSlot.TownHall) hallCount[b.Owner[k]]++;
        }
        var mineText = new string[2];
        for (int p = 0; p < 2; p++)
        {
            Check(hallCount[p] == 1 && buildings[p] == 1 && workers[p] == _data.Rules.StartingWorkers && others[p] == 0,
                $"{how}, player {p}: {hallCount[p]} halls, {buildings[p]} buildings, {workers[p]} workers, {others[p]} other units");
            if (hallCount[p] == 1) halls++;
            // A gold mine in the player's sight now (any footprint cell visible), and the nearest one's distance from the hall.
            bool sees = false;
            float nearest = float.MaxValue;
            System.Numerics.Vector2 hall = match.Bases is { } plan && plan.HallAnchor[p] >= 0
                ? Rts.Sim.ViewApi.StartBase.FootprintCenter(w.NavGrid, _data.Buildings[plan.HallType[p]], plan.HallAnchor[p])
                : new System.Numerics.Vector2(float.NaN);
            ResourceStore r = w.Resources;
            for (int k = 0; k < r.Capacity; k++)
            {
                if (!r.Alive[k]) continue;
                ResourceDef def = _data.Resources[r.TypeId[k]];
                if (def.Resource != ResourceKind.Gold) continue;
                int ax = r.Cell[k] % w.NavGrid.Width, ay = r.Cell[k] / w.NavGrid.Width;
                for (int dy = 0; dy < def.FootprintHeight; dy++)
                    for (int dx = 0; dx < def.FootprintWidth; dx++)
                        if (w.Fog.IsVisible(p, (ay + dy) * w.NavGrid.Width + ax + dx)) sees = true;
                System.Numerics.Vector2 at = w.NavGrid.CellCenter(ax, ay);
                nearest = Math.Min(nearest, System.Numerics.Vector2.Distance(at, hall));
            }
            if (sees) sawMine++;
            mineText[p] = $"p{p} {(sees ? "sees a mine" : "no mine in sight")} (nearest {nearest:0} m)";
        }
        if (match.Bases is { } bases && bases.HallAnchor[0] >= 0)
        {
            System.Numerics.Vector2 hall0 = Rts.Sim.ViewApi.StartBase.FootprintCenter(w.NavGrid, _data.Buildings[bases.HallType[0]], bases.HallAnchor[0]);
            // StartFocus: headless, the edge pan moves the live focus a few metres in the frames before the test turns it off.
            Check(System.Numerics.Vector2.Distance(match.StartFocus, hall0) < 1f, $"{how}: camera start {match.StartFocus}, hall {hall0}");
        }
        GD.Print($"  opening {how}: {mineText[0]}, {mineText[1]}");
    }

    // A minimap press, a middle drag and a box press, all held into the pause (Esc), released during it or after Resume (Esc):
    // after it a motion with no button held moves the camera 0 m and boxes nothing.
    private async Task HeldIntoPause(Main main)
    {
        Match match = main.CurrentMatch!;
        var mini = match.GetNode<Minimap>("Hud/Minimap");
        RtsCamera cam = match.GetNode<RtsCamera>("RtsCamera");
        cam.EdgePanEnabled = false;
        Rect2 rect = mini.GetGlobalRect();
        Vector2 a = rect.Position + rect.Size * 0.3f, b = rect.Position + rect.Size * 0.7f;
        Vector2 field = new(500, 250), field2 = new(700, 400);
        foreach (bool releaseWhilePaused in new[] { true, false })
        {
            string how = releaseWhilePaused ? "released while paused" : "released after resume";
            Move(a);
            await Frames(1);
            Button(MouseButton.Left, true, a);
            await Frames(1);
            Check(mini.Jumping, $"{how}: the minimap press did not start a jump-drag");
            Move(field);
            Button(MouseButton.Middle, true, field);
            await Frames(1);
            PushKey(Key.Escape);
            await Frames(2);
            Check(main.Paused && !mini.Jumping, $"{how}: paused {main.Paused}, minimap jumping {mini.Jumping}");
            if (releaseWhilePaused)
            {
                Button(MouseButton.Left, false, a);
                Button(MouseButton.Middle, false, field);
                await Frames(1);
            }
            PushKey(Key.Escape);
            await Frames(2);
            Check(!main.Paused, $"{how}: Esc did not resume");
            if (!releaseWhilePaused)
            {
                Button(MouseButton.Left, false, a);
                Button(MouseButton.Middle, false, field);
                await Frames(1);
            }
            Vector3 before = cam.GlobalPosition;
            Move(b, b - a);
            await Frames(2);
            Move(field2, field2 - field);
            await Frames(3);
            float moved = cam.GlobalPosition.DistanceTo(before);
            Check(moved < 1e-3f && !mini.Jumping, $"{how}: a motion with no button held moved the camera {moved:0.00} m (jumping {mini.Jumping})");
            GD.Print($"  held into pause ({how}): camera moved {moved:0.000} m");
        }
    }

    // ---- helpers ----

    private void Move(Vector2 at, Vector2 relative = default) =>
        GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at, Relative = relative });

    private void Button(MouseButton button, bool pressed, Vector2 at) =>
        GetViewport().PushInput(new InputEventMouseButton
        {
            ButtonIndex = button, Pressed = pressed, Position = at, GlobalPosition = at,
            ButtonMask = pressed ? (button == MouseButton.Left ? MouseButtonMask.Left : MouseButtonMask.Middle) : 0,
        });

    private async Task QuitToMenu(Main main)
    {
        PushKey(Key.Escape);
        await Frames(2);
        Check(main.Paused, "Esc did not pause");
        await ClickControl(main.Pause!.QuitToMenuButton);
        await Frames(3);
        Check(main.CurrentMatch == null && !GetTree().Paused && main.Menu!.Visible, "Quit to menu state");
    }

    private (Match Match, SimRunner Runner) StartMatch(string[] args)
    {
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        SimRunner runner = match.GetNode<SimRunner>("SimRunner");
        match.Start(_data, LaunchOptions.Parse(args));
        match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
        return (match, runner);
    }

    private async Task EndMatch(Match match)
    {
        RemoveChild(match);
        match.QueueFree();
        await Frames(2);
    }

    private async Task UntilTick(Simulation sim, int tick)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (sim.TickNumber < tick && clock.Elapsed.TotalSeconds < 60) await Frames(1);
        Check(sim.TickNumber >= tick, $"tick {sim.TickNumber} after 60 s (want {tick})");
    }

    private void PushKey(Key key)
    {
        GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    private async Task ClickControl(Control c)
    {
        Check(c.IsVisibleInTree(), $"{c.Name} is not visible");
        Vector2 at = c.GetGlobalRect().GetCenter();
        Move(at);
        await Frames(1);
        Button(MouseButton.Left, true, at);
        Button(MouseButton.Left, false, at);
        await Frames(2);
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
