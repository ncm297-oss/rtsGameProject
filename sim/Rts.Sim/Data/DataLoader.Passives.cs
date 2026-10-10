using System;
using System.Collections.Generic;
using System.Collections.Immutable;

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
    /// <c>magnitude</c> as an <c>applyStatus</c> effect's. A field the trigger doesn't use is an error. When
    /// <paramref name="statuses"/> is null (its file failed) status ids aren't checked: that file's error says why.
    /// </summary>
    private static void ResolveUnitPassives(Checker c, string[] folders, UnitFileJson?[] unitFiles, List<(int Faction, int Index)> accepted,
        string[] unitKeys, UnitDef[] units, StatusDef[]? statuses)
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
            UnitDef u = units[Array.BinarySearch(unitKeys, json.Id!, StringComparer.Ordinal)];
            if (u == null) continue;
            c.CurrentFile = $"factions/{folders[f]}/units.json";
            string p = $"units[{i}].passives";
            if (json.Passives.Count > DataLimits.MaxUnitPassives)
                c.Error(p, $"{u.Key}: {json.Passives.Count} passives, above the maximum {DataLimits.MaxUnitPassives}");
            var list = ImmutableArray.CreateBuilder<UnitPassive>(json.Passives.Count);
            int belowHp = -1, death = -1;
            for (int j = 0; j < json.Passives.Count; j++)
            {
                string q = $"{p}[{j}]";
                PassiveJson? x = c.Obj(json.Passives[j], q);
                if (x == null) continue;
                int trigger = KindOf(c, x.Trigger, q + ".trigger", DataLimits.PassiveTriggerIds, ImmutableArray<string>.Empty);
                if (trigger < 0) continue;
                string name = DataLimits.PassiveTriggerIds[trigger];
                UnitPassive passive;
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
                    passive = new UnitPassive { Trigger = PassiveTrigger.WhenBelowHp, HpFraction = (float)fraction, Status = status, Magnitude = magnitude, Units = ImmutableArray<int>.Empty };
                }
                else
                {
                    if (x.HpFraction != null) UnusedPassive(c, q + ".hpFraction", name);
                    StatusEntry(c, x.Status, x.Magnitude, x.Duration, q, statuses, statusKeys, out int status, out float magnitude, out int duration);
                    double radius = c.Pos(x.Radius, q + ".radius");
                    if (radius > DataLimits.MaxPassiveRadius)
                    {
                        c.Error(q + ".radius", $"{u.Key}: radius {radius} m is above the maximum {DataLimits.MaxPassiveRadius} m");
                        radius = 0;
                    }
                    passive = new UnitPassive
                    {
                        Trigger = PassiveTrigger.OnDeath,
                        Status = status,
                        Magnitude = magnitude,
                        DurationTicks = duration,
                        Radius = (float)radius,
                        Units = PassiveUnits(c, x.Units, q + ".units", folders, f, unitKeys, units),
                    };
                }
                ref int seen = ref trigger == (int)PassiveTrigger.WhenBelowHp ? ref belowHp : ref death;
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
        }
    }

    /// <summary>An <c>onDeath</c> passive's <c>units</c> (at <paramref name="path"/>): required, at least one, each an own-faction unit id, no repeats; file order.</summary>
    private static ImmutableArray<int> PassiveUnits(Checker c, List<string?>? list, string path, string[] folders, int faction, string[] unitKeys, UnitDef[] units)
    {
        if (c.Obj(list, path) == null) return ImmutableArray<int>.Empty;
        if (list!.Count == 0) c.Error(path, "an onDeath passive needs at least one unit type");
        var ids = ImmutableArray.CreateBuilder<int>(list.Count);
        for (int k = 0; k < list.Count; k++)
        {
            string q = $"{path}[{k}]";
            string key = c.Id(list[k], q);
            if (key.Length == 0) continue;
            int id = Array.BinarySearch(unitKeys, key, StringComparer.Ordinal);
            if (id < 0 || units[id] == null)
                c.Error(q, $"unknown unit '{key}' (not in any units.json)");
            else if (units[id].Faction != faction)
                c.Error(q, $"unit '{key}' belongs to faction '{folders[units[id].Faction]}', not '{folders[faction]}'");
            else if (ids.Contains(id))
                c.Error(q, $"unit '{key}' is listed twice");
            else
                ids.Add(id);
        }
        return ids.ToImmutable();
    }

    private static void UnusedPassive(Checker c, string path, string trigger) => c.Error(path, $"a passive with trigger '{trigger}' has no such field");
}
