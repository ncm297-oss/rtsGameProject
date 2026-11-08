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
/// QA attacks on M5-V2 (session 2026-11-07-0926): the difficulty tooltips after slot operation sequences.
/// <list type="bullet">
/// <item>Through <c>Main.tscn</c>: menu, Skirmish, a seeded random stream of Add player, Remove player, faction picks,
/// difficulty picks and map picks (a file map caps / restores slots, Generated gives them back), with a Back to the menu,
/// Load Game and Back, and Skirmish again in between. After every step every built AI slot's three item tooltips equal the
/// data text formatted with that slot's faction's gather bonus, wrapped, non-empty; the list's tooltip is the picked
/// item's.</item>
/// <item>The same stream (without the menu) on a standalone setup over a forged data copy: Hard 0.4 for the first faction,
/// 0.3333 ("33.33") for the others, so a tip left on the old faction shows.</item>
/// <item><c>UiText.Percent</c> at 0, 0.25, 0.333, 0.3333, 1 and a few float edge values.</item>
/// <item>Every shipped tip wrapped at <c>TooltipWrap.Width</c>: no line over the width, no blank line.</item>
/// <item><c>SkirmishSetup.GatherBonus</c> on a faction or difficulty past the data: 0, no throw.</item>
/// </list>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/QaM5V2bTest.tscn</c>; prints "QA M5-V2B TEST PASS" or
/// "QA M5-V2B TEST FAIL: ..." lines.
/// </summary>
public partial class QaM5V2bTest : Node
{
    private const int Steps = 400;
    private readonly List<string> _failures = new();
    private Main _main = null!;
    private UiText _ui = null!;
    private GameData _data = null!;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        string? root = null;
        try
        {
            GetTree().Root.Size = new Vector2I(1152, 648);
            await Frames(1);
            _ui = UiText.Shared ?? throw new InvalidOperationException("ui.json failed to load");
            PercentChecks();
            WrapChecks();
            _main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate<Main>();
            _main.UserArgs = Array.Empty<string>();
            _main.SettingsPath = $"user://qa-m5-v2b-{OS.GetProcessId()}/settings.cfg";
            AddChild(_main);
            await Frames(2);
            DataLoadResult loaded = DataLoader.LoadAll(ProjectSettings.GlobalizePath("res://data"));
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            _data = loaded.Data!;
            GatherBonusChecks();
            _main.ShowSetup();
            await Frames(1);
            SkirmishSetup setup = _main.Setup ?? throw new InvalidOperationException("no setup");
            CheckAll(setup, "fresh setup");
            await Fuzz(setup, withMenu: true);
            // Shipped data gives both factions the same bonus, so a stale faction can't show there: again on a forged copy
            // (Hard 0.4 for the first faction, 0.3333 for the others), a setup built on its own.
            root = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://qa-m5-v2b-{OS.GetProcessId()}"));
            _data = Forge(ProjectSettings.GlobalizePath("res://data"), root);
            var forged = new SkirmishSetup { Name = "ForgedSetup" };
            AddChild(forged);
            forged.Build(_ui, _data);
            await Frames(1);
            CheckAll(forged, "forged fresh setup");
            await Fuzz(forged, withMenu: false);
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        try { if (root != null && Directory.Exists(root)) Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        foreach (string f in _failures.Take(60)) GD.Print($"QA M5-V2B TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("QA M5-V2B TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    private async Task Fuzz(SkirmishSetup setup, bool withMenu)
    {
        var rng = new Random(5202); // test-side randomness, fixed seed
        string[] maps = new[] { "" }.Concat(_data.Maps.Select(m => m.Key)).ToArray();
        int nav = 0;
        for (int step = 0; step < Steps; step++)
        {
            int op = rng.Next(withMenu ? 8 : 7);
            string what;
            switch (op)
            {
                case 0: setup.AddSlot(); what = "add"; break;
                case 1: setup.RemoveSlot(); what = "remove"; break;
                case 2:
                case 3:
                {
                    int slot = 1 + rng.Next(SkirmishSetup.MaxSlots - 1);
                    int f = rng.Next(setup.FactionOptions[slot].ItemCount);
                    Pick(setup.FactionOptions[slot], f);
                    what = $"slot {slot} faction {f}";
                    break;
                }
                case 4:
                {
                    int slot = 1 + rng.Next(SkirmishSetup.MaxSlots - 1);
                    int d = rng.Next(3);
                    Pick(setup.DifficultyOptions[slot]!, d);
                    what = $"slot {slot} difficulty {d}";
                    break;
                }
                case 5:
                case 6:
                {
                    string id = maps[rng.Next(maps.Length)];
                    setup.PickMap(id);
                    what = $"map '{id}'";
                    break;
                }
                default:
                {
                    // Out to the menu, into Load Game, back, and into the setup again.
                    _main.ShowMenu();
                    await Frames(1);
                    _main.ShowLoadGame();
                    await Frames(1);
                    _main.LoadScreen!.BackButton.EmitSignal(BaseButton.SignalName.Pressed);
                    await Frames(1);
                    _main.ShowSetup();
                    await Frames(1);
                    Check(ReferenceEquals(_main.Setup, setup), "the setup was rebuilt by Load Game -> Back");
                    nav++;
                    what = "menu / Load Game / Back / Skirmish";
                    break;
                }
            }
            if (step % 25 == 0) await Frames(1);
            CheckAll(setup, $"step {step} ({what})");
            if (_failures.Count > 40) return;
        }
        GD.Print($"  fuzz: {Steps} steps, {nav} Load Game round trips");
    }

    // Every built AI slot (shown or hidden: hidden slots keep their picks for when they come back).
    private void CheckAll(SkirmishSetup setup, string what)
    {
        for (int slot = 1; slot < SkirmishSetup.MaxSlots; slot++)
        {
            OptionButton? list = setup.DifficultyOptions[slot];
            if (!Check(list != null && list.ItemCount == 3, $"{what}: slot {slot} has no three-item list")) continue;
            int faction = setup.FactionOptions[slot].Selected;
            for (int d = 0; d < 3; d++)
            {
                float bonus = _data.Ai[faction].Difficulties[d].GatherBonus;
                string want = TooltipWrap.Wrap(string.Format(CultureInfo.InvariantCulture, _ui.Setup(UiText.DifficultyTipKeys[d]), UiText.Percent(bonus)));
                string got = list!.GetItemTooltip(d);
                Check(got.Length > 0 && got == want, $"{what}: slot {slot} faction {faction} item {d} tip '{got}', want '{want}'");
            }
            Check(list!.TooltipText.Length > 0 && list.TooltipText == list.GetItemTooltip(list.Selected),
                $"{what}: slot {slot} list tooltip '{list.TooltipText}' is not item {list.Selected}'s");
            if (setup.SlotCount > slot && list.Selected == 2)
                Check(list.TooltipText.Replace('\n', ' ').Contains(UiText.Percent(_data.Ai[faction].Difficulties[2].GatherBonus) + "%", StringComparison.Ordinal),
                    $"{what}: slot {slot} Hard tooltip lacks the data's number");
        }
    }

    private void PercentChecks()
    {
        foreach ((float f, string want) in new[]
        {
            (0f, "0"), (0.25f, "25"), (0.333f, "33.3"), (0.3333f, "33.33"), (1f, "100"), (0.4f, "40"), (0.15f, "15"),
            (0.125f, "12.5"), (0.07f, "7"), (0.29f, "29"), (0.005f, "0.5"), (0.00001f, "0"), (2.5f, "250"),
        })
        {
            string got = UiText.Percent(f);
            Check(got == want, $"Percent({f.ToString("R", CultureInfo.InvariantCulture)}) = '{got}', want '{want}'");
        }
        // Formatted into the shipped Hard tip at the brief's values.
        foreach ((float f, string want) in new[] { (0f, "0%"), (0.25f, "25%"), (0.333f, "33.3%"), (0.3333f, "33.33%"), (1f, "100%") })
        {
            string tip = _ui.DifficultyTip(AiDifficulty.Hard, f);
            Check(tip.Contains(want, StringComparison.Ordinal) && !tip.Contains("{0}", StringComparison.Ordinal), $"Hard tip at {f}: '{tip}' lacks '{want}'");
        }
        Check(_ui.DifficultyTip((AiDifficulty)3, 0.25f) == "" && _ui.DifficultyTip((AiDifficulty)(-1), 0.25f) == "", "an out-of-range difficulty's tip is not empty");
    }

    private void WrapChecks()
    {
        int longest = 0;
        for (int d = 0; d < 3; d++)
        {
            foreach (float f in new[] { 0f, 0.25f, 0.3333f, 1f, 12.3456f })
            {
                string tip = _ui.DifficultyTip((AiDifficulty)d, f);
                longest = Math.Max(longest, tip.Length);
                string wrapped = TooltipWrap.Wrap(tip);
                foreach (string line in wrapped.Split('\n'))
                {
                    Check(line.Length <= TooltipWrap.Width, $"tip {d} at {f}: line of {line.Length} over {TooltipWrap.Width}: '{line}'");
                    Check(line.Trim().Length > 0, $"tip {d} at {f}: a blank line in '{wrapped}'");
                }
                Check(wrapped.Replace('\n', ' ') == tip, $"tip {d} at {f}: wrapping changed the words: '{wrapped}'");
            }
        }
        GD.Print($"  longest tip: {longest} characters");
    }

    private void GatherBonusChecks()
    {
        Check(SkirmishSetup.GatherBonus(_data, _data.Factions.Length, AiDifficulty.Hard) == 0f, "GatherBonus past the factions is not 0");
        Check(SkirmishSetup.GatherBonus(_data, -1, AiDifficulty.Hard) == 0f, "GatherBonus of faction -1 is not 0");
        Check(SkirmishSetup.GatherBonus(_data, 0, (AiDifficulty)7) == 0f, "GatherBonus of difficulty 7 is not 0");
        for (int f = 0; f < _data.Factions.Length; f++)
            Check(SkirmishSetup.GatherBonus(_data, f, AiDifficulty.Hard) == _data.Ai[f].Difficulties[(int)AiDifficulty.Hard].GatherBonus,
                $"GatherBonus faction {f} is not the data's");
    }

    // A copy of the data with Hard's gatherBonus 0.4 for the first faction and 0.3333 for the others.
    private GameData Forge(string dataDir, string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
        string copy = Path.Combine(root, "data");
        foreach (string dir in Directory.GetDirectories(dataDir, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(copy, Path.GetRelativePath(dataDir, dir)));
        foreach (string file in Directory.GetFiles(dataDir, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(copy, Path.GetRelativePath(dataDir, file)));
        for (int f = 0; f < _data.Factions.Length; f++)
        {
            string path = Path.Combine(copy, "factions", _data.Factions[f].Key, "ai.json");
            JsonNode ai = JsonNode.Parse(File.ReadAllText(path)) ?? throw new InvalidOperationException($"{path} is empty");
            ai["difficulties"]!["hard"]!["gatherBonus"] = f == 0 ? 0.4 : 0.3333;
            File.WriteAllText(path, ai.ToJsonString());
        }
        DataLoadResult loaded = DataLoader.LoadAll(copy);
        if (!loaded.Ok) throw new InvalidOperationException("forged data failed to load: " + string.Join("; ", loaded.Errors.Take(5)));
        GameData forged = loaded.Data!;
        Check(forged.Factions.Length > 1, "the forged data has one faction: no faction change to test");
        for (int f = 0; f < forged.Factions.Length; f++)
            Check(UiText.Percent(forged.Ai[f].Difficulties[2].GatherBonus) == (f == 0 ? "40" : "33.33"), $"forged faction {f}: Hard {forged.Ai[f].Difficulties[2].GatherBonus}");
        return forged;
    }

    private static void Pick(OptionButton list, int index)
    {
        list.Select(index);
        list.EmitSignal(OptionButton.SignalName.ItemSelected, index);
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
