using Rts.Sim.Entities;

namespace Rts.Sim.Tests;

/// <summary>M3-5 test helpers: shipped tech and Forge ids, and a cost recount of what a player has researched or queued.</summary>
public static class ResearchMaps
{
    /// <summary>Age II, researched at the Town Hall.</summary>
    public static int AgeII => TestSim.Data.FindTech("age_ii");

    /// <summary>Melee Weapons level 1 (+1 attack, melee attack type).</summary>
    public static int Melee1 => TestSim.Data.FindTech("melee_weapons_1");

    /// <summary>Melee Weapons level 2.</summary>
    public static int Melee2 => TestSim.Data.FindTech("melee_weapons_2");

    /// <summary>Armor level 1 (+1 armor, every non-siege unit).</summary>
    public static int Armor1 => TestSim.Data.FindTech("armor_1");

    /// <summary>The Malazan faction upgrade.</summary>
    public static int Moranth => TestSim.Data.FindTech("moranth_supply");

    /// <summary>The Whirlwind faction upgrade.</summary>
    public static int Dryjhna => TestSim.Data.FindTech("dryjhnas_prophecy");

    /// <summary>The Malazan Forge.</summary>
    public static int Armory => TestSim.Data.FindBuilding("malazan_armory");

    /// <summary>The Whirlwind Forge.</summary>
    public static int Smithy => TestSim.Data.FindBuilding("whirlwind_smithy");

    /// <summary>Gold and wood of every tech <paramref name="player"/> has researched.</summary>
    public static (long Gold, long Wood) ResearchedCost(World w, int player)
    {
        long gold = 0, wood = 0;
        for (int t = 0; t < w.Data.Techs.Length; t++)
        {
            if (!w.HasTech(player, t)) continue;
            gold += w.Data.Techs[t].CostGold;
            wood += w.Data.Techs[t].CostWood;
        }
        return (gold, wood);
    }

    /// <summary>Gold and wood of every item (unit or tech) in <paramref name="player"/>'s queues.</summary>
    public static (long Gold, long Wood) QueuedCost(World w, int player)
    {
        long gold = 0, wood = 0;
        BuildingStore b = w.Buildings;
        for (int k = 0; k < b.Capacity; k++)
        {
            if (!b.Alive[k] || b.Owner[k] != player) continue;
            for (int q = 0; q < b.QueueCount[k]; q++)
            {
                int type = b.QueueTypeAt(k, q);
                bool tech = b.QueueIsTechAt(k, q);
                gold += tech ? w.Data.Techs[type].CostGold : w.Data.Units[type].CostGold;
                wood += tech ? w.Data.Techs[type].CostWood : w.Data.Units[type].CostWood;
            }
        }
        return (gold, wood);
    }
}
