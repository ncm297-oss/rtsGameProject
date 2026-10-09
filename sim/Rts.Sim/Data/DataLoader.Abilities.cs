using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Rts.Sim.Data;

// M4-4a: common/statuses.json, factions/<id>/abilities.json, and each unit's abilities list (docs/03 "Data format").
public static partial class DataLoader
{
    /// <summary>
    /// <c>common/statuses.json</c> (M4-4a): ids unique (a repeat is an error at its second definition), indexed in ordinal
    /// order; <c>kind</c> one of <see cref="DataLimits.StatusKindIds"/>; <c>damageType</c> required on a damage-over-time
    /// status and refused on any other kind.
    /// </summary>
    private static StatusDef[] BuildStatuses(Checker c, StatusFileJson j, DamageTable? table)
    {
        List<StatusJson?>? list = c.Obj(j.Statuses, "statuses");
        int[] accepted = AcceptIds(c, list, "statuses", x => x?.Id, out string[] sorted);
        var defs = new StatusDef[sorted.Length];
        foreach (int i in accepted)
        {
            StatusJson x = list![i]!;
            string p = $"statuses[{i}]";
            int id = Array.BinarySearch(sorted, x.Id!, StringComparer.Ordinal);
            int kind = KindOf(c, x.Kind, p + ".kind", DataLimits.StatusKindIds, ImmutableArray<string>.Empty);
            int damageType = -1;
            if (kind == (int)StatusKind.DamageOverTime)
                damageType = c.Ref(table?.DamageTypeKeys, x.DamageType, p + ".damageType", "damage type");
            else if (kind >= 0 && x.DamageType != null)
                c.Error(p + ".damageType", $"status '{x.Id}': only a damageOverTime status has a damage type");
            defs[id] = new StatusDef
            {
                Id = id,
                Key = x.Id!,
                DisplayName = c.Text(x.DisplayName, p + ".displayName"),
                Description = c.Text(x.Description, p + ".description"),
                Kind = (StatusKind)Math.Max(kind, 0),
                DamageType = damageType,
            };
        }
        return defs;
    }

    /// <summary>
    /// Every faction's <c>abilities.json</c> (M4-4a; optional: a faction without one has no abilities). Ids unique across
    /// factions (a repeat is an error at its second definition), indexed in ordinal order of their string ids. When
    /// <paramref name="statuses"/> is null (its file failed) <c>applyStatus</c> ids aren't checked: that file's error says why.
    /// </summary>
    private static AbilityDef[] BuildAbilities(Checker c, string[] folders, DamageTable? table, StatusDef[]? statuses)
    {
        var files = new AbilityFileJson?[folders.Length];
        var firstFile = new Dictionary<string, string>(StringComparer.Ordinal); // load-time only
        var accepted = new List<(int Faction, int Index)>();
        for (int f = 0; f < folders.Length; f++)
        {
            string rel = $"factions/{folders[f]}/abilities.json";
            if (!c.Exists(rel)) continue;
            AbilityFileJson? file = files[f] = c.Read(rel, DataJsonContext.Default.AbilityFileJson);
            List<AbilityJson?>? list = file == null ? null : c.Obj(file.Abilities, "abilities");
            for (int i = 0; list != null && i < list.Count; i++)
            {
                string id = c.Id(list[i]?.Id, $"abilities[{i}].id");
                if (id.Length == 0) continue;
                if (firstFile.TryGetValue(id, out string? first))
                    c.Error($"abilities[{i}].id", $"duplicate ability id '{id}' (first defined in {first})");
                else
                {
                    firstFile.Add(id, c.CurrentFile);
                    accepted.Add((f, i));
                }
            }
        }
        string[] keys = new string[accepted.Count];
        for (int k = 0; k < accepted.Count; k++) keys[k] = files[accepted[k].Faction]!.Abilities![accepted[k].Index]!.Id!;
        Array.Sort(keys, StringComparer.Ordinal);
        string[]? statusKeys = null;
        if (statuses != null)
        {
            statusKeys = new string[statuses.Length];
            for (int s = 0; s < statuses.Length; s++) statusKeys[s] = statuses[s].Key;
        }
        var defs = new AbilityDef[keys.Length];
        foreach ((int f, int i) in accepted)
        {
            c.CurrentFile = $"factions/{folders[f]}/abilities.json";
            AbilityJson a = files[f]!.Abilities![i]!;
            int id = Array.BinarySearch(keys, a.Id!, StringComparer.Ordinal);
            defs[id] = BuildAbility(c, a, $"abilities[{i}]", id, f, table, statuses, statusKeys);
        }
        return defs;
    }

    private static AbilityDef BuildAbility(Checker c, AbilityJson a, string p, int id, int faction, DamageTable? table, StatusDef[]? statuses, string[]? statusKeys)
    {
        int kind = KindOf(c, a.Kind, p + ".kind", DataLimits.AbilityKindIds, DataLimits.PlannedAbilityKindIds);
        double range = c.Pos(a.Range, p + ".range");
        if (range > DataLimits.MaxSight)
        {
            c.Error(p + ".range", $"ability '{a.Id}': range {range} m is above the maximum {DataLimits.MaxSight} m (the largest sight)");
            range = 0;
        }
        double radius = c.Pos(a.Radius, p + ".radius");
        if (radius > DataLimits.MaxAbilityRadius)
        {
            c.Error(p + ".radius", $"ability '{a.Id}': radius {radius} m is above the maximum {DataLimits.MaxAbilityRadius} m");
            radius = 0;
        }
        if (a.Autocast == true) c.Error(p + ".autocast", $"ability '{a.Id}': autocast is not supported yet");
        int affects = KindOf(c, a.Affects, p + ".affects", DataLimits.AbilityAffectsIds, ImmutableArray<string>.Empty);
        List<AbilityEffectJson?>? list = c.Obj(a.Effects, p + ".effects");
        if (list != null && list.Count == 0) c.Error(p + ".effects", $"ability '{a.Id}' has no effects");
        var effects = ImmutableArray.CreateBuilder<AbilityEffect>(list?.Count ?? 0);
        for (int e = 0; list != null && e < list.Count; e++)
        {
            string q = $"{p}.effects[{e}]";
            AbilityEffectJson? x = c.Obj(list[e], q);
            if (x != null) effects.Add(BuildEffect(c, x, q, table, statuses, statusKeys));
        }
        return new AbilityDef
        {
            Id = id,
            Key = a.Id!,
            Faction = faction,
            DisplayName = c.Text(a.DisplayName, p + ".displayName"),
            Description = c.Text(a.Description, p + ".description"),
            Kind = (AbilityKind)Math.Max(kind, 0),
            Range = (float)range,
            Radius = (float)radius,
            CastTicks = c.Ticks(a.CastTime, p + ".castTime", 0),
            CooldownTicks = c.Ticks(a.Cooldown, p + ".cooldown", 1),
            DurationTicks = a.Duration == null ? 0 : c.Ticks(a.Duration, p + ".duration", 0),
            Affects = (AbilityAffects)Math.Max(affects, 0),
            Effects = effects.ToImmutable(),
        };
    }

    /// <summary>
    /// One effect (M4-4a): <c>damage {type, amount}</c> (amount a whole number, at least 1) or <c>applyStatus {status,
    /// magnitude, duration}</c> (a damage-over-time status takes a whole magnitude of at least 1, damage per second; a slow
    /// one a fraction above 0 and below 1; duration seconds, at least a tick). A field the kind doesn't use is an error.
    /// </summary>
    private static AbilityEffect BuildEffect(Checker c, AbilityEffectJson x, string q, DamageTable? table, StatusDef[]? statuses, string[]? statusKeys)
    {
        int kind = KindOf(c, x.Kind, q + ".kind", DataLimits.AbilityEffectKindIds, DataLimits.PlannedAbilityEffectKindIds);
        if (kind < 0) return new AbilityEffect { Kind = AbilityEffectKind.Damage, DamageType = -1, Status = -1 };
        string name = DataLimits.AbilityEffectKindIds[kind];
        if (kind == (int)AbilityEffectKind.Damage)
        {
            if (x.Status != null) Unused(c, q + ".status", name);
            if (x.Magnitude != null) Unused(c, q + ".magnitude", name);
            if (x.Duration != null) Unused(c, q + ".duration", name);
            return new AbilityEffect
            {
                Kind = AbilityEffectKind.Damage,
                DamageType = c.Ref(table?.DamageTypeKeys, x.Type, q + ".type", "damage type"),
                Amount = c.Int(x.Amount, q + ".amount", 1),
                Status = -1,
            };
        }
        if (x.Type != null) Unused(c, q + ".type", name);
        if (x.Amount != null) Unused(c, q + ".amount", name);
        int status = -1;
        if (x.Status == null) c.Error(q + ".status", "missing required field");
        else if (statusKeys != null)
        {
            status = Array.BinarySearch(statusKeys, x.Status, StringComparer.Ordinal);
            if (status < 0)
            {
                c.Error(q + ".status", $"unknown status '{x.Status}' (not in common/statuses.json)");
                status = -1;
            }
        }
        double magnitude = c.Pos(x.Magnitude, q + ".magnitude");
        if (status >= 0 && magnitude > 0)
        {
            StatusKind sk = statuses![status].Kind;
            if (sk == StatusKind.DamageOverTime && magnitude != Math.Floor(magnitude))
            {
                c.Error(q + ".magnitude", $"{magnitude} is not a whole number (damage per second of '{x.Status}')");
                magnitude = 0;
            }
            else if (sk == StatusKind.Slow && !(magnitude < 1))
            {
                c.Error(q + ".magnitude", $"{magnitude} must be below 1 (the fraction of speed '{x.Status}' takes away)");
                magnitude = 0;
            }
        }
        return new AbilityEffect
        {
            Kind = AbilityEffectKind.ApplyStatus,
            DamageType = -1,
            Status = status,
            Magnitude = (float)magnitude,
            DurationTicks = c.Ticks(x.Duration, q + ".duration", 1),
        };
    }

    private static void Unused(Checker c, string path, string kind) => c.Error(path, $"a '{kind}' effect has no such field");

    /// <summary>
    /// A required spelling from <paramref name="ids"/> (its index), or -1 after one error: "not supported yet" for a
    /// spelling in <paramref name="planned"/> (a docs/02 kind the sim doesn't run yet), else unknown.
    /// </summary>
    private static int KindOf(Checker c, string? value, string path, ImmutableArray<string> ids, ImmutableArray<string> planned)
    {
        string key = c.Text(value, path);
        if (key.Length == 0) return -1;
        int k = ids.IndexOf(key);
        if (k >= 0) return k;
        if (planned.IndexOf(key) >= 0)
            c.Error(path, $"'{key}' is not supported yet (supported: {string.Join(", ", ids)})");
        else
            c.Error(path, $"unknown value '{key}' (expected one of {string.Join(", ", ids)})");
        return -1;
    }

    /// <summary>The entries of <paramref name="list"/> with a valid id, a repeat being an error at its second definition; <paramref name="sorted"/> their ids in ordinal order.</summary>
    private static int[] AcceptIds<T>(Checker c, List<T?>? list, string path, Func<T?, string?> idOf, out string[] sorted) where T : class
    {
        var accepted = new List<int>();
        var keys = new List<string>();
        for (int i = 0; list != null && i < list.Count; i++)
        {
            string id = c.Id(idOf(list[i]), $"{path}[{i}].id");
            if (id.Length == 0) continue;
            if (keys.Contains(id))
            {
                c.Error($"{path}[{i}].id", $"duplicate id '{id}'");
                continue;
            }
            keys.Add(id);
            accepted.Add(i);
        }
        sorted = keys.ToArray();
        Array.Sort(sorted, StringComparer.Ordinal);
        return accepted.ToArray();
    }

    /// <summary>
    /// M4-4a: each unit's <c>abilities</c> list becomes <see cref="UnitDef.Abilities"/>, in file order. One error at the
    /// entry for an unknown id, another faction's ability, or a repeat; one at the list for more than
    /// <see cref="DataLimits.MaxUnitAbilities"/>.
    /// </summary>
    private static void ResolveUnitAbilities(Checker c, string[] folders, UnitFileJson?[] unitFiles, List<(int Faction, int Index)> accepted,
        string[] unitKeys, UnitDef[] units, AbilityDef[] abilities)
    {
        var keys = new string[abilities.Length];
        for (int k = 0; k < abilities.Length; k++) keys[k] = abilities[k].Key;
        foreach ((int f, int i) in accepted)
        {
            UnitJson json = unitFiles[f]!.Units![i]!;
            if (json.Abilities == null) continue;
            UnitDef u = units[Array.BinarySearch(unitKeys, json.Id!, StringComparer.Ordinal)];
            if (u == null) continue;
            c.CurrentFile = $"factions/{folders[f]}/units.json";
            string p = $"units[{i}].abilities";
            if (json.Abilities.Count > DataLimits.MaxUnitAbilities)
                c.Error(p, $"{u.Key}: {json.Abilities.Count} abilities, above the maximum {DataLimits.MaxUnitAbilities}");
            var ids = ImmutableArray.CreateBuilder<int>(json.Abilities.Count);
            for (int k = 0; k < json.Abilities.Count; k++)
            {
                string q = $"{p}[{k}]";
                string key = c.Id(json.Abilities[k], q);
                if (key.Length == 0) continue;
                int a = Array.BinarySearch(keys, key, StringComparer.Ordinal);
                if (a < 0)
                    c.Error(q, $"unknown ability '{key}' (not in any abilities.json)");
                else if (abilities[a].Faction != u.Faction)
                    c.Error(q, $"ability '{key}' belongs to faction '{folders[abilities[a].Faction]}', not '{folders[f]}'");
                else if (ids.Contains(a))
                    c.Error(q, $"ability '{key}' is listed twice");
                else
                    ids.Add(a);
            }
            u.Abilities = ids.ToImmutable();
        }
    }
}
