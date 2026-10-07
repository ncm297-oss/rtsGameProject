using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Rts.Sim.Data;

/// <summary>Reads and validates every JSON definition under <c>game/data/</c> into a <see cref="GameData"/>.</summary>
/// <remarks>
/// Collects every problem instead of stopping at the first, so one run lists all broken data.
/// String ids become dense ints in ordinal-sorted order, so ids never depend on file-system order.
/// Seconds become ticks and per-second rates become per-tick rates here; the sim never sees seconds.
/// <c>trainedAt</c> resolves to an own-faction building type id (M3-4); every <c>requires</c> entry must name a tech or
/// building id (M3-5) but stays a string until gating (M3-6); <c>model</c> and
/// <c>projectile</c> stay unresolved strings until assets and combat exist (M4, docs/03 "Data format").
/// </remarks>
public static partial class DataLoader
{
    /// <summary>Loads <c>common/</c> and every <c>factions/&lt;id&gt;/</c> folder under <paramref name="dataDir"/>.</summary>
    public static DataLoadResult LoadAll(string dataDir)
    {
        var c = new Checker(dataDir);
        if (!Directory.Exists(dataDir))
        {
            c.Error("", $"data directory '{dataDir}' does not exist");
            return new DataLoadResult(null, c.Errors);
        }

        DamageTableJson? tableJson = c.Read("common/damage_table.json", DataJsonContext.Default.DamageTableJson);
        DamageTable? table = tableJson == null ? null : BuildDamageTable(c, tableJson);
        RulesJson? rulesJson = c.Read("common/rules.json", DataJsonContext.Default.RulesJson);
        RulesDef? rules = rulesJson == null ? null : BuildRules(c, rulesJson);
        ResourceFileJson? resourcesJson = c.Read("common/resources.json", DataJsonContext.Default.ResourceFileJson);
        ResourceDef[]? resources = resourcesJson == null ? null : BuildResources(c, resourcesJson);

        string[] folders = Array.Empty<string>();
        string factionsDir = Path.Combine(dataDir, "factions");
        if (Directory.Exists(factionsDir))
        {
            folders = Directory.GetDirectories(factionsDir);
            for (int i = 0; i < folders.Length; i++) folders[i] = Path.GetFileName(folders[i]);
            Array.Sort(folders, StringComparer.Ordinal); // faction id = index in this order
        }
        if (folders.Length == 0)
        {
            c.CurrentFile = "factions/";
            c.Error("", "no faction folders found");
        }

        // Pass 1: read files and collect unit ids; a repeated id is an error at its second definition.
        var factionJsons = new FactionJson?[folders.Length];
        var unitFiles = new UnitFileJson?[folders.Length];
        var firstFile = new Dictionary<string, string>(StringComparer.Ordinal); // load-time only
        var accepted = new List<(int Faction, int Index)>();
        for (int f = 0; f < folders.Length; f++)
        {
            factionJsons[f] = c.Read($"factions/{folders[f]}/faction.json", DataJsonContext.Default.FactionJson);
            UnitFileJson? file = unitFiles[f] = c.Read($"factions/{folders[f]}/units.json", DataJsonContext.Default.UnitFileJson);
            List<UnitJson?>? list = file == null ? null : c.Obj(file.Units, "units");
            for (int i = 0; list != null && i < list.Count; i++)
            {
                string id = c.Id(list[i]?.Id, $"units[{i}].id");
                if (id.Length == 0) continue;
                if (firstFile.TryGetValue(id, out string? first))
                    c.Error($"units[{i}].id", $"duplicate unit id '{id}' (first defined in {first})");
                else
                {
                    firstFile.Add(id, c.CurrentFile);
                    accepted.Add((f, i));
                }
            }
        }

        string[] unitKeys = new string[accepted.Count];
        for (int k = 0; k < accepted.Count; k++)
            unitKeys[k] = unitFiles[accepted[k].Faction]!.Units![accepted[k].Index]!.Id!;
        Array.Sort(unitKeys, StringComparer.Ordinal);

        // Pass 2: validate and convert each accepted unit, in file order so errors come out in file order.
        var units = new UnitDef[unitKeys.Length];
        foreach ((int f, int i) in accepted)
        {
            c.CurrentFile = $"factions/{folders[f]}/units.json";
            UnitJson u = unitFiles[f]!.Units![i]!;
            int id = Array.BinarySearch(unitKeys, u.Id!, StringComparer.Ordinal);
            units[id] = BuildUnit(c, u, $"units[{i}]", id, f, table);
        }

        var factions = new FactionDef[folders.Length];
        for (int f = 0; f < folders.Length; f++)
        {
            if (factionJsons[f] == null) continue;
            c.CurrentFile = $"factions/{folders[f]}/faction.json";
            var own = ImmutableArray.CreateBuilder<int>();
            for (int id = 0; id < units.Length; id++)
                if (units[id] != null && units[id].Faction == f) own.Add(id);
            factions[f] = BuildFaction(c, factionJsons[f]!, folders[f], f, own.ToImmutable());
        }

        int errorsBeforeBuildings = c.Errors.Count;
        BuildingDef[] buildings = BuildBuildings(c, folders, out BuildingFileJson?[] buildingFiles, out List<(int Faction, int Index)> acceptedBuildings);
        // A broken buildings file is reported once, not again through every unit naming one of its buildings.
        bool buildingsClean = c.Errors.Count == errorsBeforeBuildings;
        if (buildingsClean)
            ResolveTrainedAt(c, folders, unitFiles, accepted, unitKeys, units, buildings);

        // M3-5: techs. Cross-file references (a slot's building per faction, effect units and tags, every requires list)
        // are checked only when the files they point into were read whole, so a broken or missing file is one error.
        // Every unit entry accepted (read, a valid id, not a repeat), so the unit id set is complete.
        int unitEntries = 0;
        bool unitFilesOk = true;
        foreach (UnitFileJson? file in unitFiles)
        {
            unitFilesOk &= file?.Units != null;
            unitEntries += file?.Units?.Count ?? 0;
        }
        unitFilesOk &= unitEntries == accepted.Count;
        string[] unitTags = UnitTagsOf(units);
        var techs = new TechReader(c, folders, table, units, unitKeys, unitTags, buildings);
        TechDef[] techDefs = techs.Build(buildingsClean && unitFilesOk);
        int[] ageTechs = Array.Empty<int>();
        if (buildingsClean && unitFilesOk && techs.FilesOk)
        {
            techs.ResolveRequires(buildingFiles, acceptedBuildings, unitFiles, accepted);
            ageTechs = techs.AgeTechs();
        }

        if (c.Errors.Count > 0 || table == null || rules == null || resources == null)
            return new DataLoadResult(null, c.Errors);
        var data = new GameData
        {
            DamageTable = table,
            Rules = rules,
            Resources = ImmutableArray.Create(resources),
            Factions = ImmutableArray.Create(factions),
            Units = ImmutableArray.Create(units),
            Buildings = ImmutableArray.Create(buildings),
            Trains = TrainsPerBuilding(units, buildings.Length),
            Techs = ImmutableArray.Create(techDefs),
            Research = ResearchPerBuilding(techDefs, buildings),
            UnitTags = ImmutableArray.Create(unitTags),
            AgeTechs = ImmutableArray.Create(ageTechs),
        };
        return new DataLoadResult(data, c.Errors);
    }

    /// <summary>Every tag of every unit, ordinal order, de-duplicated (the ids tech effects' <c>tags</c> resolve to).</summary>
    private static string[] UnitTagsOf(UnitDef[] units)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal); // load-time only
        foreach (UnitDef u in units)
            if (u != null) foreach (string t in u.Tags) set.Add(t);
        var tags = new string[set.Count];
        set.CopyTo(tags);
        return tags;
    }

    /// <summary>Per building type, the techs of its slot that are common or of its faction, ascending (techs are in id order).</summary>
    private static ImmutableArray<ImmutableArray<int>> ResearchPerBuilding(TechDef[] techs, BuildingDef[] buildings)
    {
        var result = ImmutableArray.CreateBuilder<ImmutableArray<int>>(buildings.Length);
        foreach (BuildingDef b in buildings)
        {
            var list = ImmutableArray.CreateBuilder<int>();
            foreach (TechDef t in techs)
                if (t.ResearchedAtSlot == b.Slot && (t.Faction < 0 || t.Faction == b.Faction)) list.Add(t.Id);
            result.Add(list.ToImmutable());
        }
        return result.MoveToImmutable();
    }

    /// <summary>
    /// M3-4: each unit's <c>trainedAt</c> must name a building of its own faction; it becomes
    /// <see cref="UnitDef.TrainedAtTypeId"/>. An unknown id and another faction's building are one error each, at the
    /// unit's <c>trainedAt</c> (a missing one was already reported).
    /// </summary>
    private static void ResolveTrainedAt(Checker c, string[] folders, UnitFileJson?[] unitFiles, List<(int Faction, int Index)> accepted,
        string[] unitKeys, UnitDef[] units, BuildingDef[] buildings)
    {
        var keys = new string[buildings.Length];
        for (int b = 0; b < buildings.Length; b++) keys[b] = buildings[b].Key;
        foreach ((int f, int i) in accepted)
        {
            UnitDef u = units[Array.BinarySearch(unitKeys, unitFiles[f]!.Units![i]!.Id!, StringComparer.Ordinal)];
            if (u == null || u.TrainedAt.Length == 0) continue;
            c.CurrentFile = $"factions/{folders[f]}/units.json";
            string path = $"units[{i}].trainedAt";
            int b = Array.BinarySearch(keys, u.TrainedAt, StringComparer.Ordinal);
            if (b < 0)
                c.Error(path, $"unknown building '{u.TrainedAt}' (not in any buildings.json)");
            else if (buildings[b].Faction != u.Faction)
                c.Error(path, $"building '{u.TrainedAt}' belongs to faction '{folders[buildings[b].Faction]}', not '{folders[f]}'");
            else
                u.TrainedAtTypeId = b;
        }
    }

    /// <summary>Per building type, the unit ids that name it in <c>trainedAt</c>, ascending (units are in id order).</summary>
    private static ImmutableArray<ImmutableArray<int>> TrainsPerBuilding(UnitDef[] units, int buildingCount)
    {
        var lists = new ImmutableArray<int>.Builder[buildingCount];
        for (int b = 0; b < buildingCount; b++) lists[b] = ImmutableArray.CreateBuilder<int>();
        for (int id = 0; id < units.Length; id++)
            if ((uint)units[id].TrainedAtTypeId < (uint)buildingCount) lists[units[id].TrainedAtTypeId].Add(id);
        var result = ImmutableArray.CreateBuilder<ImmutableArray<int>>(buildingCount);
        for (int b = 0; b < buildingCount; b++) result.Add(lists[b].ToImmutable());
        return result.MoveToImmutable();
    }

    /// <summary>Converts a duration in seconds to whole ticks at <see cref="SimConstants.TicksPerSecond"/>, rounding to nearest.</summary>
    public static int SecondsToTicks(double seconds) =>
        (int)Math.Round(seconds * SimConstants.TicksPerSecond, MidpointRounding.AwayFromZero);

    /// <summary>Converts a population cost to half-pop units (1 → 2, 1.5 → 3); -1 if it is not a multiple of 0.5.</summary>
    public static int ToHalfPop(double pop)
    {
        double half = pop * 2;
        return half == Math.Floor(half) ? (int)half : -1;
    }

    private static DamageTable? BuildDamageTable(Checker c, DamageTableJson j)
    {
        string[] classKeys = c.Keys(c.Obj(j.ArmorClasses, "armorClasses"), "armorClasses", x => x?.Id, out string[] classNames, x => x?.DisplayName);
        string[] typeKeys = c.Keys(c.Obj(j.DamageTypes, "damageTypes"), "damageTypes", x => x?.Id, out string[] typeNames, x => x?.DisplayName);
        var ignores = new bool[typeKeys.Length];
        var mult = new float[typeKeys.Length * classKeys.Length];
        for (int i = 0; j.DamageTypes != null && i < j.DamageTypes.Count; i++)
        {
            DamageTypeJson? t = j.DamageTypes[i];
            int type = t?.Id == null ? -1 : Array.BinarySearch(typeKeys, t.Id, StringComparer.Ordinal);
            if (t == null || type < 0) continue;
            string p = $"damageTypes[{i}]";
            ignores[type] = t.IgnoresArmor ?? false;
            Dictionary<string, double>? m = c.Obj(t.Multipliers, p + ".multipliers");
            if (m == null) continue;
            foreach (string key in SortedKeys(m))
            {
                if (Array.BinarySearch(classKeys, key, StringComparer.Ordinal) < 0)
                    c.Error($"{p}.multipliers.{key}", $"unknown armor class '{key}'");
            }
            for (int a = 0; a < classKeys.Length; a++)
            {
                if (m.TryGetValue(classKeys[a], out double v))
                    mult[type * classKeys.Length + a] = (float)c.NonNeg(v, $"{p}.multipliers.{classKeys[a]}");
                else
                    c.Error($"{p}.multipliers", $"no multiplier for armor class '{classKeys[a]}'");
            }
        }
        return new DamageTable
        {
            ArmorClassKeys = ImmutableArray.Create(classKeys),
            ArmorClassNames = ImmutableArray.Create(classNames),
            DamageTypeKeys = ImmutableArray.Create(typeKeys),
            DamageTypeNames = ImmutableArray.Create(typeNames),
            IgnoresArmor = ImmutableArray.Create(ignores),
            Multipliers = ImmutableArray.Create(mult),
        };
    }

    private static RulesDef BuildRules(Checker c, RulesJson j)
    {
        RateJson? rate = c.Obj(j.GatherRate, "gatherRate");
        MinesJson? start = c.Obj(j.StartMines, "startMines");
        MinesJson? exp = c.Obj(j.ExpansionMines, "expansionMines");
        RepairJson? repair = c.Obj(j.Repair, "repair");
        return new RulesDef
        {
            StartingGold = c.Int(j.StartingGold, "startingGold", 0),
            StartingWood = c.Int(j.StartingWood, "startingWood", 0),
            StartingWorkers = c.Int(j.StartingWorkers, "startingWorkers", 0),
            HalfPopCap = 2 * c.Int(j.PopCap, "popCap", 1),
            WorkerCarry = c.Int(j.WorkerCarry, "workerCarry", 1),
            GoldPerTick = (float)(c.Pos(rate?.Gold, "gatherRate.gold") / SimConstants.TicksPerSecond),
            WoodPerTick = (float)(c.Pos(rate?.Wood, "gatherRate.wood") / SimConstants.TicksPerSecond),
            StartMineCount = c.Int(start?.Count, "startMines.count", 0),
            StartMineGold = c.Int(start?.Gold, "startMines.gold", 1),
            ExpansionMineCount = c.Int(exp?.Count, "expansionMines.count", 0),
            ExpansionMineGold = c.Int(exp?.Gold, "expansionMines.gold", 1),
            TreeWood = c.Int(j.TreeWood, "treeWood", 1),
            NodeSearchRadius = (float)c.Pos(j.NodeSearchRadius, "nodeSearchRadius"),
            RepairRateFactor = repair == null ? 0f : Factor(c, repair.RateFactor, "repair.rateFactor"),
            RepairCostFactor = repair == null ? 0f : Factor(c, repair.CostFactor, "repair.costFactor"),
        };
    }

    /// <summary>
    /// A fraction from 2^-16 to 1 (a missing <c>repair</c> object is one error, reported by the caller). Repair runs in
    /// 2^16 fixed point (<c>EconomyConstants.RepairFixedOne</c>), where a smaller factor rounds to 0: a repair that
    /// restores or costs nothing (BUG-0092).
    /// </summary>
    private static float Factor(Checker c, double? value, string path)
    {
        double f = c.Pos(value, path);
        if (f > 1)
        {
            c.Error(path, $"{f} is above the maximum 1");
            return 0f;
        }
        if (f > 0 && f < MinFactor)
        {
            c.Error(path, $"{f} is below the minimum 2^-16 ({MinFactor}), which rounds to 0 in repair's fixed point");
            return 0f;
        }
        return (float)f;
    }

    /// <summary>Smallest repair factor: one unit of <c>EconomyConstants.RepairFixedOne</c> (2^16).</summary>
    private const double MinFactor = 1.0 / Economy.EconomyConstants.RepairFixedOne;

    /// <summary>Resource node types, indexed by id in ordinal order of their string ids; a repeated id is an error at its second definition.</summary>
    private static ResourceDef[] BuildResources(Checker c, ResourceFileJson j)
    {
        List<ResourceJson?>? list = c.Obj(j.Resources, "resources");
        int errorsBefore = c.Errors.Count;
        var accepted = new List<int>();
        var keys = new List<string>();
        for (int i = 0; list != null && i < list.Count; i++)
        {
            string id = c.Id(list[i]?.Id, $"resources[{i}].id");
            if (id.Length == 0) continue;
            if (keys.Contains(id))
            {
                c.Error($"resources[{i}].id", $"duplicate resource id '{id}'");
                continue;
            }
            keys.Add(id);
            accepted.Add(i);
        }
        string[] sorted = keys.ToArray();
        Array.Sort(sorted, StringComparer.Ordinal);
        var defs = new ResourceDef[sorted.Length];
        foreach (int i in accepted)
        {
            ResourceJson r = list![i]!;
            string p = $"resources[{i}]";
            int id = Array.BinarySearch(sorted, r.Id!, StringComparer.Ordinal);
            string kindKey = c.Text(r.Resource, p + ".resource");
            int kind = DataLimits.ResourceKindIds.IndexOf(kindKey);
            if (kindKey.Length > 0 && kind < 0)
                c.Error(p + ".resource", $"unknown resource '{kindKey}' (expected one of {string.Join(", ", DataLimits.ResourceKindIds)})");
            FootprintJson? fp = c.Obj(r.Footprint, p + ".footprint");
            // A missing footprint is one error, not three.
            int fw = fp == null ? 0 : c.Side(fp.Width, p + ".footprint.width");
            int fh = fp == null ? 0 : c.Side(fp.Height, p + ".footprint.height");
            // The forest placer grows forests cell by cell (BUG-0074): a tree is one cell. (A bad side already erred.)
            if (kind == (int)ResourceKind.Wood && fw > 0 && fh > 0 && (fw != 1 || fh != 1))
                c.Error(p + ".footprint", $"a wood resource's footprint must be 1 x 1, not {fw} x {fh} (forests are placed cell by cell)");
            defs[id] = new ResourceDef
            {
                Id = id,
                Key = r.Id!,
                DisplayName = c.Text(r.DisplayName, p + ".displayName"),
                Description = c.Text(r.Description, p + ".description"),
                Resource = (ResourceKind)Math.Max(kind, 0),
                FootprintWidth = fw,
                FootprintHeight = fh,
            };
        }
        // One type per kind is required: with none, the placer would silently place nothing of it (BUG-0076). Checked only
        // on an otherwise clean list, so a broken entry is one error, not two.
        for (int k = 0; list != null && c.Errors.Count == errorsBefore && k < DataLimits.ResourceKindIds.Length; k++)
        {
            bool found = false;
            foreach (ResourceDef d in defs) found |= (int)d.Resource == k;
            if (!found) c.Error("resources", $"no '{DataLimits.ResourceKindIds[k]}' resource type (one per kind is required)");
        }
        return defs;
    }

    /// <summary>
    /// Every faction's <c>buildings.json</c> (required, M3-2): ids unique across factions (a repeat is an
    /// error at its second definition), indexed in ordinal order of their string ids.
    /// </summary>
    private static BuildingDef[] BuildBuildings(Checker c, string[] folders, out BuildingFileJson?[] files, out List<(int Faction, int Index)> accepted)
    {
        files = new BuildingFileJson?[folders.Length];
        var firstFile = new Dictionary<string, string>(StringComparer.Ordinal); // load-time only
        accepted = new List<(int Faction, int Index)>();
        for (int f = 0; f < folders.Length; f++)
        {
            BuildingFileJson? file = files[f] = c.Read($"factions/{folders[f]}/buildings.json", DataJsonContext.Default.BuildingFileJson);
            List<BuildingJson?>? list = file == null ? null : c.Obj(file.Buildings, "buildings");
            for (int i = 0; list != null && i < list.Count; i++)
            {
                string id = c.Id(list[i]?.Id, $"buildings[{i}].id");
                if (id.Length == 0) continue;
                if (firstFile.TryGetValue(id, out string? first))
                    c.Error($"buildings[{i}].id", $"duplicate building id '{id}' (first defined in {first})");
                else
                {
                    firstFile.Add(id, c.CurrentFile);
                    accepted.Add((f, i));
                }
            }
        }

        var keys = new string[accepted.Count];
        for (int k = 0; k < accepted.Count; k++)
            keys[k] = files[accepted[k].Faction]!.Buildings![accepted[k].Index]!.Id!;
        Array.Sort(keys, StringComparer.Ordinal);
        var defs = new BuildingDef[keys.Length];
        foreach ((int f, int i) in accepted)
        {
            c.CurrentFile = $"factions/{folders[f]}/buildings.json";
            BuildingJson b = files[f]!.Buildings![i]!;
            string p = $"buildings[{i}]";
            int id = Array.BinarySearch(keys, b.Id!, StringComparer.Ordinal);
            string slotKey = c.Text(b.Slot, p + ".slot");
            int slot = DataLimits.BuildingSlotIds.IndexOf(slotKey);
            if (slotKey.Length > 0 && slot < 0)
                c.Error(p + ".slot", $"unknown slot '{slotKey}' (expected one of {string.Join(", ", DataLimits.BuildingSlotIds)})");
            FootprintJson? fp = c.Obj(b.Footprint, p + ".footprint");
            CostJson? cost = c.Obj(b.Cost, p + ".cost");
            double pop = c.NonNeg(b.PopProvided, p + ".popProvided");
            int halfPop = ToHalfPop(pop);
            if (halfPop < 0) c.Error(p + ".popProvided", $"pop {pop} is not a multiple of 0.5");
            if (b.DropOff == null) c.Error(p + ".dropOff", "missing required field");
            defs[id] = new BuildingDef
            {
                Id = id,
                Key = b.Id!,
                Faction = f,
                Slot = (BuildingSlot)Math.Max(slot, 0),
                DisplayName = c.Text(b.DisplayName, p + ".displayName"),
                Description = c.Text(b.Description, p + ".description"),
                // A missing footprint is one error, not three.
                FootprintWidth = fp == null ? 0 : c.Side(fp.Width, p + ".footprint.width"),
                FootprintHeight = fp == null ? 0 : c.Side(fp.Height, p + ".footprint.height"),
                Hp = c.Int(b.Hp, p + ".hp", 1),
                Armor = c.Int(b.Armor, p + ".armor", 0),
                // A missing cost is one error, like a missing footprint (BUG-0079).
                CostGold = cost == null ? 0 : c.Int(cost.Gold, p + ".cost.gold", 0),
                CostWood = cost == null ? 0 : c.Int(cost.Wood, p + ".cost.wood", 0),
                BuildTicks = c.Ticks(b.BuildTime, p + ".buildTime", 1),
                HalfPopProvided = Math.Max(halfPop, 0),
                DropOff = b.DropOff ?? false,
                Requires = c.Ids(b.Requires, p + ".requires"),
            };
        }
        return defs;
    }

    private static FactionDef BuildFaction(Checker c, FactionJson j, string folder, int id, ImmutableArray<int> units)
    {
        string key = c.Id(j.Id, "id");
        if (key.Length > 0 && key != folder)
            c.Error("id", $"faction id '{key}' does not match its folder name '{folder}'");
        TextJson? bonus = c.Obj(j.Bonus, "bonus");
        ResourceNamesJson? res = c.Obj(j.Resources, "resources");
        PaletteJson? pal = c.Obj(j.Palette, "palette");
        return new FactionDef
        {
            Id = id,
            Key = folder,
            DisplayName = c.Text(j.DisplayName, "displayName"),
            Description = c.Text(j.Description, "description"),
            BonusDisplayName = c.Text(bonus?.DisplayName, "bonus.displayName"),
            BonusDescription = c.Text(bonus?.Description, "bonus.description"),
            GoldName = c.Text(res?.Gold?.DisplayName, "resources.gold.displayName"),
            WoodName = c.Text(res?.Wood?.DisplayName, "resources.wood.displayName"),
            PrimaryColor = c.Color(pal?.Primary, "palette.primary"),
            SecondaryColor = c.Color(pal?.Secondary, "palette.secondary"),
            AccentColor = c.Color(pal?.Accent, "palette.accent"),
            Units = units,
        };
    }

    private static UnitDef BuildUnit(Checker c, UnitJson u, string p, int id, int faction, DamageTable? table)
    {
        string slotKey = c.Text(u.Slot, p + ".slot");
        int slot = DataLimits.SlotIds.IndexOf(slotKey);
        if (slotKey.Length > 0 && slot < 0)
            c.Error(p + ".slot", $"unknown slot '{slotKey}' (expected one of {string.Join(", ", DataLimits.SlotIds)})");

        double radius = c.Pos(u.Radius, p + ".radius");
        if (radius > 0 && (radius <DataLimits.MinUnitRadius || radius > DataLimits.MaxUnitRadius))
            c.Error(p + ".radius", $"radius {radius} is outside {DataLimits.MinUnitRadius}-{DataLimits.MaxUnitRadius} m");

        double pop = c.NonNeg(u.Pop, p + ".pop");
        int halfPop = ToHalfPop(pop);
        if (halfPop < 0) c.Error(p + ".pop", $"pop {pop} is not a multiple of 0.5");

        CostJson? cost = c.Obj(u.Cost, p + ".cost");
        return new UnitDef
        {
            Id = id,
            Key = u.Id!,
            Faction = faction,
            Slot = (UnitSlot)Math.Max(slot, 0),
            DisplayName = c.Text(u.DisplayName, p + ".displayName"),
            Description = c.Text(u.Description, p + ".description"),
            Model = c.Text(u.Model, p + ".model"),
            Hp = c.Int(u.Hp, p + ".hp", 1),
            Armor = c.Int(u.Armor, p + ".armor", 0),
            ArmorClass = c.Ref(table?.ArmorClassKeys, u.ArmorClass, p + ".armorClass", "armor class"),
            Attack = BuildAttack(c, c.Obj(u.Attack, p + ".attack"), p + ".attack", table),
            SpeedPerTick = (float)(c.Pos(u.Speed, p + ".speed") / SimConstants.TicksPerSecond),
            Sight = (float)c.Pos(u.Sight, p + ".sight"),
            Radius = (float)radius,
            CostGold = c.Int(cost?.Gold, p + ".cost.gold", 0),
            CostWood = c.Int(cost?.Wood, p + ".cost.wood", 0),
            HalfPop = Math.Max(halfPop, 0),
            TrainTicks = c.Ticks(u.TrainTime, p + ".trainTime", 1),
            TrainedAt = c.Text(u.TrainedAt, p + ".trainedAt"),
            Requires = c.Ids(u.Requires, p + ".requires"),
            Tags = c.Ids(u.Tags, p + ".tags"),
        };
    }

    private static AttackDef BuildAttack(Checker c, AttackJson? a, string p, DamageTable? table)
    {
        int classCount = table?.ArmorClassCount ?? 0;
        var bonus = new float[classCount];
        Array.Fill(bonus, 1f);
        if (a?.BonusVs != null)
        {
            foreach (string key in SortedKeys(a.BonusVs))
            {
                int ac = c.Ref(table?.ArmorClassKeys, key, $"{p}.bonusVs.{key}", "armor class");
                double v = c.Pos(a.BonusVs[key], $"{p}.bonusVs.{key}");
                if (ac >= 0) bonus[ac] = (float)v;
            }
        }
        return new AttackDef
        {
            Value = c.Int(a?.Value, p + ".value", 0),
            DamageType = c.Ref(table?.DamageTypeKeys, a?.Type, p + ".type", "damage type"),
            CooldownTicks = c.Ticks(a?.Cooldown, p + ".cooldown", 1),
            WindupTicks = c.Ticks(a?.Windup, p + ".windup", 0),
            Range = (float)c.NonNeg(a?.Range, p + ".range"),
            MinRange = (float)c.NonNeg(a?.MinRange ?? 0, p + ".minRange"),
            Splash = (float)c.NonNeg(a?.Splash ?? 0, p + ".splash"),
            FriendlyFire = a?.FriendlyFire ?? false,
            Projectile = a?.Projectile,
            BonusVs = ImmutableArray.Create(bonus),
        };
    }

    // Dictionary enumeration order is unspecified; sort so errors and results never depend on it.
    private static List<string> SortedKeys(Dictionary<string, double> d)
    {
        var keys = new List<string>(d.Keys);
        keys.Sort(StringComparer.Ordinal);
        return keys;
    }

    /// <summary>Error collector plus the field readers that report a missing or out-of-range value and keep going.</summary>
    private sealed class Checker
    {
        private readonly string _dir;

        public Checker(string dir) => _dir = dir;

        public List<DataError> Errors { get; } = new();
        public string CurrentFile { get; set; } = "";

        public void Error(string path, string message) => Errors.Add(new DataError(CurrentFile, path, message));

        public T? Read<T>(string relPath, JsonTypeInfo<T> info) where T : class
        {
            CurrentFile = relPath;
            string full = Path.Combine(_dir, relPath);
            if (!File.Exists(full))
            {
                Error("", "required file is missing");
                return null;
            }
            try
            {
                using FileStream stream = File.OpenRead(full);
                T? value = JsonSerializer.Deserialize(stream, info);
                if (value == null) Error("$", "file holds null instead of an object");
                return value;
            }
            catch (JsonException e)
            {
                string where = e.LineNumber is long line ? $"line {line + 1}, byte {e.BytePositionInLine + 1}" : "unknown position";
                Error(e.Path ?? "$", $"malformed JSON at {where}: {e.Message}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Error("", $"cannot read file: {e.Message}");
            }
            return null;
        }

        public T? Obj<T>(T? value, string path) where T : class
        {
            if (value == null) Error(path, "missing required field");
            return value;
        }

        public string Text(string? value, string path)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Error(path, "missing required text");
                return "";
            }
            return value;
        }

        /// <summary>A required snake_case id; returns "" (after reporting) when absent or malformed.</summary>
        public string Id(string? value, string path)
        {
            string s = Text(value, path);
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                bool ok = (ch >= 'a' && ch <= 'z') || (i > 0 && ((ch >= '0' && ch <= '9') || ch == '_'));
                if (!ok)
                {
                    Error(path, $"id '{s}' is not snake_case");
                    return "";
                }
            }
            return s;
        }

        /// <summary>True for a non-blank snake_case id (what <see cref="Id"/> accepts), reporting nothing.</summary>
        public static bool IsId(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (!((ch >= 'a' && ch <= 'z') || (i > 0 && ((ch >= '0' && ch <= '9') || ch == '_')))) return false;
            }
            return true;
        }

        /// <summary>An optional list of ids (absent means empty); every entry must pass <see cref="Id"/> (BUG-0009).</summary>
        public ImmutableArray<string> Ids(List<string?>? values, string path)
        {
            if (values == null) return ImmutableArray<string>.Empty;
            var ids = ImmutableArray.CreateBuilder<string>(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                string id = Id(values[i], $"{path}[{i}]");
                if (id.Length > 0) ids.Add(id);
            }
            return ids.ToImmutable();
        }

        // The numeric readers return 0 for a rejected value, so a caller's follow-up checks
        // (radius range, half-pop, tick rounding) never report the same field twice, and an
        // out-of-range double never narrows to float Infinity or a wrapped int (BUG-0007).

        public int Int(int? value, string path, int min)
        {
            if (value == null) Error(path, "missing required field");
            else if (value < min) Error(path, $"{value} is below the minimum {min}");
            else if (value > DataLimits.MaxInteger) Error(path, $"{value} is above the maximum {DataLimits.MaxInteger}");
            else return value.Value;
            return 0;
        }

        /// <summary>A footprint side in cells, 1 to <see cref="DataLimits.MaxFootprint"/>.</summary>
        public int Side(int? value, string path)
        {
            if (value == null) Error(path, "missing required field");
            else if (value < 1 || value > DataLimits.MaxFootprint) Error(path, $"{value} is outside 1-{DataLimits.MaxFootprint} cells");
            else return value.Value;
            return 0;
        }

        public double Pos(double? value, string path)
        {
            if (value == null) Error(path, "missing required field");
            else if (!(value > 0)) Error(path, $"{value} must be positive");
            else if (!(value <= DataLimits.MaxDecimal)) Error(path, $"{value} is above the maximum {DataLimits.MaxDecimal}");
            else return value.Value;
            return 0;
        }

        public double NonNeg(double? value, string path)
        {
            if (value == null) Error(path, "missing required field");
            else if (!(value >= 0)) Error(path, $"{value} must not be negative");
            else if (!(value <= DataLimits.MaxDecimal)) Error(path, $"{value} is above the maximum {DataLimits.MaxDecimal}");
            else return value.Value;
            return 0;
        }

        public int Ticks(double? seconds, string path, int minTicks)
        {
            int errorsBefore = Errors.Count;
            double s = minTicks > 0 ? Pos(seconds, path) : NonNeg(seconds, path);
            if (Errors.Count > errorsBefore) return 0;
            if (s > DataLimits.MaxSeconds)
            {
                Error(path, $"{s} s is above the maximum {DataLimits.MaxSeconds} s");
                return 0;
            }
            int ticks = SecondsToTicks(s);
            if (ticks < minTicks) Error(path, $"{s} s rounds to {ticks} ticks");
            return ticks;
        }

        /// <summary>Index of <paramref name="value"/> in a sorted key array; -1 (after reporting) if unknown.</summary>
        public int Ref(ImmutableArray<string>? keys, string? value, string path, string what)
        {
            if (value == null)
            {
                Error(path, "missing required field");
                return -1;
            }
            if (keys == null) return -1; // its table failed to load; that error is already reported
            int i = ImmutableArray.BinarySearch(keys.Value, value, StringComparer.Ordinal);
            if (i < 0) Error(path, $"unknown {what} '{value}' (not in common/damage_table.json)");
            return i;
        }

        public uint Color(string? hex, string path)
        {
            string s = Text(hex, path);
            if (s.Length == 0) return 0;
            if (s.Length == 7 && s[0] == '#' &&
                uint.TryParse(s.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint rgb))
                return rgb;
            Error(path, $"'{s}' is not a #RRGGBB color");
            return 0;
        }

        /// <summary>Sorted, de-duplicated ids of a list of named entries, with display names in the same order.</summary>
        public string[] Keys<T>(List<T?>? items, string path, Func<T?, string?> id, out string[] names, Func<T?, string?> name)
            where T : class
        {
            var pairs = new SortedDictionary<string, string>(StringComparer.Ordinal); // load-time only
            for (int i = 0; items != null && i < items.Count; i++)
            {
                string key = Id(id(items[i]), $"{path}[{i}].id");
                string display = Text(name(items[i]), $"{path}[{i}].displayName");
                if (key.Length == 0) continue;
                if (!pairs.TryAdd(key, display)) Error($"{path}[{i}].id", $"duplicate id '{key}'");
            }
            var keys = new string[pairs.Count];
            names = new string[pairs.Count];
            pairs.Keys.CopyTo(keys, 0);
            pairs.Values.CopyTo(names, 0);
            return keys;
        }
    }
}
