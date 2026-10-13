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
/// QA attacks on M6-VH1 (session 2026-10-13-0527). (1) All 64 ordered <c>--colour 1 a --colour 2 b</c> pairs through the real
/// <see cref="Match"/>: the two colours differ (a duplicate keeps slot 2's default, or colour 1 when slot 1 holds colour 2), and
/// the match's own <see cref="CombatViews"/> meets the corpse rules (BUG-0160 item 3: fill luminance >= 0.2, rim darker, the two
/// fills >= 0.25 apart) for that pair. (2) A seeded fuzz of <c>--colour</c> token streams (good, junk, flags as values) against
/// a model: the last good value per slot, then the duplicate rule. (3) A seeded fuzz of 400 setup colour picks through the
/// lists' own ItemSelected: picks always differ, each list greys exactly the other's colour, and Start draws the picks.
/// (4) The menu's opening on 6 seeds x 3 map sizes run to tick 400 at 8x: the AI has a worker gathering or returning
/// (BUG-0604). (5) BUG-0662 probe: a stable key in a full <see cref="GroundDiscPool"/> keeps its node (no mesh rebuild) when a
/// lower new key arrives.
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaVh1Test.tscn</c>; prints "QA VH1 TEST PASS" or "QA VH1 TEST FAIL: ..." lines.</remarks>
public partial class QaVh1Test : Node
{
    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private UiText _ui = null!;

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
            ColourFlagFuzz();
            await ColourPairsThroughMatch();
            await PoolKeepsStableKeys();
            await MenuSetupFuzzAndOpenings();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        foreach (string f in _failures.Take(60)) GD.Print($"QA VH1 TEST FAIL: {f}");
        if (_failures.Count > 60) GD.Print($"QA VH1 TEST FAIL: ... and {_failures.Count - 60} more");
        if (_failures.Count == 0) GD.Print("QA VH1 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // The effective colour of each slot after the duplicate rule (docs/02 "Game flow" 2).
    private static (int, int) Effective(LaunchOptions o) => (o.ColourIndex(0) ?? 0, o.ColourIndex(1) ?? 1);

    // The documented rule from the last good value per slot (null = no good value).
    private static (int, int) Model(int? s0, int? s1)
    {
        int c0 = s0 ?? 0, c1 = s1 ?? 1;
        if (c0 == c1) c1 = c0 == 1 ? 0 : 1;
        return (c0, c1);
    }

    // (2) Token streams: --colour with good and junk slots / colours, a flag as a value, other flags between.
    private void ColourFlagFuzz()
    {
        var rng = new Random(6620);
        string[] slots = { "1", "2", "1", "2", "3", "0", "x", "--seed" };
        string[] colours = { "1", "2", "3", "4", "5", "6", "7", "8", "0", "9", "08", "-1", "--mute", "" };
        int runs = 800, bad = 0;
        for (int r = 0; r < runs; r++)
        {
            var args = new List<string>();
            int? s0 = null, s1 = null;
            int n = rng.Next(0, 5);
            for (int k = 0; k < n; k++)
            {
                string s = slots[rng.Next(slots.Length)], c = colours[rng.Next(colours.Length)];
                args.Add("--colour");
                args.Add(s);
                if (s.StartsWith("--")) { args.Add("7"); continue; } // "--seed 7": the colour flag took no value
                args.Add(c);
                if (c.StartsWith("--")) continue; // the slot was taken, the flag refused, --mute read as a flag
                if (s is "1" or "2" && c.Length == 1 && c[0] >= '1' && c[0] <= '8')
                {
                    if (s == "1") s0 = c[0] - '1'; else s1 = c[0] - '1';
                }
            }
            LaunchOptions o = LaunchOptions.Parse(args.ToArray());
            (int g0, int g1) = Effective(o);
            (int w0, int w1) = Model(s0, s1);
            bool ok = g0 == w0 && g1 == w1 && g0 != g1 && (uint)g0 < UiText.ColourCount && (uint)g1 < UiText.ColourCount;
            if (!ok && bad++ < 10) Check(false, $"--colour fuzz '{string.Join(' ', args)}': colours {g0 + 1}, {g1 + 1}, want {w0 + 1}, {w1 + 1}");
        }
        Check(bad == 0, $"--colour fuzz: {bad} of {runs} streams wrong");
        GD.Print($"  --colour fuzz: {runs} streams, {bad} wrong");
    }

    // (1) 64 ordered pairs through the real Match.
    private async Task ColourPairsThroughMatch()
    {
        int pairs = 0, minPairAt = -1;
        float minLum = float.MaxValue, minGap = float.MaxValue;
        for (int a = 0; a < UiText.ColourCount; a++)
            for (int b = 0; b < UiText.ColourCount; b++)
            {
                string how = $"--colour 1 {a + 1} --colour 2 {b + 1}";
                var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
                AddChild(match);
                match.Start(_data, LaunchOptions.Parse(new[] { "--colour", "1", $"{a + 1}", "--colour", "2", $"{b + 1}", "--units", "0", "--no-bases", "--mute", "--no-fog", "--map-size", "128" }));
                match.GetNode<SimRunner>("SimRunner").ProcessMode = ProcessModeEnum.Disabled;
                (int w0, int w1) = Model(a, b);
                Check(match.PlayerRgb.Length == 2 && match.PlayerRgb[0] == _ui.ColourRgb(w0) && match.PlayerRgb[1] == _ui.ColourRgb(w1) && match.PlayerRgb[0] != match.PlayerRgb[1],
                    $"{how}: PlayerRgb [{string.Join(", ", match.PlayerRgb.Select(c => c.ToString("X6")))}], want colours {w0 + 1}, {w1 + 1}");
                var combat = match.GetNode<CombatViews>("World3D/CombatViews");
                for (int p = 0; p < 2; p++)
                {
                    Color fill = combat.CorpseColor(p), rim = combat.CorpseRimColor(p);
                    float lum = 0.2126f * fill.R + 0.7152f * fill.G + 0.0722f * fill.B;
                    minLum = Math.Min(minLum, lum);
                    Check(lum >= 0.2f && rim.R < fill.R && rim.G < fill.G && rim.B < fill.B, $"{how}: player {p} corpse fill {fill} (luminance {lum:0.000}) / rim {rim}");
                }
                Color x = combat.CorpseColor(0), y = combat.CorpseColor(1);
                float d = MathF.Sqrt((x.R - y.R) * (x.R - y.R) + (x.G - y.G) * (x.G - y.G) + (x.B - y.B) * (x.B - y.B));
                if (d < minGap) { minGap = d; minPairAt = a * 8 + b; }
                Check(d >= 0.25f, $"{how}: the two corpse fills are only {d:0.000} apart");
                pairs++;
                RemoveChild(match);
                match.QueueFree();
                await Frames(1);
            }
        GD.Print($"  colour pairs through Match: {pairs}, lowest corpse luminance {minLum:0.000}, closest fills {minGap:0.000} (colours {minPairAt / 8 + 1} / {minPairAt % 8 + 1})");
    }

    // (5) BUG-0662 probe. Frame 1 takes keys 10-13 in a 4-node pool; frame 2 takes 5, 10, 11, 12 (13 left, 5 new, in key order as
    // AbilityViews takes them). Keys 10-12 didn't move: they should keep their nodes, so their meshes aren't rebuilt (BUG-0520).
    private async Task PoolKeepsStableKeys()
    {
        var match = GD.Load<PackedScene>("res://scenes/Match.tscn").Instantiate<Match>();
        AddChild(match);
        match.Start(_data, LaunchOptions.Parse(new[] { "--units", "0", "--no-bases", "--mute", "--no-fog", "--no-combat" }));
        SimRunner runner = match.GetNode<SimRunner>("SimRunner");
        runner.ProcessMode = ProcessModeEnum.Disabled;
        Rts.Sim.Map.Heightmap map = runner.Simulation!.World.Heightmap;
        var holder = new Node3D { Name = "QaVh1Pool" };
        AddChild(holder);
        var pool = new GroundDiscPool(holder, "QaVh1Disc", 64, 4);
        var mid = new System.Numerics.Vector2(map.Width * 0.5f, map.Height * 0.5f);
        System.Numerics.Vector2 At(int key) => mid + new System.Numerics.Vector2(key * 3f, 0f);
        void Frame(int[] keys)
        {
            pool.Begin();
            foreach (int k in keys)
            {
                GroundDisc d = pool.Take(k);
                d.Place(map, At(k), 1.5f, 0f, 0.1f);
                d.Visible = true;
            }
            pool.End();
        }
        Frame(new[] { 10, 11, 12, 13 });
        var before = new Dictionary<int, GroundDisc>();
        foreach (int k in new[] { 10, 11, 12 }) before[k] = pool.NodeOf(k)!;
        int rebuildsBefore = 0;
        for (int n = 0; n < pool.Nodes; n++) rebuildsBefore += pool.Node(n).Rebuilds;
        Frame(new[] { 5, 10, 11, 12 });
        int rebuilt = -rebuildsBefore - 1; // key 5's own mesh is the one rebuild expected
        for (int n = 0; n < pool.Nodes; n++) rebuilt += pool.Node(n).Rebuilds;
        int moved = 0;
        foreach (int k in new[] { 10, 11, 12 }) if (!ReferenceEquals(pool.NodeOf(k), before[k])) moved++;
        // A probe until BUG-0662 is triaged: the stable keys' nodes changed hands, so each mesh was rebuilt.
        if (moved > 0)
            GD.Print($"  KNOWN BUG-0662 probe: a new key 5 in a full pool moved {moved} of 3 stable keys (10-12) to other nodes ({rebuilt} extra mesh rebuilds), pool {pool.Nodes} nodes");
        else
            GD.Print($"  pool: stable keys kept their nodes, pool {pool.Nodes} nodes");
        Check(pool.InUse == 4 && pool.NodeOf(13) == null, $"pool: in use {pool.InUse}, key 13 holds {(pool.NodeOf(13) != null ? "a node" : "none")}");
        int visible = 0;
        for (int n = 0; n < pool.Nodes; n++) if (pool.Node(n).Visible) visible++;
        Check(visible == 4, $"pool: {visible} nodes visible, want 4");
        pool.Free();
        RemoveChild(holder);
        holder.QueueFree();
        RemoveChild(match);
        match.QueueFree();
        await Frames(2);
    }

    // (3) and (4) through the menu.
    private async Task MenuSetupFuzzAndOpenings()
    {
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.UserArgs = Array.Empty<string>();
        AddChild(main);
        await Frames(2);
        Check(main.Menu is { Visible: true }, "the menu did not show");
        MainMenu menu = main.Menu!;
        SkirmishSetup setup = main.Setup!;
        await ClickControl(menu.SkirmishButton);

        var rng = new Random(6621);
        int refused = 0, wrong = 0;
        for (int step = 0; step < 400; step++)
        {
            int slot = rng.Next(2), c = rng.Next(UiText.ColourCount);
            int other = setup.ColourPick(1 - slot);
            int want = c == other ? LaunchOptions.FirstFreeColour(other) : c;
            if (c == other) refused++;
            setup.ColourOptions[slot].Select(c);
            setup.ColourOptions[slot].EmitSignal(OptionButton.SignalName.ItemSelected, (long)c);
            bool ok = setup.ColourPick(slot) == want && setup.ColourPick(1 - slot) == other && setup.ColourPick(0) != setup.ColourPick(1)
                && GreyedOnly(setup, 0, setup.ColourPick(1)) && GreyedOnly(setup, 1, setup.ColourPick(0));
            if (!ok && wrong++ < 10)
                Check(false, $"setup fuzz step {step}: slot {slot + 1} picked colour {c + 1} (other holds {other + 1}): picks {setup.ColourPick(0) + 1}, {setup.ColourPick(1) + 1}, want {want + 1} on slot {slot + 1}");
        }
        Check(wrong == 0, $"setup fuzz: {wrong} of 400 picks wrong");
        GD.Print($"  setup fuzz: 400 picks ({refused} on the other's colour), {wrong} wrong; last picks {setup.ColourPick(0) + 1}, {setup.ColourPick(1) + 1}");
        int p0 = setup.ColourPick(0), p1 = setup.ColourPick(1);
        await ClickControl(setup.StartButton);
        await Frames(2);
        Check(main.CurrentMatch is { } m0 && m0.PlayerRgb.Length == 2 && m0.PlayerRgb[0] == _ui.ColourRgb(p0) && m0.PlayerRgb[1] == _ui.ColourRgb(p1),
            $"Start after the fuzz: PlayerRgb [{string.Join(", ", main.CurrentMatch?.PlayerRgb.Select(c => c.ToString("X6")) ?? Array.Empty<string>())}], picks {p0 + 1}, {p1 + 1}");
        await QuitToMenu(main);

        // (4) The menu's openings: by tick 400 the AI has a worker at work, on every seed and size.
        ulong[] seeds = { 1, 2, 3, 7, 42, 9001 };
        int sizes = setup.MapSizeOption.ItemCount, gathering = 0, runs = 0, latest = 0, ticksNoneStanding = 0;
        foreach (ulong seed in seeds)
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
                if (match == null) { Check(false, $"{how}: no match"); continue; }
                runs++;
                SimRunner runner = match.GetNode<SimRunner>("SimRunner");
                match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
                runner.ProcessMode = ProcessModeEnum.Disabled; // tick by hand: the AI is in the sim, so this is the same match
                Simulation sim = runner.Simulation!;
                int first = -1, idleTicks = 0, gold0 = sim.World.Gold[1];
                while (sim.TickNumber < 500)
                {
                    sim.Tick();
                    int w = AiWorking(sim.World);
                    if (first < 0 && w > 0) first = (int)sim.TickNumber;
                    if (sim.TickNumber >= 300 && w == 0) idleTicks++;
                }
                Check(first >= 0 && first <= 400, $"{how}: no AI worker gathering or returning by tick 400 (first at {first})");
                if (first >= 0 && first <= 400) gathering++;
                latest = Math.Max(latest, first);
                ticksNoneStanding += idleTicks;
                GD.Print($"  opening {how}: AI first gathering / returning at tick {first}; ticks 300-500 with none standing at a node or drop-off: {idleTicks} of 200; gold {gold0} -> {sim.World.Gold[1]}");
                await QuitToMenu(main);
            }
        GD.Print($"  openings: {gathering} of {runs} with the AI at work by tick 400 (latest first tick {latest}); (a snapshot check is not enough on Normal) {ticksNoneStanding} of {runs * 200} ticks in 300-500 had no AI worker in Gathering / Returning");
        Check(runs == seeds.Length * sizes && gathering == runs, $"openings: {gathering} of {runs} (want {seeds.Length * sizes})");
        // AiSeatTest's own menu match (seed 1, the first size, slot 2 Hard) sampled at 8x: which ticks near 400 have no AI worker
        // standing at a node or a drop-off (a walking worker is Moving)? Its Check reads one frame's tick, which the frame time picks.
        await ClickControl(menu.SkirmishButton);
        setup.SeedEdit.Text = "1";
        setup.SeedEdit.EmitSignal(LineEdit.SignalName.TextChanged, setup.SeedEdit.Text);
        setup.MapSizeOption.Select(0);
        setup.DifficultyOption.Select((int)AiDifficulty.Hard);
        await ClickControl(setup.StartButton);
        await Frames(2);
        if (main.CurrentMatch is { } hard)
        {
            SimRunner r = hard.GetNode<SimRunner>("SimRunner");
            r.ProcessMode = ProcessModeEnum.Disabled;
            Simulation sim = r.Simulation!;
            var none = new List<long>();
            while (sim.TickNumber < 440)
            {
                sim.Tick();
                if (sim.TickNumber >= 400 && AiWorking(sim.World) == 0) none.Add(sim.TickNumber);
            }
            GD.Print($"  AiSeatTest's menu match (seed 1, Hard): ticks 400-440 with no AI worker gathering / returning: {none.Count} [{string.Join(", ", none)}]");
            await QuitToMenu(main);
        }
        RemoveChild(main);
        main.QueueFree();
        await Frames(2);
    }

    private static int AiWorking(World w)
    {
        UnitStore u = w.Units;
        int working = 0;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == 1 && u.State[i] is UnitState.Gathering or UnitState.Returning) working++;
        return working;
    }

    private static bool GreyedOnly(SkirmishSetup setup, int slot, int taken)
    {
        OptionButton list = setup.ColourOptions[slot];
        for (int c = 0; c < list.ItemCount; c++)
            if (list.IsItemDisabled(c) != (c == taken)) return false;
        return list.ItemCount == UiText.ColourCount;
    }

    // ---- helpers ----

    private async Task QuitToMenu(Main main)
    {
        PushKey(Key.Escape);
        await Frames(2);
        Check(main.Paused, "Esc did not pause");
        await ClickControl(main.Pause!.QuitToMenuButton);
        await Frames(3);
        Check(main.CurrentMatch == null && !GetTree().Paused && main.Menu!.Visible, "Quit to menu state");
    }

    private void Move(Vector2 at) => GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at });

    private void Button(MouseButton button, bool pressed, Vector2 at) =>
        GetViewport().PushInput(new InputEventMouseButton
        {
            ButtonIndex = button, Pressed = pressed, Position = at, GlobalPosition = at,
            ButtonMask = pressed ? MouseButtonMask.Left : 0,
        });

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
