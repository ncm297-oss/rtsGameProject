using System.Globalization;
using System.Text.RegularExpressions;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA D13 (session 2026-10-11-0314): the Line v Shock pass moved the line units' bonus vs Mounted. One value is stated in
/// three places that must agree with the data: docs/02's "Template bonuses: Line deals ×N vs Mounted", each shipped faction
/// page's line-unit note ("**Heavy Infantry:** ×N vs Mounted", "**Raider:** ×N vs Mounted"), and every shipped Line unit's
/// <c>attack.bonusVs.mounted</c> (the brief: "the same value for both factions"). A later pass that edits one and not the
/// others fails here, naming the place.
/// </summary>
public class LineMountedBonusParityQaTests
{
    private static GameData Data => TestSim.Data;

    private static float MountedBonus(UnitDef u) => u.Attack.BonusVs[Data.DamageTable.ArmorClassKeys.IndexOf("mounted")];

    private static float Docs02Template()
    {
        string line = PageTables.Lines(Path.Combine("docs", "02-game-design.md")).Single(l => l.StartsWith("Template bonuses:", StringComparison.Ordinal));
        Match m = Regex.Match(line, @"Line deals ×([0-9.]+) vs Mounted");
        Assert.True(m.Success, $"docs/02 template bonuses line has no 'Line deals ×N vs Mounted': {line}");
        return float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void EveryShippedLineUnit_CarriesDocs02sTemplateBonusVsMounted()
    {
        float template = Docs02Template();
        UnitDef[] line = Data.Units.Where(u => u.Slot == UnitSlot.Line).ToArray();
        Assert.True(line.Length == Data.Factions.Length, $"expected a Line unit per shipped faction, found {line.Length}");
        foreach (UnitDef u in line)
            Assert.True(MountedBonus(u) == template, $"{u.Key}: bonusVs.mounted {MountedBonus(u)}, docs/02 template says ×{template}");
    }

    /// <summary>D28 (BUG-1210): one row per data faction, its Line unit found by slot, not a typed pair of ids.</summary>
    [Theory]
    [MemberData(nameof(ContentFactions.Theory), MemberType = typeof(ContentFactions))]
    public void TheFactionPagesLineUnitNote_StatesTheShippedBonusVsMounted(string faction)
    {
        int f = Data.FindFaction(faction);
        UnitDef u = Data.Units.Single(x => x.Faction == f && x.Slot == UnitSlot.Line);
        string note = PageTables.FactionLines(faction).Single(l => l.StartsWith($"- **{u.DisplayName}:**", StringComparison.Ordinal));
        Match m = Regex.Match(note, @"×([0-9.]+) vs Mounted");
        Assert.True(m.Success, $"{faction}.md: the {u.DisplayName} note states no '×N vs Mounted': {note}");
        Assert.Equal(MountedBonus(u), float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
    }
}
