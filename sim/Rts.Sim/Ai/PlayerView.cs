using System;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Vision;

namespace Rts.Sim.Ai;

/// <summary>
/// One player's fog- and stealth-filtered window on a match (M5-1, docs/03 "AI architecture"): the only way AI code reads
/// the world. Answers the player's own units, buildings, economy, techs and queues always; an enemy unit only while the
/// player sees it (<see cref="FogStore.CanSeeUnit"/>: its cell visible at the last fog update or revealed by a high-ground
/// hit, never an enemy's stealthed unit the player doesn't detect); an enemy building only while the player sees it
/// (<see cref="FogStore.CanSeeBuilding"/>) or as a ghost from its last-known list; a resource node as the player last saw
/// it (M5-2, BUG-0540: a node gone out of sight stays answered until its ground is seen); and the explored / visible state
/// of every cell.
/// </summary>
/// <remarks>
/// <para>
/// Read-only and stateless: it caches nothing, so one kept across ticks always answers for the current state, the same as
/// one built fresh (read it between ticks, as the view does). What the player remembers (ghosts, nodes) is the fog's. Every answer is a value (ids, handles, positions, the
/// <see cref="SeenUnit"/> / <see cref="SeenBuilding"/> / <see cref="SeenResource"/> / <see cref="BuildingGhost"/> records);
/// no world, store or fog reference leaves it. A slot the player may not see answers false with a default record, so the
/// out value leaks nothing either.
/// </para>
/// <para>
/// Enumeration is slot order through a cursor (<c>int slot = -1; while (view.NextUnit(ref slot, out SeenUnit u)) ...</c>):
/// no buffer, no callback, no allocation. Constructing one allocates the object alone (no per-player buffer).
/// </para>
/// </remarks>
public sealed class PlayerView
{
    private readonly World _world;
    private readonly FogStore _fog;
    private readonly UnitStore _units;
    private readonly BuildingStore _buildings;
    private readonly ResourceStore _resources;
    private readonly GameData _data;
    private readonly int _player;

    /// <summary>The view of player <paramref name="player"/> (0 to the match's player count - 1) on <paramref name="world"/>.</summary>
    public PlayerView(World world, int player)
    {
        ArgumentNullException.ThrowIfNull(world);
        if ((uint)player >= (uint)world.Config.PlayerCount) throw new ArgumentOutOfRangeException(nameof(player));
        _world = world;
        _fog = world.Fog;
        _units = world.Units;
        _buildings = world.Buildings;
        _resources = world.Resources;
        _data = world.Data;
        _player = player;
        Faction = world.FactionOf(player);
    }

    /// <summary>The player this view belongs to.</summary>
    public int Player => _player;

    /// <summary>The faction the player plays (<c>GameData.Factions</c>).</summary>
    public int Faction { get; }

    /// <summary>The match's tick number (between ticks: the next tick's).</summary>
    public int TickNumber => _world.TickNumber;

    // ---------- the fog grid ----------

    /// <summary>Map width in cells (cells are row-major, <c>y * MapWidth + x</c>).</summary>
    public int MapWidth => _fog.Width;

    /// <summary>Map height in cells.</summary>
    public int MapHeight => _fog.Height;

    /// <summary>Whether the player has ever seen <paramref name="cell"/> (explored or visible); false out of range.</summary>
    public bool IsExplored(int cell) => _fog.IsExplored(_player, cell);

    /// <summary>Whether <paramref name="cell"/> was inside one of the player's sight circles at the last fog update; false out of range.</summary>
    public bool IsVisible(int cell) => _fog.IsVisible(_player, cell);

    // ---------- own economy ----------

    /// <summary>The player's gold.</summary>
    public int Gold => _world.Gold[_player];

    /// <summary>The player's wood.</summary>
    public int Wood => _world.Wood[_player];

    /// <summary>The player's population in half-pop (1 pop = 2).</summary>
    public int HalfPop => _world.HalfPop[_player];

    /// <summary>The player's population cap in half-pop.</summary>
    public int HalfPopCap => _world.HalfPopCap[_player];

    /// <summary>The player's age (1 at start, 2 after <c>age_ii</c>).</summary>
    public int Age => _world.Age(_player);

    /// <summary>Whether the player has researched tech <paramref name="techId"/>; false for an unknown id.</summary>
    public bool HasTech(int techId) => _world.HasTech(_player, techId);

    // ---------- units ----------

    /// <summary>Unit slots in the match (the cursor's range).</summary>
    public int UnitSlots => _units.Capacity;

    /// <summary>
    /// Unit slot <paramref name="slot"/> as the player sees it: true for a live own unit, or a live enemy unit
    /// <see cref="FogStore.CanSeeUnit"/> grants; false (and a default record) for anything else.
    /// </summary>
    public bool TryGetUnit(int slot, out SeenUnit unit)
    {
        if (!_fog.CanSeeUnit(_player, slot)) // own units included; false for a dead slot or out of range
        {
            unit = default;
            return false;
        }
        unit = UnitAt(slot);
        return true;
    }

    /// <summary>The next unit after slot <paramref name="slot"/> the player sees (start at -1); false when there is none.</summary>
    public bool NextUnit(ref int slot, out SeenUnit unit)
    {
        int n = _units.Capacity;
        int s = _fog.NextSeenUnit(_player, First(slot, n)); // CanSeeUnit's answer as one loop (2,500 slots x 8 players a tick)
        slot = s;
        if (s >= n)
        {
            unit = default;
            return false;
        }
        unit = UnitAt(s);
        return true;
    }

    private SeenUnit UnitAt(int s) =>
        new(new EntityHandle(s, _units.Generation[s]), _units.Owner[s], _units.TypeId[s], _units.Position[s], _units.Hp[s], _units.State[s]);

    // ---------- buildings ----------

    /// <summary>Building slots in the match (the cursor's range, and the ghost list's).</summary>
    public int BuildingSlots => _buildings.Capacity;

    /// <summary>
    /// Building slot <paramref name="slot"/> as the player sees it now: true for a live own building, or a live enemy building
    /// <see cref="FogStore.CanSeeBuilding"/> grants (a footprint cell visible at the last update, or a tower revealed by its
    /// hit from high ground); false (and a default record) otherwise.
    /// </summary>
    public bool TryGetBuilding(int slot, out SeenBuilding building)
    {
        if (!_fog.CanSeeBuilding(_player, slot))
        {
            building = default;
            return false;
        }
        building = new SeenBuilding(new EntityHandle(slot, _buildings.Generation[slot]), _buildings.Owner[slot],
            _buildings.TypeId[slot], _buildings.Cell[slot], _buildings.Hp[slot], _buildings.UnderConstruction[slot]);
        return true;
    }

    /// <summary>The next building after slot <paramref name="slot"/> the player sees now (start at -1); false when there is none.</summary>
    public bool NextBuilding(ref int slot, out SeenBuilding building)
    {
        int n = _buildings.Capacity;
        ReadOnlySpan<bool> alive = _buildings.Alive;
        for (int s = First(slot, n); s < n; s++)
        {
            if (!alive[s] || !TryGetBuilding(s, out building)) continue;
            slot = s;
            return true;
        }
        slot = n;
        building = default;
        return false;
    }

    /// <summary>
    /// The player's remembered enemy building in slot <paramref name="slot"/>'s entry of its last-known list
    /// (<see cref="FogStore.Ghosts"/>), as the player's own screen shows it: an entry that is known, whose building the player
    /// does not see now (<see cref="TryGetBuilding"/> false, or the slot now holds another building), and no cell of whose
    /// remembered footprint is visible. The building may be gone (it died out of sight); the entry is what was last seen.
    /// </summary>
    public bool TryGetGhost(int slot, out BuildingGhost ghost)
    {
        ReadOnlySpan<BuildingGhost> list = _fog.Ghosts(_player);
        if ((uint)slot >= (uint)list.Length || !list[slot].Known)
        {
            ghost = default;
            return false;
        }
        BuildingGhost g = list[slot];
        // Seen now: the live answer is the building, not its memory. A cell in sight shows the ground as it is (BUG-0310:
        // an entry outlives a building that died in sight until the next update).
        if (TryGetBuilding(slot, out SeenBuilding live) && live.Handle.Generation == g.Generation || _fog.SeesFootprint(_player, g.TypeId, g.Cell))
        {
            ghost = default;
            return false;
        }
        ghost = g;
        return true;
    }

    /// <summary>The next ghost after slot <paramref name="slot"/> (start at -1); false when there is none. The ghost's handle is (slot, <see cref="BuildingGhost.Generation"/>).</summary>
    public bool NextGhost(ref int slot, out BuildingGhost ghost)
    {
        int n = _buildings.Capacity;
        if (_fog.GhostCount(_player) > 0)
        {
            ReadOnlySpan<BuildingGhost> list = _fog.Ghosts(_player);
            for (int s = First(slot, n); s < n; s++)
            {
                if (!list[s].Known || !TryGetGhost(s, out ghost)) continue;
                slot = s;
                return true;
            }
        }
        slot = n;
        ghost = default;
        return false;
    }

    // ---------- own production queues ----------

    /// <summary>Items in the queue of the player's own building in slot <paramref name="buildingSlot"/>; 0 for any other slot.</summary>
    public int QueueCount(int buildingSlot) => IsOwnBuilding(buildingSlot) ? _buildings.QueueCount[buildingSlot] : 0;

    /// <summary>
    /// Item <paramref name="item"/> (0 = the head, in progress) of the queue of the player's own building in slot
    /// <paramref name="buildingSlot"/>: a unit type id, or a tech id when <paramref name="isTech"/>. False (-1, false) for
    /// another player's building, an empty slot, or past the queue.
    /// </summary>
    public bool TryGetQueueItem(int buildingSlot, int item, out int typeId, out bool isTech)
    {
        typeId = -1;
        isTech = false;
        if (!IsOwnBuilding(buildingSlot)) return false;
        typeId = _buildings.QueueTypeAt(buildingSlot, item);
        if (typeId < 0) return false;
        isTech = _buildings.QueueIsTechAt(buildingSlot, item);
        return true;
    }

    /// <summary>Ticks of work done on the head item of the player's own building in slot <paramref name="buildingSlot"/>; 0 for any other slot.</summary>
    public int QueueProgress(int buildingSlot) => IsOwnBuilding(buildingSlot) ? _buildings.Progress[buildingSlot] : 0;

    private bool IsOwnBuilding(int slot) =>
        (uint)slot < (uint)_buildings.Capacity && _buildings.Alive[slot] && _buildings.Owner[slot] == _player;

    // ---------- resource nodes ----------

    /// <summary>Resource node slots in the match (the cursor's range).</summary>
    public int ResourceSlots => _resources.Capacity;

    /// <summary>
    /// Resource slot <paramref name="slot"/> as the player knows it (M5-2, BUG-0540): the node of the player's node memory
    /// (<see cref="FogStore.NodeAt"/>), the one seen there at the last fog update that showed its ground. True while the node
    /// is alive (what is left in it only while a footprint cell is visible, -1 otherwise), and after it is gone (felled or
    /// mined out where the player can't look; <c>Remaining</c> -1) until a cell of its footprint is visible again, the
    /// rule the player's own screen draws trees with (<c>ViewApi.SeenResources</c>). False (and a default record) for a slot
    /// the player has no memory of, a node seen gone, or out of range.
    /// </summary>
    public bool TryGetResource(int slot, out SeenResource node)
    {
        if ((uint)slot >= (uint)_resources.Capacity)
        {
            node = default;
            return false;
        }
        NodeMemory m = _fog.NodeAt(_player, slot);
        if (!m.Known)
        {
            node = default;
            return false;
        }
        bool live = _resources.Alive[slot] && _resources.Generation[slot] == m.Generation;
        bool visible = _fog.SeesNodeFootprint(_player, m.TypeId, m.Cell);
        if (!live && visible) // gone, and its ground is in sight: the player sees it is gone (before the next update drops it)
        {
            node = default;
            return false;
        }
        node = new SeenResource(new EntityHandle(slot, m.Generation), m.TypeId, m.Cell, live && visible ? _resources.Remaining[slot] : -1);
        return true;
    }

    /// <summary>The next resource node after slot <paramref name="slot"/> the player knows (start at -1); false when there is none.</summary>
    public bool NextResource(ref int slot, out SeenResource node)
    {
        int n = _resources.Capacity;
        for (int s = First(slot, n); s < n; s++)
        {
            if (!_fog.NodeAt(_player, s).Known || !TryGetResource(s, out node)) continue;
            slot = s;
            return true;
        }
        slot = n;
        node = default;
        return false;
    }

    // ---------- own orders' rules (M5-2) ----------

    /// <summary>
    /// Whether the player may queue a unit of <paramref name="unitTypeId"/> at its own building in slot
    /// <paramref name="buildingSlot"/> now: <see cref="World.CanTrain"/>, which reads only the player's own state (a finished
    /// own building, its queue, the player's techs, buildings and gold), so it reveals nothing; any other slot is
    /// <see cref="TrainError.NoBuilding"/>.
    /// </summary>
    public bool CanTrain(int buildingSlot, int unitTypeId, out TrainError reason) => _world.CanTrain(_player, buildingSlot, unitTypeId, out reason);

    /// <summary>Whether the player may queue tech <paramref name="techId"/> at its own building in slot <paramref name="buildingSlot"/> now: <see cref="World.CanResearch"/> (own state only, as <see cref="CanTrain"/>).</summary>
    public bool CanResearch(int buildingSlot, int techId, out ResearchError reason) => _world.CanResearch(_player, buildingSlot, techId, out reason);

    /// <summary>
    /// M5-2: whether the player may place a building of <paramref name="typeId"/> with its anchor at
    /// <paramref name="anchorCell"/>, judged on what the player knows (docs/01 2026-10-09: <see cref="World.CanPlace"/> counts
    /// hidden enemy buildings, so the AI may not ask it). The rules, first broken reported: the type, the faction and its
    /// <c>requires</c>; the map; every footprint cell explored; terrain (passable, no ramp, one level: a cell blocked by a
    /// building or a node is judged by the next rules, not by the grid); the player's own buildings, enemy buildings it sees
    /// now, and its ghosts (the remembered footprint, also of a building gone unseen); resource nodes it knows (alive on its
    /// explored ground, or remembered after they went out of its sight); cost; a free building slot. Not checked: units in
    /// the way and the never-seal rule (the Build applies them, and a refused Build costs nothing). A hidden enemy building
    /// the player never saw doesn't refuse here (the Build will). Read-only and allocation-free.
    /// </summary>
    public bool CanPlace(int typeId, int anchorCell, out PlacementError reason)
    {
        reason = CheckPlace(typeId, anchorCell);
        return reason == PlacementError.None;
    }

    private PlacementError CheckPlace(int typeId, int anchorCell)
    {
        if ((uint)typeId >= (uint)_data.Buildings.Length) return PlacementError.UnknownType;
        BuildingDef def = _data.Buildings[typeId];
        if (Faction != def.Faction) return PlacementError.WrongFaction;
        if (!Requirements.Met(_world, _player, def.RequiresTechs, def.RequiresBuildings)) return PlacementError.Requires;
        NavGrid g = _world.NavGrid;
        int w = g.Width;
        if ((uint)anchorCell >= (uint)(w * g.Height)) return PlacementError.OffMap;
        int x0 = anchorCell % w, y0 = anchorCell / w, fw = def.FootprintWidth, fh = def.FootprintHeight;
        if (x0 + fw > w || y0 + fh > g.Height) return PlacementError.OffMap;
        // Explored before terrain: the player judges ground it has seen.
        for (int y = y0; y < y0 + fh; y++)
            for (int x = x0; x < x0 + fw; x++)
                if (!_fog.IsExplored(_player, y * w + x)) return PlacementError.Unexplored;
        int level = g.LevelAt(x0, y0);
        for (int y = y0; y < y0 + fh; y++)
            for (int x = x0; x < x0 + fw; x++)
            {
                NavFlags f = g.FlagsAt(x, y);
                if ((f & NavFlags.Ramp) != 0 || g.LevelAt(x, y) != level) return PlacementError.Blocked;
                if ((f & NavFlags.Blocked) == 0) continue;
                // A live node on ground the player explored: it saw it (no match spawns nodes later).
                if ((f & NavFlags.Resource) != 0) return PlacementError.Blocked;
                if ((f & NavFlags.Building) == 0) return PlacementError.Blocked; // cliff, border, a pocket: terrain
                int k = _buildings.SlotAt(x, y);
                if (k >= 0 && KnowsBuilding(k)) return PlacementError.Blocked;
            }
        // Remembered enemy buildings (their footprint as last seen; one may be gone or have moved slot).
        if (_fog.GhostCount(_player) > 0)
        {
            ReadOnlySpan<BuildingGhost> list = _fog.Ghosts(_player);
            for (int s = 0; s < list.Length; s++)
            {
                BuildingGhost gh = list[s];
                if (!gh.Known) continue;
                BuildingDef gd = _data.Buildings[gh.TypeId];
                if (Overlaps(gh.Cell, gd.FootprintWidth, gd.FootprintHeight, x0, y0, fw, fh, w)) return PlacementError.Blocked;
            }
        }
        // Nodes the player remembers that are gone (felled out of its sight, BUG-0540): it still believes they stand.
        int nodes = _resources.HighWater; // no node was ever past it
        for (int s = 0; s < nodes; s++)
        {
            NodeMemory m = _fog.NodeAt(_player, s);
            if (!m.Known || _resources.Alive[s] && _resources.Generation[s] == m.Generation) continue;
            ResourceDef rd = _data.Resources[m.TypeId];
            if (Overlaps(m.Cell, rd.FootprintWidth, rd.FootprintHeight, x0, y0, fw, fh, w)) return PlacementError.Blocked;
        }
        if (Gold < def.CostGold || Wood < def.CostWood) return PlacementError.CannotAfford;
        if (_buildings.FreeCount == 0) return PlacementError.StoreFull;
        return PlacementError.None;
    }

    /// <summary>Whether the player knows live building slot <paramref name="k"/> is there: its own, one it sees now, or one its ghost list holds (same building).</summary>
    private bool KnowsBuilding(int k) =>
        _buildings.Owner[k] == _player || _fog.CanSeeBuilding(_player, k) || _fog.Ghosts(_player)[k].Generation == _buildings.Generation[k];

    /// <summary>Whether a footprint at <paramref name="cell"/> of <paramref name="aw"/> x <paramref name="ah"/> overlaps the rectangle (<paramref name="x0"/>, <paramref name="y0"/>, <paramref name="bw"/>, <paramref name="bh"/>).</summary>
    private static bool Overlaps(int cell, int aw, int ah, int x0, int y0, int bw, int bh, int width)
    {
        int ax = cell % width, ay = cell / width;
        return ax < x0 + bw && x0 < ax + aw && ay < y0 + bh && y0 < ay + ah;
    }

    /// <summary>The first slot a cursor at <paramref name="slot"/> looks at: the one after it, 0 before the start, <paramref name="n"/> (none) past the end (no overflow at <c>int.MaxValue</c>).</summary>
    private static int First(int slot, int n) => slot < 0 ? 0 : slot >= n ? n : slot + 1;
}
