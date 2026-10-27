using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Vision;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-1 criterion 1: a brute-force oracle for <see cref="PlayerView"/>, and the fuzz scene the oracle and hash-twin rows play.
/// The oracle walks the world's stores slot by slot and decides each answer from the rule written out again (docs/03
/// "Vision, detection, fog"): the fog's byte map (the view's copy of the hashed bits PlayerView reads), the stealth rule from
/// the public <c>IsStealthed</c> / <c>IsDetectedBy</c>, the zone rule and the two high-ground reveals; and it cross-checks its
/// unit and building answers against <c>Fog.CanSeeUnit</c> / <c>CanSeeBuilding</c>.
/// </summary>
internal static class PlayerViewOracle
{
    /// <summary>What one comparison pass counted.</summary>
    internal sealed class Tally
    {
        public long Leaks, Misses, FieldMismatches, OracleDisagreements;
        public long OwnUnits, EnemyUnitsSeen, EnemyUnitsHidden, StealthHidden, StealthDetected, Revealed, InStorm;
        public long OwnBuildings, EnemyBuildingsSeen, Ghosts, GhostsOfDeadBuildings, GhostSites, Resources, ResourcesUnknownAmount, ResourcesRemembered;
        public long QueueItems;
        public long AbilityCooldowns, OwnCasts; // M5-4c
        public long AlliedUnitsSeen, AlliedStealthSeen; // M6-S4
        public string First = "";

        public void Fail(string what)
        {
            if (First.Length == 0) First = what;
        }

        public override string ToString() =>
            $"leaks {Leaks}, misses {Misses}, field mismatches {FieldMismatches}, oracle v fog {OracleDisagreements}; units own {OwnUnits} / enemy seen {EnemyUnitsSeen} / hidden {EnemyUnitsHidden} " +
            $"(stealth hidden {StealthHidden}, detected {StealthDetected}, high-ground revealed {Revealed}, in a storm {InStorm}); buildings own {OwnBuildings} / enemy seen {EnemyBuildingsSeen}; " +
            $"ghosts {Ghosts} (of dead buildings {GhostsOfDeadBuildings}, sites {GhostSites}); resources {Resources} (amount unknown {ResourcesUnknownAmount}, remembered gone {ResourcesRemembered}); queue items {QueueItems}; " +
            $"own cooldowns running {AbilityCooldowns}, own casts {OwnCasts}; allied units seen {AlliedUnitsSeen} (stealthed {AlliedStealthSeen})";
    }

    /// <summary>Compares every answer of <paramref name="view"/> (player <paramref name="p"/>) with the oracle, adding to <paramref name="t"/>.</summary>
    internal static void Check(World w, PlayerView view, int p, Tally t, string at)
    {
        FogStore fog = w.Fog;
        ReadOnlySpan<byte> vis = fog.Visibility(p);
        UnitStore u = w.Units;
        BuildingStore b = w.Buildings;
        ResourceStore r = w.Resources;

        // Cells.
        for (int c = 0; c < vis.Length; c++)
        {
            Compare(t, view.IsExplored(c), vis[c] != VisionConstants.Unexplored, $"{at}: cell {c} explored");
            Compare(t, view.IsVisible(c), vis[c] == VisionConstants.Visible, $"{at}: cell {c} visible");
        }
        Compare(t, view.IsExplored(-1) || view.IsExplored(vis.Length) || view.IsVisible(vis.Length) || view.IsVisible(int.MinValue), false, $"{at}: a cell off the map");

        // M6-S4: the sides, from the seats written out again (team 0: its own side; a named team: its seats).
        for (int q = -1; q <= w.Config.PlayerCount; q++)
        {
            Same(t, view.IsEnemy(q) ? 1 : 0, Allied(w, p, q) ? 0 : 1, $"{at}: IsEnemy({q})");
            Same(t, view.TeamOf(q), (uint)q < (uint)w.Config.PlayerCount ? w.Config.SeatOf(q).Team : 0, $"{at}: TeamOf({q})");
        }
        bool hasAlly = false;
        for (int q = 0; q < w.Config.PlayerCount; q++) hasAlly |= q != p && Allied(w, p, q);
        Same(t, view.HasAlly ? 1 : 0, hasAlly ? 1 : 0, $"{at}: HasAlly");

        // Own economy and techs.
        Same(t, view.Gold, w.Gold[p], $"{at}: gold");
        Same(t, view.Wood, w.Wood[p], $"{at}: wood");
        Same(t, view.HalfPop, w.HalfPop[p], $"{at}: pop");
        Same(t, view.HalfPopCap, w.HalfPopCap[p], $"{at}: pop cap");
        Same(t, view.Age, w.Age(p), $"{at}: age");
        for (int k = 0; k < w.Data.Techs.Length; k++) Same(t, view.HasTech(k) ? 1 : 0, w.HasTech(p, k) ? 1 : 0, $"{at}: tech {k}");

        // Units, by random access and by the cursor.
        int cursor = -1;
        for (int j = 0; j < u.Capacity; j++)
        {
            bool expected = ExpectUnit(w, p, j, t);
            if (expected != fog.CanSeeUnit(p, j))
            {
                t.OracleDisagreements++;
                t.Fail($"{at}: the oracle says {expected} for unit {j}, Fog.CanSeeUnit {!expected}");
            }
            bool got = view.TryGetUnit(j, out SeenUnit su);
            Compare(t, got, expected, $"{at}: unit {j} (owner {u.Owner[j]}, alive {u.Alive[j]})");
            if (got && expected)
            {
                var want = new SeenUnit(new EntityHandle(j, u.Generation[j]), u.Owner[j], u.TypeId[j], u.Position[j], u.Hp[j],
                    u.State[j] == UnitState.Idle && u.BuildTarget[j].Generation != 0 ? UnitState.Building : u.State[j]); // BUG-0640: a builder between its stuck stop and its stand
                Field(t, su == want, $"{at}: unit {j}'s fields");
            }
            if (!got) Field(t, su == default, $"{at}: hidden unit {j}'s out value is not default");
            Abilities(w, view, p, j, t, at);
            if (!expected) continue;
            bool next = view.NextUnit(ref cursor, out SeenUnit nu);
            Cursor(t, next, cursor, j, nu == su, $"{at}: the unit cursor");
        }
        End(t, view.NextUnit(ref cursor, out SeenUnit end), cursor, u.Capacity, end == default, $"{at}: the unit cursor");
        Field(t, !view.TryGetUnit(-1, out _) && !view.TryGetUnit(u.Capacity, out _) && !view.TryGetUnit(int.MaxValue, out _), $"{at}: a unit slot out of range");
        Field(t, !view.TryGetAbilityReady(-1, 0, out _) && !view.TryGetAbilityReady(u.Capacity, 0, out _) && !view.TryGetCast(int.MaxValue, out _), $"{at}: an ability answer for a slot out of range");
        int past = int.MaxValue;
        Field(t, !view.NextUnit(ref past, out _), $"{at}: a unit cursor at int.MaxValue");

        // Buildings and ghosts.
        ReadOnlySpan<BuildingGhost> ghosts = fog.Ghosts(p);
        int bCursor = -1, gCursor = -1;
        for (int k = 0; k < b.Capacity; k++)
        {
            bool expected = b.Alive[k] && (b.Owner[k] == p || FootprintVisible(w, vis, b.TypeId[k], b.Cell[k]) || fog.BuildingRevealed(p, k));
            if (expected != fog.CanSeeBuilding(p, k))
            {
                t.OracleDisagreements++;
                t.Fail($"{at}: the oracle says {expected} for building {k}, Fog.CanSeeBuilding {!expected}");
            }
            if (expected && b.Owner[k] == p) t.OwnBuildings++;
            else if (expected) t.EnemyBuildingsSeen++;
            bool got = view.TryGetBuilding(k, out SeenBuilding sb);
            Compare(t, got, expected, $"{at}: building {k}");
            if (got && expected)
                Field(t, sb == new SeenBuilding(new EntityHandle(k, b.Generation[k]), b.Owner[k], b.TypeId[k], b.Cell[k], b.Hp[k], b.UnderConstruction[k]), $"{at}: building {k}'s fields");
            if (!got) Field(t, sb == default, $"{at}: hidden building {k}'s out value is not default");
            if (expected)
            {
                bool next = view.NextBuilding(ref bCursor, out SeenBuilding nb);
                Cursor(t, next, bCursor, k, nb == sb, $"{at}: the building cursor");
            }

            BuildingGhost g = ghosts[k];
            bool ghost = g.Known && !(expected && b.Generation[k] == g.Generation) && !FootprintVisible(w, vis, g.TypeId, g.Cell);
            if (ghost)
            {
                t.Ghosts++;
                if (!b.Alive[k] || b.Generation[k] != g.Generation) t.GhostsOfDeadBuildings++;
                if (g.Site) t.GhostSites++;
                Field(t, g.Owner != p, $"{at}: player {p} has a ghost of its own building {k}");
            }
            bool gotGhost = view.TryGetGhost(k, out BuildingGhost vg);
            Compare(t, gotGhost, ghost, $"{at}: ghost {k}");
            Field(t, gotGhost ? vg == g : vg == default, $"{at}: ghost {k}'s fields");
            if (ghost)
            {
                bool next = view.NextGhost(ref gCursor, out BuildingGhost ng);
                Cursor(t, next, gCursor, k, ng == g, $"{at}: the ghost cursor");
            }

            // Queues: the player's own buildings only.
            bool own = b.Alive[k] && b.Owner[k] == p;
            int count = own ? b.QueueCount[k] : 0;
            Same(t, view.QueueCount(k), count, $"{at}: queue count of building {k}");
            Same(t, view.QueueProgress(k), own ? b.Progress[k] : 0, $"{at}: queue progress of building {k}");
            for (int item = 0; item <= count; item++)
            {
                bool gotItem = view.TryGetQueueItem(k, item, out int type, out bool isTech);
                bool wantItem = item < count;
                Compare(t, gotItem, wantItem, $"{at}: queue item {item} of building {k}");
                Field(t, wantItem ? type == b.QueueTypeAt(k, item) && isTech == b.QueueIsTechAt(k, item) : type == -1 && !isTech, $"{at}: queue item {item} of building {k}'s fields");
                if (wantItem) t.QueueItems++;
            }
            if (!own) Field(t, !view.TryGetQueueItem(k, 0, out _, out _), $"{at}: player {p} reads building {k}'s queue");
        }
        End(t, view.NextBuilding(ref bCursor, out SeenBuilding bEnd), bCursor, b.Capacity, bEnd == default, $"{at}: the building cursor");
        End(t, view.NextGhost(ref gCursor, out BuildingGhost gEnd), gCursor, b.Capacity, gEnd == default, $"{at}: the ghost cursor");
        Field(t, !view.TryGetBuilding(-1, out _) && !view.TryGetGhost(-1, out _) && !view.TryGetGhost(b.Capacity, out _) && view.QueueCount(-1) == 0, $"{at}: a building slot out of range");

        // Resource nodes (M5-2, BUG-0540): the node the player last saw in each slot, alive or gone out of its sight, until
        // the player sees that ground again; the amount only for a live node in sight. The oracle keeps its own memory per
        // world, rebuilt at every fog update from the byte map (NodeOracle).
        NodeOracle nodes = NodeOracle.For(w);
        nodes.Observe(w, p, t, at);
        int rCursor = -1;
        for (int k = 0; k < r.Capacity; k++)
        {
            bool expected = false, visible = false, live = false;
            (int gen, int type, int cell) m = nodes.Entry(p, k);
            if (m.gen != 0)
            {
                live = r.Alive[k] && r.Generation[k] == m.gen;
                visible = NodeVisible(w, vis, m.type, m.cell);
                expected = live || !visible;
            }
            bool got = view.TryGetResource(k, out SeenResource sr);
            Compare(t, got, expected, $"{at}: resource {k}");
            if (got && expected)
            {
                Field(t, sr == new SeenResource(new EntityHandle(k, m.gen), m.type, m.cell, live && visible ? r.Remaining[k] : -1), $"{at}: resource {k}'s fields");
                t.Resources++;
                if (!(live && visible)) t.ResourcesUnknownAmount++;
                if (!live) t.ResourcesRemembered++;
                bool next = view.NextResource(ref rCursor, out SeenResource nr);
                Cursor(t, next, rCursor, k, nr == sr, $"{at}: the resource cursor");
            }
            if (!got) Field(t, sr == default, $"{at}: unknown resource {k}'s out value is not default");
        }
        End(t, view.NextResource(ref rCursor, out SeenResource rEnd), rCursor, r.Capacity, rEnd == default, $"{at}: the resource cursor");
    }

    /// <summary>
    /// M5-4c: the own-unit ability answers of unit slot <paramref name="j"/>: a ready tick for each ability its type lists and a
    /// cast in progress, for a live unit of player <paramref name="p"/> only; false and 0 / -1 for every other slot and index
    /// (an enemy's cooldown or cast never leaks).
    /// </summary>
    private static void Abilities(World w, PlayerView view, int p, int j, Tally t, string at)
    {
        UnitStore u = w.Units;
        bool own = u.Alive[j] && u.Owner[j] == p;
        int listed = u.Alive[j] ? w.Data.Units[u.TypeId[j]].Abilities.Length : 0;
        for (int a = -1; a <= DataLimits.MaxUnitAbilities; a++)
        {
            bool want = own && a >= 0 && a < listed;
            int ready = want ? u.AbilityReadyTick[j * DataLimits.MaxUnitAbilities + a] : 0;
            bool got = view.TryGetAbilityReady(j, a, out int readyTick);
            Compare(t, got, want, $"{at}: unit {j}'s ability {a} ready tick (owner {u.Owner[j]})");
            Field(t, readyTick == ready, $"{at}: unit {j}'s ability {a} ready tick {readyTick}, want {ready}");
            if (want && ready > w.TickNumber) t.AbilityCooldowns++;
        }
        int cast = own ? u.CastAbility[j] : -1;
        bool gotCast = view.TryGetCast(j, out int index);
        Compare(t, gotCast, cast >= 0, $"{at}: unit {j}'s cast (owner {u.Owner[j]})");
        Field(t, index == cast, $"{at}: unit {j}'s cast {index}, want {cast}");
        if (cast >= 0) t.OwnCasts++;
    }

    /// <summary>M6-S4: the alliance rule from the seats: the same player, or both on one named team (team 0 is no team).</summary>
    internal static bool Allied(World w, int a, int b)
    {
        if (a == b) return true;
        int n = w.Config.PlayerCount;
        if ((uint)a >= (uint)n || (uint)b >= (uint)n) return false;
        int ta = w.Config.SeatOf(a).Team;
        return ta != 0 && ta == w.Config.SeatOf(b).Team;
    }

    /// <summary>The oracle's unit rule for player <paramref name="p"/> and slot <paramref name="j"/>, tallying why.</summary>
    private static bool ExpectUnit(World w, int p, int j, Tally t)
    {
        UnitStore u = w.Units;
        if (!u.Alive[j]) return false;
        if (u.Owner[j] == p)
        {
            t.OwnUnits++;
            return true;
        }
        FogStore fog = w.Fog;
        bool storm = w.Zones.BlockerCount > 0;
        // Without a blocking zone, the unit's cell in the byte map; with one, the fog's zone rule by the unit's own centre.
        bool cell = storm ? fog.SeesUnitCell(p, j) : fog.Visibility(p)[fog.CellOf(u.Position[j])] == VisionConstants.Visible;
        bool revealed = fog.Revealed(p, j);
        bool stealthed = w.IsStealthed(j);
        // M6-S4: stealth hides a unit only from its owner's enemies; an ally sees it wherever its own vision does.
        bool allied = Allied(w, p, u.Owner[j]);
        bool hidden = stealthed && !allied && !w.IsDetectedBy(p, j);
        bool sees = !hidden && (cell || revealed);
        if (stealthed && !hidden) t.StealthDetected++;
        if (hidden && (cell || revealed)) t.StealthHidden++;
        if (sees && !cell) t.Revealed++;
        if (sees && storm) t.InStorm++;
        if (sees) t.EnemyUnitsSeen++; else t.EnemyUnitsHidden++;
        if (sees && allied) t.AlliedUnitsSeen++;
        if (sees && allied && stealthed) t.AlliedStealthSeen++;
        return sees;
    }

    private static bool NodeVisible(World w, ReadOnlySpan<byte> vis, int type, int anchor)
    {
        ResourceDef def = w.Data.Resources[type];
        for (int dy = 0; dy < def.FootprintHeight; dy++)
            for (int dx = 0; dx < def.FootprintWidth; dx++)
                if (vis[anchor + dy * w.Fog.Width + dx] == VisionConstants.Visible) return true;
        return false;
    }

    /// <summary>
    /// The oracle's own node memory (M5-2, BUG-0540), one per world, written again from the rule: at every fog update (a new
    /// <c>Fog.Version</c>) each live node with a footprint cell visible in the byte map is recorded in its slot, and a recorded
    /// node that is gone (slot free or reused) is dropped once a cell of its remembered footprint is visible. The callers check
    /// every player after every tick, so it sees every update (a skipped one is reported).
    /// </summary>
    internal sealed class NodeOracle
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<World, NodeOracle> s_all = new();
        private readonly int[] _gen, _type, _cell, _version;
        private readonly int _slots;

        private NodeOracle(World w)
        {
            _slots = w.Resources.Capacity;
            int players = w.Config.PlayerCount;
            _gen = new int[players * _slots];
            _type = new int[players * _slots];
            _cell = new int[players * _slots];
            _version = new int[players];
        }

        internal static NodeOracle For(World w) => s_all.GetValue(w, x => new NodeOracle(x));

        internal (int Gen, int Type, int Cell) Entry(int p, int k) => (_gen[p * _slots + k], _type[p * _slots + k], _cell[p * _slots + k]);

        internal void Observe(World w, int p, Tally t, string at)
        {
            int version = w.Fog.Version(p);
            if (version == _version[p]) return;
            if (version != _version[p] + 1)
            {
                t.FieldMismatches++;
                t.Fail($"{at}: the node oracle missed a fog update (version {_version[p]} to {version})");
            }
            _version[p] = version;
            ReadOnlySpan<byte> vis = w.Fog.Visibility(p);
            ResourceStore r = w.Resources;
            for (int k = 0; k < _slots; k++)
            {
                int e = p * _slots + k;
                if (r.Alive[k] && NodeVisible(w, vis, r.TypeId[k], r.Cell[k]))
                {
                    _gen[e] = r.Generation[k];
                    _type[e] = r.TypeId[k];
                    _cell[e] = r.Cell[k];
                    continue;
                }
                if (_gen[e] == 0 || r.Alive[k] && r.Generation[k] == _gen[e]) continue;
                if (NodeVisible(w, vis, _type[e], _cell[e])) _gen[e] = 0;
            }
        }
    }

    private static bool FootprintVisible(World w, ReadOnlySpan<byte> vis, int type, int anchor)
    {
        BuildingDef def = w.Data.Buildings[type];
        for (int dy = 0; dy < def.FootprintHeight; dy++)
            for (int dx = 0; dx < def.FootprintWidth; dx++)
                if (vis[anchor + dy * w.Fog.Width + dx] == VisionConstants.Visible) return true;
        return false;
    }

    /// <summary>
    /// The cursor's next answer against the oracle's next expected slot <paramref name="expected"/>: a slot before it is one the
    /// oracle hides (a leak), none or one past it skips an expected slot (a miss).
    /// </summary>
    private static void Cursor(Tally t, bool next, int got, int expected, bool sameValue, string what)
    {
        if (next && got < expected)
        {
            t.Leaks++;
            t.Fail($"{what} gave slot {got}, which the oracle hides (next expected {expected})");
        }
        else if (!next || got > expected)
        {
            t.Misses++;
            t.Fail($"{what} skipped slot {expected} (gave {(next ? got : -1)})");
        }
        else Field(t, sameValue, $"{what} at slot {got}: the record differs from the random-access answer");
    }

    /// <summary>After the oracle's last expected slot the cursor must answer nothing more (any answer is a leak) and park at <paramref name="capacity"/>.</summary>
    private static void End(Tally t, bool next, int at, int capacity, bool defaultValue, string what)
    {
        if (next)
        {
            t.Leaks++;
            t.Fail($"{what} gave slot {at} past the oracle's last");
        }
        else Field(t, at == capacity && defaultValue, $"{what} ended at {at}, not {capacity}, or with a value");
    }

    private static void Compare(Tally t, bool got, bool expected, string what)
    {
        if (got == expected) return;
        if (got) t.Leaks++; else t.Misses++;
        t.Fail($"{what}: view {got}, oracle {expected}");
    }

    private static void Same(Tally t, int got, int expected, string what)
    {
        if (got == expected) return;
        t.FieldMismatches++;
        t.Fail($"{what}: view {got}, world {expected}");
    }

    private static void Field(Tally t, bool ok, string what)
    {
        if (ok) return;
        t.FieldMismatches++;
        t.Fail(what);
    }
}

/// <summary>
/// The M5-1 fuzz scene: four hostile players (indices past 2 included) on a generated multi-level map with the stealth test
/// data (<see cref="StealthTests.Data"/>): per player a town hall that trains workers, a watchtower (shoots, 16 m detector) and
/// two houses (buildings to die, in and out of each other's sight), Heavy Infantry casting the test-only stealth, Laborers
/// that detect 10 m, Crossbowmen, and Priests casting Sandstorm; armies attack-move at each other.
/// </summary>
internal static class PlayerViewScene
{
    internal const int Players = 4;
    private const int Infantry = 8, Detectors = 2, Bowmen = 5, Priests = 2;

    internal static GameData Data => StealthTests.Data;

    /// <summary>The scene of <paramref name="seed"/>; M6-S4: with <paramref name="teams"/> (one per player) the seats' teams, else every seat team 0.</summary>
    internal static Simulation New(ulong seed, int[]? teams = null)
    {
        var map = MapGenParams.Default with { Forests = 6, GoldMines = 4 };
        var config = new SimConfig(seed, Players, Players * (Infantry + Detectors + Bowmen + Priests + 16) + 8, 2048) { Data = Data, Map = map };
        if (teams != null) config = config with { Seats = TeamTests.Seats(teams) };
        var sim = new Simulation(config);
        NavGrid g = sim.World.NavGrid;
        List<int> open = Open(g);
        var rng = new SimRng(seed, 511);
        int heavy = Data.FindUnit("malazan_heavy_infantry"), laborer = Data.FindUnit("malazan_laborer"),
            bow = Data.FindUnit("malazan_crossbowman"), priest = Data.FindUnit("whirlwind_priest");
        int centre = open[rng.NextInt(0, open.Count)];
        int cx = centre % g.Width, cy = centre / g.Width;
        for (int p = 0; p < Players; p++)
        {
            // Four corners of a 32-cell square round the centre: neighbours meet, opposite corners start unseen.
            int px = cx + (p % 2 == 0 ? -16 : 16), py = cy + (p < 2 ? -16 : 16);
            // A spy of every other player at this base: each sees every base at the start, until the tower shoots it.
            for (int q = 0; q < Players; q++)
                if (q != p) sim.Enqueue(Command.SpawnUnit(q, laborer, Cell(g, Near(g, open, px, py, ref rng))));
            FactionDef f = Data.Factions[p % Data.Factions.Length];
            sim.Enqueue(Command.SpawnBuilding(p, OfSlot(f.Id, BuildingSlot.TownHall), Cell(g, Near(g, open, px, py, ref rng))));
            sim.Enqueue(Command.SpawnBuilding(p, OfSlot(f.Id, BuildingSlot.WatchTower), Cell(g, Near(g, open, px, py, ref rng))));
            for (int h = 0; h < 2; h++)
                sim.Enqueue(Command.SpawnBuilding(p, OfSlot(f.Id, BuildingSlot.House), Cell(g, Near(g, open, px + (h == 0 ? -8 : 8), py, ref rng))));
            for (int k = 0; k < Infantry + Detectors + Bowmen + Priests; k++)
            {
                int type = k < Infantry ? heavy : k < Infantry + Detectors ? laborer : k < Infantry + Detectors + Bowmen ? bow : priest;
                sim.Enqueue(Command.SpawnUnit(p, type, Cell(g, Near(g, open, px, py, ref rng))));
            }
        }
        return sim;
    }

    private static int OfSlot(int faction, BuildingSlot slot)
    {
        foreach (BuildingDef b in Data.Buildings)
            if (b.Faction == faction && b.Slot == slot) return b.Id;
        throw new InvalidOperationException($"no {slot} for faction {faction}");
    }

    private static Vector2 Cell(NavGrid g, int c) => g.CellCenter(c % g.Width, c / g.Width);

    private static int Near(NavGrid g, List<int> open, int x, int y, ref SimRng rng)
    {
        for (int tries = 0; tries < 40; tries++)
        {
            int px = x + rng.NextInt(-6, 7), py = y + rng.NextInt(-6, 7);
            if (px >= 0 && py >= 0 && px < g.Width && py < g.Height && g.IsPassable(px, py)) return py * g.Width + px;
        }
        return open[rng.NextInt(0, open.Count)];
    }

    private static List<int> Open(NavGrid g)
    {
        var open = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
            if (g.IsPassable(c % g.Width, c / g.Width)) open.Add(c);
        return open;
    }

    /// <summary>
    /// The orders of tick <paramref name="t"/>: every 150 ticks each fighter attack-moves to a random live enemy (from tick
    /// 300: the armies first hold, so ghosts form before buildings die), each Laborer walks to a random cell, each Priest casts
    /// Sandstorm on an enemy; every 40 ticks each Heavy Infantry casts its stealth with chance 1/3; every 200 ticks each town
    /// hall queues a worker. Read from the world (test-side, as a script would), sent as commands. At ticks 600 and 1200 the
    /// first live house of each player is destroyed through the test seam (<c>BuildingStore.Free</c>, as GhostListTests do),
    /// wherever it stands: a building gone in some players' sight and out of others'. M5-2 (BUG-0540): at ticks 500 and 1100
    /// every 9th live resource node is emptied through the seam (<c>ResourceStore.Take</c>), wherever it stands: nodes felled
    /// in some players' sight and out of others'.
    /// </summary>
    internal static void Orders(Simulation sim, int t, ref SimRng rng)
    {
        if (t == 500 || t == 1100)
        {
            ResourceStore r = sim.World.Resources;
            int live = 0;
            for (int k = 0; k < r.Capacity; k++)
                if (r.Alive[k] && live++ % 9 == 0) r.Take(r.HandleOf(k), int.MaxValue);
        }
        World w = sim.World;
        UnitStore u = w.Units;
        BuildingStore b = w.Buildings;
        int heavy = Data.FindUnit("malazan_heavy_infantry"), laborer = Data.FindUnit("malazan_laborer"), priest = Data.FindUnit("whirlwind_priest");
        if (t == 600 || t == 1200)
        {
            for (int p = 0; p < Players; p++)
                for (int k = 0; k < b.Capacity; k++)
                {
                    if (!b.Alive[k] || b.Owner[k] != p || Data.Buildings[b.TypeId[k]].Slot != BuildingSlot.House) continue;
                    Assert.True(b.Free(b.HandleOf(k)));
                    break;
                }
        }
        if (t % 200 == 5)
        {
            for (int k = 0; k < b.Capacity; k++)
            {
                if (!b.Alive[k] || b.UnderConstruction[k] || Data.Buildings[b.TypeId[k]].Slot != BuildingSlot.TownHall) continue;
                int faction = Data.Buildings[b.TypeId[k]].Faction;
                int worker = Data.Factions[faction].Units[0];
                foreach (int ut in Data.Factions[faction].Units)
                    if (Data.Units[ut].Slot == UnitSlot.Worker) worker = ut;
                sim.Enqueue(Command.Train(b.Owner[k], Centre(w, k), worker));
            }
        }
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            var h = new EntityHandle(i, u.Generation[i]);
            int owner = u.Owner[i];
            if (t % 40 == 3 && u.TypeId[i] == heavy && rng.NextInt(0, 3) == 0)
            {
                sim.Enqueue(Command.UseAbility(owner, h, 0, u.Position[i]));
                continue;
            }
            if (t % 150 != 2 || t < 300) continue;
            if (u.TypeId[i] == laborer)
            {
                var to = new Vector2(rng.NextInt(0, w.NavGrid.Width) * MapConstants.CellSize + 1f, rng.NextInt(0, w.NavGrid.Height) * MapConstants.CellSize + 1f);
                sim.Enqueue(Command.Move(owner, h, to));
                continue;
            }
            Vector2 enemy = u.Position[i];
            for (int tries = 0; tries < 8; tries++)
            {
                int j = rng.NextInt(0, u.Capacity);
                if (u.Alive[j] && u.Owner[j] != owner) { enemy = u.Position[j]; break; }
            }
            if (u.TypeId[i] == priest) sim.Enqueue(Command.UseAbility(owner, h, 0, enemy));
            else sim.Enqueue(Command.AttackMove(owner, h, enemy));
        }
    }

    /// <summary>The centre (m) of building slot <paramref name="k"/>'s footprint.</summary>
    private static Vector2 Centre(World w, int k)
    {
        BuildingDef def = w.Data.Buildings[w.Buildings.TypeId[k]];
        int anchor = w.Buildings.Cell[k];
        return new Vector2((anchor % w.Fog.Width + def.FootprintWidth / 2f) * MapConstants.CellSize, (anchor / w.Fog.Width + def.FootprintHeight / 2f) * MapConstants.CellSize);
    }
}

/// <summary>Reads everything a <see cref="PlayerView"/> answers, as an AI would each think (allocation-free): the hash-twin and perf rows' workload.</summary>
internal static class PlayerViewReader
{
    /// <summary>Walks every cursor, the own economy and every own queue, and with <paramref name="cells"/> every cell; returns a checksum of the answers.</summary>
    internal static long ReadAll(PlayerView v, bool cells)
    {
        long sum = v.Gold + v.Wood + v.HalfPop + v.HalfPopCap + v.Age + v.TickNumber;
        int s = -1;
        while (v.NextUnit(ref s, out SeenUnit u)) sum += u.Handle.Index + u.Hp + u.TypeId + (long)u.Position.X;
        s = -1;
        while (v.NextBuilding(ref s, out SeenBuilding b))
        {
            sum += b.Handle.Index + b.Hp + b.Cell;
            int n = v.QueueCount(s);
            for (int item = 0; item < n; item++)
                if (v.TryGetQueueItem(s, item, out int type, out bool isTech)) sum += type + (isTech ? 1 : 0);
        }
        s = -1;
        while (v.NextGhost(ref s, out BuildingGhost g)) sum += g.Cell + g.TypeId;
        s = -1;
        while (v.NextResource(ref s, out SeenResource r)) sum += r.Cell + r.Remaining;
        if (!cells) return sum;
        int count = v.MapWidth * v.MapHeight;
        for (int c = 0; c < count; c++)
            if (v.IsVisible(c)) sum += 2;
            else if (v.IsExplored(c)) sum++;
        return sum;
    }
}
