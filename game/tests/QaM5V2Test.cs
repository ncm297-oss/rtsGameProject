using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Rts.Sim.Data;

namespace Rts.Game.Tests;

/// <summary>
/// M5-V2 (docs/02 "AI opponent": Hard's gather bonus "shown in the skirmish setup"): each AI slot's Difficulty list carries
/// one tooltip per item, the <c>ui.json</c> <c>setup.difficulty_*_tip</c> text with <c>{0}</c> the slot's faction's
/// <c>ai.json</c> gather bonus in percent, and the list's own tooltip is the picked item's.
/// <list type="bullet">
/// <item>Shipped data: every slot's three item tooltips equal the data text (Hard's naming the data's number, 25 today), and
/// the list's tooltip follows a pick.</item>
/// <item>Forged data (a copy of <c>res://data</c> under <c>user://</c> with Hard's <c>gatherBonus</c> 0.4 for the first faction
/// and 0.15 for the others): Hard reads "40" / "15" by the slot's faction, and a faction pick re-applies the tips.</item>
/// <item><c>ui.json</c> checks: a Hard tip without <c>{0}</c>, an Easy or Hard tip without <c>{1}</c> (M7-VH1: the think
/// interval), or any tip with <c>{2}</c> is a load error naming the key.</item>
/// </list>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM5V2Test.tscn</c>; prints "QA M5-V2 TEST PASS" or
/// "QA M5-V2 TEST FAIL: ..." lines. Windowed with <c>-- --shots &lt;dir&gt;</c> it saves the forged setup with Hard's popup tip.
/// </summary>
public partial class QaM5V2Test : Node
{
    private const float ForgedFirst = 0.4f, ForgedOther = 0.15f;

    private readonly List<string> _failures = new();
    private UiText _ui = null!;
    private string? _shots;

    public override async void _Ready()
    {
        string root = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://qa-m5-v2-{OS.GetProcessId()}"));
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            string[] args = OS.GetCmdlineUserArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--shots") _shots = args[i + 1];
            await Frames(1);
            string dataDir = ProjectSettings.GlobalizePath("res://data");
            DataLoadResult loaded = DataLoader.LoadAll(dataDir);
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            GameData data = loaded.Data!;
            _ui = UiText.Shared ?? throw new InvalidOperationException("ui.json failed to load");

            SkirmishSetup shipped = await NewSetup(data);
            for (int slot = 1; slot < SkirmishSetup.MaxSlots; slot++) CheckSlot(shipped, data, slot, "shipped");
            int f1 = shipped.FactionOptions[1].Selected;
            float hard = data.Ai[f1].Difficulties[(int)AiDifficulty.Hard].GatherBonus;
            GD.Print($"  shipped Hard tip (slot 1, {data.Factions[f1].Key}, gatherBonus {hard.ToString(CultureInfo.InvariantCulture)}): '{shipped.DifficultyOption.GetItemTooltip((int)AiDifficulty.Hard)}'");
            Check(Plain(shipped.DifficultyOption.GetItemTooltip((int)AiDifficulty.Hard)).Contains(Expected(hard) + "%", StringComparison.Ordinal),
                $"shipped Hard tip names no '{Expected(hard)}%'");
            // The list's tooltip follows the pick, back and forth.
            foreach (int pick in new[] { (int)AiDifficulty.Hard, (int)AiDifficulty.Easy, (int)AiDifficulty.Normal })
            {
                Pick(shipped.DifficultyOption, pick);
                Check(shipped.DifficultyOption.TooltipText == shipped.DifficultyOption.GetItemTooltip(pick) && shipped.DifficultyOption.TooltipText.Length > 0,
                    $"after picking item {pick} the list's tooltip is '{shipped.DifficultyOption.TooltipText}'");
            }
            shipped.QueueFree();
            await Frames(1);

            GameData forged = Forge(dataDir, root, data);
            SkirmishSetup setup = await NewSetup(forged);
            for (int slot = 1; slot < SkirmishSetup.MaxSlots; slot++)
            {
                CheckSlot(setup, forged, slot, "forged");
                string want = setup.FactionOptions[slot].Selected == 0 ? "40" : "15";
                Check(Plain(setup.DifficultyOptions[slot]!.GetItemTooltip((int)AiDifficulty.Hard)).Contains(want + "%", StringComparison.Ordinal),
                    $"forged slot {slot} (faction {setup.FactionOptions[slot].Selected}) Hard tip lacks '{want}%': '{setup.DifficultyOptions[slot]!.GetItemTooltip((int)AiDifficulty.Hard)}'");
            }
            if (forged.Factions.Length > 1)
            {
                // Hard picked, then every faction in turn: the item tips and the list's tip follow the faction.
                Pick(setup.DifficultyOption, (int)AiDifficulty.Hard);
                for (int f = forged.Factions.Length - 1; f >= 0; f--)
                {
                    Pick(setup.FactionOptions[1], f);
                    CheckSlot(setup, forged, 1, $"forged after picking faction {f}");
                    string want = f == 0 ? "40" : "15";
                    Check(Plain(setup.DifficultyOption.TooltipText).Contains(want + "%", StringComparison.Ordinal) && setup.DifficultyOption.TooltipText == setup.DifficultyOption.GetItemTooltip((int)AiDifficulty.Hard),
                        $"faction {f} picked: the list's tooltip is '{setup.DifficultyOption.TooltipText}', want Hard's with '{want}%'");
                }
            }
            else _failures.Add("the data has one faction: the faction-change row needs two");
            if (_shots != null) await Shot(setup);
            setup.QueueFree();

            UiChecks();
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        foreach (string f in _failures.Take(60)) GD.Print($"QA M5-V2 TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M5-V2 TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private async Task<SkirmishSetup> NewSetup(GameData data)
    {
        var setup = new SkirmishSetup { Name = "SkirmishSetup" };
        AddChild(setup);
        setup.Build(_ui, data);
        for (int s = setup.SlotCount; s < SkirmishSetup.MaxSlots; s++) setup.AddSlot();
        await Frames(2);
        return setup;
    }

    // Each of the slot's items: exactly the ui.json tip with the slot's faction's gather bonus in percent, wrapped as the
    // other setup tooltips; slot 0 has no list.
    private void CheckSlot(SkirmishSetup setup, GameData data, int slot, string what)
    {
        OptionButton? list = setup.DifficultyOptions[slot];
        if (!Check(list != null && list.ItemCount == SkirmishSetup.DifficultyKeys.Length, $"{what} slot {slot}: no three-item difficulty list")) return;
        int faction = setup.FactionOptions[slot].Selected;
        SetupText[] tips = { SetupText.DifficultyEasyTip, SetupText.DifficultyNormalTip, SetupText.DifficultyHardTip };
        for (int d = 0; d < tips.Length; d++)
        {
            string template = _ui.Setup(tips[d]);
            float bonus = data.Ai[faction].Difficulties[d].GatherBonus;
            // M7-VH1 (BUG-1147): {1} is the think interval the AI keeps, in seconds; M7-V3a (BUG-1200): with its unit, the
            // number from setup.seconds_one when it is exactly 1, else setup.seconds_other.
            double think = data.Ai[faction].Difficulties[d].ThinkTicks / (double)Rts.Sim.SimConstants.TicksPerSecond;
            string number = Math.Round(think, 2).ToString(CultureInfo.InvariantCulture);
            string phrase = string.Format(CultureInfo.InvariantCulture, _ui.Setup(number == "1" ? SetupText.SecondsOne : SetupText.SecondsOther), number);
            string want = TooltipWrap.Wrap(string.Format(CultureInfo.InvariantCulture, template, Expected(bonus), phrase));
            string got = list!.GetItemTooltip(d);
            Check(template.Length > 0 && got == want, $"{what} slot {slot} faction {faction} item {d}: tooltip '{got}', want '{want}'");
        }
        Check(list!.TooltipText == list.GetItemTooltip(list.Selected), $"{what} slot {slot}: the list's tooltip '{list.TooltipText}' isn't item {list.Selected}'s");
        Check(setup.DifficultyOptions[0] == null, $"{what}: slot 0 has a difficulty list");
    }

    // The test's own percent: the fraction times 100, invariant, at most two decimals (25, 40, 15, 12.5).
    private static string Expected(float fraction) => Math.Round((double)fraction * 100.0, 2).ToString(CultureInfo.InvariantCulture);

    private static string Plain(string tip) => tip.Replace('\n', ' ');

    // A click on an item: the pick and the list's ItemSelected signal.
    private static void Pick(OptionButton list, int index)
    {
        list.Select(index);
        list.EmitSignal(OptionButton.SignalName.ItemSelected, index);
    }

    // A copy of the data with Hard's gatherBonus set per faction (the first ForgedFirst, the others ForgedOther).
    private GameData Forge(string dataDir, string root, GameData shipped)
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
        string copy = Path.Combine(root, "data");
        foreach (string dir in Directory.GetDirectories(dataDir, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(copy, Path.GetRelativePath(dataDir, dir)));
        Directory.CreateDirectory(copy);
        foreach (string file in Directory.GetFiles(dataDir, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(copy, Path.GetRelativePath(dataDir, file)));
        for (int f = 0; f < shipped.Factions.Length; f++)
        {
            string path = Path.Combine(copy, "factions", shipped.Factions[f].Key, "ai.json");
            JsonNode ai = JsonNode.Parse(File.ReadAllText(path)) ?? throw new InvalidOperationException($"{path} is empty");
            ai["difficulties"]!["hard"]!["gatherBonus"] = f == 0 ? (double)(decimal)ForgedFirst : (double)(decimal)ForgedOther;
            File.WriteAllText(path, ai.ToJsonString());
        }
        DataLoadResult loaded = DataLoader.LoadAll(copy);
        if (!loaded.Ok) throw new InvalidOperationException("forged data failed to load: " + string.Join("; ", loaded.Errors.Take(5)));
        GameData forged = loaded.Data!;
        for (int f = 0; f < forged.Factions.Length; f++)
            Check(Expected(forged.Ai[f].Difficulties[(int)AiDifficulty.Hard].GatherBonus) == (f == 0 ? "40" : "15"),
                $"forged faction {f}: Hard gatherBonus {forged.Ai[f].Difficulties[(int)AiDifficulty.Hard].GatherBonus}");
        return forged;
    }

    // The keys' load rules: Hard must name {0}; Easy and Hard must name {1} (M7-VH1), Normal may; none {2}; the shipped file loads.
    private void UiChecks()
    {
        string path = ProjectSettings.GlobalizePath("res://data/common/ui.json");
        string json = File.ReadAllText(path);
        var errors = new List<string>();
        Check(UiText.Parse(json, errors) != null && errors.Count == 0, $"the shipped ui.json: {string.Join("; ", errors)}");
        foreach ((string key, string text, bool loads) in new[]
        {
            ("difficulty_hard_tip", "Gathers more every {1} s.", false),
            ("difficulty_hard_tip", "Gathers {0}% and {2}.", false),
            ("difficulty_hard_tip", "Gathers {0}% faster.", false),
            ("difficulty_hard_tip", "Gathers {0:0}% every {1} s.", true),
            ("difficulty_hard_tip", "Gathers {0}% and {1}.", true),
            ("difficulty_easy_tip", "Slow {1}.", true),
            ("difficulty_easy_tip", "Gathers {0}% more.", false),
            ("difficulty_easy_tip", "Gathers {0}% more every {1} s.", true),
            ("difficulty_normal_tip", "Broken {0.", false),
            ("difficulty_normal_tip", "Relaxed.", true),
            ("difficulty_normal_tip", "Every {1} s.", true),
            ("difficulty_normal_tip", "Every {2} s.", false),
        })
        {
            JsonNode forged = JsonNode.Parse(json)!;
            forged["setup"]![key] = text;
            errors.Clear();
            UiText? ui = UiText.Parse(forged.ToJsonString(), errors);
            Check((ui != null) == loads && (loads || errors.Any(e => e.Contains("setup." + key, StringComparison.Ordinal))),
                $"ui.json setup.{key} = '{text}': loads {ui != null} (want {loads}), errors {string.Join("; ", errors)}");
        }
    }

    // Windowed with `-- --shots <dir>`: slot 1's Difficulty list open, the Hard item hovered until its tooltip shows (as
    // TooltipWrapTest.HoverShot does for the Map list).
    private async Task Shot(SkirmishSetup setup)
    {
        if (DisplayServer.GetName() == "headless") return;
        Directory.CreateDirectory(_shots!);
        OptionButton list = setup.DifficultyOption;
        list.ShowPopup();
        await Frames(3);
        PopupMenu popup = list.GetPopup();
        float rowHeight = popup.Size.Y / (float)Math.Max(1, list.ItemCount);
        var at = new Vector2(popup.Position.X + popup.Size.X * 0.5f, popup.Position.Y + rowHeight * ((int)AiDifficulty.Hard + 0.5f));
        for (int k = 0; k < 3; k++)
        {
            Input.ParseInputEvent(new InputEventMouseMotion { Position = at + new Vector2(k, 0), GlobalPosition = at + new Vector2(k, 0) });
            await Frames(2);
        }
        await ToSignal(GetTree().CreateTimer(1.5, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        // Bounded: a minimized window draws nothing.
        int drawn = 0;
        void OnDraw() => drawn++;
        RenderingServer.FramePostDraw += OnDraw;
        for (int frames = 0; frames < 300 && drawn < 3; frames++) await Frames(1);
        RenderingServer.FramePostDraw -= OnDraw;
        if (drawn >= 3)
        {
            string path = Path.Combine(_shots!, "m5-v2-hard-tip.png");
            GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print($"  screenshot {path}");
        }
        else GD.Print($"  screenshot m5-v2-hard-tip skipped: the window drew {drawn} frames");
        popup.Hide();
        await Frames(1);
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
