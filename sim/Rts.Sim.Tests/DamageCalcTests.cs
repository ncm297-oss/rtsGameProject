using Rts.Sim.Combat;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>M4-1 criterion 1: the damage formula (docs/02 "Damage formula", "Damage type x armor class").</summary>
public class DamageCalcTests
{
    private static DamageTable Table => TestSim.Data.DamageTable;

    private static int Type(string key) => Table.DamageTypeKeys.IndexOf(key);

    private static int Class(string key) => Table.ArmorClassKeys.IndexOf(key);

    [Fact]
    public void WorkedExample_CrossbowBoltOnRaider_Is6()
    {
        // docs/02: a Crossbowman (9 pierce, x1.3 vs Heavy) on Raider line infantry (Heavy, armor 1): 9 x 0.6 x 1.3 = 7.0 -> 7 - 1 = 6.
        Assert.Equal(6, DamageCalc.Compute(Table, Type("pierce"), Class("heavy"), attack: 9, bonusVs: 1.3f, armor: 1));

        // The same through the shipped units' data.
        UnitDef crossbow = TestSim.Data.Units[TestSim.Data.FindUnit("malazan_crossbowman")];
        UnitDef raider = TestSim.Data.Units[TestSim.Data.FindUnit("whirlwind_raider")];
        Assert.Equal(1, raider.Armor);
        Assert.Equal(6, DamageCalc.Compute(Table, crossbow.Attack, 0, raider.ArmorClass, raider.Armor));
    }

    // Attack 10 against every cell of the docs/02 table, by hand: round half up (12.5 -> 13, 7.5 -> 8).
    [Theory]
    [InlineData("melee", "light", 10)]
    [InlineData("melee", "heavy", 10)]
    [InlineData("melee", "mounted", 10)]
    [InlineData("melee", "giant", 10)]
    [InlineData("melee", "structure", 4)]
    [InlineData("pierce", "light", 13)]
    [InlineData("pierce", "heavy", 6)]
    [InlineData("pierce", "mounted", 10)]
    [InlineData("pierce", "giant", 13)]
    [InlineData("pierce", "structure", 2)]
    [InlineData("siege", "light", 5)]
    [InlineData("siege", "heavy", 8)]
    [InlineData("siege", "mounted", 5)]
    [InlineData("siege", "giant", 8)]
    [InlineData("siege", "structure", 30)]
    [InlineData("magic", "light", 10)]
    [InlineData("magic", "heavy", 13)]
    [InlineData("magic", "mounted", 10)]
    [InlineData("magic", "giant", 13)]
    [InlineData("magic", "structure", 4)]
    public void EveryTableCell_AtArmor0_AndArmorAboveRaw(string type, string armorClass, int expectedAt0)
    {
        int t = Type(type), c = Class(armorClass);
        Assert.True(t >= 0 && c >= 0);
        Assert.Equal(expectedAt0, DamageCalc.Compute(Table, t, c, attack: 10, bonusVs: 1f, armor: 0));
        // Armor at or above the raw damage: the floor of 1, except magic, which ignores armor.
        int expectedArmored = type == "magic" ? expectedAt0 : 1;
        Assert.Equal(expectedArmored, DamageCalc.Compute(Table, t, c, attack: 10, bonusVs: 1f, armor: expectedAt0));
        Assert.Equal(expectedArmored, DamageCalc.Compute(Table, t, c, attack: 10, bonusVs: 1f, armor: 1000));
    }

    [Fact]
    public void MagicIgnoresArmor_OnlyMagic()
    {
        for (int t = 0; t < Table.DamageTypeKeys.Length; t++)
            Assert.Equal(Table.DamageTypeKeys[t] == "magic", Table.IgnoresArmor[t]);
        int magic = Type("magic"), light = Class("light");
        Assert.Equal(9, DamageCalc.Compute(Table, magic, light, attack: 9, bonusVs: 1f, armor: 0));
        Assert.Equal(9, DamageCalc.Compute(Table, magic, light, attack: 9, bonusVs: 1f, armor: 5));
    }

    [Fact]
    public void MissingBonusVs_IsOne_AndAGivenOneApplies()
    {
        // Heavy Infantry: bonusVs { mounted: 1.5 } only.
        UnitDef hi = TestSim.Data.Units[TestSim.Data.FindUnit("malazan_heavy_infantry")];
        Assert.Equal(1f, hi.Attack.BonusVs[Class("light")]);
        Assert.Equal(10, DamageCalc.Compute(Table, hi.Attack, 0, Class("light"), 0));
        Assert.Equal(10, DamageCalc.Compute(Table, hi.Attack, 0, Class("heavy"), 0));
        Assert.Equal(15, DamageCalc.Compute(Table, hi.Attack, 0, Class("mounted"), 0));
        // Two Heavy Infantry: 10 x 1.0 x 1.0 - 3 = 7 (M4-1 criterion 2's hit).
        Assert.Equal(7, DamageCalc.Compute(Table, hi.Attack, 0, hi.ArmorClass, hi.Armor));
    }

    [Fact]
    public void RoundsHalfUp_NotToEven()
    {
        int pierce = Type("pierce"), light = Class("light");
        // 2 x 1.25 = 2.5 -> 3 (banker's rounding would give 2); 6 x 1.25 = 7.5 -> 8.
        Assert.Equal(3, DamageCalc.Compute(Table, pierce, light, attack: 2, bonusVs: 1f, armor: 0));
        Assert.Equal(8, DamageCalc.Compute(Table, pierce, light, attack: 6, bonusVs: 1f, armor: 0));
        // A zero attack still does the floor of 1.
        Assert.Equal(1, DamageCalc.Compute(Table, pierce, light, attack: 0, bonusVs: 1f, armor: 0));
    }

    [Fact]
    public void MeleeWeaponsAndArmorTechs_EachChangeAHitByExactlyOne()
    {
        // Through the sim: two Heavy Infantry, the forge techs set on one side or the other.
        int weapons = TestSim.Data.FindTech("melee_weapons_1"), armor = TestSim.Data.FindTech("armor_1");
        Assert.True(weapons >= 0 && armor >= 0);
        Assert.Equal(7, CombatScenes.FirstHitDamage(techP0: -1, techP1: -1));
        Assert.Equal(8, CombatScenes.FirstHitDamage(techP0: weapons, techP1: -1)); // player 0 hits first: its attack +1
        Assert.Equal(6, CombatScenes.FirstHitDamage(techP0: -1, techP1: armor));   // player 1's armor +1
        Assert.Equal(7, CombatScenes.FirstHitDamage(techP0: weapons, techP1: armor));
    }
}
