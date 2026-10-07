using System;
using System.Collections.Immutable;
using Rts.Sim.Determinism;

namespace Rts.Sim.Data;

/// <summary>All loaded definitions, immutable, indexed by dense int ids. Build it with <see cref="DataLoader.LoadAll"/>.</summary>
/// <remarks>
/// Tick code indexes the arrays by id. The string lookups (<see cref="FindUnit"/>, <see cref="FindFaction"/>, <see cref="FindResource"/>, <see cref="FindBuilding"/>)
/// are for load time, commands from tests/UI, and tooling: they binary-search the sorted key arrays.
/// </remarks>
public sealed class GameData
{
    internal GameData()
    {
    }

    /// <summary>Damage type × armor class table.</summary>
    public required DamageTable DamageTable { get; init; }
    /// <summary>Global economy rules.</summary>
    public required RulesDef Rules { get; init; }
    /// <summary>Factions, indexed by faction id (ordinal order of their string ids).</summary>
    public required ImmutableArray<FactionDef> Factions { get; init; }
    /// <summary>Unit types of every faction, indexed by unit id (ordinal order of their string ids).</summary>
    public required ImmutableArray<UnitDef> Units { get; init; }
    /// <summary>Resource node types (trees, gold mines), indexed by resource type id (ordinal order of their string ids). The loader always sets it; empty only for hand-built data.</summary>
    public ImmutableArray<ResourceDef> Resources { get; init; } = ImmutableArray<ResourceDef>.Empty;
    /// <summary>Building types of every faction, indexed by building id (ordinal order of their string ids). The loader always sets it; empty only for hand-built data.</summary>
    public ImmutableArray<BuildingDef> Buildings { get; init; } = ImmutableArray<BuildingDef>.Empty;
    /// <summary>Per building type id, the unit type ids it trains (whose <see cref="UnitDef.TrainedAtTypeId"/> names it), ascending; built at load (M3-4). Empty for hand-built data.</summary>
    /// <remarks>Derived from the units' <see cref="UnitDef.TrainedAtTypeId"/>, which <see cref="ContentHash"/> covers.</remarks>
    public ImmutableArray<ImmutableArray<int>> Trains { get; init; } = ImmutableArray<ImmutableArray<int>>.Empty;

    /// <summary>The unit type ids building type <paramref name="buildingTypeId"/> trains, ascending (locked ones included: <c>World.CanTrain</c> says which can be queued now); empty for an unknown type.</summary>
    public ImmutableArray<int> UnitsTrainedAt(int buildingTypeId) =>
        (uint)buildingTypeId < (uint)Trains.Length ? Trains[buildingTypeId] : ImmutableArray<int>.Empty;

    /// <summary>Techs of <c>common/techs.json</c> and every faction's <c>techs.json</c>, indexed by tech id (ordinal order of their string ids across the files), M3-5. Empty for hand-built data.</summary>
    public ImmutableArray<TechDef> Techs { get; init; } = ImmutableArray<TechDef>.Empty;

    /// <summary>Per building type id, the tech ids it researches (a tech of its slot, common or of its faction), ascending; built at load (M3-5). Empty for hand-built data.</summary>
    /// <remarks>Derived from the techs' slot and faction and the buildings', which <see cref="ContentHash"/> covers.</remarks>
    public ImmutableArray<ImmutableArray<int>> Research { get; init; } = ImmutableArray<ImmutableArray<int>>.Empty;

    /// <summary>Every tag any unit carries, ordinal order, de-duplicated: the ids <see cref="TechEffect.Tags"/> index. Derived from the units.</summary>
    public ImmutableArray<string> UnitTags { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>Tech ids of <see cref="DataLimits.AgeTechIds"/>, in order (entry k researched = Age k + 2). Empty for hand-built data.</summary>
    public ImmutableArray<int> AgeTechs { get; init; } = ImmutableArray<int>.Empty;

    /// <summary>The tech ids building type <paramref name="buildingTypeId"/> researches, ascending (<c>World.CanResearch</c> says which can be queued now); empty for an unknown type.</summary>
    public ImmutableArray<int> TechsResearchableAt(int buildingTypeId) =>
        (uint)buildingTypeId < (uint)Research.Length ? Research[buildingTypeId] : ImmutableArray<int>.Empty;

    /// <summary>Stable 64-bit hash of every field of every definition, in id order; replays store it and refuse to play on other data.</summary>
    /// <remarks>
    /// FNV-1a through <see cref="StateHasher"/>, strings char by char, so it is the same in every
    /// process. Display text is included: a replay is tied to the exact data it was recorded with.
    /// A new field on any def must be added here (DataContentHashTests changes every field in turn).
    /// </remarks>
    public ulong ContentHash()
    {
        var h = new StateHasher();
        DamageTable t = DamageTable;
        AddAll(ref h, t.ArmorClassKeys);
        AddAll(ref h, t.ArmorClassNames);
        AddAll(ref h, t.DamageTypeKeys);
        AddAll(ref h, t.DamageTypeNames);
        h.Add(t.IgnoresArmor.Length);
        foreach (bool b in t.IgnoresArmor) h.Add(b);
        AddAll(ref h, t.Multipliers);

        RulesDef r = Rules;
        h.Add(r.StartingGold);
        h.Add(r.StartingWood);
        h.Add(r.StartingWorkers);
        h.Add(r.HalfPopCap);
        h.Add(r.WorkerCarry);
        h.Add(r.GoldPerTick);
        h.Add(r.WoodPerTick);
        h.Add(r.StartMineCount);
        h.Add(r.StartMineGold);
        h.Add(r.ExpansionMineCount);
        h.Add(r.ExpansionMineGold);
        h.Add(r.TreeWood);
        h.Add(r.NodeSearchRadius);
        h.Add(r.RepairRateFactor);
        h.Add(r.RepairCostFactor);

        h.Add(Factions.Length);
        foreach (FactionDef f in Factions)
        {
            h.Add(f.Id);
            h.Add(f.Key);
            h.Add(f.DisplayName);
            h.Add(f.Description);
            h.Add(f.BonusDisplayName);
            h.Add(f.BonusDescription);
            h.Add(f.GoldName);
            h.Add(f.WoodName);
            h.Add((ulong)f.PrimaryColor);
            h.Add((ulong)f.SecondaryColor);
            h.Add((ulong)f.AccentColor);
            h.Add(f.Units.Length);
            foreach (int id in f.Units) h.Add(id);
        }

        h.Add(Units.Length);
        foreach (UnitDef u in Units)
        {
            h.Add(u.Id);
            h.Add(u.Key);
            h.Add(u.Faction);
            h.Add((int)u.Slot);
            h.Add(u.DisplayName);
            h.Add(u.Description);
            h.Add(u.Model);
            h.Add(u.Hp);
            h.Add(u.Armor);
            h.Add(u.ArmorClass);
            AttackDef a = u.Attack;
            h.Add(a.Value);
            h.Add(a.DamageType);
            h.Add(a.CooldownTicks);
            h.Add(a.WindupTicks);
            h.Add(a.Range);
            h.Add(a.MinRange);
            h.Add(a.Splash);
            h.Add(a.FriendlyFire);
            h.Add(a.Projectile);
            AddAll(ref h, a.BonusVs);
            h.Add(u.SpeedPerTick);
            h.Add(u.Sight);
            h.Add(u.Radius);
            h.Add(u.CostGold);
            h.Add(u.CostWood);
            h.Add(u.HalfPop);
            h.Add(u.TrainTicks);
            h.Add(u.TrainedAt);
            h.Add(u.TrainedAtTypeId);
            AddAll(ref h, u.Requires);
            AddAll(ref h, u.Tags);
        }

        h.Add(Resources.Length);
        foreach (ResourceDef d in Resources)
        {
            h.Add(d.Id);
            h.Add(d.Key);
            h.Add(d.DisplayName);
            h.Add(d.Description);
            h.Add((int)d.Resource);
            h.Add(d.FootprintWidth);
            h.Add(d.FootprintHeight);
        }

        h.Add(Buildings.Length);
        foreach (BuildingDef b in Buildings)
        {
            h.Add(b.Id);
            h.Add(b.Key);
            h.Add(b.Faction);
            h.Add((int)b.Slot);
            h.Add(b.DisplayName);
            h.Add(b.Description);
            h.Add(b.FootprintWidth);
            h.Add(b.FootprintHeight);
            h.Add(b.Hp);
            h.Add(b.Armor);
            h.Add(b.CostGold);
            h.Add(b.CostWood);
            h.Add(b.BuildTicks);
            h.Add(b.HalfPopProvided);
            h.Add(b.DropOff);
            AddAll(ref h, b.Requires);
        }

        h.Add(Techs.Length);
        foreach (TechDef tech in Techs)
        {
            h.Add(tech.Id);
            h.Add(tech.Key);
            h.Add(tech.Faction);
            h.Add(tech.DisplayName);
            h.Add(tech.Description);
            h.Add((int)tech.ResearchedAtSlot);
            h.Add(tech.CostGold);
            h.Add(tech.CostWood);
            h.Add(tech.ResearchTicks);
            AddAll(ref h, tech.Requires);
            h.Add(tech.Effects.Length);
            foreach (TechEffect e in tech.Effects)
            {
                h.Add((int)e.Stat);
                h.Add(e.Amount);
                h.Add(e.AttackType);
                AddAll(ref h, e.Tags);
                AddAll(ref h, e.Units);
                h.Add(e.Siege);
            }
        }
        return h.Value;
    }

    private static void AddAll(ref StateHasher h, ImmutableArray<int> items)
    {
        h.Add(items.IsDefault ? -1 : items.Length);
        if (items.IsDefault) return;
        foreach (int i in items) h.Add(i);
    }

    private static void AddAll(ref StateHasher h, ImmutableArray<string> items)
    {
        h.Add(items.Length);
        foreach (string s in items) h.Add(s);
    }

    private static void AddAll(ref StateHasher h, ImmutableArray<float> items)
    {
        h.Add(items.Length);
        foreach (float f in items) h.Add(f);
    }

    /// <summary>Unit id for a string id, or -1.</summary>
    public int FindUnit(string key) => Find(Units, static u => u.Key, key);

    /// <summary>Resource type id for a string id, or -1.</summary>
    public int FindResource(string key) => Find(Resources, static r => r.Key, key);

    /// <summary>Building type id for a string id, or -1.</summary>
    public int FindBuilding(string key) => Find(Buildings, static b => b.Key, key);

    /// <summary>Tech id for a string id, or -1.</summary>
    public int FindTech(string key) => Find(Techs, static t => t.Key, key);

    /// <summary>Faction id for a string id, or -1.</summary>
    public int FindFaction(string key) => Find(Factions, static f => f.Key, key);

    // Binary search: the arrays are in ordinal order of their keys, which is how ids were assigned.
    private static int Find<T>(ImmutableArray<T> items, Func<T, string> keyOf, string key)
    {
        int lo = 0, hi = items.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            int c = string.CompareOrdinal(keyOf(items[mid]), key);
            if (c == 0) return mid;
            if (c < 0) lo = mid + 1; else hi = mid - 1;
        }
        return -1;
    }
}
