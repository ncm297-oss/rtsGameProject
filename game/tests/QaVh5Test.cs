using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Game.Tests;

/// <summary>
/// QA for M6-VH5 (session 2026-11-06-0926).
/// <list type="bullet">
/// <item>(1) <see cref="TooltipWrap"/> fuzz: 20,000 seeded strings (words 1-90 characters, single / double / triple spaces,
/// leading and trailing spaces, existing line breaks, widths 1-80). Every output under BUG-1126's ruling (M7-VH1,
/// <see cref="TooltipWrapRule"/>): words and data breaks the text's, the spaces beside every break consumed, no blank line
/// the wrap made, a line over the width only a single word, and greedy: no break where the next word fit.
/// Plus the named edges (empty, null, exactly the width, a 61-character word, a trailing space at the break).</item>
/// <item>(2) The slot give-back in <c>SkirmishSetup.ApplyMap</c>: per-slot faction / difficulty / team kept across Raraku ->
/// Vathar Crossing; colours unique after a slot comes back even when a shown slot took its colour meanwhile; 3 slots ->
/// Raraku -> Vathar Crossing gives 3 (not 4); 2 slots -> Vathar Crossing stays 2; the one-team rule follows the slots;
/// Add / Remove buttons and <c>MatchArgs</c> follow; Raraku -> Pale Hills -> Vathar Crossing -> Generated.</item>
/// <item>(3) Pale Hills with 4 slots from the menu at 1152 x 648 and 1280 x 720: every shown setup control inside the window,
/// the map line inside its holder and clear of Add / Remove / Start; the match: the minimap inside the window with trees on
/// its resources layer; the first second's frame count printed (an S3 bar, not a check, under 30 FPS).</item>
/// </list>
/// </summary>
/// <remarks>Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaVh5Test.tscn</c>; prints "QA VH5 TEST PASS" or
/// "QA VH5 TEST FAIL: ..." lines. Windowed with <c>-- --shots &lt;dir&gt;</c> it saves the setup and the match at each size.</remarks>
public partial class QaVh5Test : Node
{
    private readonly List<string> _failures = new();
    private GameData _data = null!;
    private string _root = "";
    private string? _shots;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            string[] args = OS.GetCmdlineUserArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--shots") _shots = args[i + 1];
            await Frames(1);
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            WrapEdges();
            WrapFuzz();
            _root = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://qa-vh5-test-{OS.GetProcessId()}"));
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            Directory.CreateDirectory(Path.Combine(_root, "replays"));
            Directory.CreateDirectory(Path.Combine(_root, "saves"));
            Main main = NewMain();
            AddChild(main);
            await Frames(2);
            try
            {
                await SlotGiveBack(main);
                await PaleHills(main, new Vector2I(1152, 648));
                await PaleHills(main, new Vector2I(1280, 720));
            }
            finally
            {
                if (main.CurrentMatch != null) main.QuitToMenu();
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
        if (GetTree().Paused) GetTree().Paused = false;
        foreach (string f in _failures.Take(60)) GD.Print($"QA VH5 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA VH5 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- (1) the wrap helper ----

    private void WrapEdges()
    {
        Check(TooltipWrap.Wrap("") == "", "empty");
        Check(TooltipWrap.Wrap(null!) == "", "null");
        string w60 = new string('a', 60);
        Check(TooltipWrap.Wrap(w60) == w60, "a 60-character word changed");
        string w61 = new string('a', 61);
        Check(TooltipWrap.Wrap(w61) == w61, "a 61-character word was cut");
        Check(TooltipWrap.Wrap("ab " + w61 + " cd") == "ab\n" + w61 + "\ncd", $"a 61-character word between words: '{TooltipWrap.Wrap("ab " + w61 + " cd")}'");
        // Existing breaks: each line wraps on its own; a break right at the width is not doubled.
        string para = new string('b', 60) + "\n" + new string('c', 30) + " " + new string('d', 30);
        string paraWant = new string('b', 60) + "\n" + new string('c', 30) + "\n" + new string('d', 30);
        Check(TooltipWrap.Wrap(para) == paraWant, $"a 60-character line then a 61-character line: '{TooltipWrap.Wrap(para)}'");
        // Text kept exactly as data: a trailing space at the width, leading line breaks (the shipped data has neither).
        // Since M6-VH6 (BUG-1115) an edge space is never the break, so no empty last line.
        Check(TooltipWrap.Wrap(new string('e', 60) + " ") == new string('e', 60) + " ", "a trailing space at the width");
        Check(TooltipWrap.Wrap("\n\n" + w61) == "\n\n" + w61, "leading line breaks");
        // The three shipped map sentences and both presets: lines of whole words, 3-4 lines for 170+ characters.
        foreach (MapDef m in _data.Maps) CheckShipped($"map {m.Key}", m.Description);
        foreach (StartingResourcesDef p in _data.Rules.StartingResources) CheckShipped($"preset {p.Key}", p.Description);
    }

    private void CheckShipped(string what, string text)
    {
        string w = TooltipWrap.Wrap(text);
        string[] lines = w.Split('\n');
        Check(w.Replace('\n', ' ') == text, $"{what}: joined text differs: '{w}'");
        Check(lines.All(l => l.Length <= TooltipWrap.Width && l.Length > 0 && !l.StartsWith(' ') && !l.EndsWith(' ')), $"{what}: a line too long, empty or space-edged: '{w}'");
        if (text.Length >= 170) Check(lines.Length is >= 3 and <= 4, $"{what}: {text.Length} characters as {lines.Length} lines");
        GD.Print($"  {what}: {text.Length} characters, {lines.Length} lines, longest {lines.Max(l => l.Length)}");
    }

    private void WrapFuzz()
    {
        var rng = new Random(5150); // test-side only: the sim's RNG rule doesn't apply to view tests
        int failures = 0;
        for (int n = 0; n < 20000 && failures < 5; n++)
        {
            int width = n % 4 == 0 ? TooltipWrap.Width : 1 + rng.Next(80);
            bool single = n % 3 != 0;
            var sb = new StringBuilder();
            if (!single && rng.Next(4) == 0) sb.Append(' ', 1 + rng.Next(3));
            int words = rng.Next(0, 40);
            for (int k = 0; k < words; k++)
            {
                int len = rng.Next(10) == 0 ? 1 + rng.Next(90) : 1 + rng.Next(12);
                for (int c = 0; c < len; c++) sb.Append((char)('a' + rng.Next(26)));
                if (k == words - 1) break;
                if (rng.Next(15) == 0) sb.Append('\n');
                else sb.Append(' ', single ? 1 : 1 + rng.Next(3));
            }
            if (!single && rng.Next(4) == 0) sb.Append(' ', 1 + rng.Next(3));
            string text = sb.ToString();
            string w = TooltipWrap.Wrap(text, width);
            string? why = WrapFault(text, w, width, single);
            if (why != null)
            {
                failures++;
                Check(false, $"fuzz {n} width {width}: {why}: in '{text.Replace("\n", "\\n")}' out '{w.Replace("\n", "\\n")}'");
            }
        }
    }

    // M7-VH1 (BUG-1126's ruling): the rule is shared with QaVh6Test; greedy now holds for every spacing (the consumed gap
    // is known), not only single-spaced text.
    private static string? WrapFault(string text, string w, int width, bool single) => TooltipWrapRule.Fault(text, w, width, out _);

    // ---- (2) the slot give-back ----

    private async Task SlotGiveBack(Main main)
    {
        int raraku = _data.FindMap("raraku"), vathar = _data.FindMap("vathar_crossing"), pale = _data.FindMap("pale_hills");
        if (!Check(raraku >= 0 && vathar >= 0 && pale >= 0, "the three maps are not all in the data")) return;
        main.ShowSetup();
        await Frames(2);
        SkirmishSetup s = main.Setup!;
        s.PickMap("");
        while (s.SlotCount < SkirmishSetup.MaxSlots) s.AddSlot();
        // Per-slot picks on the slots Raraku will hide.
        for (int slot = 2; slot < 4; slot++)
        {
            Click(s.FactionOptions[slot], s.FactionOptions[slot].ItemCount - 1);
            if (s.DifficultyOptions[slot] is OptionButton d) Click(d, d.ItemCount - 1);
            Click(s.TeamOptions[slot], 2);
        }
        int[] colours = Enumerable.Range(0, 4).Select(s.ColourPick).ToArray();
        int f2 = s.FactionPick(2), f3 = s.FactionPick(3);
        var d2 = s.DifficultyPickOf(2);
        Click(s.MapOption, raraku + 1);
        Check(s.SlotCount == 2 && s.AddSlotButton.Disabled, $"4 slots, Raraku: {s.SlotCount} slots, Add disabled {s.AddSlotButton.Disabled}");
        // Slot 1 takes slot 3's colour while slot 3 is hidden.
        s.PickColour(1, colours[3]);
        Click(s.MapOption, vathar + 1);
        int[] shown = Enumerable.Range(0, s.SlotCount).Select(s.ColourPick).ToArray();
        Check(s.SlotCount == 4 && shown.Distinct().Count() == 4, $"Raraku -> Vathar: {s.SlotCount} slots, colours [{string.Join(",", shown)}]");
        Check(s.FactionPick(2) == f2 && s.FactionPick(3) == f3 && s.DifficultyPickOf(2) == d2 && s.TeamPick(2) == 2 && s.TeamPick(3) == 2,
            $"slots 3-4 picks not kept: factions {s.FactionPick(2)}/{s.FactionPick(3)} (want {f2}/{f3}), teams {s.TeamPick(2)}/{s.TeamPick(3)}");
        Check(s.AddSlotButton.Disabled && !s.RemoveSlotButton.Disabled && !s.StartButton.Disabled,
            $"Vathar 4 slots: Add disabled {s.AddSlotButton.Disabled}, Remove disabled {s.RemoveSlotButton.Disabled}, Start disabled {s.StartButton.Disabled}");
        string[]? args = s.MatchArgs();
        int pi = args == null ? -1 : Array.IndexOf(args, "--players");
        Check(args != null && pi >= 0 && args[pi + 1] == "4" && args.Contains("vathar_crossing"), $"Vathar args [{(args == null ? "null" : string.Join(' ', args))}]");
        // Raraku -> Pale Hills -> Vathar -> Generated: 2, 4, 4, 4.
        Click(s.MapOption, raraku + 1);
        int a = s.SlotCount;
        Click(s.MapOption, pale + 1);
        int b = s.SlotCount;
        Click(s.MapOption, vathar + 1);
        int c = s.SlotCount;
        Click(s.MapOption, 0);
        Check(a == 2 && b == 4 && c == 4 && s.SlotCount == 4 && !s.MapSizeOption.Disabled, $"Raraku/Pale/Vathar/Generated: {a}/{b}/{c}/{s.SlotCount}");

        // One team: slots 1-2 on team 1, slots 3-4 on team 2. Raraku shows only team 1: Start off; Vathar: on again.
        Click(s.TeamOptions[0], 1);
        Click(s.TeamOptions[1], 1);
        Click(s.MapOption, raraku + 1);
        bool rarakuOff = s.StartButton.Disabled && s.OneTeam;
        Click(s.MapOption, vathar + 1);
        Check(rarakuOff && !s.StartButton.Disabled && !s.OneTeam, $"one-team rule across Raraku -> Vathar: Raraku off {rarakuOff}, Vathar Start disabled {s.StartButton.Disabled}");
        Click(s.TeamOptions[0], 0);
        Click(s.TeamOptions[1], 0);
        Click(s.MapOption, 0);

        // 3 slots -> Raraku -> Vathar: 3, not 4. 2 slots -> Vathar: 2.
        s.RemoveSlot();
        Click(s.MapOption, raraku + 1);
        Click(s.MapOption, vathar + 1);
        int three = s.SlotCount;
        Check(three == 3 && !s.AddSlotButton.Disabled, $"3 slots, Raraku, Vathar: {three} slots, Add disabled {s.AddSlotButton.Disabled}");
        // The player's Add on Vathar (now 4): Raraku then Generated. The player's last count was 4.
        s.AddSlot();
        Click(s.MapOption, raraku + 1);
        Click(s.MapOption, 0);
        int afterAdd = s.SlotCount;
        // BUG-1114 (fixed in M6-VH6): Add player raises the count Generated gives back.
        Check(afterAdd == 4, $"BUG-1114: 3 slots, Raraku, Vathar, Add player (4), Raraku, Generated: {afterAdd} slots (want 4)");
        Click(s.MapOption, 0);
        while (s.SlotCount > 2) s.RemoveSlot();
        Click(s.MapOption, vathar + 1);
        Check(s.SlotCount == 2 && !s.AddSlotButton.Disabled, $"2 slots, Vathar: {s.SlotCount}");
        Click(s.MapOption, 0);
        Check(s.SlotCount == 2, $"2 slots, Vathar, Generated: {s.SlotCount}");
        main.ShowMenu();
        await Frames(1);
    }

    // ---- (3) Pale Hills, 4 slots, from the menu ----

    private async Task PaleHills(Main main, Vector2I size)
    {
        string what = $"Pale Hills 4 slots at {size.X} x {size.Y}";
        GetTree().Root.Size = size;
        await Frames(3);
        main.ShowSetup();
        await Frames(2);
        SkirmishSetup s = main.Setup!;
        s.PickMap("");
        while (s.SlotCount < SkirmishSetup.MaxSlots) s.AddSlot();
        Click(s.MapOption, _data.FindMap("pale_hills") + 1);
        await Frames(3);
        Rect2 view = GetViewport().GetVisibleRect().Grow(0.5f);
        var outside = new List<string>();
        foreach (Node n in s.FindChildren("*", nameof(Control), true, false))
        {
            var c = (Control)n;
            if (!c.IsVisibleInTree() || c.Size.X <= 0 || c.Size.Y <= 0) continue;
            if (!view.Encloses(c.GetGlobalRect())) outside.Add($"{c.Name} {c.GetGlobalRect()}");
        }
        Check(outside.Count == 0, $"{what}: controls outside the window: {string.Join("; ", outside.Take(8))}");
        Label line = s.MapPlayersLabel;
        Rect2 lr = line.GetGlobalRect();
        float textBottom = lr.Position.Y + line.GetLineCount() * line.GetLineHeight();
        var textRect = new Rect2(lr.Position, new Vector2(lr.Size.X, textBottom - lr.Position.Y));
        Rect2 holder = line.GetParent<Control>().GetGlobalRect();
        foreach (Button btn in new[] { s.AddSlotButton, s.RemoveSlotButton, s.StartButton, s.BackButton })
            Check(!textRect.Intersects(btn.GetGlobalRect()), $"{what}: the map line {textRect} overlaps {btn.Name} {btn.GetGlobalRect()}");
        Check(line.Visible && holder.Grow(0.5f).Encloses(lr) && line.GetLineCount() <= 2 && view.Encloses(textRect),
            $"{what}: the map line '{line.Text}' {lr} ({line.GetLineCount()} lines) in holder {holder}");
        await Shot($"setup-pale-{size.X}");

        Action<Match>? before = main.BeforeStart;
        main.BeforeStart = null; // real speed
        var press = System.Diagnostics.Stopwatch.StartNew();
        s.StartButton.EmitSignal(BaseButton.SignalName.Pressed);
        press.Stop();
        main.BeforeStart = before;
        Match? match = main.CurrentMatch;
        if (!Check(match?.Simulation != null, $"{what}: no match")) return;
        var frames = new List<double>();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        double last = 0;
        while (watch.Elapsed.TotalSeconds < 1.0)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            double now = watch.Elapsed.TotalMilliseconds;
            frames.Add(now - last);
            last = now;
        }
        var mini = match!.GetNode<Minimap>("Hud/Minimap");
        var props = match.GetNode<PropsView>("World3D/PropsView");
        int tree = _data.FindResource("tree");
        int trees = tree >= 0 ? props.ShownCount(tree) : -1;
        Rect2 mr = mini.GetGlobalRect();
        int resPixels = 0;
        Image res = mini.ResourcesImage;
        for (int y = 0; y < res.GetHeight(); y++)
            for (int x = 0; x < res.GetWidth(); x++)
                if (res.GetPixel(x, y).A > 0.01f) resPixels++;
        string mode = DisplayServer.GetName() == "headless" ? "headless" : "windowed";
        GD.Print(string.Create(CultureInfo.InvariantCulture,
            $"  {what} ({mode}): Start press {press.Elapsed.TotalMilliseconds:0} ms, first second {frames.Count} frames, avg {frames.Average():0.0} ms, worst {frames.Max():0.0} ms, {trees} trees, minimap {mr} res {res.GetWidth()}x{res.GetHeight()} with {resPixels} resource pixels, refreshes {mini.Refreshes}"));
        Check(match.Simulation!.World.Config.MapId == "pale_hills" && match.Simulation.World.Config.PlayerCount == 4, $"{what}: map {match.Simulation.World.Config.MapId}, {match.Simulation.World.Config.PlayerCount} players");
        Check(trees > 3000, $"{what}: {trees} trees shown");
        Check(mini.IsVisibleInTree() && mr.Size.X > 64 && view.Encloses(mr), $"{what}: minimap {mr} in {view}");
        Check(resPixels > 500, $"{what}: the minimap's resources layer has {resPixels} pixels");
        if (mode == "windowed") Check(frames.Count >= 30, $"{what}: first second {frames.Count} frames (< 30 FPS)");
        await Shot($"match-pale-{size.X}");
        main.QuitToMenu();
        await Frames(2);
        GC.Collect();
        GetTree().Root.Size = new Vector2I(1152, 648);
        await Frames(2);
    }

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
        main.SettingsPath = $"user://qa-vh5-test-{OS.GetProcessId()}/settings.cfg";
        main.ReplayDir = Path.Combine(_root, "replays");
        main.SaveDir = Path.Combine(_root, "saves");
        main.BeforeStart = m => m.GetNode<SimRunner>("SimRunner").FixedFrameDelta = 0.05;
        return main;
    }

    private async Task Shot(string name)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        int drawn = 0;
        void OnDraw() => drawn++;
        RenderingServer.FramePostDraw += OnDraw;
        for (int f = 0; f < 300 && drawn < 3; f++) await Frames(1);
        RenderingServer.FramePostDraw -= OnDraw;
        if (drawn < 3) { GD.Print($"  screenshot {name} skipped (no draw)"); return; }
        string path = $"{_shots}/qa-vh5-{name}.png";
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
