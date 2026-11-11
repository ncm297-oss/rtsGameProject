using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using Rts.Sim.Data;

namespace Rts.Game.Tests;

/// <summary>
/// QA for M6-VH6 (session 2026-11-07-0236).
/// <list type="bullet">
/// <item>(1) The slot give-back state machine (BUG-1114) against a model: 400 seeded sequences of 40 steps (Add player,
/// Remove player, a map pick among Generated and every map file, a team pick, a colour pick). The model: the player's last
/// explicit count P (Add on a shown count n: P = n + 1; Remove: P = n - 1), and the shown count is min(P, the map's cap):
/// a cap only takes, a give-back never passes P. After every step: the shown count, Add / Remove enabled, every shown slot's
/// team as last picked, colours unique among shown slots, Start off exactly when the one-team rule holds, and
/// <c>MatchArgs</c>' <c>--players</c> the shown count.</item>
/// <item>(2) <see cref="TooltipWrap"/> edges (BUG-1115 item 3): edge spaces (one, two, both ends), a 61-character word with
/// edge spaces, double spaces, existing line breaks at the edge; and a 20,000-string fuzz of edge-spaced text under BUG-1126's
/// ruling (M7-VH1, <see cref="TooltipWrapRule"/>): the spaces beside every break consumed, the text's other spaces kept, and
/// no blank line the wrap made anywhere (first, last or middle: 0 of 20,000).</item>
/// </list>
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaVh6Test.tscn</c>; prints "QA VH6 TEST PASS" or
/// "QA VH6 TEST FAIL: ..." lines.</remarks>
public partial class QaVh6Test : Node
{
    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private string _root = "";

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            await Frames(1);
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            WrapEdges();
            WrapEdgeFuzz();
            _root = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://qa-vh6-test-{OS.GetProcessId()}"));
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            Directory.CreateDirectory(Path.Combine(_root, "replays"));
            Directory.CreateDirectory(Path.Combine(_root, "saves"));
            Main main = NewMain();
            AddChild(main);
            await Frames(2);
            try
            {
                await SlotFuzz(main);
            }
            finally
            {
                RemoveChild(main);
                main.QueueFree();
                await Frames(2);
            }
            try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures.Take(60)) GD.Print($"QA VH6 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA VH6 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- (1) the slot give-back against a model ----

    private async Task SlotFuzz(Main main)
    {
        main.ShowSetup();
        await Frames(2);
        SkirmishSetup s = main.Setup!;
        int maps = _data.Maps.Length;
        Check(maps >= 3, $"{maps} maps in the data");
        var rng = new Random(1126); // test-side only
        int sequences = 400, steps = 40, failures = 0, giveBacks = 0, capsBit = 0, oneTeamSeen = 0;
        for (int seq = 0; seq < sequences && failures < 5; seq++)
        {
            // A fresh start each sequence: Generated, two slots, every team None.
            s.PickMap("");
            while (s.SlotCount > LaunchOptions.MinPlayers) s.RemoveSlot();
            for (int slot = 0; slot < SkirmishSetup.MaxSlots; slot++) Click(s.TeamOptions[slot], 0);
            int p = s.SlotCount;
            int cap = SkirmishSetup.MaxSlots;
            var teams = new int[SkirmishSetup.MaxSlots];
            var log = new StringBuilder("Generated");
            for (int step = 0; step < steps; step++)
            {
                int before = s.SlotCount;
                int op = rng.Next(10);
                if (op < 3)
                {
                    log.Append(", Add");
                    s.AddSlot();
                    if (before < cap) p = before + 1;
                }
                else if (op < 5)
                {
                    log.Append(", Remove");
                    s.RemoveSlot();
                    if (before > LaunchOptions.MinPlayers) p = before - 1;
                }
                else if (op < 8)
                {
                    int item = rng.Next(maps + 1);
                    MapDef? m = item == 0 ? null : _data.Maps[item - 1];
                    log.Append(", ").Append(m?.Key ?? "Generated");
                    Click(s.MapOption, item);
                    cap = m == null ? SkirmishSetup.MaxSlots : Math.Clamp(m.Players, LaunchOptions.MinPlayers, SkirmishSetup.MaxSlots);
                    if (s.SlotCount > before) giveBacks++;
                    if (s.SlotCount < before) capsBit++;
                }
                else if (op < 9)
                {
                    int slot = rng.Next(s.SlotCount);
                    int team = rng.Next(3); // None, Team 1, Team 2: one team comes up often
                    log.Append($", team {slot}={team}");
                    Click(s.TeamOptions[slot], team);
                    teams[slot] = team;
                }
                else
                {
                    int slot = rng.Next(s.SlotCount);
                    int colour = rng.Next(UiText.ColourCount);
                    log.Append($", colour {slot}={colour}");
                    s.PickColour(slot, colour);
                }
                int want = Math.Min(p, cap);
                string? why = null;
                if (s.SlotCount != want) why = $"{s.SlotCount} slots (want min(P {p}, cap {cap}) = {want})";
                else if (s.AddSlotButton.Disabled != (want >= cap)) why = $"Add disabled {s.AddSlotButton.Disabled} at {want}/{cap}";
                else if (s.RemoveSlotButton.Disabled != (want <= LaunchOptions.MinPlayers)) why = $"Remove disabled {s.RemoveSlotButton.Disabled} at {want}";
                else
                {
                    for (int slot = 0; slot < s.SlotCount && why == null; slot++)
                        if (s.TeamPick(slot) != teams[slot]) why = $"slot {slot} team {s.TeamPick(slot)} (want {teams[slot]})";
                    var colours = new HashSet<int>();
                    for (int slot = 0; slot < s.SlotCount && why == null; slot++)
                        if (!colours.Add(s.ColourPick(slot))) why = $"colour {s.ColourPick(slot)} held twice";
                    bool oneTeam = teams[0] != 0;
                    for (int slot = 1; slot < s.SlotCount; slot++) if (teams[slot] != teams[0]) oneTeam = false;
                    if (oneTeam) oneTeamSeen++;
                    if (why == null && (s.OneTeam != oneTeam || s.StartButton.Disabled != oneTeam || s.OneTeamLabel.Visible != oneTeam))
                        why = $"one team {oneTeam}: OneTeam {s.OneTeam}, Start disabled {s.StartButton.Disabled}, label {s.OneTeamLabel.Visible}";
                    if (why == null && !oneTeam)
                    {
                        string[]? args = s.MatchArgs();
                        int pi = args == null ? -1 : Array.IndexOf(args, "--players");
                        if (args == null || pi < 0 || args[pi + 1] != want.ToString()) why = $"MatchArgs [{(args == null ? "null" : string.Join(' ', args))}] (want --players {want})";
                    }
                }
                if (why != null)
                {
                    failures++;
                    Check(false, $"slot fuzz {seq} step {step}: {why}; steps: {log}");
                    break;
                }
            }
        }
        GD.Print($"  slot fuzz: {sequences} x {steps} steps, {giveBacks} give-backs on a map pick, {capsBit} caps bit, {oneTeamSeen} one-team states, {failures} failures");
        s.PickMap("");
        while (s.SlotCount > LaunchOptions.MinPlayers) s.RemoveSlot();
        for (int slot = 0; slot < SkirmishSetup.MaxSlots; slot++) Click(s.TeamOptions[slot], 0);
        main.ShowMenu();
        await Frames(1);
    }

    // ---- (2) the wrap at the text's edges ----

    private void WrapEdges()
    {
        string e60 = new string('e', TooltipWrap.Width), w61 = new string('w', 61);
        Edge(" " + e60, " " + e60, "one leading space");
        Edge("  " + e60, "  " + e60, "two leading spaces");
        Edge(e60 + "  ", e60 + "  ", "two trailing spaces");
        Edge(" " + e60 + " ", " " + e60 + " ", "a space at both ends");
        Edge(" " + w61 + " ", " " + w61 + " ", "a 61-character word with a space at both ends");
        Edge(" " + w61 + " x ", " " + w61 + "\nx ", "a 61-character word, a word, edge spaces");
        Edge("  " + e60 + " x", "  " + e60 + "\nx", "two leading spaces then a word past the width");
        Edge(e60 + " x  ", e60 + "\nx  ", "a break then trailing spaces");
        Edge("\n\n" + e60 + " ", "\n\n" + e60 + " ", "leading line breaks and a trailing space");
        // BUG-1126 (M7-VH1): a space beside a data line break is consumed, not made a blank line; a double space at a break
        // is consumed whole (the ruling), so no line is over the width with a space in it.
        Edge(e60 + " \n", e60 + "\n", "a space before a data break at the width");
        Edge("\n " + w61, "\n" + w61, "a space after a data break before a long word");
        Edge(e60 + "  " + w61, e60 + "\n" + w61, "a double space after a full line before a long word");
        Edge("ab  cd", "ab  cd", "a double space inside a short line (kept)");
        Edge("ab \n cd", "ab\ncd", "spaces beside a data break in a short text");
        GD.Print($"  e60 + \" \\n\" wraps to '{Esc(TooltipWrap.Wrap(e60 + " \n"))}'; \"\\n \" + w61 to '{Esc(TooltipWrap.Wrap("\n " + w61))}'");
        // A double space at the break: both consumed (M7-VH1; M6-VH5 kept one at the line's end).
        Edge(new string('a', 30) + "  " + new string('b', 30), new string('a', 30) + "\n" + new string('b', 30), "a double space at the break");
    }

    private void Edge(string text, string want, string what)
    {
        string got = TooltipWrap.Wrap(text);
        Check(got == want, $"wrap edge, {what}: '{Esc(got)}' (want '{Esc(want)}')");
        string? why = TooltipWrapRule.Fault(text, got, TooltipWrap.Width, out _);
        Check(why == null, $"wrap edge, {what}: {why}: '{Esc(got)}'");
    }

    private void WrapEdgeFuzz()
    {
        var rng = new Random(1115); // test-side only
        int failures = 0, middleBlanks = 0;
        string? middleExample = null;
        for (int n = 0; n < 20000 && failures < 5; n++)
        {
            int width = n % 2 == 0 ? TooltipWrap.Width : 1 + rng.Next(80);
            var sb = new StringBuilder();
            if (rng.Next(3) == 0) sb.Append('\n', 1 + rng.Next(2));
            sb.Append(' ', rng.Next(4));
            int words = rng.Next(1, 30);
            for (int k = 0; k < words; k++)
            {
                int len = rng.Next(8) == 0 ? width + rng.Next(3) : 1 + rng.Next(12); // words at and just past the width
                for (int c = 0; c < len; c++) sb.Append((char)('a' + rng.Next(26)));
                if (k == words - 1) break;
                int sep = rng.Next(12);
                if (sep == 0) sb.Append('\n');
                else sb.Append(' ', sep < 9 ? 1 : 2);
            }
            sb.Append(' ', rng.Next(4));
            if (rng.Next(3) == 0) sb.Append('\n', 1 + rng.Next(2));
            string text = sb.ToString();
            string w = TooltipWrap.Wrap(text, width);
            // M7-VH1 (BUG-1126's ruling): every blank line the wrap made is a failure now, first, last or middle.
            string? why = TooltipWrapRule.Fault(text, w, width, out bool blank);
            if (blank)
            {
                middleBlanks++;
                middleExample ??= $"width {width} in '{Esc(text)}' out '{Esc(w)}'";
            }
            if (why != null)
            {
                failures++;
                Check(false, $"edge fuzz {n} width {width}: {why}: in '{Esc(text)}' out '{Esc(w)}'");
            }
        }
        GD.Print($"  wrap edge fuzz: 20000 strings, {failures} failures, {middleBlanks} with a blank line the wrap made");
        Check(middleBlanks == 0, $"{middleBlanks} fuzzed strings with a blank line the wrap made (BUG-1126)");
        if (middleExample != null) GD.Print($"  e.g. {middleExample}");
    }

    private static string Esc(string s) => s.Replace("\n", "\\n");

    // ---- helpers ----

    private static void Click(OptionButton list, int item)
    {
        list.Select(item);
        list.EmitSignal(OptionButton.SignalName.ItemSelected, (long)item);
    }

    private Main NewMain()
    {
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.UserArgs = new[] { Main.NoConfineFlag };
        main.SettingsPath = $"user://qa-vh6-test-{OS.GetProcessId()}/settings.cfg";
        main.ReplayDir = Path.Combine(_root, "replays");
        main.SaveDir = Path.Combine(_root, "saves");
        return main;
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
