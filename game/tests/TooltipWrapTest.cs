using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Rts.Sim.Data;

namespace Rts.Game.Tests;

/// <summary>
/// M6-VH5 (BUG-1081 item 2): tooltips are pre-wrapped by <see cref="TooltipWrap"/>.
/// <list type="bullet">
/// <item>The helper: a 180-character text is 3-4 lines, no line past <see cref="TooltipWrap.Width"/>, every word whole, and the
/// lines joined with spaces give the text back; a short text is unchanged; a line break in the text is kept; a word longer
/// than the width is a line of its own, unbroken.</item>
/// <item>Every shipped description a tooltip shows (maps, starting resources presets, AI openings, units, buildings, techs,
/// abilities) wraps the same way: lines within the width (or one long word), joined text equal to the data.</item>
/// <item>The setup, through the shell: the Map and Starting resources lists' item tooltips are the wrapped data text; Raraku's
/// 180-character description shows as 3-4 lines.</item>
/// </list>
/// </summary>
/// <remarks>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/TooltipWrapTest.tscn</c>; prints "TOOLTIP WRAP TEST PASS"
/// or "TOOLTIP WRAP TEST FAIL: ..." lines. Windowed with <c>-- --shots &lt;dir&gt;</c> it opens the Map list, hovers the
/// longest map's item and saves the tooltip as <c>tooltip-wrap-map.png</c>.
/// </remarks>
public partial class TooltipWrapTest : Node
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
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            Helper();
            ShippedText();
            _root = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://tooltip-wrap-test-{OS.GetProcessId()}"));
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            Directory.CreateDirectory(_root);
            await Setup();
            try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        foreach (string f in _failures.Take(60)) GD.Print($"TOOLTIP WRAP TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("TOOLTIP WRAP TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // ---- the helper ----

    private void Helper()
    {
        // 180 characters of ordinary words (lengths 2-11), as a long data description.
        const string long180 = "Rival camps face each other across open desert of sand, scrub and sparse palms. Forests are few, "
            + "twin mesas flank a wide open center, and long sightlines leave little room to hide.";
        Check(long180.Length == 180, $"the sample is {long180.Length} characters");
        string wrapped = TooltipWrap.Wrap(long180);
        string[] lines = wrapped.Split('\n');
        Check(lines.Length is >= 3 and <= 4, $"180 characters wrap to {lines.Length} lines: '{wrapped}'");
        CheckWrap(long180, wrapped, "the 180-character sample");

        Check(TooltipWrap.Wrap("Short text.") == "Short text.", "a short text changed");
        Check(TooltipWrap.Wrap("") == "", "an empty text changed");
        string sixty = new string('a', 29) + " " + new string('b', 30);
        Check(sixty.Length == TooltipWrap.Width && TooltipWrap.Wrap(sixty) == sixty, "a text of exactly the width was broken");
        Check(TooltipWrap.Wrap(sixty + " c") == sixty + "\nc", $"one character past the width: '{TooltipWrap.Wrap(sixty + " c")}'");
        // A line break already in the text is kept, and each line wraps on its own.
        string twoLines = long180 + "\nGold 100  Wood 50";
        string w2 = TooltipWrap.Wrap(twoLines);
        Check(w2.EndsWith("\nGold 100  Wood 50", StringComparison.Ordinal) && w2.StartsWith(wrapped, StringComparison.Ordinal),
            $"a text with a line break: '{w2}'");
        // A word longer than the width: a line of its own, never cut.
        string word = new string('x', 75);
        string w3 = TooltipWrap.Wrap($"A very {word} word here");
        Check(w3 == $"A very\n{word}\nword here", $"a 75-character word: '{w3}'");
        // A double space survives (only the break's own space is replaced).
        string spaced = "Two  spaces " + long180;
        CheckWrap(spaced, TooltipWrap.Wrap(spaced), "a double space");
    }

    // ---- every shipped description a tooltip shows ----

    private void ShippedText()
    {
        var texts = new List<(string What, string Text)>();
        foreach (MapDef m in _data.Maps) texts.Add(($"map {m.Key}", m.Description));
        foreach (StartingResourcesDef p in _data.Rules.StartingResources) texts.Add(($"preset {p.Key}", p.Description));
        foreach (AiDef ai in _data.Ai) foreach (AiOpening o in ai.Openings) texts.Add(($"opening {o.Id}", o.Description));
        foreach (UnitDef u in _data.Units) texts.Add(($"unit {u.Id}", u.Description));
        foreach (BuildingDef b in _data.Buildings) texts.Add(($"building {b.Id}", b.Description));
        foreach (TechDef t in _data.Techs) texts.Add(($"tech {t.Id}", t.Description));
        foreach (AbilityDef a in _data.Abilities) texts.Add(($"ability {a.Id}", a.Description));
        int longest = 0, mostLines = 0;
        foreach ((string what, string text) in texts)
        {
            string w = TooltipWrap.Wrap(text);
            CheckWrap(text, w, what);
            longest = Math.Max(longest, text.Length);
            mostLines = Math.Max(mostLines, w.Split('\n').Length);
        }
        Check(texts.Count > 20, $"only {texts.Count} descriptions found");
        GD.Print($"  {texts.Count} descriptions, longest {longest} characters, at most {mostLines} lines");
    }

    // ---- the setup's lists, through the shell ----

    private async Task Setup()
    {
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
        main.UserArgs = new[] { Main.NoConfineFlag };
        main.SettingsPath = $"user://tooltip-wrap-test-{OS.GetProcessId()}/settings.cfg"; // BUG-0770: never the player's file
        main.ReplayDir = Path.Combine(_root, "replays");
        main.SaveDir = Path.Combine(_root, "saves");
        AddChild(main);
        await Frames(2);
        try
        {
            main.ShowSetup();
            await Frames(2);
            SkirmishSetup setup = main.Setup!;
            OptionButton maps = setup.MapOption;
            for (int m = 0; m < _data.Maps.Length; m++)
            {
                string tip = maps.GetItemTooltip(m + 1);
                CheckWrap(_data.Maps[m].Description, tip, $"the Map list's {_data.Maps[m].Key} tooltip");
            }
            int raraku = _data.FindMap("raraku");
            if (Check(raraku >= 0, "no raraku map"))
            {
                string tip = maps.GetItemTooltip(raraku + 1);
                Check(_data.Maps[raraku].Description.Length >= 170 && tip.Split('\n').Length is >= 3 and <= 4,
                    $"Raraku's {_data.Maps[raraku].Description.Length}-character description shows as {tip.Split('\n').Length} lines: '{tip}'");
            }
            OptionButton presets = setup.ResourcesOption;
            for (int i = 0; i < presets.ItemCount; i++)
                CheckWrap(_data.Rules.StartingResources[i].Description, presets.GetItemTooltip(i), $"the Starting resources list's item {i} tooltip");
            await HoverShot(maps);
            main.ShowMenu();
            await Frames(1);
        }
        finally
        {
            RemoveChild(main);
            main.QueueFree();
            await Frames(2);
        }
    }

    // Windowed with `-- --shots <dir>`: the Map list open, the longest map's item hovered until its tooltip shows.
    private async Task HoverShot(OptionButton list)
    {
        if (_shots == null || DisplayServer.GetName() == "headless") return;
        int item = 1;
        for (int m = 1; m < _data.Maps.Length; m++) if (_data.Maps[m].Description.Length > _data.Maps[item - 1].Description.Length) item = m + 1;
        list.ShowPopup();
        await Frames(3);
        PopupMenu popup = list.GetPopup();
        float rowHeight = popup.Size.Y / (float)Math.Max(1, list.ItemCount);
        var at = new Vector2(popup.Position.X + popup.Size.X * 0.5f, popup.Position.Y + rowHeight * (item + 0.5f));
        for (int k = 0; k < 3; k++)
        {
            Input.ParseInputEvent(new InputEventMouseMotion { Position = at + new Vector2(k, 0), GlobalPosition = at + new Vector2(k, 0) });
            await Frames(2);
        }
        await ToSignal(GetTree().CreateTimer(1.5, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        // Bounded: a minimized window draws nothing (an unbounded FramePostDraw wait hangs the run).
        int drawn = 0;
        void OnDraw() => drawn++;
        RenderingServer.FramePostDraw += OnDraw;
        for (int frames = 0; frames < 300 && drawn < 3; frames++) await Frames(1);
        RenderingServer.FramePostDraw -= OnDraw;
        if (drawn < 3)
        {
            GD.Print($"  screenshot tooltip-wrap-map skipped: the window drew {drawn} frames (mode {DisplayServer.WindowGetMode()})");
            popup.Hide();
            return;
        }
        string path = $"{_shots}/tooltip-wrap-map.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"  screenshot {path}");
        popup.Hide();
        await Frames(1);
    }

    // Lines within the width (a longer line is one word), words whole, and the breaks turned back to spaces give the data.
    private void CheckWrap(string data, string shown, string what)
    {
        string[] lines = shown.Split('\n');
        string[] dataLines = data.Split('\n');
        bool joins = string.Join(" ", lines) == string.Join(" ", dataLines) && lines.Length >= dataLines.Length;
        bool fits = lines.All(l => l.Length <= TooltipWrap.Width || !l.Contains(' '));
        bool whole = data.Split(new[] { ' ', '\n' }).SequenceEqual(shown.Split(new[] { ' ', '\n' }));
        Check(joins && fits && whole && (data.Length <= TooltipWrap.Width || lines.Length > 1 || !data.Contains(' ')),
            $"{what}: joins {joins}, fits {fits}, whole words {whole}: '{shown}'");
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
