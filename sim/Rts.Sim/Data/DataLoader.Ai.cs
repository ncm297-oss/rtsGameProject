using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Rts.Sim.Data;

public static partial class DataLoader
{
    /// <summary>
    /// M5-1: reads every faction's <c>factions/&lt;id&gt;/ai.json</c> (required) into an <see cref="AiDef"/> per faction, in
    /// faction id order. Every difficulty of <see cref="DataLimits.AiDifficultyIds"/> must be there; build-order steps and
    /// compositions name ids of the faction's own (or, for a tech, a common one). <paramref name="refs"/> false skips the id
    /// checks (a units, buildings or techs file is already broken, and reported once). Every other error at once.
    /// </summary>
    private static AiDef[] BuildAi(Checker c, string[] folders, bool refs, string[] unitKeys, UnitDef[] units, BuildingDef[] buildings, TechDef[] techs)
    {
        var buildingKeys = new string[buildings.Length];
        for (int b = 0; b < buildings.Length; b++) buildingKeys[b] = buildings[b].Key;
        var techKeys = new string[techs.Length];
        for (int t = 0; t < techs.Length; t++) techKeys[t] = techs[t].Key;
        var ids = new AiIds(unitKeys, units, buildingKeys, buildings, techKeys, techs, folders);

        var result = new AiDef[folders.Length];
        for (int f = 0; f < folders.Length; f++)
        {
            AiFileJson? json = c.Read($"factions/{folders[f]}/ai.json", DataJsonContext.Default.AiFileJson);
            if (json == null) continue;
            result[f] = BuildAiDef(c, json, f, refs ? ids : null);
        }
        return result;
    }

    /// <summary>The id tables a faction's <c>ai.json</c> resolves against (load time only).</summary>
    private sealed record AiIds(string[] UnitKeys, UnitDef[] Units, string[] BuildingKeys, BuildingDef[] Buildings, string[] TechKeys,
        TechDef[] Techs, string[] Folders);

    private static AiDef BuildAiDef(Checker c, AiFileJson j, int faction, AiIds? ids)
    {
        var difficulties = ImmutableArray.CreateBuilder<AiDifficultyDef>(DataLimits.AiDifficultyIds.Length);
        AiDifficultiesJson? d = c.Obj(j.Difficulties, "difficulties");
        for (int k = 0; k < DataLimits.AiDifficultyIds.Length; k++)
        {
            AiDifficultyJson? one = d == null ? null : (AiDifficulty)k switch
            {
                AiDifficulty.Easy => d.Easy,
                AiDifficulty.Normal => d.Normal,
                _ => d.Hard,
            };
            string p = "difficulties." + DataLimits.AiDifficultyIds[k];
            if (d != null) c.Obj(one, p);
            difficulties.Add(one == null ? default : BuildAiDifficulty(c, one, p));
        }

        AiAttackJson? attack = c.Obj(j.Attack, "attack");
        AiRetreatJson? retreat = c.Obj(j.Retreat, "retreat");
        return new AiDef
        {
            Faction = faction,
            Difficulties = difficulties.MoveToImmutable(),
            BuildOrder = BuildAiSteps(c, j.BuildOrder, faction, ids),
            Compositions = BuildAiCompositions(c, j.Compositions, faction, ids),
            AttackMinHalfPop = attack == null ? 0 : HalfPopField(c, attack.MinArmyPop, "attack.minArmyPop"),
            AttackMinCost = attack == null ? 0 : c.Int(attack.MinArmyCost, "attack.minArmyCost", 0),
            RetreatHpFraction = retreat == null ? 0f : (float)Fraction(c, retreat.HpFraction, "retreat.hpFraction"),
            RetreatLossFraction = retreat == null ? 0f : (float)Fraction(c, retreat.LossFraction, "retreat.lossFraction"),
        };
    }

    private static AiDifficultyDef BuildAiDifficulty(Checker c, AiDifficultyJson j, string p)
    {
        double wave = c.Pos(j.WaveSizeMultiplier, p + ".waveSizeMultiplier");
        if (wave > DataLimits.MaxAiWaveSizeMultiplier)
        {
            c.Error(p + ".waveSizeMultiplier", $"{wave} is above the maximum {DataLimits.MaxAiWaveSizeMultiplier}");
            wave = 0;
        }
        return new AiDifficultyDef
        {
            ThinkTicks = c.Ticks(j.ThinkInterval, p + ".thinkInterval", 1),
            BuildOrderSlackTicks = c.Ticks(j.BuildOrderSlack, p + ".buildOrderSlack", 0),
            WaveSizeMultiplier = (float)wave,
            GatherBonus = (float)Fraction(c, j.GatherBonus, p + ".gatherBonus"),
            FocusFire = Flag(c, j.FocusFire, p + ".focusFire"),
            PullBackWounded = Flag(c, j.PullBackWounded, p + ".pullBackWounded"),
            UseAbilities = Flag(c, j.UseAbilities, p + ".useAbilities"),
            ExpandAfterTicks = c.Ticks(j.ExpandAfter, p + ".expandAfter", 0),
        };
    }

    private static ImmutableArray<AiStep> BuildAiSteps(Checker c, List<AiStepJson?>? list, int faction, AiIds? ids)
    {
        if (c.Obj(list, "buildOrder") == null) return ImmutableArray<AiStep>.Empty;
        if (list!.Count == 0)
        {
            c.Error("buildOrder", "the build order has no steps (at least one)");
            return ImmutableArray<AiStep>.Empty;
        }
        if (list.Count > DataLimits.MaxAiBuildOrderSteps)
        {
            // One error, not one per step past the limit: a runaway file is reported once.
            c.Error("buildOrder", $"{list.Count} steps is above the maximum {DataLimits.MaxAiBuildOrderSteps}");
            return ImmutableArray<AiStep>.Empty;
        }
        var steps = ImmutableArray.CreateBuilder<AiStep>(list.Count);
        var first = new Dictionary<(AiStepTrigger, int, AiStepAction, string), int>(); // load-time only
        for (int i = 0; i < list.Count; i++)
        {
            string p = $"buildOrder[{i}]";
            AiStepJson? s = c.Obj(list[i], p);
            if (s == null) continue;
            int errors = c.Errors.Count;
            (AiStepTrigger trigger, int at) = AiAt(c, s.At, p + ".at");
            AiStepAction action = AiAction(c, s.Do, p + ".do", out bool actionOk);
            int target = -1;
            string key = "";
            int count = 1;
            if (actionOk)
            {
                if (action == AiStepAction.Expand)
                {
                    if (s.Id != null) c.Error(p + ".id", "an expand step names no id");
                }
                else
                {
                    key = c.Id(s.Id, p + ".id");
                    if (key.Length > 0 && ids != null) target = AiTarget(c, ids, action, key, faction, p + ".id");
                }
                if (action == AiStepAction.Research)
                {
                    if (s.Count != null) c.Error(p + ".count", "a research step takes no count (a tech is researched once)");
                }
                else if (s.Count != null)
                {
                    if (s.Count < 1 || s.Count > DataLimits.MaxAiStepCount)
                        c.Error(p + ".count", $"{s.Count} is outside 1-{DataLimits.MaxAiStepCount}");
                    else count = s.Count.Value;
                }
            }
            if (c.Errors.Count != errors) continue;
            if (first.TryGetValue((trigger, at, action, key), out int earlier))
            {
                c.Error(p, $"the same step as buildOrder[{earlier}] (same at, do and id): merge them into one count");
                continue;
            }
            first.Add((trigger, at, action, key), i);
            steps.Add(new AiStep { Trigger = trigger, At = at, Action = action, Target = target, TargetKey = key, Count = count });
        }
        return steps.ToImmutable();
    }

    /// <summary>A step's <c>at</c>: exactly one of <c>time</c> (s, to ticks), <c>workers</c> (a count) or <c>pop</c> (to half-pop).</summary>
    private static (AiStepTrigger Trigger, int At) AiAt(Checker c, AiAtJson? at, string p)
    {
        if (c.Obj(at, p) == null) return (AiStepTrigger.Time, 0);
        int set = (at!.Time != null ? 1 : 0) + (at.Workers != null ? 1 : 0) + (at.Pop != null ? 1 : 0);
        if (set != 1)
        {
            c.Error(p, $"set exactly one of {string.Join(", ", DataLimits.AiStepTriggerIds)} ({set} set)");
            return (AiStepTrigger.Time, 0);
        }
        if (at.Time != null) return (AiStepTrigger.Time, c.Ticks(at.Time, p + ".time", 0));
        if (at.Workers != null) return (AiStepTrigger.Workers, c.Int(at.Workers, p + ".workers", 0));
        return (AiStepTrigger.Pop, HalfPopField(c, at.Pop, p + ".pop"));
    }

    private static AiStepAction AiAction(Checker c, string? value, string p, out bool ok)
    {
        ok = false;
        string s = c.Text(value, p);
        if (s.Length == 0) return AiStepAction.Build;
        int k = DataLimits.AiStepActionIds.IndexOf(s);
        if (k < 0)
        {
            c.Error(p, $"unknown action '{s}' (expected one of {string.Join(", ", DataLimits.AiStepActionIds)})");
            return AiStepAction.Build;
        }
        ok = true;
        return (AiStepAction)k;
    }

    /// <summary>The type id a step's <c>id</c> names for <paramref name="action"/>: a building or unit of the faction, or a common or own tech; -1 after one error.</summary>
    private static int AiTarget(Checker c, AiIds ids, AiStepAction action, string key, int faction, string p)
    {
        string own = ids.Folders[faction];
        switch (action)
        {
            case AiStepAction.Build:
            {
                int b = Array.BinarySearch(ids.BuildingKeys, key, StringComparer.Ordinal);
                if (b < 0) c.Error(p, $"unknown building '{key}' (not in any buildings.json)");
                else if (ids.Buildings[b].Faction != faction)
                    c.Error(p, $"building '{key}' belongs to faction '{ids.Folders[ids.Buildings[b].Faction]}', not '{own}'");
                else return b;
                return -1;
            }
            case AiStepAction.Train:
                return AiUnit(c, ids, key, faction, p);
            default:
            {
                int t = Array.BinarySearch(ids.TechKeys, key, StringComparer.Ordinal);
                if (t < 0) c.Error(p, $"unknown tech '{key}' (not in any techs.json)");
                else if (ids.Techs[t].Faction >= 0 && ids.Techs[t].Faction != faction)
                    c.Error(p, $"tech '{key}' belongs to faction '{ids.Folders[ids.Techs[t].Faction]}', not '{own}'");
                else return t;
                return -1;
            }
        }
    }

    private static int AiUnit(Checker c, AiIds ids, string key, int faction, string p)
    {
        int u = Array.BinarySearch(ids.UnitKeys, key, StringComparer.Ordinal);
        if (u < 0) c.Error(p, $"unknown unit '{key}' (not in any units.json)");
        else if (ids.Units[u].Faction != faction)
            c.Error(p, $"unit '{key}' belongs to faction '{ids.Folders[ids.Units[u].Faction]}', not '{ids.Folders[faction]}'");
        else return u;
        return -1;
    }

    /// <summary>One composition per age (Age I, then one per <see cref="DataLimits.AgeTechIds"/> entry), each a non-empty map of own unit ids to weights above 0.</summary>
    private static ImmutableArray<AiComposition> BuildAiCompositions(Checker c, List<AiCompositionJson?>? list, int faction, AiIds? ids)
    {
        int ages = 1 + DataLimits.AgeTechIds.Length;
        var byAge = new AiComposition?[ages];
        bool badAge = false; // an entry's age already reported: a missing age then is that same mistake, not a second one
        if (c.Obj(list, "compositions") == null) return ImmutableArray<AiComposition>.Empty;
        for (int i = 0; i < list!.Count; i++)
        {
            string p = $"compositions[{i}]";
            AiCompositionJson? e = c.Obj(list[i], p);
            if (e == null) continue;
            int age = c.Int(e.Age, p + ".age", 1);
            if (e.Age != null && age > ages)
            {
                c.Error(p + ".age", $"{age} is above the last age, {ages}");
                age = 0;
            }
            badAge |= age < 1;
            Dictionary<string, double>? map = c.Obj(e.Units, p + ".units");
            if (map != null && map.Count == 0) c.Error(p + ".units", "no units (at least one)");
            var unitIds = new List<int>(map?.Count ?? 0);
            var weights = new List<float>(map?.Count ?? 0);
            foreach (string key in map == null ? new List<string>() : SortedKeys(map))
            {
                string kp = $"{p}.units.{key}";
                double w = c.Pos(map![key], kp);
                if (!Checker.IsId(key))
                {
                    c.Error(kp, $"id '{key}' is not snake_case");
                    continue;
                }
                // BUG-0541 item 1 / BUG-0573 item 2: a weight the float can't hold (1e-50) would divide by 0 in the production manager.
                if (w > 0 && (float)w == 0f)
                {
                    c.Error(kp, $"weight {w} rounds to 0");
                    continue;
                }
                int u = ids == null ? -1 : AiUnit(c, ids, key, faction, kp);
                if (u < 0 || w <= 0) continue;
                unitIds.Add(u);
                weights.Add((float)w);
            }
            if (age < 1) continue;
            if (byAge[age - 1] != null)
            {
                c.Error(p + ".age", $"a second composition for Age {age} (one per age)");
                badAge = true;
                continue;
            }
            // SortedKeys gives ordinal key order, which is unit id order: the ids come out ascending.
            byAge[age - 1] = new AiComposition { Age = age, Units = ImmutableArray.CreateRange(unitIds), Weights = ImmutableArray.CreateRange(weights) };
        }
        var result = ImmutableArray.CreateBuilder<AiComposition>(ages);
        for (int a = 0; a < ages; a++)
        {
            if (byAge[a] == null && !badAge) c.Error("compositions", $"no composition for Age {a + 1} (one per age, 1-{ages})");
            result.Add(byAge[a] ?? new AiComposition { Age = a + 1, Units = ImmutableArray<int>.Empty, Weights = ImmutableArray<float>.Empty });
        }
        return result.MoveToImmutable();
    }

    /// <summary>True when no error so far is in a file whose path ends with one of <paramref name="suffixes"/>.</summary>
    private static bool NoErrorIn(Checker c, params string[] suffixes)
    {
        foreach (DataError e in c.Errors)
            foreach (string suffix in suffixes)
                if (e.File.EndsWith(suffix, StringComparison.Ordinal)) return false;
        return true;
    }

    /// <summary>
    /// M5-2: <c>rules.json</c> <c>ai</c> (required): <c>goldShare</c> a fraction 0-1; <c>houseWhenPopWithin</c> pop, 0 or more, a
    /// multiple of 0.5, at most the pop cap; <c>placementSearchRadius</c> and <c>expandHallReach</c> m, above 0, at most
    /// <see cref="DataLimits.MaxAiSearchRadius"/>; <c>buildingSpacing</c> m, 0 to <see cref="DataLimits.MaxAiSpacing"/>. Meters go
    /// to whole cells, rounded up. M5-3: <c>scoutAfter</c> and <c>waveCooldown</c> s (0-3,600, to ticks); <c>scoutRadius</c> and
    /// <c>rallyDistance</c> m (1-64), <c>scoutReach</c> and <c>rallyReach</c> m (1-16), <c>scoutGrid</c> m (16-256, BUG-0593), kept in meters. A refused <c>popCap</c> is reported once: the cap rule is skipped then.
    /// </summary>
    private static AiRulesDef BuildAiRules(Checker c, AiRulesJson? j, RulesDef? rules)
    {
        j = c.Obj(j, "ai");
        if (j == null) return new AiRulesDef();
        double within = c.NonNeg(j.HouseWhenPopWithin, "ai.houseWhenPopWithin");
        int halfPop = 0;
        if (within * 2 != Math.Floor(within * 2)) c.Error("ai.houseWhenPopWithin", $"{within} is not a multiple of 0.5");
        else if (rules != null && rules.HalfPopCap > 0 && within * 2 > rules.HalfPopCap) c.Error("ai.houseWhenPopWithin", $"{within} is above the population cap {rules.HalfPopCap / 2}");
        else halfPop = (int)(within * 2);
        return new AiRulesDef
        {
            GoldShare = (float)Fraction(c, j.GoldShare, "ai.goldShare"),
            HouseHalfPopMargin = halfPop,
            PlacementSearchCells = Cells(Distance(c, j.PlacementSearchRadius, "ai.placementSearchRadius", DataLimits.MaxAiSearchRadius, positive: true)),
            SpacingCells = Cells(Distance(c, j.BuildingSpacing, "ai.buildingSpacing", DataLimits.MaxAiSpacing, positive: false)),
            ExpandReachCells = Cells(Distance(c, j.ExpandHallReach, "ai.expandHallReach", DataLimits.MaxAiSearchRadius, positive: true)),
            ScoutAfterTicks = c.Ticks(j.ScoutAfter, "ai.scoutAfter", 0),
            ScoutRadius = Meters(c, j.ScoutRadius, "ai.scoutRadius", DataLimits.MaxAiSearchRadius),
            ScoutReach = Meters(c, j.ScoutReach, "ai.scoutReach", DataLimits.MaxAiReach),
            ScoutGrid = Meters(c, j.ScoutGrid, "ai.scoutGrid", DataLimits.MaxAiScoutGrid, DataLimits.MinAiScoutGrid),
            RallyDistance = Meters(c, j.RallyDistance, "ai.rallyDistance", DataLimits.MaxAiSearchRadius),
            RallyReach = Meters(c, j.RallyReach, "ai.rallyReach", DataLimits.MaxAiReach),
            WaveCooldownTicks = c.Ticks(j.WaveCooldown, "ai.waveCooldown", 0),
        };
    }

    /// <summary>M5-3: a required distance in meters, <paramref name="min"/> (1 by default) to <paramref name="max"/> inclusive; 0 when refused.</summary>
    private static float Meters(Checker c, double? value, string path, double max, double min = 1)
    {
        if (value == null) c.Error(path, "missing required field");
        else if (!(value >= min && value <= max)) c.Error(path, $"{value} m is outside {min}-{max} m");
        else return (float)value.Value;
        return 0f;
    }

    /// <summary>A required distance in meters: above 0 (or 0 or more), at most <paramref name="max"/>; 0 when refused.</summary>
    private static double Distance(Checker c, double? value, string path, double max, bool positive)
    {
        double d = positive ? c.Pos(value, path) : c.NonNeg(value, path);
        if (d > max)
        {
            c.Error(path, $"{d} m is above the maximum {max} m");
            return 0;
        }
        return d;
    }

    /// <summary>Meters to whole map cells, rounded up.</summary>
    private static int Cells(double meters) => (int)Math.Ceiling(meters / Map.MapConstants.CellSize);

    /// <summary>A required fraction, 0 to 1 inclusive.</summary>
    private static double Fraction(Checker c, double? value, string path)
    {
        if (value == null) c.Error(path, "missing required field");
        else if (!(value >= 0 && value <= 1)) c.Error(path, $"{value} is outside 0-1");
        else return value.Value;
        return 0;
    }

    /// <summary>A required true / false.</summary>
    private static bool Flag(Checker c, bool? value, string path)
    {
        if (value == null) c.Error(path, "missing required field");
        return value ?? false;
    }

    /// <summary>A required population, 0 or more and a multiple of 0.5, in half-pop.</summary>
    private static int HalfPopField(Checker c, double? value, string path)
    {
        int errors = c.Errors.Count;
        double pop = c.NonNeg(value, path);
        if (c.Errors.Count != errors) return 0;
        int half = ToHalfPop(pop);
        if (half < 0) c.Error(path, $"{pop} is not a multiple of 0.5");
        return Math.Max(half, 0);
    }
}
