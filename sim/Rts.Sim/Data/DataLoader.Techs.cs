using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Rts.Sim.Data;

public static partial class DataLoader
{
    /// <summary>
    /// M3-5: reads <c>common/techs.json</c> and every <c>factions/&lt;id&gt;/techs.json</c> (all required) into
    /// <see cref="TechDef"/>s. M3-6: resolves every <c>requires</c> list (techs, buildings, units) to tech and building
    /// type ids and every <c>requiresAnyOf</c> to building slots, and rejects a tech id that is also a building id and a
    /// <c>requires</c> cycle. Tech ids are unique across the files (a repeat is an error at its second definition) and
    /// ordered ordinally across them.
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
        private readonly string[] _buildingKeys;
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
            _buildingKeys = new string[buildings.Length];
            for (int b = 0; b < buildings.Length; b++) _buildingKeys[b] = buildings[b].Key;
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
            (ImmutableArray<string> anyOf, int anyCount, ImmutableArray<int> anySlots) = AnyOf(t.RequiresAnyOf, p + ".requiresAnyOf", faction, refs);
            List<TechEffectJson?>? effects = _c.Obj(t.Effects, p + ".effects");
            var built = ImmutableArray.CreateBuilder<TechEffect>(effects?.Count ?? 0);
            for (int j = 0; effects != null && j < effects.Count; j++)
            {
                TechEffectJson? e = _c.Obj(effects[j], $"{p}.effects[{j}]");
                if (e == null) continue;
                int errors = _c.Errors.Count;
                TechEffect effect = BuildEffect(e, $"{p}.effects[{j}]", faction, refs);
                built.Add(effect);
                // BUG-0099: filters that no unit meets (a typo, most likely) would make a tech that changes nothing.
                if (refs && _c.Errors.Count == errors && !MatchesAnyUnit(effect, faction))
                    _c.Error($"{p}.effects[{j}].appliesTo", faction < 0
                        ? "no unit of any faction matches every filter set here, so the effect would change nothing"
                        : $"no unit of faction '{_folders[faction]}' matches every filter set here, so the effect would change nothing");
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
                RequiresAnyOf = anyOf,
                RequiresAnyOfCount = anyCount,
                RequiresAnyOfSlots = anySlots,
                Effects = built.ToImmutable(),
            };
        }

        /// <summary>
        /// M3-6: the optional <c>requiresAnyOf {count, of}</c>. Each <c>of</c> entry is a building slot id, or (in a
        /// faction's own techs file) one of its building ids, which counts as that building's slot; a slot may be named
        /// once. <c>count</c> is 1 to the number of entries. One error at the field that breaks a rule; the building-id
        /// check needs the buildings files read whole (<paramref name="refs"/>).
        /// </summary>
        private (ImmutableArray<string> Of, int Count, ImmutableArray<int> Slots) AnyOf(RequiresAnyOfJson? j, string p, int faction, bool refs)
        {
            if (j == null) return (ImmutableArray<string>.Empty, 0, ImmutableArray<int>.Empty);
            List<string?>? of = _c.Obj(j.Of, p + ".of");
            int entries = of?.Count ?? 0, count = 0;
            if (j.Count == null) _c.Error(p + ".count", "missing required field");
            else if (of != null && (j.Count < 1 || j.Count > entries))
                _c.Error(p + ".count", $"{j.Count} is outside 1-{entries} (the number of 'of' entries)");
            else count = j.Count.Value;
            var ids = ImmutableArray.CreateBuilder<string>(entries);
            var slots = new SortedSet<int>(); // load-time only
            for (int i = 0; i < entries; i++)
            {
                string path = $"{p}.of[{i}]";
                string id = _c.Id(of![i], path);
                if (id.Length == 0) continue;
                ids.Add(id);
                int slot = DataLimits.BuildingSlotIds.IndexOf(id);
                if (slot < 0)
                {
                    if (!refs) continue; // the buildings files are broken; that is already reported
                    int b = Array.BinarySearch(_buildingKeys, id, StringComparer.Ordinal);
                    if (b < 0 || faction < 0 || _buildings[b].Faction != faction)
                    {
                        _c.Error(path, faction < 0
                            ? $"'{id}' is not a building slot id (a common tech names slots: {string.Join(", ", DataLimits.BuildingSlotIds)})"
                            : $"'{id}' is neither a building slot id nor a building of faction '{_folders[faction]}'");
                        continue;
                    }
                    slot = (int)_buildings[b].Slot;
                }
                if (!slots.Add(slot))
                    _c.Error(path, $"'{id}' names slot '{DataLimits.BuildingSlotIds[slot]}' again (each slot counts once)");
            }
            return (ids.ToImmutable(), count, ToArray(slots));
        }

        /// <summary>True if some unit (of <paramref name="faction"/>, or of any faction for a common tech, -1) meets every filter <paramref name="e"/> sets.</summary>
        private bool MatchesAnyUnit(in TechEffect e, int faction)
        {
            foreach (UnitDef u in _units)
                if ((faction < 0 || u.Faction == faction) && Economy.TechState.Matches(e, u, _unitTags)) return true;
            return false;
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
            // BUG-0098: a filter set to [] matches no unit; it must not read as "no filter" (every unit of every faction).
            if (a?.Tags is { Count: 0 }) _c.Error(ap + ".tags", "an empty list matches no unit (leave the filter out to match every unit)");
            if (a?.Units is { Count: 0 }) _c.Error(ap + ".units", "an empty list matches no unit (leave the filter out to match every unit)");
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
        /// M3-6: every <c>requires</c> entry of every tech, building and unit must name a tech or a building id (one error
        /// each, at the entry) and is resolved to <c>RequiresTechs</c> / <c>RequiresBuildings</c> on its def. Entries that
        /// are not snake_case ids were already reported. A tech id that is also a building id is one error at the tech's
        /// id (BUG-0099: the entry naming it would be ambiguous), and a <c>requires</c> cycle among techs and buildings is
        /// one error at the entry that closes it (BUG-0099: nothing in it could ever be met). BUG-0100: an entry naming
        /// another faction's building or tech (or, in a common tech, any faction's) is one error at the entry, and an
        /// any-of that some faction can never fill is one error at the field (<see cref="CheckAnyOfReachable"/>).
        /// </summary>
        public void ResolveRequires(BuildingFileJson?[] buildingFiles, List<(int Faction, int Index)> buildings,
            UnitFileJson?[] unitFiles, List<(int Faction, int Index)> units)
        {
            foreach ((int file, int i) in _accepted)
            {
                string key = _files[file]!.Techs![i]!.Id!;
                if (Array.BinarySearch(_buildingKeys, key, StringComparer.Ordinal) < 0) continue;
                _c.CurrentFile = FileOf(file);
                _c.Error($"techs[{i}].id", $"tech id '{key}' is also a building id (a requires entry naming it would be ambiguous)");
            }

            // Nodes of the requires graph: tech t is node t, building type b is node techs + b. Units are never required.
            int techs = _keys.Length;
            var edges = new List<Edge>[techs + _buildings.Length];
            for (int n = 0; n < edges.Length; n++) edges[n] = new List<Edge>();
            foreach ((int file, int i) in _accepted)
            {
                _c.CurrentFile = FileOf(file);
                TechJson json = _files[file]!.Techs![i]!;
                TechDef def = _defs[Array.BinarySearch(_keys, json.Id!, StringComparer.Ordinal)];
                (def.RequiresTechs, def.RequiresBuildings) = Resolve(json.Requires, $"techs[{i}].requires", def.Faction, edges[def.Id]);
            }
            foreach ((int f, int i) in buildings)
            {
                _c.CurrentFile = $"factions/{_folders[f]}/buildings.json";
                BuildingJson json = buildingFiles[f]!.Buildings![i]!;
                BuildingDef def = _buildings[Array.BinarySearch(_buildingKeys, json.Id!, StringComparer.Ordinal)];
                (def.RequiresTechs, def.RequiresBuildings) = Resolve(json.Requires, $"buildings[{i}].requires", def.Faction, edges[techs + def.Id]);
            }
            foreach ((int f, int i) in units)
            {
                _c.CurrentFile = $"factions/{_folders[f]}/units.json";
                UnitJson json = unitFiles[f]!.Units![i]!;
                UnitDef def = _units[Array.BinarySearch(_unitKeys, json.Id!, StringComparer.Ordinal)];
                (def.RequiresTechs, def.RequiresBuildings) = Resolve(json.Requires, $"units[{i}].requires", def.Faction, null);
            }
            FindCycles(edges);
            // Reachability means something only on otherwise clean data: a broken any-of or requires entry is reported once.
            if (_c.Errors.Count == 0) CheckAnyOfReachable(buildingFiles, buildings, unitFiles, units);
        }

        /// <summary>One <c>requires</c> entry as a graph edge: the node it names, and where it is written (for the error).</summary>
        private readonly record struct Edge(int To, string File, string Path);

        /// <summary>
        /// A <c>requires</c> list of a def of faction <paramref name="owner"/> (-1: a common tech) resolved to (tech ids,
        /// building type ids), each ascending and de-duplicated; an unknown id is one error at its entry, and so is one the
        /// owner can never meet (BUG-0100): another faction's building (a building requirement is an own finished
        /// building, and a player can't place another faction's) or another faction's tech (it can't research it); a
        /// common tech, which every faction researches, can name neither. Each resolved entry is added to
        /// <paramref name="edges"/> (null for a unit's list).
        /// </summary>
        private (ImmutableArray<int> Techs, ImmutableArray<int> Buildings) Resolve(List<string?>? requires, string path, int owner, List<Edge>? edges)
        {
            var techs = new SortedSet<int>(); // load-time only
            var buildings = new SortedSet<int>();
            for (int j = 0; requires != null && j < requires.Count; j++)
            {
                string? id = requires[j];
                if (!Checker.IsId(id)) continue;
                // An id that is both a tech and a building was reported at the tech's id; it reads as the tech here.
                int t = Array.BinarySearch(_keys, id!, StringComparer.Ordinal);
                int b = t >= 0 ? -1 : Array.BinarySearch(_buildingKeys, id!, StringComparer.Ordinal);
                if (t < 0 && b < 0)
                {
                    _c.Error($"{path}[{j}]", $"unknown tech or building '{id}' (requires names a tech or building id)");
                    continue;
                }
                int named = t >= 0 ? _defs[t].Faction : _buildings[b].Faction;
                if (named >= 0 && named != owner)
                {
                    _c.Error($"{path}[{j}]", owner < 0
                        ? $"a common tech can't require '{id}' of faction '{_folders[named]}': no other faction could ever meet it"
                        : $"'{id}' belongs to faction '{_folders[named]}', not '{_folders[owner]}', so it can never be met");
                    continue;
                }
                if (t >= 0) techs.Add(t);
                else buildings.Add(b);
                edges?.Add(new Edge(t >= 0 ? t : _keys.Length + b, _c.CurrentFile, $"{path}[{j}]"));
            }
            return (ToArray(techs), ToArray(buildings));
        }

        private static ImmutableArray<int> ToArray(SortedSet<int> set)
        {
            var result = ImmutableArray.CreateBuilder<int>(set.Count);
            foreach (int x in set) result.Add(x);
            return result.MoveToImmutable();
        }

        /// <summary>
        /// BUG-0100: for each faction, what it can ever have, found by a fixpoint from "nothing built, nothing researched":
        /// a building of the faction, or a common or own tech, becomes reachable once everything its resolved
        /// <c>requires</c> names is reachable and (a tech) its any-of has at least <c>count</c> listed slots whose own
        /// building is reachable. A tech whose any-of some faction can't fill so is one error at its
        /// <c>requiresAnyOf</c>, naming the factions. Members that require the tech themselves, directly or through
        /// others, never become reachable before it, so they don't count. BUG-0134: a tech is reachable only once the
        /// faction's building of its <c>researchedAt</c> slot is, and a unit once its <c>trainedAt</c> building and its
        /// <c>requires</c> are; a faction with no any-of error that still can never have some building, tech or unit (a
        /// building requiring a tech researched only at its own slot, say) is one error at the first such entry, naming
        /// them all. Load time only.
        /// </summary>
        private void CheckAnyOfReachable(BuildingFileJson?[] buildingFiles, List<(int Faction, int Index)> buildings,
            UnitFileJson?[] unitFiles, List<(int Faction, int Index)> units)
        {
            int techs = _keys.Length;
            var blocked = new List<string>?[techs];
            var unreachable = new List<(string File, string Path, string Ids)>(); // one per faction, in faction order
            for (int f = 0; f < _folders.Length; f++)
            {
                var techOk = new bool[techs];
                var buildingOk = new bool[_buildings.Length];
                // Per slot, the faction's building type (BUG-0010: exactly one each in a clean file).
                var inSlot = new int[DataLimits.BuildingSlotIds.Length];
                Array.Fill(inSlot, -1);
                foreach (BuildingDef bd in _buildings)
                    if (bd.Faction == f) inSlot[(int)bd.Slot] = bd.Id;
                for (bool changed = true; changed;)
                {
                    changed = false;
                    foreach (BuildingDef bd in _buildings)
                    {
                        if (bd.Faction != f || buildingOk[bd.Id] || !AllOk(bd.RequiresTechs, bd.RequiresBuildings, techOk, buildingOk)) continue;
                        buildingOk[bd.Id] = changed = true;
                    }
                    foreach (TechDef td in _defs)
                    {
                        if ((td.Faction >= 0 && td.Faction != f) || techOk[td.Id] || !AllOk(td.RequiresTechs, td.RequiresBuildings, techOk, buildingOk)) continue;
                        if (AnyOfReachable(td, inSlot, buildingOk) < td.RequiresAnyOfCount) continue;
                        int at = inSlot[(int)td.ResearchedAtSlot];
                        if (at < 0 || !buildingOk[at]) continue; // BUG-0134: researched only at a building it can't have yet
                        techOk[td.Id] = changed = true;
                    }
                }
                foreach (TechDef td in _defs)
                {
                    if ((td.Faction >= 0 && td.Faction != f) || td.RequiresAnyOfCount <= 0) continue;
                    if (AnyOfReachable(td, inSlot, buildingOk) < td.RequiresAnyOfCount) (blocked[td.Id] ??= new List<string>()).Add(_folders[f]);
                }
                if (!AnyBlocked(blocked, _folders[f])) AddUnreachable(f, techOk, buildingOk, buildingFiles, buildings, unitFiles, units, unreachable);
            }
            foreach ((int file, int i) in _accepted)
            {
                TechJson json = _files[file]!.Techs![i]!;
                TechDef td = _defs[Array.BinarySearch(_keys, json.Id!, StringComparer.Ordinal)];
                List<string>? factions = blocked[td.Id];
                if (factions == null) continue;
                _c.CurrentFile = FileOf(file);
                _c.Error($"techs[{i}].requiresAnyOf", $"needs {td.RequiresAnyOfCount} of its slots built, but faction(s) {string.Join(", ", factions)} can "
                    + "never have that many (the other listed buildings need this tech first, directly or through others)");
            }
            foreach ((string file, string path, string message) in unreachable)
            {
                _c.CurrentFile = file;
                _c.Error(path, message);
            }
        }

        private static bool AnyBlocked(List<string>?[] blocked, string folder)
        {
            foreach (List<string>? factions in blocked)
                if (factions != null && factions.Contains(folder)) return true;
            return false;
        }

        /// <summary>
        /// BUG-0134: after faction <paramref name="f"/>'s fixpoint, its buildings, the techs it may research (common or own)
        /// and its units that it can still never have, as one entry at the first of them (a building's <c>requires</c>, else
        /// a tech's <c>researchedAt</c>, else a unit's <c>trainedAt</c>), naming the rest.
        /// </summary>
        private void AddUnreachable(int f, bool[] techOk, bool[] buildingOk, BuildingFileJson?[] buildingFiles, List<(int Faction, int Index)> buildings,
            UnitFileJson?[] unitFiles, List<(int Faction, int Index)> units, List<(string File, string Path, string Ids)> into)
        {
            var ids = new List<string>();
            string? file = null, path = null;
            foreach ((int bf, int i) in buildings)
            {
                if (bf != f) continue;
                BuildingDef bd = _buildings[Array.BinarySearch(_buildingKeys, buildingFiles[bf]!.Buildings![i]!.Id!, StringComparer.Ordinal)];
                if (buildingOk[bd.Id]) continue;
                ids.Add(bd.Key);
                if (path == null) (file, path) = ($"factions/{_folders[f]}/buildings.json", $"buildings[{i}].requires");
            }
            foreach ((int tf, int i) in _accepted)
            {
                TechDef td = _defs[Array.BinarySearch(_keys, _files[tf]!.Techs![i]!.Id!, StringComparer.Ordinal)];
                if ((td.Faction >= 0 && td.Faction != f) || techOk[td.Id]) continue;
                ids.Add(td.Key);
                if (path == null) (file, path) = (FileOf(tf), $"techs[{i}].researchedAt");
            }
            foreach ((int uf, int i) in units)
            {
                if (uf != f) continue;
                UnitDef ud = _units[Array.BinarySearch(_unitKeys, unitFiles[uf]!.Units![i]!.Id!, StringComparer.Ordinal)];
                bool ok = (uint)ud.TrainedAtTypeId < (uint)buildingOk.Length && buildingOk[ud.TrainedAtTypeId] && AllOk(ud.RequiresTechs, ud.RequiresBuildings, techOk, buildingOk);
                if (ok) continue;
                ids.Add(ud.Key);
                if (path == null) (file, path) = ($"factions/{_folders[f]}/units.json", $"units[{i}].trainedAt");
            }
            if (path == null) return;
            string rest = ids.Count == 1 ? "" : $" (nor {string.Join(", ", ids.GetRange(1, ids.Count - 1))})";
            into.Add((file!, path, $"faction '{_folders[f]}' can never have '{ids[0]}'{rest}: each needs something that needs it first "
                + "(for example a tech researched only at a building that requires it)"));
        }

        private static bool AllOk(ImmutableArray<int> techs, ImmutableArray<int> buildings, bool[] techOk, bool[] buildingOk)
        {
            foreach (int t in techs)
                if (!techOk[t]) return false;
            foreach (int b in buildings)
                if (!buildingOk[b]) return false;
            return true;
        }

        private static int AnyOfReachable(TechDef td, int[] inSlot, bool[] buildingOk)
        {
            int n = 0;
            foreach (int slot in td.RequiresAnyOfSlots)
                if (inSlot[slot] >= 0 && buildingOk[inSlot[slot]]) n++;
            return n;
        }

        private string NodeKey(int node) => node < _keys.Length ? _keys[node] : _buildingKeys[node - _keys.Length];

        /// <summary>
        /// Depth-first search over the requires graph in node order, entries in file order; each edge back to a node still
        /// on the path closes a cycle and is one error at that entry (a self-requirement is such an edge). Iterative, so a
        /// long chain can't overflow the stack.
        /// </summary>
        private void FindCycles(List<Edge>[] edges)
        {
            var state = new byte[edges.Length]; // 0 unvisited, 1 on the current path, 2 done
            var stack = new Stack<(int Node, int Next)>();
            for (int start = 0; start < edges.Length; start++)
            {
                if (state[start] != 0) continue;
                state[start] = 1;
                stack.Push((start, 0));
                while (stack.Count > 0)
                {
                    (int node, int next) = stack.Pop();
                    if (next == edges[node].Count)
                    {
                        state[node] = 2;
                        continue;
                    }
                    stack.Push((node, next + 1));
                    Edge e = edges[node][next];
                    if (state[e.To] == 1)
                    {
                        _c.CurrentFile = e.File;
                        _c.Error(e.Path, e.To == node
                            ? $"'{NodeKey(node)}' requires itself, so it can never be met"
                            : $"requires cycle: '{NodeKey(node)}' needs '{NodeKey(e.To)}', which needs '{NodeKey(node)}' (directly or through others), so none of them can ever be met");
                    }
                    else if (state[e.To] == 0)
                    {
                        state[e.To] = 1;
                        stack.Push((e.To, 0));
                    }
                }
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
