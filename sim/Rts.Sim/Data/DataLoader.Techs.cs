using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Rts.Sim.Data;

public static partial class DataLoader
{
    /// <summary>
    /// M3-5: reads <c>common/techs.json</c> and every <c>factions/&lt;id&gt;/techs.json</c> (all required) into
    /// <see cref="TechDef"/>s, and checks every <c>requires</c> list (techs, buildings, units) against the tech and
    /// building ids. Tech ids are unique across the files (a repeat is an error at its second definition) and ordered
    /// ordinally across them.
    /// </summary>
    private sealed class TechReader
    {
        private const string CommonFile = "common/techs.json";

        private readonly Checker _c;
        private readonly string[] _folders;
        private readonly DamageTable? _table;
        private readonly UnitDef[] _units;
        private readonly string[] _unitKeys;
        private readonly string[] _unitTags;
        private readonly BuildingDef[] _buildings;
        // Index 0 is the common file (faction -1); index f + 1 is faction f's file.
        private readonly TechFileJson?[] _files;
        private readonly List<(int File, int Index)> _accepted = new();
        private string[] _keys = Array.Empty<string>();
        private TechDef[] _defs = Array.Empty<TechDef>();

        public TechReader(Checker c, string[] folders, DamageTable? table, UnitDef[] units, string[] unitKeys, string[] unitTags, BuildingDef[] buildings)
        {
            _c = c;
            _folders = folders;
            _table = table;
            _units = units;
            _unitKeys = unitKeys;
            _unitTags = unitTags;
            _buildings = buildings;
            _files = new TechFileJson?[folders.Length + 1];
        }

        /// <summary>True when every techs file was read and holds a <c>techs</c> list, so the id set is complete.</summary>
        public bool FilesOk { get; private set; }

        private string FileOf(int file) => file == 0 ? CommonFile : $"factions/{_folders[file - 1]}/techs.json";

        /// <summary>Reads and converts every tech; <paramref name="refs"/> false skips the checks into other files (already broken).</summary>
        public TechDef[] Build(bool refs)
        {
            var firstFile = new Dictionary<string, string>(StringComparer.Ordinal); // load-time only
            FilesOk = true;
            for (int file = 0; file < _files.Length; file++)
            {
                TechFileJson? json = _files[file] = _c.Read(FileOf(file), DataJsonContext.Default.TechFileJson);
                List<TechJson?>? list = json == null ? null : _c.Obj(json.Techs, "techs");
                FilesOk &= list != null;
                for (int i = 0; list != null && i < list.Count; i++)
                {
                    string id = _c.Id(list[i]?.Id, $"techs[{i}].id");
                    if (id.Length == 0) continue;
                    if (firstFile.TryGetValue(id, out string? first))
                        _c.Error($"techs[{i}].id", $"duplicate tech id '{id}' (first defined in {first})");
                    else
                    {
                        firstFile.Add(id, _c.CurrentFile);
                        _accepted.Add((file, i));
                    }
                }
            }

            _keys = new string[_accepted.Count];
            for (int k = 0; k < _accepted.Count; k++)
                _keys[k] = _files[_accepted[k].File]!.Techs![_accepted[k].Index]!.Id!;
            Array.Sort(_keys, StringComparer.Ordinal);

            _defs = new TechDef[_keys.Length];
            foreach ((int file, int i) in _accepted)
            {
                _c.CurrentFile = FileOf(file);
                TechJson t = _files[file]!.Techs![i]!;
                int id = Array.BinarySearch(_keys, t.Id!, StringComparer.Ordinal);
                _defs[id] = BuildTech(t, $"techs[{i}]", id, file - 1, refs);
            }
            return _defs;
        }

        private TechDef BuildTech(TechJson t, string p, int id, int faction, bool refs)
        {
            string slotKey = _c.Text(t.ResearchedAt, p + ".researchedAt");
            int slot = DataLimits.BuildingSlotIds.IndexOf(slotKey);
            if (slotKey.Length > 0 && slot < 0)
                _c.Error(p + ".researchedAt", $"unknown slot '{slotKey}' (expected one of {string.Join(", ", DataLimits.BuildingSlotIds)})");
            else if (slot >= 0 && refs)
            {
                // A common tech is researched at every faction's building of the slot, a faction's at its own.
                for (int f = 0; f < _folders.Length; f++)
                {
                    if (faction >= 0 && f != faction) continue;
                    bool found = false;
                    foreach (BuildingDef b in _buildings) found |= b.Faction == f && (int)b.Slot == slot;
                    if (!found) _c.Error(p + ".researchedAt", $"faction '{_folders[f]}' has no '{slotKey}' building to research it at");
                }
            }
            CostJson? cost = _c.Obj(t.Cost, p + ".cost");
            List<TechEffectJson?>? effects = _c.Obj(t.Effects, p + ".effects");
            var built = ImmutableArray.CreateBuilder<TechEffect>(effects?.Count ?? 0);
            for (int j = 0; effects != null && j < effects.Count; j++)
            {
                TechEffectJson? e = _c.Obj(effects[j], $"{p}.effects[{j}]");
                if (e != null) built.Add(BuildEffect(e, $"{p}.effects[{j}]", faction, refs));
            }
            return new TechDef
            {
                Id = id,
                Key = t.Id!,
                Faction = faction,
                DisplayName = _c.Text(t.DisplayName, p + ".displayName"),
                Description = _c.Text(t.Description, p + ".description"),
                ResearchedAtSlot = (BuildingSlot)Math.Max(slot, 0),
                // A missing cost is one error, like a building's.
                CostGold = cost == null ? 0 : _c.Int(cost.Gold, p + ".cost.gold", 0),
                CostWood = cost == null ? 0 : _c.Int(cost.Wood, p + ".cost.wood", 0),
                ResearchTicks = _c.Ticks(t.ResearchTime, p + ".researchTime", 1),
                Requires = _c.Ids(t.Requires, p + ".requires"),
                Effects = built.ToImmutable(),
            };
        }

        private TechEffect BuildEffect(TechEffectJson e, string p, int faction, bool refs)
        {
            string statKey = _c.Text(e.Stat, p + ".stat");
            int stat = DataLimits.TechStatIds.IndexOf(statKey);
            if (statKey.Length > 0 && stat < 0)
                _c.Error(p + ".stat", $"unknown stat '{statKey}' (expected one of {string.Join(", ", DataLimits.TechStatIds)})");
            float amount = Amount(e.Amount, p + ".amount", stat);
            AppliesToJson? a = _c.Obj(e.AppliesTo, p + ".appliesTo");
            string ap = p + ".appliesTo";
            return new TechEffect
            {
                Stat = (TechStat)Math.Max(stat, 0),
                Amount = amount,
                AttackType = a?.AttackType == null ? -1 : _c.Ref(_table?.DamageTypeKeys, a.AttackType, ap + ".attackType", "damage type"),
                Tags = a == null ? ImmutableArray<int>.Empty : Resolve(a.Tags, ap + ".tags", refs, TagOf),
                Units = a == null ? ImmutableArray<int>.Empty : Resolve(a.Units, ap + ".units", refs, key => UnitOf(key, faction)),
                Siege = a?.Siege == null ? -1 : a.Siege.Value ? 1 : 0,
            };
        }

        /// <summary>
        /// A non-zero amount in sim units: whole points for attack / armor / hp, meters for range, seconds to ticks for
        /// ability cooldown (negative shortens it). 0 (after reporting) when missing or out of bounds.
        /// </summary>
        private float Amount(double? value, string path, int stat)
        {
            if (value == null)
            {
                _c.Error(path, "missing required field");
                return 0f;
            }
            double v = value.Value;
            if (v == 0 || !double.IsFinite(v))
            {
                _c.Error(path, $"{v} must be a non-zero number");
                return 0f;
            }
            bool whole = stat == (int)TechStat.Attack || stat == (int)TechStat.Armor || stat == (int)TechStat.Hp;
            if (whole)
            {
                if (Math.Abs(v) > DataLimits.MaxInteger) _c.Error(path, $"{v} is beyond +/-{DataLimits.MaxInteger}");
                else if (v != Math.Floor(v)) _c.Error(path, $"{v} is not a whole number ({DataLimits.TechStatIds[stat]} changes by whole points)");
                else return (float)v;
                return 0f;
            }
            if (stat == (int)TechStat.AbilityCooldown)
            {
                if (Math.Abs(v) > DataLimits.MaxSeconds) _c.Error(path, $"{v} s is beyond +/-{DataLimits.MaxSeconds} s");
                else if (SecondsToTicks(v) == 0) _c.Error(path, $"{v} s rounds to 0 ticks");
                else return SecondsToTicks(v);
                return 0f;
            }
            // Range in meters (an unknown stat was already reported; its amount is only bounded).
            if (Math.Abs(v) > DataLimits.MaxDecimal) _c.Error(path, $"{v} is beyond +/-{DataLimits.MaxDecimal}");
            else return (float)v;
            return 0f;
        }

        private (int Id, string? Error) TagOf(string tag)
        {
            int i = Array.BinarySearch(_unitTags, tag, StringComparer.Ordinal);
            return (i, i < 0 ? $"unknown tag '{tag}' (no unit carries it)" : null);
        }

        private (int Id, string? Error) UnitOf(string key, int faction)
        {
            int i = Array.BinarySearch(_unitKeys, key, StringComparer.Ordinal);
            if (i < 0) return (-1, $"unknown unit '{key}' (not in any units.json)");
            // A faction's upgrade changes its own units only.
            if (faction >= 0 && _units[i].Faction != faction)
                return (-1, $"unit '{key}' belongs to faction '{_folders[_units[i].Faction]}', not '{_folders[faction]}'");
            return (i, null);
        }

        /// <summary>
        /// An optional list of ids resolved to ints (ascending, de-duplicated). Each entry must be a snake_case id; with
        /// <paramref name="refs"/> each must also resolve (one error each, at the entry).
        /// </summary>
        private ImmutableArray<int> Resolve(List<string?>? values, string path, bool refs, Func<string, (int Id, string? Error)> resolve)
        {
            if (values == null) return ImmutableArray<int>.Empty;
            var set = new SortedSet<int>(); // load-time only
            for (int i = 0; i < values.Count; i++)
            {
                string id = _c.Id(values[i], $"{path}[{i}]");
                if (id.Length == 0 || !refs) continue;
                (int r, string? error) = resolve(id);
                if (error != null) _c.Error($"{path}[{i}]", error);
                else set.Add(r);
            }
            var result = ImmutableArray.CreateBuilder<int>(set.Count);
            foreach (int r in set) result.Add(r);
            return result.MoveToImmutable();
        }

        /// <summary>
        /// Every <c>requires</c> entry of every tech, building and unit must name a tech or a building id (one error each,
        /// at the entry). Entries that are not snake_case ids were already reported. Whether the requirement is met is M3-6.
        /// </summary>
        public void ResolveRequires(BuildingFileJson?[] buildingFiles, List<(int Faction, int Index)> buildings,
            UnitFileJson?[] unitFiles, List<(int Faction, int Index)> units)
        {
            var buildingKeys = new string[_buildings.Length];
            for (int b = 0; b < _buildings.Length; b++) buildingKeys[b] = _buildings[b].Key;
            foreach ((int file, int i) in _accepted)
            {
                _c.CurrentFile = FileOf(file);
                Check(_files[file]!.Techs![i]!.Requires, $"techs[{i}].requires", buildingKeys);
            }
            foreach ((int f, int i) in buildings)
            {
                _c.CurrentFile = $"factions/{_folders[f]}/buildings.json";
                Check(buildingFiles[f]!.Buildings![i]!.Requires, $"buildings[{i}].requires", buildingKeys);
            }
            foreach ((int f, int i) in units)
            {
                _c.CurrentFile = $"factions/{_folders[f]}/units.json";
                Check(unitFiles[f]!.Units![i]!.Requires, $"units[{i}].requires", buildingKeys);
            }
        }

        private void Check(List<string?>? requires, string path, string[] buildingKeys)
        {
            for (int j = 0; requires != null && j < requires.Count; j++)
            {
                string? id = requires[j];
                if (!Checker.IsId(id)) continue;
                if (Array.BinarySearch(_keys, id!, StringComparer.Ordinal) < 0 && Array.BinarySearch(buildingKeys, id!, StringComparer.Ordinal) < 0)
                    _c.Error($"{path}[{j}]", $"unknown tech or building '{id}' (requires names a tech or building id)");
            }
        }

        /// <summary>The tech ids of <see cref="DataLimits.AgeTechIds"/>, in order; each must be a common tech (one error each otherwise).</summary>
        public int[] AgeTechs()
        {
            var ids = new int[DataLimits.AgeTechIds.Length];
            _c.CurrentFile = CommonFile;
            for (int k = 0; k < ids.Length; k++)
            {
                string key = DataLimits.AgeTechIds[k];
                ids[k] = Array.BinarySearch(_keys, key, StringComparer.Ordinal);
                if (ids[k] < 0 || _defs[ids[k]].Faction >= 0)
                    _c.Error("techs", $"no common tech '{key}' (every age after the first is researched through one)");
            }
            return ids;
        }
    }
}
