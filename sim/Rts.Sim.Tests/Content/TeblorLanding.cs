using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D30: the Teblor's M7-4 landing (D31) as a test copy, made here and not through <c>Abilities/BloodOilScenes</c> (BUG-1280):
/// the four rows of docs/03 "For the data track (M7-4)" (the <c>teblor_frenzied</c> status with <c>damageBonus</c> 0.2 and an
/// <c>onEnd</c> of Slowed 0.2 for 4 s; <c>blood_oil_frenzy</c> applying it; Blood-oil Anointing's <c>statusDuration</c> +4 s
/// on it; <c>scale</c> 1.25 on every Teblor building), the player-facing words the rows need (written here as a
/// proposal; the owner reads them at D31), and teblor.md and docs/02 as the landing would leave them. Every edit replaces
/// rather than appends, so the copy is the same whether or not the shipped files already carry a row. Each page edit must
/// find its text exactly once, so a page that drifts from what this copy expects fails here by name.
/// </summary>
internal static class TeblorLanding
{
    /// <summary>The status id docs/03 gives (it sorts after <c>stealthed</c>: the M4 statuses keep their state-hash numbers).</summary>
    public const string Status = "teblor_frenzied";

    /// <summary>The status's name (docs/03's row).</summary>
    public const string StatusName = "Blood-oil Frenzy";

    /// <summary>The status's tooltip: the damage bonus and the aftermath, the row's own numbers (the attack speed is the ability's).</summary>
    public const string StatusDescription = "Attacks faster and deals 20% more damage. When it ends, the unit is Slowed 20% for 4 seconds.";

    /// <summary>Blood-oil Anointing's description with its third effect.</summary>
    public const string AnointingDescription =
        "Blood-oil rubbed into the skin before battle: Warriors and Destrier Riders gain +2 armor, and Blood-oil Frenzy lasts 4 s longer. Needs Age II.";

    private const string ShippedAnointing = "Blood-oil rubbed into the skin before battle: Warriors and Destrier Riders gain +2 armor. Needs Age II.";

    /// <summary>The four data files the landing edits, relative to <c>game/data/</c>.</summary>
    public static readonly string[] Files =
    {
        "common/statuses.json", "factions/teblor/abilities.json", "factions/teblor/techs.json", "factions/teblor/buildings.json",
    };

    /// <summary>The landing's edit of data file <paramref name="rel"/> (one of <see cref="Files"/>), in place on its root.</summary>
    public static void EditRoot(string rel, JsonObject root)
    {
        switch (rel)
        {
            case "common/statuses.json":
            {
                JsonArray rows = root["statuses"]!.AsArray();
                JsonObject row = new()
                {
                    ["id"] = Status, ["displayName"] = StatusName, ["description"] = StatusDescription, ["kind"] = "frenzied",
                    ["damageBonus"] = 0.2,
                    ["onEnd"] = new JsonObject { ["status"] = "slowed", ["magnitude"] = 0.2, ["duration"] = 4 },
                };
                int at = IndexOf(rows, Status);
                if (at >= 0) rows[at] = row;
                else rows.Add(row);
                break;
            }
            case "factions/teblor/abilities.json":
            {
                JsonObject frenzy = root["abilities"]!.AsArray().Single(a => (string)a!["id"]! == "blood_oil_frenzy")!.AsObject();
                frenzy["effects"] = new JsonArray(new JsonObject
                {
                    ["kind"] = "applyStatus", ["status"] = Status, ["magnitude"] = 0.4, ["duration"] = 10,
                });
                break;
            }
            case "factions/teblor/techs.json":
            {
                JsonObject anointing = root["techs"]!.AsArray().Single(t => (string)t!["id"]! == "teblor_blood_oil_anointing")!.AsObject();
                JsonArray effects = anointing["effects"]!.AsArray();
                for (int i = effects.Count - 1; i >= 0; i--)
                    if ((string?)effects[i]!["stat"] == "statusDuration") effects.RemoveAt(i);
                effects.Add(new JsonObject { ["stat"] = "statusDuration", ["amount"] = 4, ["statuses"] = new JsonArray(Status) });
                anointing["description"] = AnointingDescription;
                break;
            }
            case "factions/teblor/buildings.json":
                foreach (JsonNode? b in root["buildings"]!.AsArray()) b!["scale"] = 1.25;
                break;
            default:
                throw new ArgumentException($"the landing does not edit '{rel}'");
        }
    }

    /// <summary>
    /// The reverse: data file <paramref name="rel"/> as it is before the landing (no <c>teblor_frenzied</c> row, the Frenzy
    /// applying the Zealot's <c>frenzied</c>, the Anointing without its third effect, no building <c>scale</c>), whether or
    /// not the shipped file has landed, so the pins' "before" side stays testable after D31.
    /// </summary>
    public static void BeforeRoot(string rel, JsonObject root)
    {
        switch (rel)
        {
            case "common/statuses.json":
            {
                JsonArray rows = root["statuses"]!.AsArray();
                int at = IndexOf(rows, Status);
                if (at >= 0) rows.RemoveAt(at);
                break;
            }
            case "factions/teblor/abilities.json":
            {
                JsonObject frenzy = root["abilities"]!.AsArray().Single(a => (string)a!["id"]! == "blood_oil_frenzy")!.AsObject();
                frenzy["effects"] = new JsonArray(new JsonObject
                {
                    ["kind"] = "applyStatus", ["status"] = "frenzied", ["magnitude"] = 0.4, ["duration"] = 10,
                });
                break;
            }
            case "factions/teblor/techs.json":
            {
                JsonObject anointing = root["techs"]!.AsArray().Single(t => (string)t!["id"]! == "teblor_blood_oil_anointing")!.AsObject();
                JsonArray effects = anointing["effects"]!.AsArray();
                for (int i = effects.Count - 1; i >= 0; i--)
                    if ((string?)effects[i]!["stat"] == "statusDuration") effects.RemoveAt(i);
                anointing["description"] = ShippedAnointing;
                break;
            }
            case "factions/teblor/buildings.json":
                foreach (JsonNode? b in root["buildings"]!.AsArray()) b!.AsObject().Remove("scale");
                break;
            default:
                throw new ArgumentException($"the landing does not edit '{rel}'");
        }
    }

    private static int IndexOf(JsonArray rows, string id)
    {
        for (int i = 0; i < rows.Count; i++) if ((string?)rows[i]!["id"] == id) return i;
        return -1;
    }

    /// <summary>Applies the landing to a temp copy of the data.</summary>
    public static void Edit(TestDataDir dir)
    {
        foreach (string rel in Files) dir.EditJson(rel, root => EditRoot(rel, root));
    }

    /// <summary>The landing's data files as JSON text (for a <see cref="ContentScope"/>).</summary>
    public static Dictionary<string, string> FileTexts()
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string rel in Files)
        {
            JsonObject root = JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.Shipped, rel)))!.AsObject();
            EditRoot(rel, root);
            texts[rel] = root.ToJsonString();
        }
        return texts;
    }

    private static readonly Lazy<GameData> s_data = new(() => AiScenes.DataWith(Edit));

    /// <summary>The shipped data with the landing applied.</summary>
    public static GameData Data => s_data.Value;

    private static readonly Lazy<GameData> s_before = new(() => AiScenes.DataWith(dir =>
    {
        foreach (string rel in Files) dir.EditJson(rel, root => BeforeRoot(rel, root));
    }));

    /// <summary>The shipped data as it is before the landing (<see cref="BeforeRoot"/>): today's shipped data until D31.</summary>
    public static GameData BeforeData => s_before.Value;

    /// <summary>The landing with <paramref name="wrong"/> applied after it (a wrong copy, for the pins' negative tests).</summary>
    public static GameData DataWith(Action<TestDataDir> wrong) => AiScenes.DataWith(dir =>
    {
        Edit(dir);
        wrong(dir);
    });

    // ---------- the docs as the landing leaves them ----------

    private const string Page = "docs/factions/teblor.md";
    private const string Doc02 = "docs/02-game-design.md";

    /// <summary>teblor.md's edits at the landing: (what the page says now, what it says then).</summary>
    private static readonly (string Find, string Replace)[] PageEdits =
    {
        // The faction upgrade line: the note goes.
        ("**duration +4 s** (the rule exists (M7-4); the row lands with D31).", "**duration +4 s**."),
        // The Abilities row: the status's name, its damage bonus and its aftermath.
        ("| Own units in the radius are Frenzied (+40% attack speed) for 10 s. No effect on buildings |",
            "| Own units in the radius get Blood-oil Frenzy (+40% attack speed, +20% damage) for 10 s, then are Slowed 20% for 4 s. No effect on buildings |"),
        ("`{ \"kind\": \"applyStatus\", \"status\": \"frenzied\", \"magnitude\": 0.4, \"duration\": 10 }`",
            "`{ \"kind\": \"applyStatus\", \"status\": \"teblor_frenzied\", \"magnitude\": 0.4, \"duration\": 10 }`"),
        ("Two parts of the design wait for their rows: **+20% damage** while Frenzied, and the **aftermath**, Slowed 20% for 4 s\n"
            + "when the Frenzy ends (the rules exist (M7-4: a status's `damageBonus` and `onEnd`, on a Teblor status of its own beside\n"
            + "the Zealot's `frenzied`); the rows land with D31).",
            "The status, `teblor_frenzied` in `common/statuses.json` (\"Blood-oil Frenzy\"), carries the rest: **+20% damage** while\n"
            + "it lasts (`damageBonus` 0.2), and the **aftermath**, Slowed 20% for 4 s when it ends (`onEnd`)."),
        // The Buildings table gains its Scale column; the prose drops its note.
        ("| Slot | Name | Id | HP | Armor | Cost (G/W) | Build (s) | Footprint | Sight | Provides | Requires |\n"
            + "| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |",
            "| Slot | Name | Id | HP | Armor | Cost (G/W) | Build (s) | Footprint | Sight | Provides | Requires | Scale |\n"
            + "| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |"),
        ("Models are 1.25× scale visually but keep template footprints\n(`buildings.json` `scale`, the view's model size; the rule exists (M7-4); the row lands with D31).",
            "Models are 1.25× scale visually but keep template footprints\n(`buildings.json` `scale`, the view's model size: the Scale column)."),
        // The Techs table and its note.
        ("| Age II | Warrior +2 armor; Destrier Rider +2 armor |", "| Age II | Warrior +2 armor; Destrier Rider +2 armor; Blood-oil Frenzy duration +4 s |"),
        ("Its Frenzy duration +4 s is a third effect, a `statusDuration`\non the Frenzy's status (the rule exists (M7-4); the row lands with D31).",
            "Its Frenzy duration +4 s is a third effect, a `statusDuration`\non the Frenzy's status (`statuses` `[\"teblor_frenzied\"]`, no `appliesTo`)."),
        // The Owner review row of the Anointing.
        ("| Blood-oil Anointing | " + ShippedAnointing + " |", "| Blood-oil Anointing | " + AnointingDescription + " |"),
    };

    /// <summary>docs/02's edits at the landing: the Frenzy status's row, and the plain Frenzied's sources without the Teblor ability.</summary>
    private static readonly (string Find, string Replace)[] Doc02Edits =
    {
        ("| Frenzied | " + AbilityContentTests.FrenziedEffect + " | Blood-oil Frenzy, Zealot passive |",
            "| Frenzied | " + AbilityContentTests.FrenziedEffect + " | Zealot passive |\n"
            + "| Blood-oil Frenzy | " + AbilityContentTests.FrenziedEffect + "; its own attacks deal +20% damage; when it ends: Slowed 20% for 4 s | Blood-oil Frenzy |"),
    };

    /// <summary>teblor.md as the landing leaves it (today's page, or the page itself once it has landed).</summary>
    public static string[] PageLines() => Edited(Page, PageEdits, landed: true);

    /// <summary>teblor.md as it is before the landing (the reverse edits; today's page until D31).</summary>
    public static string[] BeforePageLines() => Edited(Page, PageEdits, landed: false);

    /// <summary>docs/02 as the landing leaves it.</summary>
    public static string[] Doc02Lines() => Edited(Doc02, Doc02Edits, landed: true);

    /// <summary>
    /// The Buildings rows of teblor.md with a Scale cell exactly when the header has the column: 1.25 added to a row one
    /// cell short of it, the last cell dropped from a row one cell longer than a header without it.
    /// </summary>
    private static string ScaleCells(string text)
    {
        string[] lines = text.Split('\n');
        int h = Array.FindIndex(lines, l => l.StartsWith("| Slot | Name | Id | HP |", StringComparison.Ordinal));
        int width = PageTables.Cells(lines[h]).Length;
        for (int i = h + 2; i < lines.Length && lines[i].StartsWith('|'); i++)
        {
            int cells = PageTables.Cells(lines[i]).Length;
            if (cells == width - 1) lines[i] += " 1.25 |";
            else if (cells == width + 1) lines[i] = lines[i][..(lines[i].TrimEnd().TrimEnd('|').LastIndexOf('|') + 1)];
        }
        return string.Join("\n", lines);
    }

    /// <summary>
    /// <paramref name="relativePath"/> with <paramref name="edits"/> applied forward (<paramref name="landed"/>) or back. An
    /// edit whose source text is there exactly once is made; one whose result is already there is skipped (the file is on
    /// that side already); anything else fails naming the text, so a page that drifts from this copy fails here.
    /// </summary>
    private static string[] Edited(string relativePath, (string Find, string Replace)[] edits, bool landed)
    {
        string text = string.Join("\n", File.ReadAllLines(Path.Combine(TestDataDir.RepoRoot(), relativePath)));
        foreach ((string a, string b) in edits)
        {
            (string find, string replace) = landed ? (a, b) : (b, a);
            int count = CountOf(text, find);
            if (count == 0 && CountOf(text, replace) == 1) continue;
            Assert.True(count == 1, $"{relativePath}: the landing's edit finds '{find}' {count} times, expected once (the page drifted from TeblorLanding)");
            text = text.Replace(find, replace, StringComparison.Ordinal);
        }
        if (relativePath == Page) text = ScaleCells(text);
        return text.Split('\n');
    }

    private static int CountOf(string text, string find)
    {
        int n = 0;
        for (int i = text.IndexOf(find, StringComparison.Ordinal); i >= 0; i = text.IndexOf(find, i + find.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    /// <summary>A scope for the landing: <paramref name="data"/> (default the landing's), its files, and the landing's pages.</summary>
    public static ContentScope Scope(GameData? data = null, string[]? page = null) => new()
    {
        CopyData = data ?? Data,
        Files = FileTexts(),
        Docs = new Dictionary<string, string[]> { [Page] = page ?? PageLines(), [Doc02] = Doc02Lines() },
    };
}
