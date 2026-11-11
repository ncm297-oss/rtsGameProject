using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Rts.Sim.Data;

namespace Rts.Game.Tests;

/// <summary>
/// M7-VH1 (BUG-1147 item 1): the Easy and Hard difficulty tips say the AI's think interval from the data
/// (<c>ai.json</c> <c>thinkInterval</c>, <c>{1}</c> in <c>ui.json</c>), not typed words.
/// <list type="bullet">
/// <item>Shipped data: every AI slot's Easy tip reads "Thinks every 2 seconds, ..." and Hard's "... thinks every 0.5 seconds,
/// ..." (the M5-V2 text with "half second" now the data's 0.5), the other words as before.</item>
/// <item>A copy of the data with the first faction's Easy / Normal / Hard <c>thinkInterval</c> 7 / 2 / 0.333 and the others'
/// 3 / 1 / 1.5: the tips read "7", "0.35" (0.333 s is 7 ticks, the interval the AI keeps) and "3", "1.5" by the slot's
/// faction, and picking the faction re-applies them.</item>
/// <item>M7-V3a (BUG-1200): the number comes with its unit from <c>setup.seconds_one</c> / <c>seconds_other</c>: a forged Easy and
/// Hard interval of exactly 1 s (20 ticks) reads "every 1 second,", 19 and 21 ticks "every 0.95 seconds," / "every 1.05 seconds,";
/// the shipped tips read exactly as before (the two sentences above).</item>
/// </list>
/// Headless: <c>&amp; $env:GODOT --headless --path game res://tests/DifficultyTipTest.tscn</c>; prints "DIFFICULTY TIP TEST
/// PASS" or "DIFFICULTY TIP TEST FAIL: ..." lines.
/// </summary>
public partial class DifficultyTipTest : Node
{
    // The shipped M5-V2 sentences with the interval as the data gives it (the other words unchanged).
    private const string ShippedEasy = "Thinks every 2 seconds, follows its build order slowly, attacks in small waves, doesn't micro, uses abilities and expands late.";
    private const string ShippedHardStart = "Fair play with tighter decisions: thinks every 0.5 seconds, focuses fire, pulls back wounded units, uses abilities well, and gathers ";

    private readonly List<string> _failures = new();
    private UiText _ui = null!;

    public override async void _Ready()
    {
        string root = Path.GetFullPath(ProjectSettings.GlobalizePath($"user://difficulty-tip-test-{OS.GetProcessId()}"));
        try
        {
            await Frames(1);
            string dataDir = ProjectSettings.GlobalizePath("res://data");
            DataLoadResult loaded = DataLoader.LoadAll(dataDir);
            if (!loaded.Ok) throw new InvalidOperationException("data failed to load");
            GameData data = loaded.Data!;
            _ui = UiText.Shared ?? throw new InvalidOperationException("ui.json failed to load");

            SkirmishSetup shipped = await NewSetup(data);
            for (int slot = 1; slot < SkirmishSetup.MaxSlots; slot++)
            {
                OptionButton list = shipped.DifficultyOptions[slot]!;
                string easy = Plain(list.GetItemTooltip((int)AiDifficulty.Easy));
                string hard = Plain(list.GetItemTooltip((int)AiDifficulty.Hard));
                Check(easy == ShippedEasy, $"shipped slot {slot} Easy tip '{easy}'");
                Check(hard.StartsWith(ShippedHardStart, StringComparison.Ordinal), $"shipped slot {slot} Hard tip '{hard}'");
                Check(!easy.Contains('{') && !hard.Contains('{'), $"shipped slot {slot}: a placeholder left in '{easy}' / '{hard}'");
            }
            GD.Print($"  shipped Easy: '{Plain(shipped.DifficultyOption.GetItemTooltip(0))}'");
            GD.Print($"  shipped Hard: '{Plain(shipped.DifficultyOption.GetItemTooltip(2))}'");
            shipped.QueueFree();
            await Frames(1);

            GameData forged = Forge(dataDir, root, data);
            SkirmishSetup setup = await NewSetup(forged);
            for (int slot = 1; slot < SkirmishSetup.MaxSlots; slot++) CheckSlot(setup, slot, $"forged slot {slot}");
            for (int f = forged.Factions.Length - 1; f >= 0; f--)
            {
                Pick(setup.FactionOptions[1], f);
                CheckSlot(setup, 1, $"forged slot 1 after picking faction {f}");
            }
            GD.Print($"  forged faction 0 Easy: '{Plain(setup.DifficultyOption.GetItemTooltip(0))}'");
            GD.Print($"  forged faction 0 Hard: '{Plain(setup.DifficultyOption.GetItemTooltip(2))}'");
            Check(forged.Factions.Length > 1, "the data has one faction: the faction-change row needs two");
            Check(SkirmishSetup.ThinkSeconds(forged, -1, AiDifficulty.Hard) == 0f && SkirmishSetup.ThinkSeconds(forged, 0, (AiDifficulty)7) == 0f,
                "ThinkSeconds out of range is not 0");
            foreach ((float s, string want) in new[] { (2f, "2"), (0.5f, "0.5"), (0.35f, "0.35"), (7f, "7"), (1.5f, "1.5"), (0.05f, "0.05") })
                Check(UiText.Seconds(s) == want, $"Seconds({s}) = '{UiText.Seconds(s)}', want '{want}'");
            setup.QueueFree();
            await Frames(1);

            // BUG-1200: exactly 1 s is singular; 19 and 21 ticks either side are plural. The forged setup reads the data.
            foreach ((int ticks, string want) in new[] { (20, "1 second"), (19, "0.95 seconds"), (21, "1.05 seconds"), (40, "2 seconds"), (10, "0.5 seconds") })
            {
                string phrase = _ui.SecondsPhrase(ticks / (float)Rts.Sim.SimConstants.TicksPerSecond);
                Check(phrase == want, $"{ticks} ticks: '{phrase}', want '{want}'");
                GameData one = Forge(dataDir, root, data, ticks / 20.0, 1.0, ticks / 20.0);
                SkirmishSetup s1 = await NewSetup(one);
                for (int slot = 1; slot < SkirmishSetup.MaxSlots; slot++)
                {
                    string easy = Plain(s1.DifficultyOptions[slot]!.GetItemTooltip((int)AiDifficulty.Easy));
                    string hard = Plain(s1.DifficultyOptions[slot]!.GetItemTooltip((int)AiDifficulty.Hard));
                    Check(easy.StartsWith($"Thinks every {want}, follows", StringComparison.Ordinal), $"Easy at {ticks} ticks, slot {slot}: '{easy}'");
                    Check(hard.Contains($"thinks every {want}, focuses fire", StringComparison.Ordinal), $"Hard at {ticks} ticks, slot {slot}: '{hard}'");
                }
                if (ticks == 20) GD.Print($"  forged Easy at exactly 1 s: '{Plain(s1.DifficultyOption.GetItemTooltip(0))}'");
                s1.QueueFree();
                await Frames(1);
            }
        }
        catch (Exception ex)
        {
            _failures.Add($"exception: {ex}");
        }
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        foreach (string f in _failures.Take(60)) GD.Print($"DIFFICULTY TIP TEST FAIL: {f}");
        if (_failures.Count == 0) GD.Print("DIFFICULTY TIP TEST PASS");
        SceneExit.Quit(this, _failures.Count == 0 ? 0 : 1);
    }

    // The forged intervals' texts: faction 0's Easy 7 s and Hard 0.333 s (7 ticks: 0.35 s), the others' Easy 3 s and Hard 1.5 s.
    private void CheckSlot(SkirmishSetup setup, int slot, string what)
    {
        OptionButton list = setup.DifficultyOptions[slot]!;
        bool first = setup.FactionOptions[slot].Selected == 0;
        string easy = Plain(list.GetItemTooltip((int)AiDifficulty.Easy)), hard = Plain(list.GetItemTooltip((int)AiDifficulty.Hard));
        string wantEasy = first ? "Thinks every 7 seconds," : "Thinks every 3 seconds,";
        string wantHard = first ? "thinks every 0.35 seconds," : "thinks every 1.5 seconds,";
        Check(easy.StartsWith(wantEasy, StringComparison.Ordinal), $"{what}: Easy '{easy}', want '{wantEasy} ...'");
        Check(hard.Contains(wantHard, StringComparison.Ordinal), $"{what}: Hard '{hard}', want '... {wantHard} ...'");
        Check(list.TooltipText == list.GetItemTooltip(list.Selected), $"{what}: the list's tooltip is not the picked item's");
    }

    private GameData Forge(string dataDir, string root, GameData shipped) => Forge(dataDir, root, shipped, -1, -1, -1);

    // Every faction's Easy / Normal / Hard thinkInterval; a negative easy: the first faction 7 / 2 / 0.333, the others 3 / 1 / 1.5.
    private GameData Forge(string dataDir, string root, GameData shipped, double easy, double normal, double hard)
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
        string copy = Path.Combine(root, "data");
        Directory.CreateDirectory(copy);
        foreach (string dir in Directory.GetDirectories(dataDir, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(copy, Path.GetRelativePath(dataDir, dir)));
        foreach (string file in Directory.GetFiles(dataDir, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(copy, Path.GetRelativePath(dataDir, file)));
        for (int f = 0; f < shipped.Factions.Length; f++)
        {
            string path = Path.Combine(copy, "factions", shipped.Factions[f].Key, "ai.json");
            JsonNode ai = JsonNode.Parse(File.ReadAllText(path)) ?? throw new InvalidOperationException($"{path} is empty");
            JsonNode d = ai["difficulties"]!;
            d["easy"]!["thinkInterval"] = easy >= 0 ? easy : f == 0 ? 7.0 : 3.0;
            d["normal"]!["thinkInterval"] = easy >= 0 ? normal : f == 0 ? 2.0 : 1.0;
            d["hard"]!["thinkInterval"] = easy >= 0 ? hard : f == 0 ? 0.333 : 1.5;
            File.WriteAllText(path, ai.ToJsonString());
        }
        DataLoadResult loaded = DataLoader.LoadAll(copy);
        if (!loaded.Ok) throw new InvalidOperationException("forged data failed to load: " + string.Join("; ", loaded.Errors.Take(5)));
        return loaded.Data!;
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

    private static string Plain(string tip) => tip.Replace('\n', ' ');

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
