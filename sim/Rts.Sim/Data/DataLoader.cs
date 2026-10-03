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
/// <c>trainedAt</c>, <c>requires</c>, <c>model</c> and <c>projectile</c> stay unresolved strings until
/// buildings, techs and assets exist (M3/M4, docs/03 "Data format").
/// </remarks>
public static class DataLoader
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

        if (c.Errors.Count > 0 || table == null || rules == null)
            return new DataLoadResult(null, c.Errors);
        var data = new GameData
        {
            DamageTable = table,
            Rules = rules,
            Factions = ImmutableArray.Create(factions),
            Units = ImmutableArray.Create(units),
        };
        return new DataLoadResult(data, c.Errors);
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
        };
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
            Requires = ImmutableArray.CreateRange(u.Requires ?? new List<string>()),
            Tags = ImmutableArray.CreateRange(u.Tags ?? new List<string>()),
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

        public int Int(int? value, string path, int min)
        {
            if (value == null) Error(path, "missing required field");
            else if (value < min) Error(path, $"{value} is below the minimum {min}");
            return value ?? 0;
        }

        public double Pos(double? value, string path)
        {
            if (value == null) Error(path, "missing required field");
            else if (!(value > 0)) Error(path, $"{value} must be positive");
            return value ?? 0;
        }

        public double NonNeg(double? value, string path)
        {
            if (value == null) Error(path, "missing required field");
            else if (!(value >= 0)) Error(path, $"{value} must not be negative");
            return value ?? 0;
        }

        public int Ticks(double? seconds, string path, int minTicks)
        {
            double s = minTicks > 0 ? Pos(seconds, path) : NonNeg(seconds, path);
            int ticks = SecondsToTicks(s);
            if (seconds > 0 && ticks < minTicks) Error(path, $"{s} s rounds to {ticks} ticks");
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
