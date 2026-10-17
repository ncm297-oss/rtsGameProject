using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Rts.Sim;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.ViewApi;

namespace Rts.Game.Tests;

/// <summary>
/// QA M6-V5 (2026-10-16-1654): attacks on the 2-4 player slots the dev scenes don't make.
/// <list type="bullet">
/// <item>Flags: <c>--players</c> junk (1, 5, 0, 9, 02, +3, words, empty, missing, a flag) refused with the next flag still read;
/// every slot count x faction per slot x controller per slot (human, easy, normal, hard; both <c>--ai</c> forms for slot 2)
/// through <c>LaunchOptions.Parse</c> and <c>SimRunner.SeatsFor</c>; a 3000-stream <c>--colour</c> fuzz over 2-4 players
/// (the match's colours always distinct, the docs' "keeps its default, else first free" rule exactly).</item>
/// <item>Setup screen: every slot count x faction x difficulty combo through the lists, <c>MatchArgs</c>, <c>Parse</c>,
/// <c>SeatsFor</c>; a 3000-step fuzz of Add / Remove / <c>PickColour</c> / the lists' <c>ItemSelected</c> (shown colours
/// distinct, each list greys exactly the other shown slots' colours, a free pick kept, the buttons' disabled states).</item>
/// <item>Start locations: the menu's match (<c>--units 0</c>, AI seats) on seeds 1-7 at 3 and 4 seats on the 128 map, and
/// seeds 1-3 on 192 and 256: one hall per seat on its own start location, halls apart, each seeing a gold mine, distinct
/// colours, the starting workers.</item>
/// <item>Hash twin: a 4-seat match (three AIs, Easy / Normal / Hard) run twice to tick 2400 gives the same StateHash at every
/// 100th tick, another seed differs; the AIs build; no NaN / out-of-map unit, no negative stock, per tick.</item>
/// <item>Minimap: with <c>--no-fog</c> and four seats, every unit's dot is its own seat's colour.</item>
/// <item>Match over: 4 seats, the human second out (Defeat, the match still running); 4 seats, a Draw of the last two (a
/// never-building executioner); 4 seats, the human wins as the last two fall together; the stats' defeat cells.</item>
/// <item>The real Main with junk <c>--players</c> (1, 5, x) or a slot past the players starts a 2- (or 3-) seat match;
/// <c>--players 4 --units 12</c> (the dev army) seats four, each with a hall, armies for two.</item>
/// </list>
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM6V5Test.tscn</c>; prints "QA M6-V5 TEST PASS" or "QA M6-V5 TEST FAIL: ..." lines.</remarks>
public partial class QaM6V5Test : Node
{
    private static readonly string[] Dev = { "--units", "0", "--no-bases", "--mute", "--seed", "2", "--speed", "8" };

    private readonly List<string> _failures = new();
    private readonly List<DefeatEvent> _events = new();
    private GameData _data = null!;
    private UiText _ui = null!;
    private Main _main = null!;
    private int _ended;

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
            _main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
            _main.UserArgs = Array.Empty<string>();
            _main.SettingsPath = $"user://qa-m6v5-{OS.GetProcessId()}/settings.cfg";
            AddChild(_main);
            await Frames(2);
            PlayersJunk();
            FlagMatrix();
            ColourFuzz();
            await SetupMatrix();
            await SetupFuzz();
            await StartLocations();
            await HashTwin();
            await MinimapDots();
            await HumanSecondOut();
            await DrawOfTheLastTwo();
            await HumanWinsLastTwoTogether();
            await JunkThroughMain();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        if (GetTree().Paused) GetTree().Paused = false;
        foreach (string f in _failures.Take(60)) GD.Print($"QA M6-V5 TEST FAIL: {f}");
        GD.Print($"QA M6-V5: {_failures.Count} failures");
        if (_failures.Count == 0) GD.Print("QA M6-V5 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- flags ----

    private void PlayersJunk()
    {
        foreach (string v in new[] { "1", "5", "0", "9", "02", "+3", "-2", "three", "", " 3", "3 ", "４" })
        {
            LaunchOptions o = LaunchOptions.Parse(new[] { "--players", v, "--seed", "5" });
            Check(o.Players == null && o.Seed == 5, $"--players '{v}': Players {o.Players}, seed {o.Seed} (want null, 5)");
        }
        LaunchOptions f = LaunchOptions.Parse(new[] { "--players", "--seed", "5" });
        Check(f.Players == null && f.Seed == 5, $"--players --seed 5: Players {f.Players}, seed {f.Seed} (BUG-0041)");
        LaunchOptions m = LaunchOptions.Parse(new[] { "--players" });
        Check(m.Players == null, $"--players at the end: {m.Players}");
        for (int n = 2; n <= 4; n++) Check(LaunchOptions.Parse(new[] { "--players", n.ToString() }).Players == n, $"--players {n} not read");
        Check(LaunchOptions.Parse(new[] { "--players", "3", "--players", "x" }).Players == 3, "a junk repeat of --players dropped the good value");
        Check(LaunchOptions.Parse(new[] { "--players", "3", "--players", "4" }).Players == 4, "the last good --players does not win");
        Check(LaunchOptions.HasMatchFlag(new[] { "--players", "3" }), "--players does not skip the menu");
        // --ai's two-value form: a lone digit at the end, or a digit then a flag.
        LaunchOptions a = LaunchOptions.Parse(new[] { "--ai", "3" });
        Check(Enumerable.Range(0, 4).All(s => a.AiOf(s) == null), "--ai 3 (no difficulty) set a seat");
        LaunchOptions b = LaunchOptions.Parse(new[] { "--ai", "3", "--seed", "7" });
        Check(b.AiOf(2) == null && b.Seed == 7, $"--ai 3 --seed 7: slot 3 {b.AiOf(2)}, seed {b.Seed}");
        LaunchOptions c = LaunchOptions.Parse(new[] { "--ai", "9", "hard", "--seed", "7" });
        Check(Enumerable.Range(0, 4).All(s => c.AiOf(s) == null) && c.Seed == 7, $"--ai 9 hard: seats set, seed {c.Seed}");
        LaunchOptions d = LaunchOptions.Parse(new[] { "--ai", "1", "hard" });
        Check(d.AiOf(0) == null, "--ai 1 hard made the local player an AI");
        Check(LaunchOptions.Parse(new[] { "--ai", "2", "hard", "--ai", "easy" }).AiOf(1) == AiDifficulty.Easy, "--ai 2 hard then --ai easy: the last does not win");
        Check(LaunchOptions.Parse(new[] { "--ai", "easy", "--ai", "2", "hard" }).AiOf(1) == AiDifficulty.Hard, "--ai easy then --ai 2 hard: the last does not win");
        Check(LaunchOptions.Parse(new[] { "--ai", "4", "Hard" }).AiOf(3) == null, "--ai 4 Hard (case) accepted");
    }

    // Every slot count x faction per slot x controller per slot (0 human, 1+d AI of difficulty d) through the flags.
    private void FlagMatrix()
    {
        int factions = _data.Factions.Length;
        int rows = 0, bad = 0;
        for (int n = 2; n <= 4; n++)
        {
            int fCombos = Pow(factions, n), cCombos = Pow(4, n - 1);
            for (int fc = 0; fc < fCombos; fc++)
                for (int cc = 0; cc < cCombos; cc++)
                {
                    var args = new List<string> { "--players", n.ToString() };
                    int[] f = new int[n];
                    var ai = new AiDifficulty?[n];
                    for (int s = 0, x = fc; s < n; s++, x /= factions)
                    {
                        f[s] = x % factions;
                        args.AddRange(new[] { "--faction", (s + 1).ToString(), _data.Factions[f[s]].Key });
                    }
                    for (int s = 1, x = cc; s < n; s++, x /= 4)
                    {
                        int k = x % 4;
                        if (k == 0) continue;
                        ai[s] = (AiDifficulty)(k - 1);
                        string id = DataLimits.AiDifficultyIds[k - 1];
                        if (s == 1 && (fc + cc) % 2 == 0) args.AddRange(new[] { "--ai", id }); // M6-V2's one-value form
                        else args.AddRange(new[] { "--ai", (s + 1).ToString(), id });
                    }
                    LaunchOptions o = LaunchOptions.Parse(args.ToArray());
                    int[] slotFactions = { -1, -1, -1, -1 };
                    var slotAi = new AiDifficulty?[4];
                    for (int s = 0; s < 4; s++)
                    {
                        slotFactions[s] = o.FactionId(s) is string id ? _data.FindFaction(id) : -1;
                        slotAi[s] = o.AiOf(s);
                    }
                    ImmutableArray<PlayerSeat> seats = SimRunner.SeatsFor(o.Players ?? 2, factions, slotFactions, slotAi);
                    var want = Enumerable.Range(0, n).Select(p => new PlayerSeat(f[p], ai[p] != null ? SeatController.Ai : SeatController.Human, ai[p] ?? AiDifficulty.Normal)).ToArray();
                    rows++;
                    if (o.Players != n || !seats.SequenceEqual(want))
                    {
                        if (bad++ < 5) Check(false, $"flags [{string.Join(' ', args)}]: seats [{string.Join(", ", seats)}], want [{string.Join(", ", want)}]");
                    }
                }
        }
        Check(bad == 0 && rows > 1000, $"flag matrix: {bad} of {rows} rows wrong");
        GD.Print($"QA M6-V5: flag matrix {rows} rows, {bad} wrong");
    }

    // Random --colour streams over 2-4 players: the match's colours are distinct; a slot's last good pick is kept when no
    // earlier slot holds it, else it takes its default, else the first free colour (docs/03 "Implementation (M6-V5)").
    private void ColourFuzz()
    {
        var rng = new RandomNumberGenerator { Seed = 5150 };
        int bad = 0;
        for (int run = 0; run < 3000; run++)
        {
            int n = 2 + run % 3;
            var args = new List<string> { "--players", n.ToString() };
            int?[] last = new int?[4];
            int k = rng.RandiRange(0, 8);
            for (int i = 0; i < k; i++)
            {
                int s = rng.RandiRange(1, 4), c = rng.RandiRange(0, 9); // 0 and 9 are junk
                args.AddRange(new[] { "--colour", s.ToString(), c.ToString() });
                if (c >= 1 && c <= UiText.ColourCount) last[s - 1] = c - 1;
            }
            LaunchOptions o = LaunchOptions.Parse(args.ToArray());
            var held = new bool[UiText.ColourCount];
            var got = new int[n];
            string why = "";
            for (int s = 0; s < 4; s++)
            {
                int want = last[s] ?? s;
                if (held[want]) want = !held[s] ? s : Array.IndexOf(held, false);
                int eff = o.ColourIndex(s) ?? s;
                if (s < n) got[s] = eff;
                if (eff != want) why += $" slot {s + 1} colour {eff + 1} (want {want + 1});";
                held[eff] = true;
            }
            if (got.Distinct().Count() != n) why += $" colours [{string.Join(", ", got.Select(c => c + 1))}] not distinct;";
            if (why.Length > 0 && bad++ < 5) Check(false, $"--colour fuzz [{string.Join(' ', args)}]:{why}");
        }
        Check(bad == 0, $"--colour fuzz: {bad} of 3000 streams wrong");
    }

    // ---- the setup screen ----

    private async Task SetupMatrix()
    {
        _main.ShowSetup();
        await Frames(2);
        SkirmishSetup setup = _main.Setup!;
        int factions = _data.Factions.Length;
        int rows = 0, bad = 0;
        for (int n = 2; n <= 4; n++)
        {
            while (setup.SlotCount < n) setup.AddSlot();
            while (setup.SlotCount > n) setup.RemoveSlot();
            Check(setup.SlotCount == n && setup.SlotLabels.Count(l => l.Visible) == n, $"setup at {n} slots: {setup.SlotCount}, {setup.SlotLabels.Count(l => l.Visible)} shown");
            int fCombos = Pow(factions, n), dCombos = Pow(3, n - 1);
            for (int fc = 0; fc < fCombos; fc++)
                for (int dc = 0; dc < dCombos; dc++)
                {
                    int[] f = new int[n];
                    var ai = new AiDifficulty?[n];
                    for (int s = 0, x = fc; s < n; s++, x /= factions) setup.FactionOptions[s].Select(f[s] = x % factions);
                    for (int s = 1, x = dc; s < n; s++, x /= 3)
                    {
                        ai[s] = (AiDifficulty)(x % 3);
                        setup.DifficultyOptions[s]!.Select(x % 3);
                    }
                    string[] args = setup.MatchArgs()!;
                    LaunchOptions o = LaunchOptions.Parse(args);
                    int[] slotFactions = { -1, -1, -1, -1 };
                    var slotAi = new AiDifficulty?[4];
                    for (int s = 0; s < 4; s++)
                    {
                        slotFactions[s] = o.FactionId(s) is string id ? _data.FindFaction(id) : -1;
                        slotAi[s] = o.AiOf(s);
                    }
                    ImmutableArray<PlayerSeat> seats = SimRunner.SeatsFor(o.Players ?? 2, factions, slotFactions, slotAi);
                    var want = Enumerable.Range(0, n).Select(p => new PlayerSeat(f[p], p == 0 ? SeatController.Human : SeatController.Ai, ai[p] ?? AiDifficulty.Normal)).ToArray();
                    var colours = Enumerable.Range(0, n).Select(s => o.ColourIndex(s) ?? s).ToArray();
                    bool coloursOk = colours.Distinct().Count() == n && Enumerable.Range(0, n).All(s => colours[s] == setup.ColourPick(s));
                    rows++;
                    if (o.Players != n || o.UnitsPerPlayer != 0 || !seats.SequenceEqual(want) || !coloursOk)
                        if (bad++ < 5) Check(false, $"setup [{string.Join(' ', args)}]: seats [{string.Join(", ", seats)}], want [{string.Join(", ", want)}], colours [{string.Join(", ", colours)}]");
                }
        }
        Check(bad == 0, $"setup matrix: {bad} of {rows} rows wrong");
        GD.Print($"QA M6-V5: setup matrix {rows} rows, {bad} wrong");
        await Click(setup.BackButton);
    }

    private async Task SetupFuzz()
    {
        _main.ShowSetup();
        await Frames(2);
        SkirmishSetup setup = _main.Setup!;
        var rng = new RandomNumberGenerator { Seed = 1654 };
        int bad = 0;
        for (int step = 0; step < 3000; step++)
        {
            int op = rng.RandiRange(0, 9), before = setup.SlotCount;
            string what;
            bool keptExpected = false;
            int slot = -1, colour = -1;
            if (op == 0) { setup.AddSlot(); what = "Add"; }
            else if (op == 1) { setup.RemoveSlot(); what = "Remove"; }
            else
            {
                slot = rng.RandiRange(0, setup.SlotCount - 1);
                colour = rng.RandiRange(0, UiText.ColourCount - 1);
                keptExpected = Enumerable.Range(0, setup.SlotCount).All(s => s == slot || setup.ColourPick(s) != colour);
                if (op % 2 == 0) { setup.PickColour(slot, colour); what = $"PickColour({slot}, {colour})"; }
                else
                {
                    setup.ColourOptions[slot].Select(colour);
                    setup.ColourOptions[slot].EmitSignal(OptionButton.SignalName.ItemSelected, (long)colour);
                    what = $"ItemSelected({slot}, {colour})";
                }
            }
            int n = setup.SlotCount;
            var picks = Enumerable.Range(0, n).Select(setup.ColourPick).ToArray();
            string why = "";
            if (n < 2 || n > 4) why += $" {n} slots;";
            if (op == 0 && n != Math.Min(4, before + 1) || op == 1 && n != Math.Max(2, before - 1)) why += $" {before} -> {n} slots;";
            if (picks.Distinct().Count() != n) why += " colours not distinct;";
            if (keptExpected && setup.ColourPick(slot) != colour) why += $" a free pick moved to {setup.ColourPick(slot)};";
            if (setup.AddSlotButton.Disabled != (n == 4) || setup.RemoveSlotButton.Disabled != (n == 2)) why += " button states;";
            if (setup.SlotLabels.Count(l => l.Visible) != n) why += " shown rows;";
            for (int s = 0; s < n; s++)
            {
                OptionButton list = setup.ColourOptions[s];
                for (int c = 0; c < list.ItemCount; c++)
                    if (list.IsItemDisabled(c) != Enumerable.Range(0, n).Any(o => o != s && picks[o] == c)) { why += $" slot {s} greys {c} wrongly;"; break; }
            }
            string[] args = setup.MatchArgs()!;
            LaunchOptions parsed = LaunchOptions.Parse(args);
            if (parsed.Players != n || Enumerable.Range(0, n).Any(s => (parsed.ColourIndex(s) ?? s) != picks[s])) why += $" args [{string.Join(' ', args)}] disagree;";
            if (why.Length > 0 && bad++ < 5) Check(false, $"setup fuzz step {step} {what}: picks [{string.Join(", ", picks)}]:{why}");
        }
        Check(bad == 0, $"setup fuzz: {bad} of 3000 steps wrong");
        while (setup.SlotCount > 2) setup.RemoveSlot();
        for (int s = 0; s < 4; s++) setup.PickColour(s, s);
        await Click(setup.BackButton);
    }

    // ---- matches from the menu's args ----

    private string[] MenuArgs(ulong seed, int size, int players, params string[] extra)
    {
        var args = new List<string> { "--seed", seed.ToString(), "--map-size", size.ToString(), "--units", "0", "--players", players.ToString(), "--mute" };
        string[] ai = { "", "easy", "normal", "hard" };
        for (int s = 2; s <= players; s++) args.AddRange(new[] { "--ai", s.ToString(), ai[s - 1] });
        args.AddRange(extra);
        return args.ToArray();
    }

    private async Task StartLocations()
    {
        var runs = new List<(ulong Seed, int Size, int Players)>();
        for (ulong s = 1; s <= 7; s++) { runs.Add((s, 128, 3)); runs.Add((s, 128, 4)); }
        for (ulong s = 1; s <= 3; s++) { runs.Add((s, 192, 4)); runs.Add((s, 256, 4)); runs.Add((s, 256, 3)); }
        // Real starts for faction mixes (all of one faction, alternating the other way) and human seats past slot 1 (flags).
        var mixes = new List<(int Run, string[] Extra, int[] Factions, bool[] Ai)>();
        int factions = _data.Factions.Length;
        foreach (int players in new[] { 3, 4 })
        {
            for (int f = 0; f < factions; f++)
            {
                runs.Add(((ulong)(10 + f), 128, players));
                var extra = new List<string>();
                for (int s = 1; s <= players; s++) extra.AddRange(new[] { "--faction", s.ToString(), _data.Factions[f].Key });
                mixes.Add((runs.Count - 1, extra.ToArray(), Enumerable.Repeat(f, players).ToArray(), Enumerable.Range(0, players).Select(p => p > 0).ToArray()));
            }
            runs.Add((12, 128, players));
            var alt = new List<string>();
            for (int s = 1; s <= players; s++) alt.AddRange(new[] { "--faction", s.ToString(), _data.Factions[(s % factions)].Key });
            mixes.Add((runs.Count - 1, alt.ToArray(), Enumerable.Range(0, players).Select(p => (p + 1) % factions).ToArray(), Enumerable.Range(0, players).Select(p => p > 0).ToArray()));
        }
        int nearestWorst = 0;
        for (int run = 0; run < runs.Count; run++)
        {
            (ulong seed, int size, int players) = runs[run];
            string how = $"seed {seed} map {size} {players} seats";
            int mix = mixes.FindIndex(m => m.Run == run);
            _main.StartSkirmish(mix >= 0 ? MenuArgs(seed, size, players, mixes[mix].Extra) : MenuArgs(seed, size, players));
            if (mix >= 0 && _main.CurrentMatch is Match mm)
            {
                how += $" factions [{string.Join(", ", mixes[mix].Factions)}]";
                Check(Enumerable.Range(0, players).All(p => mm.Seats[p].Faction == mixes[mix].Factions[p] && (mm.Seats[p].Controller == SeatController.Ai) == mixes[mix].Ai[p]),
                    $"{how}: seats [{string.Join(", ", mm.Seats)}]");
            }
            Match? match = _main.CurrentMatch;
            if (match?.Simulation == null) { Check(false, $"{how}: no match"); continue; }
            Simulation sim = match.Simulation;
            match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
            await Ticks(sim, 2);
            World w = sim.World;
            Check(w.Config.PlayerCount == players && w.StartLocations.Length == players && match.Seats.Length == players
                && match.Seats.Skip(1).All(x => x.Controller == SeatController.Ai), $"{how}: {w.Config.PlayerCount} players, {w.StartLocations.Length} start locations, seats [{string.Join(", ", match.Seats)}]");
            Check(match.PlayerRgb.Length == players && match.PlayerRgb.Distinct().Count() == players, $"{how}: colours [{string.Join(", ", match.PlayerRgb.Select(c => c.ToString("X6")))}]");
            var centres = new List<System.Numerics.Vector2>();
            int[] workers = new int[players];
            for (int i = 0; i < w.Units.Capacity; i++)
                if (w.Units.Alive[i] && (uint)w.Units.Owner[i] < (uint)players && _data.Units[w.Units.TypeId[i]].Slot == UnitSlot.Worker) workers[w.Units.Owner[i]]++;
            for (int p = 0; p < players; p++)
            {
                Check(workers[p] == _data.Rules.StartingWorkers, $"{how}: player {p} has {workers[p]} workers");
                if (match.Bases == null || match.Bases.HallAnchor[p] < 0) { Check(false, $"{how}: no hall planned for player {p}"); continue; }
                System.Numerics.Vector2 at = StartBase.FootprintCenter(w.NavGrid, _data.Buildings[match.Bases.HallType[p]], match.Bases.HallAnchor[p]);
                int st = w.StartLocations[p].Cell;
                System.Numerics.Vector2 start = w.NavGrid.CellCenter(st % w.NavGrid.Width, st / w.NavGrid.Width);
                Check(System.Numerics.Vector2.Distance(at, start) < 1.5f * MapConstants.CellSize, $"{how}: player {p}'s hall at {at}, its start location at {start}");
                Check(centres.TrueForAll(c => System.Numerics.Vector2.Distance(c, at) > 20f), $"{how}: player {p}'s hall at {at} beside another's");
                centres.Add(at);
                int halls = 0;
                for (int k = 0; k < w.Buildings.Capacity; k++)
                    if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == p && _data.Buildings[w.Buildings.TypeId[k]].Slot == BuildingSlot.TownHall) halls++;
                Check(halls == 1, $"{how}: player {p} has {halls} town halls");
                Check(SeesMine(w, p, out float nearest), $"{how}: player {p} sees no gold mine (nearest {nearest:0} m)");
                nearestWorst = Math.Max(nearestWorst, (int)nearest);
            }
            await QuitMatch();
        }
        GD.Print($"QA M6-V5: start locations {runs.Count} matches, worst nearest mine {nearestWorst} m");
    }

    private bool SeesMine(World w, int player, out float nearest)
    {
        bool sees = false;
        nearest = float.MaxValue;
        int st = w.StartLocations[player].Cell;
        System.Numerics.Vector2 home = w.NavGrid.CellCenter(st % w.NavGrid.Width, st / w.NavGrid.Width);
        ResourceStore r = w.Resources;
        for (int k = 0; k < r.Capacity; k++)
        {
            if (!r.Alive[k]) continue;
            ResourceDef def = _data.Resources[r.TypeId[k]];
            if (def.Resource != ResourceKind.Gold) continue;
            int ax = r.Cell[k] % w.NavGrid.Width, ay = r.Cell[k] / w.NavGrid.Width;
            bool seen = false;
            for (int dy = 0; dy < def.FootprintHeight; dy++)
                for (int dx = 0; dx < def.FootprintWidth; dx++)
                    if (w.Fog.IsVisible(player, (ay + dy) * w.NavGrid.Width + ax + dx)) seen = true;
            sees |= seen;
            float d = System.Numerics.Vector2.Distance(w.NavGrid.CellCenter(ax, ay), home);
            nearest = Math.Min(nearest, d);
        }
        return sees;
    }

    private async Task HashTwin()
    {
        var a = await HashRun(3);
        var b = await HashRun(3);
        var c = await HashRun(4);
        int same = 0;
        for (int i = 0; i < Math.Min(a.Count, b.Count); i++) if (a[i] == b[i]) same++;
        Check(a.Count >= 24 && a.Count == b.Count && same == a.Count, $"hash twin: {same} of {a.Count} / {b.Count} checkpoints equal (want all)");
        if (same != a.Count)
            for (int i = 0; i < Math.Min(a.Count, b.Count); i++)
                if (a[i] != b[i]) { Check(false, $"hash twin: first divergence at checkpoint {i} (tick {(i + 1) * 100}): {a[i]:X16} vs {b[i]:X16}"); break; }
        Check(c.Count > 0 && a.Count > 0 && c[0] != a[0], "hash twin: seed 4's first checkpoint equals seed 3's");
        GD.Print($"QA M6-V5: hash twin {a.Count} checkpoints, {same} equal");
    }

    private async Task<List<ulong>> HashRun(ulong seed)
    {
        _main.StartSkirmish(MenuArgs(seed, 128, 4));
        Match match = _main.CurrentMatch!;
        SimRunner runner = match.GetNode<SimRunner>("SimRunner");
        Simulation sim = runner.Simulation!;
        match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
        runner.FixedFrameDelta = 1.0 / 20;
        runner.GameSpeed = SimRunner.MaxSpeed;
        var hashes = new List<ulong>();
        int nanOrOut = 0, negative = 0;
        World w = sim.World;
        float maxX = w.NavGrid.Width * MapConstants.CellSize, maxY = w.NavGrid.Height * MapConstants.CellSize;
        runner.Ticked += s =>
        {
            if (s.TickNumber % 100 == 0 && s.TickNumber <= 2400) hashes.Add(s.StateHash());
            UnitStore u = s.World.Units;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                System.Numerics.Vector2 p = u.Position[i];
                if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || p.X < 0 || p.Y < 0 || p.X > maxX || p.Y > maxY) nanOrOut++;
            }
            for (int p = 0; p < s.World.Config.PlayerCount; p++) if (s.World.Gold[p] < 0 || s.World.Wood[p] < 0) negative++;
        };
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (sim.TickNumber < 2400 && clock.Elapsed.TotalSeconds < 240) await Frames(1);
        Check(sim.TickNumber >= 2400, $"hash run seed {seed}: tick {sim.TickNumber} after {clock.Elapsed.TotalSeconds:0} s");
        int[] buildings = new int[4], units = new int[4];
        for (int k = 0; k < w.Buildings.Capacity; k++) if (w.Buildings.Alive[k] && (uint)w.Buildings.Owner[k] < 4u) buildings[w.Buildings.Owner[k]]++;
        for (int k = 0; k < w.Units.Capacity; k++) if (w.Units.Alive[k] && (uint)w.Units.Owner[k] < 4u) units[w.Units.Owner[k]]++;
        Check(nanOrOut == 0 && negative == 0, $"hash run seed {seed}: {nanOrOut} NaN / out-of-map unit samples, {negative} negative stocks");
        Check(Enumerable.Range(1, 3).All(p => buildings[p] > 1), $"hash run seed {seed}: the AIs' buildings [{string.Join(", ", buildings)}] by tick {sim.TickNumber} (want > 1 for players 1-3)");
        GD.Print($"QA M6-V5: seed {seed} 4 seats at tick {sim.TickNumber} ({clock.Elapsed.TotalSeconds:0.0} s): buildings [{string.Join(", ", buildings)}], units [{string.Join(", ", units)}], defeated [{string.Join(", ", Enumerable.Range(0, 4).Select(w.IsDefeated))}]");
        runner.FixedFrameDelta = null;
        runner.GameSpeed = 1.0;
        await QuitMatch();
        return hashes;
    }

    private async Task MinimapDots()
    {
        _main.StartSkirmish(MenuArgs(5, 128, 4, "--no-fog"));
        Match match = _main.CurrentMatch!;
        Simulation sim = match.Simulation!;
        match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
        await Ticks(sim, 4);
        var mini = match.GetNode<Minimap>("Hud/Minimap");
        mini.Refresh(sim);
        UnitStore u = sim.World.Units;
        int[] right = new int[4], wrong = new int[4];
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || (uint)u.Owner[i] >= 4u || !mini.Raster!.TryPixelOf(u.Position[i], out int x, out int y)) continue;
            Color c = UnitViews.ColorFromRgb(match.PlayerRgb[u.Owner[i]]);
            Color got = mini.DotsImage.GetPixel(x, y);
            if (Mathf.Abs(got.R - c.R) <= 0.01f && Mathf.Abs(got.G - c.G) <= 0.01f && Mathf.Abs(got.B - c.B) <= 0.01f) right[u.Owner[i]]++;
            else wrong[u.Owner[i]]++;
        }
        Check(right.All(n => n > 0) && wrong.All(n => n == 0), $"minimap dots, no fog, 4 seats: right [{string.Join(", ", right)}], wrong [{string.Join(", ", wrong)}]");
        await QuitMatch();
    }

    // ---- match over ----

    // 4 seats: player 1 razes player 3 (no banner), then player 0: Defeat while 1 and 2 stand (MatchOver false); Continue
    // runs the match on; the stats' defeat cells for 3 and 0.
    private async Task HumanSecondOut()
    {
        Match match = DevStart(4);
        Simulation sim = match.Simulation!;
        World w = sim.World;
        await Halls(sim, new[] { 0, 1, 2, 3 });
        await RazeOne(sim, 1, 3);
        await Ticks(sim, 10);
        Check(!_main.BannerUp && match.Outcome == MatchOutcome.None && !sim.MatchOver, $"second out: after 3 fell banner {_main.BannerUp}, outcome {match.Outcome}");
        await RazeOne(sim, 1, 0, waitDefeat: false);
        await UntilBanner(sim, "second out");
        Check(_main.Banner!.Title.Text == _ui.MatchOver(MatchOverText.Defeat) && match.Outcome == MatchOutcome.Defeat && !sim.MatchOver && !w.IsDefeated(1) && !w.IsDefeated(2),
            $"second out: title '{_main.Banner.Title.Text}', outcome {match.Outcome}, MatchOver {sim.MatchOver} (want false)");
        int bannerTick = sim.TickNumber;
        await Click(_main.Banner.ContinueButton);
        await Ticks(sim, 30);
        Check(!_main.BannerUp && sim.TickNumber >= bannerTick + 30 && _ended == 1 && match.Outcome == MatchOutcome.Defeat, $"second out: after Continue tick {sim.TickNumber}, ended {_ended}");
        _main.ShowStats();
        CheckStats(match, 4, "second out");
        _main.CloseStats();
        await QuitMatch();
    }

    // 4 seats, seat 3 never builds (the executioner, outside the match-over count): it razes player 2 (no banner), then 0
    // and 1 fall on one tick: a Draw.
    private async Task DrawOfTheLastTwo()
    {
        Match match = DevStart(4);
        Simulation sim = match.Simulation!;
        World w = sim.World;
        int[] together = { 0, 1 };
        int[] anchors = await RowHalls(sim, together);
        await Halls(sim, new[] { 2 }, offset: true);
        Check(w.Buildings.Count == 3 && !w.EverBuilt(3), $"draw staging: {w.Buildings.Count} halls, seat 3 built {w.EverBuilt(3)}");
        await RazeOne(sim, 3, 2);
        await Ticks(sim, 10);
        Check(!_main.BannerUp && match.Outcome == MatchOutcome.None && !sim.MatchOver, $"draw: after 2 fell banner {_main.BannerUp}, outcome {match.Outcome}");
        await Batteries(sim, 3, together, anchors);
        await UntilBanner(sim, "draw of the last two");
        Check(match.Outcome == MatchOutcome.Draw && _main.Banner!.Title.Text == _ui.MatchOver(MatchOverText.Draw) && sim.MatchOver,
            $"draw: outcome {match.Outcome}, title '{_main.Banner!.Title.Text}', MatchOver {sim.MatchOver}, events [{string.Join(", ", _events)}]");
        Check(_events.Count == 3 && _events[1].Tick == _events[2].Tick && _events[0].Tick < _events[1].Tick, $"draw: events [{string.Join(", ", _events)}]");
        await Click(_main.Banner.StatsButton);
        CheckStats(match, 4, "draw");
        await Type(Key.Escape);
        await QuitMatch();
    }

    // 4 seats: player 0 razes player 1 (no banner), then players 2 and 3 fall on one tick: Victory on that tick.
    private async Task HumanWinsLastTwoTogether()
    {
        Match match = DevStart(4);
        Simulation sim = match.Simulation!;
        World w = sim.World;
        int[] together = { 2, 3 };
        int[] anchors = await RowHalls(sim, together);
        await Halls(sim, new[] { 0, 1 }, offset: true);
        Check(w.Buildings.Count == 4, $"victory staging: {w.Buildings.Count} halls");
        await RazeOne(sim, 0, 1);
        await Ticks(sim, 10);
        Check(!_main.BannerUp && match.Outcome == MatchOutcome.None, $"victory: after 1 fell banner {_main.BannerUp}, outcome {match.Outcome}");
        await Batteries(sim, 0, together, anchors);
        await UntilBanner(sim, "victory, last two together");
        Check(match.Outcome == MatchOutcome.Victory && _main.Banner!.Title.Text == _ui.MatchOver(MatchOverText.Victory) && sim.MatchOver
            && _events.Count == 3 && _events[1].Tick == _events[2].Tick && sim.TickNumber == _events[2].Tick + 1,
            $"victory: outcome {match.Outcome}, title '{_main.Banner!.Title.Text}', events [{string.Join(", ", _events)}], tick {sim.TickNumber}");
        await Click(_main.Banner.StatsButton);
        CheckStats(match, 4, "victory together");
        await Type(Key.Escape);
        await QuitMatch();
    }

    private void CheckStats(Match match, int players, string how)
    {
        MatchStats stats = _main.Stats!;
        int col = Array.FindIndex(MatchStats.Columns, c => c.Label == MatchOverText.DefeatedAt);
        Check(stats.Visible && stats.RowCount == players, $"{how}: stats visible {stats.Visible}, {stats.RowCount} rows (want {players})");
        for (int p = 0; p < Math.Min(players, stats.RowCount); p++)
        {
            DefeatEvent? e = _events.Where(x => x.Player == p).Select(x => (DefeatEvent?)x).FirstOrDefault();
            string want = e is DefeatEvent d ? _ui.Clock(d.Tick + 1) : _ui.MatchOver(MatchOverText.Standing);
            Check(stats.Cell(p, col) == want && stats.Swatch(p).ToRgba32() >> 8 == match.PlayerRgb[p], $"{how}: row {p} defeated '{stats.Cell(p, col)}' (want '{want}')");
        }
    }

    private Match DevStart(int players, params string[] extra)
    {
        var args = Dev.Concat(new[] { "--players", players.ToString() }).ToList();
        for (int s = 1; s <= players; s++) args.AddRange(new[] { "--faction", s.ToString(), "malazan" });
        _main.StartSkirmish(args.Concat(extra).ToArray());
        Match match = _main.CurrentMatch ?? throw new InvalidOperationException("no match");
        _events.Clear();
        _ended = 0;
        match.Ended += _ => _ended++;
        match.GetNode<SimRunner>("SimRunner").Ticked += s => _events.AddRange(s.World.DefeatEvents.ToArray());
        match.GetNode<RtsCamera>("RtsCamera").EdgePanEnabled = false;
        return match;
    }

    private static (int X, int Y) Spot(NavGrid g, int p, bool offset)
    {
        (int X, int Y)[] at = { (g.Width / 4, g.Height / 2), (g.Width * 3 / 4, g.Height / 2), (g.Width / 2, g.Height / 6), (g.Width / 2, g.Height * 5 / 6) };
        (int X, int Y)[] off = { (g.Width / 6, g.Height / 6), (g.Width * 5 / 6, g.Height * 5 / 6), (g.Width / 2, g.Height / 6), (g.Width / 2, g.Height * 5 / 6) };
        return offset ? off[p] : at[p];
    }

    private async Task Halls(Simulation sim, int[] owners, bool offset = false)
    {
        World w = sim.World;
        foreach (int p in owners)
        {
            int hall = DefeatStaging.Hall(_data, w, p);
            (int x, int y) = Spot(w.NavGrid, p, offset);
            DefeatStaging.SpawnAt(sim, p, hall, DefeatStaging.Anchor(w, p, hall, x, y));
        }
        await Ticks(sim, 3);
    }

    private async Task<int[]> RowHalls(Simulation sim, int[] owners)
    {
        World w = sim.World;
        int[] halls = DefeatStaging.Halls(_data, w, owners, out bool alike);
        Check(alike, "row halls differ");
        int[] anchors = DefeatStaging.Row(w, owners, halls);
        for (int k = 0; k < owners.Length; k++) DefeatStaging.SpawnAt(sim, owners[k], halls[k], anchors[k]);
        await Ticks(sim, 3);
        return anchors;
    }

    private async Task Batteries(Simulation sim, int executioner, int[] fall, int[] anchors)
    {
        World w = sim.World;
        NavGrid g = w.NavGrid;
        int shooter = DefeatStaging.Shooter(_data);
        var batteries = new System.Numerics.Vector2[fall.Length][];
        for (int k = 0; k < fall.Length; k++)
        {
            batteries[k] = DefeatStaging.Battery(g, _data.Buildings[w.Buildings.TypeId[DefeatStaging.BuildingAt(w, fall[k], anchors[k]).Index]], anchors[k]);
            foreach (System.Numerics.Vector2 at in batteries[k]) sim.Enqueue(Command.SpawnUnit(executioner, shooter, at));
        }
        await Ticks(sim, 3);
        var guns = new EntityHandle[fall.Length][];
        for (int k = 0; k < fall.Length; k++)
        {
            EntityHandle target = DefeatStaging.BuildingAt(w, fall[k], anchors[k]);
            guns[k] = batteries[k].Select(at => DefeatStaging.UnitAt(w, executioner, at)).ToArray();
            Check(target != default && guns[k].All(h => h != default), $"battery {k}: hall {target}, guns [{string.Join(", ", guns[k])}]");
            foreach (EntityHandle gun in guns[k]) if (gun != default && target != default) sim.Enqueue(Command.Attack(executioner, gun, target, isBuilding: true));
        }
        Check(DefeatStaging.SamePhase(guns), "batteries not in phase");
    }

    // `killer`'s siege ring razes `victim`'s only building.
    private async Task RazeOne(Simulation sim, int killer, int victim, bool waitDefeat = true)
    {
        World w = sim.World;
        NavGrid g = w.NavGrid;
        EntityHandle target = BuildingOf(w, victim);
        int siege = StartBase.UnitOfSlot(_data, w.FactionOf(killer), UnitSlot.Siege);
        int cell = w.Buildings.Cell[target.Index];
        BuildingDef def = _data.Buildings[w.Buildings.TypeId[target.Index]];
        int cx = cell % g.Width, cy = cell / g.Width, level = g.LevelAt(cx, cy), placed = 0;
        for (int r = 4; r <= 5 && placed < 6; r++)
            for (int y = cy - r; y <= cy + def.FootprintHeight - 1 + r && placed < 6; y++)
                for (int x = cx - r; x <= cx + def.FootprintWidth - 1 + r && placed < 6; x++)
                {
                    bool ring = x == cx - r || x == cx + def.FootprintWidth - 1 + r || y == cy - r || y == cy + def.FootprintHeight - 1 + r;
                    if (!ring || (x + y) % 2 != 0 || !g.IsPassable(x, y) || g.LevelAt(x, y) != level) continue;
                    sim.Enqueue(Command.SpawnUnit(killer, siege, g.CellCenter(x, y)));
                    placed++;
                }
        await Ticks(sim, 3);
        foreach (EntityHandle u in DefeatStaging.UnitsOf(w, killer))
            if (w.Units.TypeId[u.Index] == siege) sim.Enqueue(Command.Attack(killer, u, target, isBuilding: true));
        if (!waitDefeat) return;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!w.IsDefeated(victim) && clock.Elapsed.TotalSeconds < 90) await Frames(1);
        Check(w.IsDefeated(victim), $"player {victim} not razed by {killer} by tick {sim.TickNumber}");
    }

    private static EntityHandle BuildingOf(World w, int player)
    {
        BuildingStore b = w.Buildings;
        for (int i = 0; i < b.Capacity; i++) if (b.Alive[i] && b.Owner[i] == player) return new EntityHandle(i, b.Generation[i]);
        throw new InvalidOperationException($"player {player} has no building");
    }

    private async Task UntilBanner(Simulation sim, string how)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!_main.BannerUp && clock.Elapsed.TotalSeconds < 90) await Frames(1);
        Check(_main.BannerUp && _ended == 1, $"{how}: banner {_main.BannerUp}, ended {_ended} by tick {sim.TickNumber}");
        if (!_main.BannerUp) throw new InvalidOperationException($"{how}: no banner");
    }

    // ---- junk through the real Main ----

    private async Task JunkThroughMain()
    {
        foreach ((string[] args, int want) in new[]
        {
            (new[] { "--players", "1", "--units", "0", "--mute" }, 2),
            (new[] { "--players", "5", "--units", "0", "--mute" }, 2),
            (new[] { "--players", "x", "--units", "0", "--mute" }, 2),
            (new[] { "--players", "3", "--ai", "4", "hard", "--faction", "4", "whirlwind", "--colour", "4", "1", "--units", "0", "--mute" }, 3),
            (new[] { "--players", "4", "--faction", "3", "no_such_faction", "--units", "0", "--mute" }, 4),
            (new[] { "--skirmish", "--players", "4", "--mute" }, 4),
        })
        {
            string how = string.Join(' ', args);
            _main.StartSkirmish(args);
            Match? match = _main.CurrentMatch;
            if (match?.Simulation == null) { Check(false, $"[{how}]: no match"); continue; }
            await Ticks(match.Simulation, 4);
            World w = match.Simulation.World;
            Check(w.Config.PlayerCount == want && match.PlayerRgb.Distinct().Count() == want && match.Bases != null && Enumerable.Range(0, want).All(p => match.Bases.HallAnchor[p] >= 0),
                $"[{how}]: {w.Config.PlayerCount} players (want {want}), {match.PlayerRgb.Distinct().Count()} colours, bases planned {match.Bases != null}");
            await QuitMatch();
        }
        // Human seats past slot 1 from the flags: --players 4 with only slot 3 an AI.
        _main.StartSkirmish(new[] { "--players", "4", "--units", "0", "--mute", "--ai", "3", "hard" });
        Match hm = _main.CurrentMatch!;
        await Ticks(hm.Simulation!, 2);
        Check(hm.Seats.Length == 4 && hm.Seats[1].Controller == SeatController.Human && hm.Seats[2] is { Controller: SeatController.Ai, Difficulty: AiDifficulty.Hard }
            && hm.Seats[3].Controller == SeatController.Human, $"--players 4 --ai 3 hard: seats [{string.Join(", ", hm.Seats)}]");
        await QuitMatch();
        // The dev army with four seats: two armies, four halls.
        _main.StartSkirmish(new[] { "--players", "4", "--units", "12", "--mute", "--seed", "3" });
        Match m = _main.CurrentMatch!;
        await Ticks(m.Simulation!, 4);
        World ww = m.Simulation!.World;
        int[] soldiers = new int[4], halls = new int[4];
        for (int i = 0; i < ww.Units.Capacity; i++)
            if (ww.Units.Alive[i] && (uint)ww.Units.Owner[i] < 4u && _data.Units[ww.Units.TypeId[i]].Slot != UnitSlot.Worker) soldiers[ww.Units.Owner[i]]++;
        for (int k = 0; k < ww.Buildings.Capacity; k++)
            if (ww.Buildings.Alive[k] && (uint)ww.Buildings.Owner[k] < 4u && _data.Buildings[ww.Buildings.TypeId[k]].Slot == BuildingSlot.TownHall) halls[ww.Buildings.Owner[k]]++;
        Check(ww.Config.PlayerCount == 4 && soldiers[0] > 0 && soldiers[1] > 0 && soldiers[2] == 0 && soldiers[3] == 0 && halls.All(h => h == 1),
            $"--players 4 --units 12: soldiers [{string.Join(", ", soldiers)}], halls [{string.Join(", ", halls)}]");
        GD.Print($"QA M6-V5: --players 4 --units 12 soldiers [{string.Join(", ", soldiers)}], halls [{string.Join(", ", halls)}]");
        await QuitMatch();
    }

    // ---- plumbing ----

    private static int Pow(int b, int e)
    {
        int r = 1;
        for (int i = 0; i < e; i++) r *= b;
        return r;
    }

    private async Task QuitMatch()
    {
        if (_main.BannerUp) await Click(_main.Banner!.MenuButton);
        else
        {
            _main.PauseMatch();
            await Frames(2);
            await Click(_main.Pause!.QuitToMenuButton);
        }
        await Frames(2);
        Check(_main.CurrentMatch == null && !GetTree().Paused, $"quit to menu: match {_main.CurrentMatch != null}, paused {GetTree().Paused}");
    }

    private async Task Ticks(Simulation sim, int n)
    {
        int until = sim.TickNumber + n;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (sim.TickNumber < until && clock.Elapsed.TotalSeconds < 30) await Frames(1);
        Check(sim.TickNumber >= until, $"tick {sim.TickNumber} after 30 s (want {until})");
    }

    private async Task Click(Control c)
    {
        Check(c.IsVisibleInTree(), $"{c.Name} is not visible");
        Vector2 at = c.GetGlobalRect().GetCenter();
        GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        await Frames(1);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Left });
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
        await Frames(2);
    }

    private async Task Type(Key key)
    {
        GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
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
