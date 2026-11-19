using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;

namespace Rts.Sim.Data;

// M4-4b-3a: each unit's passives list (docs/03 "Implementation (M4-4b-3a)", "For the data track (M4-4b-3a)").
public static partial class DataLoader
{
    /// <summary>
    /// Each unit's <c>passives</c> becomes <see cref="UnitDef.Passives"/> (file order) with its per-trigger indices. At most
    /// <see cref="DataLimits.MaxUnitPassives"/>, one per trigger. <c>whenBelowHp {hpFraction, status, magnitude}</c>:
    /// <c>hpFraction</c> above 0 and below 1. <c>onDeath {status, magnitude, duration, radius, units}</c>: <c>duration</c>
    /// seconds (at least a tick; whole seconds for damage over time), <c>radius</c> above 0 and at most
    /// <see cref="DataLimits.MaxPassiveRadius"/>, <c>units</c> at least one own-faction unit id, no repeats. <c>status</c> and
    /// <c>magnitude</c> as an <c>applyStatus</c> effect's. M7-2: <c>onHit {status, magnitude, duration}</c>, an
    /// <c>applyStatus</c> entry's rules. A field the trigger doesn't use is an error. M7-4: a <c>whenBelowHp</c> passive may
    /// not name a status with an <c>onEnd</c> (one error at its <c>status</c>). M8-3: <c>nearbyOwn {units, radius, damageBonus,
    /// maxStacks}</c>: <c>units</c> as <c>onDeath</c>'s, <c>radius</c> above 0 and at most the same bound, <c>damageBonus</c>
    /// above 0 and at most <see cref="DataLimits.MaxPassiveDamageBonus"/>, <c>maxStacks</c> a whole number 1 to
    /// <see cref="DataLimits.MaxPassiveStacks"/>; <c>damageBonus</c> and <c>maxStacks</c> are errors on the other triggers. When
    /// <paramref name="statuses"/> is null (its file failed) status ids aren't checked: that file's error says why.
    /// </summary>
    private static void ResolveUnitPassives(Checker c, string[] folders, UnitFileJson?[] unitFiles, List<(int Faction, int Index)> accepted,
        KeyIndex unitKeys, UnitDef[] units, StatusDef[]? statuses)
    {
        string[]? statusKeys = null;
        if (statuses != null)
        {
            statusKeys = new string[statuses.Length];
            for (int s = 0; s < statuses.Length; s++) statusKeys[s] = statuses[s].Key;
        }
        foreach ((int f, int i) in accepted)
        {
            UnitJson json = unitFiles[f]!.Units![i]!;
            if (json.Passives == null) continue;
            UnitDef u = units[unitKeys.IdOf(json.Id!)];
            if (u == null) continue;
            c.CurrentFile = $"factions/{folders[f]}/units.json";
            string p = $"units[{i}].passives";
            if (json.Passives.Count > DataLimits.MaxUnitPassives)
                c.Error(p, $"{u.Key}: {json.Passives.Count} passives, above the maximum {DataLimits.MaxUnitPassives}");
            var list = ImmutableArray.CreateBuilder<UnitPassive>(json.Passives.Count);
            int belowHp = -1, death = -1, hit = -1, near = -1;
            for (int j = 0; j < json.Passives.Count; j++)
            {
                string q = $"{p}[{j}]";
                PassiveJson? x = c.Obj(json.Passives[j], q);
                if (x == null) continue;
                int trigger = KindOf(c, x.Trigger, q + ".trigger", DataLimits.PassiveTriggerIds, ImmutableArray<string>.Empty);
                if (trigger < 0) continue;
                string name = DataLimits.PassiveTriggerIds[trigger];
                UnitPassive passive;
                // M8-3: the pack's two fields belong to nearbyOwn alone.
                if (trigger != (int)PassiveTrigger.NearbyOwn)
                {
                    if (x.DamageBonus != null) UnusedPassive(c, q + ".damageBonus", name);
                    if (x.MaxStacks != null) UnusedPassive(c, q + ".maxStacks", name);
                }
                if (trigger == (int)PassiveTrigger.WhenBelowHp)
                {
                    if (x.Duration != null) UnusedPassive(c, q + ".duration", name);
                    if (x.Radius != null) UnusedPassive(c, q + ".radius", name);
                    if (x.Units != null) UnusedPassive(c, q + ".units", name);
                    double fraction = 0;
                    if (x.HpFraction == null) c.Error(q + ".hpFraction", "missing required field");
                    else if (!(x.HpFraction.Value > 0 && x.HpFraction.Value < 1))
                        c.Error(q + ".hpFraction", $"{x.HpFraction.Value} is outside 0-1, exclusive (the fraction of the unit's maximum hit points)");
                    else fraction = x.HpFraction.Value;
                    StatusAndMagnitude(c, x.Status, x.Magnitude, q, statuses, statusKeys, out int status, out float magnitude);
                    // M7-4: a held entry is refreshed every phase 5 and never runs out, so its onEnd could never fire.
                    if (status >= 0 && statuses![status].OnEndStatus >= 0)
                    {
                        c.Error(q + ".status", $"{u.Key}: status '{x.Status}' has an onEnd, and a whenBelowHp passive holds its entry (it never ends while held)");
                        status = -1;
                    }
                    passive =new UnitPassive { Trigger = PassiveTrigger.WhenBelowHp, HpFraction = (float)fraction, Status = status, Magnitude = magnitude, Units = ImmutableArray<int>.Empty };
                }
                else if (trigger == (int)PassiveTrigger.OnHit)
                {
                    // M7-2: the whenBelowHp shape plus a duration (an applyStatus entry's rules); no aura fields.
                    if (x.HpFraction != null) UnusedPassive(c, q + ".hpFraction", name);
                    if (x.Radius != null) UnusedPassive(c, q + ".radius", name);
                    if (x.Units != null) UnusedPassive(c, q + ".units", name);
                    StatusEntry(c, x.Status, x.Magnitude, x.Duration, q, statuses, statusKeys, out int status, out float magnitude, out int duration);
                    passive = new UnitPassive { Trigger = PassiveTrigger.OnHit, Status = status, Magnitude = magnitude, DurationTicks = duration, Units = ImmutableArray<int>.Empty };
                }
                else if (trigger == (int)PassiveTrigger.NearbyOwn)
                {
                    // M8-3: a damage factor, not a status: no status fields; the onDeath passive's units and radius rules.
                    if (x.HpFraction != null) UnusedPassive(c, q + ".hpFraction", name);
                    if (x.Status != null) UnusedPassive(c, q + ".status", name);
                    if (x.Magnitude != null) UnusedPassive(c, q + ".magnitude", name);
                    if (x.Duration != null) UnusedPassive(c, q + ".duration", name);
                    passive = new UnitPassive
                    {
                        Trigger = PassiveTrigger.NearbyOwn,
                        Status = -1, // no status: the field's "none"
                        Radius = PassiveRadius(c, x.Radius, q + ".radius", u.Key),
                        Units = PassiveUnits(c, x.Units, q + ".units", folders, f, unitKeys, units, name, u.Key),
                        DamageBonus = PackBonus(c, x.DamageBonus, q + ".damageBonus", u.Key),
                        MaxStacks = PackStacks(c, x.MaxStacks, q + ".maxStacks", u.Key),
                    };
                }
                else
                {
                    if (x.HpFraction != null) UnusedPassive(c, q + ".hpFraction", name);
                    StatusEntry(c, x.Status, x.Magnitude, x.Duration, q, statuses, statusKeys, out int status, out float magnitude, out int duration);
                    passive = new UnitPassive
                    {
                        Trigger = PassiveTrigger.OnDeath,
                        Status = status,
                        Magnitude = magnitude,
                        DurationTicks = duration,
                        Radius = PassiveRadius(c, x.Radius, q + ".radius", u.Key),
                        Units = PassiveUnits(c, x.Units, q + ".units", folders, f, unitKeys, units, name, u.Key),
                    };
                }
                ref int seen = ref trigger == (int)PassiveTrigger.WhenBelowHp ? ref belowHp
                    : ref trigger == (int)PassiveTrigger.OnDeath ? ref death
                    : ref trigger == (int)PassiveTrigger.OnHit ? ref hit : ref near;
                if (seen >= 0)
                {
                    c.Error(q + ".trigger", $"{u.Key} already has a '{name}' passive (passives[{seen}]); a unit has at most one per trigger");
                    continue;
                }
                seen = list.Count;
                list.Add(passive);
            }
            u.Passives = list.ToImmutable();
            u.BelowHpPassive = belowHp;
            u.DeathPassive = death;
            u.HitPassive = hit;
            u.NearbyPassive = near;
        }
    }

    /// <summary>An <c>onDeath</c> or (M8-3) <c>nearbyOwn</c> passive's <c>radius</c>: required, above 0, at most <see cref="DataLimits.MaxPassiveRadius"/>; 0 after an error.</summary>
    private static float PassiveRadius(Checker c, double? value, string path, string unit)
    {
        if (value is not double radius)
        {
            c.Error(path, $"{unit}: missing required field");
            return 0f;
        }
        if (!(radius > 0))
        {
            c.Error(path, $"{unit}: radius {radius.ToString(CultureInfo.InvariantCulture)} m must be positive");
            return 0f;
        }
        if (radius > DataLimits.MaxPassiveRadius)
        {
            c.Error(path, $"{unit}: radius {radius.ToString(CultureInfo.InvariantCulture)} m is above the maximum {DataLimits.MaxPassiveRadius} m");
            return 0f;
        }
        return (float)radius;
    }

    /// <summary>M8-3: a <c>nearbyOwn</c> passive's <c>damageBonus</c>: required, above 0, at most <see cref="DataLimits.MaxPassiveDamageBonus"/>; 0 after an error.</summary>
    private static float PackBonus(Checker c, double? value, string path, string unit)
    {
        if (value is not double b)
        {
            c.Error(path, $"{unit}: missing required field");
            return 0f;
        }
        if (!(b > 0))
        {
            c.Error(path, $"{unit}: damageBonus {b.ToString(CultureInfo.InvariantCulture)} must be above 0");
            return 0f;
        }
        if (b > DataLimits.MaxPassiveDamageBonus)
        {
            c.Error(path, $"{unit}: damageBonus {b.ToString(CultureInfo.InvariantCulture)} is above the maximum {DataLimits.MaxPassiveDamageBonus}");
            return 0f;
        }
        return (float)b;
    }

    /// <summary>M8-3: a <c>nearbyOwn</c> passive's <c>maxStacks</c>: required, a whole number 1 to <see cref="DataLimits.MaxPassiveStacks"/>; 0 after an error.</summary>
    private static int PackStacks(Checker c, double? value, string path, string unit)
    {
        if (value is not double n)
        {
            c.Error(path, $"{unit}: missing required field");
            return 0;
        }
        if (!(n >= 1 && n <= DataLimits.MaxPassiveStacks && n == Math.Floor(n)))
        {
            c.Error(path, $"{unit}: maxStacks {n.ToString(CultureInfo.InvariantCulture)} is not a whole number 1-{DataLimits.MaxPassiveStacks}");
            return 0;
        }
        return (int)n;
    }

    /// <summary>An <c>onDeath</c> or (M8-3) <c>nearbyOwn</c> passive's <c>units</c> (at <paramref name="path"/>): required, at least one, each an own-faction unit id, no repeats; file order.</summary>
    private static ImmutableArray<int> PassiveUnits(Checker c, List<string?>? list, string path, string[] folders, int faction, KeyIndex unitKeys, UnitDef[] units, string trigger, string unit)
    {
        if (c.Obj(list, path) == null) return ImmutableArray<int>.Empty;
        if (list!.Count == 0) c.Error(path, $"{unit}: an {trigger} passive needs at least one unit type");
        var ids = ImmutableArray.CreateBuilder<int>(list.Count);
        for (int k = 0; k < list.Count; k++)
        {
            string q = $"{path}[{k}]";
            string key = c.Id(list[k], q);
            if (key.Length == 0) continue;
            int id = unitKeys.IdOf(key);
            if (id < 0 || units[id] == null)
                c.Error(q, $"{unit}: unknown unit '{key}' (not in any units.json)");
            else if (units[id].Faction != faction)
                c.Error(q, $"{unit}: unit '{key}' belongs to faction '{folders[units[id].Faction]}', not '{folders[faction]}'");
            else if (ids.Contains(id))
                c.Error(q, $"{unit}: unit '{key}' is listed twice");
            else
                ids.Add(id);
        }
        return ids.ToImmutable();
    }

    private static void UnusedPassive(Checker c, string path, string trigger) => c.Error(path, $"a passive with trigger '{trigger}' has no such field");
}
