using System;
using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;

namespace Rts.Sim.Vision;

/// <summary>
/// Fog of war (M4-3a, docs/03 "Vision, detection, fog"): per player a row-major byte per map cell, 0 unexplored, 1
/// explored, 2 visible (<see cref="VisionConstants"/>), rebuilt by <see cref="VisionSystem"/> every
/// <see cref="VisionConstants.UpdateInterval"/> ticks from the player's own units and buildings, and the high-ground
/// reveals combat sets. Read-only outside the sim (the view's fog shader and minimap, M4-V4).
/// </summary>
/// <remarks>
/// <para>
/// A viewer stamps a precomputed circle (one per distinct radius, built here at load from squared integer cell distances,
/// no trig) centred on its own cell: a cell is inside when its centre is within the radius of the viewer's cell centre.
/// The high-ground rule (docs/02) is applied per cell: a cell above the viewer's level is visible only within
/// <see cref="VisionConstants.LipRadius"/>. The viewer's level is its own cell's, so a unit on a ramp sees as from the
/// lower level.
/// </para>
/// <para>
/// M4-4b-2: a Blinded unit stamps its blind status's smaller circle, and after the stamps each zone that blocks vision hides
/// its cells from every player but its owner, except the cells that player's own units standing inside it see. A unit
/// (BUG-0360) is hidden by its own centre, not its cell's: each blocking zone keeps, per player, which cells of its box the
/// units inside it saw and which the player's circles covered at the last update (<see cref="ZoneStore"/>, hashed with it).
/// </para>
/// <para>
/// State (hashed): the explored bits (a packed copy of "not unexplored", kept as cells are first seen), the visible bits
/// (a packed copy of "visible", rewritten at every update; combat reads them) and the reveals. The visible bits are a
/// function of where the units stood at the last update, which the state no longer holds, so they are state, not
/// derived (BUG-0215): a save (M6) stores them. Derived and not hashed: the byte map (the two bit sets per cell, for the
/// view), <see cref="Version"/> and the stamp boxes.
/// </para>
/// </remarks>
public sealed class FogStore
{
    private readonly World _world;
    private readonly UnitStore _units;
    private readonly BuildingStore _buildings;
    private readonly GameData _data;
    private readonly byte[] _levels;
    private readonly int _width;
    private readonly int _height;
    private readonly int _players;

    private readonly byte[][] _visibility;
    private readonly ulong[][] _explored;
    private readonly ulong[][] _visible;
    private readonly int[] _version;

    // Per (unit slot, player): the tick a high-ground reveal ends, and the slot's generation when it was set (a recycled
    // slot never inherits it).
    private readonly int[] _revealUntil;
    private readonly int[] _revealGeneration;

    // BUG-0270: the same pair per (building slot, player), for a tower's hit from high ground.
    private readonly int[] _buildingRevealUntil;
    private readonly int[] _buildingRevealGeneration;

    // M4-3b: per (player, building slot) the last-known building there (hashed), and per player the entries in use
    // (derived from them, kept as entries change).
    private readonly BuildingGhost[] _ghosts;
    private readonly int[] _ghostCount;
    private readonly int _buildingSlots;

    // Circle masks, ascending by radius: per mask, the half-width of each row from -R to R, and its squared cell limit.
    private readonly int[][] _halfWidths;
    private readonly int[] _limits;
    private readonly int[] _unitMask;
    private readonly int[] _statusMask; // M4-4b-2: per status, a blind's sight mask (-1 for other kinds)
    private readonly int[] _buildingMask;
    private readonly int[] _lipMask;

    // Scratch for one player's update: one int layer of row-span marks per viewer level, laid over the flow-field build
    // queue (idle in phase 12, never kept across phases, cleared at every update's start).
    private readonly int[] _scratch;
    private readonly bool[] _levelUsed = new bool[MapConstants.LevelCount];
    private readonly int[] _box = new int[4 * MapConstants.LevelCount]; // per level: min x, min y, max x, max y
    private readonly int[] _visibleBox; // per player: the box of the last update's stamps (every visible cell is inside)

    /// <summary>Creates the fog for <paramref name="players"/> players on <paramref name="map"/>, every cell unexplored.</summary>
    internal FogStore(World world, int players, Heightmap map, UnitStore units, BuildingStore buildings, GameData data, int[] scratch)
    {
        _world = world;
        _units = units;
        _buildings = buildings;
        _data = data;
        _levels = map.LevelArray;
        _width = map.Width;
        _height = map.Height;
        _players = players;
        int cells = _width * _height;
        if ((long)scratch.Length < (long)MapConstants.LevelCount * cells) throw new ArgumentException("fog scratch is smaller than an int a cell per level", nameof(scratch));
        // Combine sums the three layers by name (the levels of docs/02: 0-2).
        if (MapConstants.LevelCount != 3) throw new InvalidOperationException("FogStore.Combine is written for three levels");
        _scratch = scratch;
        _visibility = new byte[players][];
        _explored = new ulong[players][];
        _visible = new ulong[players][];
        for (int p = 0; p < players; p++)
        {
            _visibility[p] = new byte[cells];
            _explored[p] = new ulong[(cells + 63) / 64];
            _visible[p] = new ulong[(cells + 63) / 64];
        }
        _version = new int[players];
        _visibleBox = new int[4 * players];
        for (int p = 0; p < players; p++) _visibleBox[4 * p] = int.MaxValue; // empty
        _revealUntil = new int[units.Capacity * players];
        _revealGeneration = new int[units.Capacity * players];
        _buildingSlots = buildings.Capacity;
        _buildingRevealUntil = new int[_buildingSlots * players];
        _buildingRevealGeneration = new int[_buildingSlots * players];
        _ghosts = new BuildingGhost[players * _buildingSlots];
        _ghostCount = new int[players];
        for (int i = 0; i < cells; i++)
            if (_levels[i] > 0) MultiLevel = true;

        // One mask per distinct radius (sights, and the lip cut to each sight), ascending, so a bigger index covers a smaller one.
        // No cell is farther than the map's diagonal, so a bigger circle adds nothing (BUG-0216: not its longer side).
        int maxLimit = _width * _width + _height * _height;
        var limits = new int[2 * (data.Units.Length + data.Buildings.Length + data.Statuses.Length)];
        int count = 0;
        foreach (UnitDef u in data.Units) AddLimits(limits, ref count, u.Sight, maxLimit);
        foreach (BuildingDef b in data.Buildings) AddLimits(limits, ref count, b.Sight, maxLimit);
        // M4-4b-2: a Blinded unit stamps its blind's sight.
        foreach (StatusDef st in data.Statuses)
            if (st.Kind == StatusKind.Blind) AddLimits(limits, ref count, st.Sight, maxLimit);
        Array.Sort(limits, 0, count);
        int distinct = 0;
        for (int k = 0; k < count; k++)
            if (distinct == 0 || limits[k] != limits[distinct - 1]) limits[distinct++] = limits[k];
        _halfWidths = new int[distinct][];
        for (int m = 0; m < distinct; m++) _halfWidths[m] = Circle(limits[m]);
        _limits = limits[..distinct];
        _lipMask = new int[distinct];
        for (int m = 0; m < distinct; m++)
            _lipMask[m] = Array.BinarySearch(limits, 0, distinct, Math.Min(limits[m], Limit(VisionConstants.LipRadius, maxLimit)));
        _unitMask = new int[data.Units.Length];
        for (int t = 0; t < _unitMask.Length; t++) _unitMask[t] = MaskOf(limits, distinct, data.Units[t].Sight, maxLimit);
        _buildingMask = new int[data.Buildings.Length];
        for (int t = 0; t < _buildingMask.Length; t++) _buildingMask[t] = MaskOf(limits, distinct, data.Buildings[t].Sight, maxLimit);
        _statusMask = new int[data.Statuses.Length];
        for (int t = 0; t < _statusMask.Length; t++)
            _statusMask[t] = data.Statuses[t].Kind == StatusKind.Blind ? MaskOf(limits, distinct, data.Statuses[t].Sight, maxLimit) : -1;
    }

    /// <summary>Map width in cells (the visibility arrays are row-major, <c>y * Width + x</c>).</summary>
    public int Width => _width;

    /// <summary>Map height in cells.</summary>
    public int Height => _height;

    /// <summary>Number of players.</summary>
    public int PlayerCount => _players;

    /// <summary>
    /// <paramref name="player"/>'s fog, one byte per cell, row-major: <see cref="VisionConstants.Unexplored"/>,
    /// <see cref="VisionConstants.Explored"/> or <see cref="VisionConstants.Visible"/>. Changes only on an update tick;
    /// empty for no such player.
    /// </summary>
    public ReadOnlySpan<byte> Visibility(int player) =>
        (uint)player < (uint)_players ? _visibility[player] : ReadOnlySpan<byte>.Empty;

    /// <summary>How many fog updates ran for <paramref name="player"/> (+1 each): a view re-uploads its texture when it moves. Derived, not hashed; 0 for no such player.</summary>
    public int Version(int player) => (uint)player < (uint)_players ? _version[player] : 0;

    /// <summary>Whether <paramref name="cell"/> was inside one of <paramref name="player"/>'s sight circles at the last update; false out of range.</summary>
    public bool IsVisible(int player, int cell) =>
        (uint)player < (uint)_players && (uint)cell < (uint)_levels.Length && Bit(_visible[player], cell);

    /// <summary>Whether <paramref name="player"/> has ever seen <paramref name="cell"/> (explored or visible); false out of range.</summary>
    public bool IsExplored(int player, int cell) =>
        (uint)player < (uint)_players && (uint)cell < (uint)_levels.Length && Bit(_explored[player], cell);

    /// <summary>
    /// Whether <paramref name="player"/> sees live unit slot <paramref name="slot"/>: its own, its cell visible at the last
    /// update, or revealed to it by a high-ground hit. What the view shows; combat adds each unit's own sight right now
    /// (<see cref="VisionSystem"/>). M4-4b-2 / BUG-0360: a unit whose centre stands in another player's live zone that blocks
    /// vision is seen only through the eyes of <paramref name="player"/>'s units inside that zone, whatever its cell's
    /// centre; one outside every such zone is seen where <paramref name="player"/>'s circles covered its cell, also a cell
    /// the zone hides. False for a dead slot or no such player.
    /// </summary>
    public bool CanSeeUnit(int player, int slot)
    {
        if ((uint)player >= (uint)_players || (uint)slot >= (uint)_units.Capacity || !_units.Alive[slot]) return false;
        return _units.Owner[slot] == player || SeesUnit(player, slot);
    }

    /// <summary>Whether <paramref name="player"/> sees live building slot <paramref name="slot"/>: its own, any footprint cell visible at the last update, or (BUG-0270) a tower revealed to it by a hit from high ground. False for a dead slot or no such player.</summary>
    public bool CanSeeBuilding(int player, int slot)
    {
        if ((uint)player >= (uint)_players || (uint)slot >= (uint)_buildings.Capacity || !_buildings.Alive[slot]) return false;
        return _buildings.Owner[slot] == player || SeesBuildingCells(player, slot);
    }

    /// <summary>
    /// <paramref name="player"/>'s last-known enemy buildings (M4-3b, docs/02: ghosts in explored fog), indexed by building
    /// slot (<see cref="Entities.BuildingStore.Capacity"/> entries; an empty one has <see cref="BuildingGhost.Generation"/>
    /// 0). An entry is set (or refreshed) at every fog update the building is seen (any footprint cell visible), and kept
    /// while it isn't, also after the building is gone; it is dropped at the first update that shows any of its footprint
    /// cells while that building is gone. Changes only on an update tick; empty for no such player.
    /// </summary>
    public ReadOnlySpan<BuildingGhost> Ghosts(int player) =>
        (uint)player < (uint)_players ? _ghosts.AsSpan(player * _buildingSlots, _buildingSlots) : ReadOnlySpan<BuildingGhost>.Empty;

    /// <summary>How many entries of <see cref="Ghosts"/> are in use for <paramref name="player"/>; 0 for no such player.</summary>
    public int GhostCount(int player) => (uint)player < (uint)_players ? _ghostCount[player] : 0;

    /// <summary>Whether <paramref name="player"/>'s last-known list holds <paramref name="building"/> (its slot, with that generation).</summary>
    internal bool HasGhost(int player, EntityHandle building) =>
        (uint)player < (uint)_players && (uint)building.Index < (uint)_buildingSlots && building.Generation != 0
        && _ghosts[player * _buildingSlots + building.Index].Generation == building.Generation;

    /// <summary><paramref name="player"/>'s entry for building slot <paramref name="slot"/> (both in range).</summary>
    internal ref readonly BuildingGhost GhostAt(int player, int slot) => ref _ghosts[player * _buildingSlots + slot];

    /// <summary>Whether any cell of a footprint of type <paramref name="typeId"/> anchored at <paramref name="anchor"/> is visible to <paramref name="player"/>.</summary>
    internal bool SeesFootprint(int player, int typeId, int anchor)
    {
        ulong[] vis = _visible[player];
        BuildingDef def = _data.Buildings[typeId];
        for (int dy = 0; dy < def.FootprintHeight; dy++)
            for (int dx = 0; dx < def.FootprintWidth; dx++)
                if (Bit(vis, anchor + dy * _width + dx)) return true;
        return false;
    }

    /// <summary>
    /// Test seam (M4-3b): every cell explored for every player, as if the whole map had been seen once (visible cells stay
    /// visible). For the construction scenes whose subject is another placement rule; a match never calls it.
    /// </summary>
    internal void ExploreAllForTests()
    {
        for (int p = 0; p < _players; p++)
        {
            byte[] vis = _visibility[p];
            ulong[] bits = _explored[p];
            for (int c = 0; c < vis.Length; c++)
            {
                bits[c >> 6] |= 1UL << (c & 63);
                if (vis[c] == VisionConstants.Unexplored) vis[c] = VisionConstants.Explored;
            }
            _version[p]++;
        }
    }

    /// <summary>True when any cell of the map is above level 0: else the high-ground compares are skipped.</summary>
    internal bool MultiLevel { get; }

    /// <summary>
    /// Unit slot <paramref name="slot"/>'s cell is visible to <paramref name="player"/>, or the unit is revealed to it. While a
    /// zone that blocks vision lives (BUG-0360) whether such a zone hides the unit goes by the unit's own centre, as its
    /// statuses do, not by its cell's (<see cref="SeesUnitNearZones"/>).
    /// </summary>
    internal bool SeesUnit(int player, int slot)
    {
        if (_world.Zones.BlockerCount > 0) return SeesUnitNearZones(player, slot);
        return Bit(_visible[player], CellOf(_units.Position[slot])) || Revealed(player, slot);
    }

    /// <summary>Any footprint cell of building slot <paramref name="slot"/> is visible to <paramref name="player"/>, or (BUG-0270) the building is revealed to it.</summary>
    internal bool SeesBuildingCells(int player, int slot) =>
        SeesFootprint(player, _buildings.TypeId[slot], _buildings.Cell[slot]) || BuildingRevealed(player, slot);

    /// <summary>Whether building slot <paramref name="slot"/> is revealed to <paramref name="player"/> (BUG-0270: a tower's hit from high ground, not yet over, set on the building now in the slot).</summary>
    internal bool BuildingRevealed(int player, int slot)
    {
        int k = slot * _players + player;
        return _buildingRevealUntil[k] > _world.TickNumber && _buildingRevealGeneration[k] == _buildings.Generation[slot];
    }

    /// <summary>The tick building slot <paramref name="slot"/>'s reveal to <paramref name="player"/> ends; 0 when none was set on the building now in the slot.</summary>
    internal int BuildingRevealEnd(int player, int slot)
    {
        int k = slot * _players + player;
        return _buildingRevealGeneration[k] == _buildings.Generation[slot] ? _buildingRevealUntil[k] : 0;
    }

    /// <summary>Reveals building <paramref name="attacker"/> (a tower) to <paramref name="player"/> until tick <paramref name="until"/> (a later hit refreshes it).</summary>
    internal void RevealBuilding(EntityHandle attacker, int player, int until)
    {
        int k = attacker.Index * _players + player;
        _buildingRevealUntil[k] = until;
        _buildingRevealGeneration[k] = attacker.Generation;
    }

    /// <summary>Bit <paramref name="cell"/> of a packed cell set (64 cells a word).</summary>
    private static bool Bit(ulong[] bits, int cell) => (bits[cell >> 6] & (1UL << (cell & 63))) != 0;

    /// <summary>Whether unit slot <paramref name="slot"/> is revealed to <paramref name="player"/> (a high-ground hit's reveal not yet over, set on this unit, not an earlier one in the slot).</summary>
    internal bool Revealed(int player, int slot)
    {
        int k = slot * _players + player;
        // During tick t the world's tick number is t; between ticks it is the next tick's, so a reveal shows while it
        // will still hold on the next tick, as the hash counts it.
        return _revealUntil[k] > _world.TickNumber && _revealGeneration[k] == _units.Generation[slot];
    }

    /// <summary>The tick unit slot <paramref name="slot"/>'s reveal to <paramref name="player"/> ends (it shows while the tick number is below it); 0 when none was set on the unit now in the slot.</summary>
    internal int RevealEnd(int player, int slot)
    {
        int k = slot * _players + player;
        return _revealGeneration[k] == _units.Generation[slot] ? _revealUntil[k] : 0;
    }

    /// <summary>Reveals unit <paramref name="attacker"/> to <paramref name="player"/> until tick <paramref name="until"/> (a later hit refreshes it).</summary>
    internal void Reveal(EntityHandle attacker, int player, int until)
    {
        int k = attacker.Index * _players + player;
        _revealUntil[k] = until;
        _revealGeneration[k] = attacker.Generation;
    }

    /// <summary>The cell holding <paramref name="p"/> (m), clamped onto the map.</summary>
    internal int CellOf(Vector2 p)
    {
        int x = (int)(p.X / MapConstants.CellSize), y = (int)(p.Y / MapConstants.CellSize);
        if (!(x >= 0)) x = 0;
        else if (x >= _width) x = _width - 1;
        if (!(y >= 0)) y = 0;
        else if (y >= _height) y = _height - 1;
        return y * _width + x;
    }

    /// <summary>The centre (m) of the cell holding <paramref name="p"/>, clamped onto the map (M4-4b-2: whether a zone covers a unit's cell).</summary>
    internal Vector2 CellCentreOf(Vector2 p)
    {
        int c = CellOf(p);
        return new Vector2((c % _width + 0.5f) * MapConstants.CellSize, (c / _width + 0.5f) * MapConstants.CellSize);
    }

    /// <summary>The level of the cell holding <paramref name="p"/> (a ramp reports its lower level).</summary>
    internal int LevelAt(Vector2 p) => _levels[CellOf(p)];

    /// <summary>The cell a building stamps its sight from: the one holding its footprint's centre (an even side takes the higher of the two middle cells).</summary>
    internal int CentreCell(int buildingSlot)
    {
        int anchor = _buildings.Cell[buildingSlot];
        BuildingDef def = _data.Buildings[_buildings.TypeId[buildingSlot]];
        return anchor + def.FootprintHeight / 2 * _width + def.FootprintWidth / 2;
    }

    /// <summary>The level building slot <paramref name="slot"/> stands on (its centre cell's).</summary>
    internal int BuildingLevel(int slot) => _levels[CentreCell(slot)];

    /// <summary>
    /// One fog update for every player (docs/03 phase 12): every live own unit (any state) and building (a site under
    /// construction too) stamps its circle, then the cells it may see become visible and last update's visible cells that
    /// none sees now become explored. Allocation-free.
    /// </summary>
    /// <remarks>
    /// A stamp writes two numbers a row into its viewer level's layer (+1 where the row's span starts, -1 just past its
    /// end), so its cost is the circle's height, not its area; one pass per player then runs the sums along each row of
    /// the stamped box. A cell is seen when a layer at or above its own level covers it (the high-ground rule); the lip is
    /// stamped into the top layer, which covers every level.
    /// </remarks>
    internal void Update()
    {
        int n = _levels.Length;
        Array.Clear(_scratch, 0, MapConstants.LevelCount * n); // other phases leave their own data in it
        UnitStore u = _units;
        ReadOnlySpan<bool> bAlive = _buildings.Alive;
        ReadOnlySpan<int> bOwner = _buildings.Owner;
        ReadOnlySpan<int> bType = _buildings.TypeId;
        ZoneStore zones = _world.Zones;
        int[] inside = _world.Neighbors; // movement's scratch: free in phase 12 (and between ticks)
        for (int p = 0; p < _players; p++)
        {
            byte[] vis = _visibility[p];
            // M4-4b-2: whether a zone hides cells from p (one that blocks vision, of another owner), and p's units inside one.
            bool blocked = zones.BlockerCount > 0 && BlocksFor(zones, p);
            bool blinded = u.Statuses.BlindedUnits > 0;
            int insideCount = 0;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i] || u.Owner[i] != p) continue;
                int m = blinded ? UnitMask(i) : _unitMask[u.TypeId[i]];
                if (m >= 0) StampViewer(vis, CellOf(u.Position[i]), m);
                if (blocked && InsideBlocker(zones, p, u.Position[i])) inside[insideCount++] = i;
            }
            for (int j = 0; j < bAlive.Length; j++)
            {
                if (!bAlive[j] || bOwner[j] != p) continue;
                int m = _buildingMask[bType[j]];
                if (m >= 0) StampViewer(vis, CentreCell(j), m);
            }
            if (blocked) MarkBlocked(zones, p, vis, inside, insideCount);
            Combine(p, blocked ? zones : null);
            if (blocked) ClearBlocked(zones, p, vis);
            UpdateGhosts(p);
        }
    }

    /// <summary>
    /// The fog mask unit slot <paramref name="i"/> stamps: its type's sight, or (M4-4b-2) its blind's when that circle is
    /// smaller (masks ascend by radius). The derived sight; nothing here is hashed.
    /// </summary>
    private int UnitMask(int i)
    {
        int m = _unitMask[_units.TypeId[i]];
        int b = _units.Statuses.BlindOf(i);
        if (b >= 0 && _statusMask[b] >= 0 && _statusMask[b] < m) m = _statusMask[b];
        return m;
    }

    /// <summary>Whether live zone <paramref name="k"/> hides cells from <paramref name="p"/> (M4-4b-2): it blocks vision and is another player's.</summary>
    private static bool HidesFrom(ZoneStore zones, int k, int p) => zones.Alive[k] && zones.BlocksVision(k) && zones.Owner[k] != p;

    /// <summary>Whether any live zone hides cells from player <paramref name="p"/> (M4-4b-2).</summary>
    private static bool BlocksFor(ZoneStore zones, int p)
    {
        for (int k = 0; k < zones.End; k++)
            if (HidesFrom(zones, k, p)) return true;
        return false;
    }

    /// <summary>Whether <paramref name="pos"/> (a unit of <paramref name="p"/>) stands inside a zone that hides cells from <paramref name="p"/> (M4-4b-2).</summary>
    private static bool InsideBlocker(ZoneStore zones, int p, Vector2 pos)
    {
        for (int k = 0; k < zones.End; k++)
            if (HidesFrom(zones, k, p) && zones.Contains(k, pos)) return true;
        return false;
    }

    /// <summary>
    /// M4-4b-2, after <paramref name="p"/>'s stamps: every cell whose centre is within a zone that hides cells from
    /// <paramref name="p"/> is marked blocked (the high bit of its visibility byte) unless one of <paramref name="p"/>'s units
    /// standing inside that same zone (<paramref name="inside"/>: p's units inside any such zone) sees it by the stamp's rule
    /// with its sight now (Blinded or not). <see cref="Combine"/> treats a blocked cell as uncovered. The pass covers each
    /// zone's bounding box only. BUG-0360: it also records, for every cell of the box, whether such a unit inside the zone
    /// sees it (the zone's seen-from-inside bits, which <see cref="SeesUnit"/> reads for a unit standing in the zone).
    /// </summary>
    private void MarkBlocked(ZoneStore zones, int p, byte[] vis, int[] inside, int insideCount)
    {
        int side = zones.BoxSide;
        for (int k = 0; k < zones.End; k++)
        {
            if (!HidesFrom(zones, k, p)) continue;
            zones.ClearFogBits(k, p);
            Vector2 centre = zones.Center[k];
            float r = zones.Radius(k), r2 = r * r;
            ZoneBox(centre, r, out int x0, out int y0, out int x1, out int y1);
            for (int y = y0; y <= y1; y++)
            {
                float cy = (y + 0.5f) * MapConstants.CellSize - centre.Y;
                for (int x = x0; x <= x1; x++)
                {
                    float cx = (x + 0.5f) * MapConstants.CellSize - centre.X;
                    bool centreInside = cx * cx + cy * cy <= r2;
                    if (!centreInside && insideCount == 0) continue; // nothing to record: no unit of p inside
                    int c = y * _width + x;
                    bool seen = SeenFromInside(zones, k, c, x, y, inside, insideCount);
                    if (seen) zones.SetFogBit(false, k, p, (y - y0) * side + (x - x0));
                    else if (centreInside) vis[c] |= BlockedBit;
                }
            }
        }
    }

    /// <summary>
    /// BUG-0360, in <see cref="Combine"/> before the layers are zeroed: for every zone that hides cells from
    /// <paramref name="p"/>, the cells of its box that <paramref name="p"/>'s stamps cover by the high-ground rule, zones
    /// aside (the zone's covered bits: a unit standing outside every zone in a cell the zone hides is seen if they are set).
    /// Each row's marks are summed from the stamped box's left edge (<paramref name="nx0"/>; no mark lies left of it).
    /// </summary>
    private void RecordCovered(ZoneStore zones, int p, int nx0, int ny0, int nx1, int ny1)
    {
        if (nx0 == int.MaxValue) return; // nothing stamped: nothing covered
        int[] s = _scratch;
        int w = _width, n = _levels.Length, n2 = 2 * n, side = zones.BoxSide;
        for (int k = 0; k < zones.End; k++)
        {
            if (!HidesFrom(zones, k, p)) continue;
            ZoneBox(zones.Center[k], zones.Radius(k), out int x0, out int y0, out int x1, out int y1);
            int ya = Math.Max(y0, ny0), yb = Math.Min(y1, ny1), xb = Math.Min(x1, nx1);
            for (int y = ya; y <= yb; y++)
            {
                int run0 = 0, run1 = 0, run2 = 0;
                for (int x = nx0; x <= xb; x++)
                {
                    int c = y * w + x;
                    run0 += s[c];
                    run1 += s[n + c];
                    run2 += s[n2 + c];
                    if (x < x0) continue;
                    int level = _levels[c];
                    if (run2 > 0 || (run1 > 0 && level <= 1) || (run0 > 0 && level == 0))
                        zones.SetFogBit(true, k, p, (y - y0) * side + (x - x0));
                }
            }
        }
    }

    /// <summary>
    /// BUG-0360: <see cref="SeesUnit"/> while a zone that blocks vision lives. A unit whose own centre stands inside one or
    /// more zones that hide cells from <paramref name="player"/> (the statuses' test, live zones) is seen only where, at the
    /// last update, a unit of <paramref name="player"/> standing inside each of those zones saw its cell (a zone made since
    /// has no record yet: it hides at once). A unit outside every such zone is seen where its cell is visible, or where its
    /// cell is one a live zone hides by its centre and <paramref name="player"/>'s circles covered it at the last update.
    /// </summary>
    private bool SeesUnitNearZones(int player, int slot)
    {
        ZoneStore zones = _world.Zones;
        Vector2 pos = _units.Position[slot];
        int c = CellOf(pos);
        int x = c % _width, y = c / _width;
        bool inside = false;
        for (int k = 0; k < zones.End; k++)
        {
            if (!HidesFrom(zones, k, player) || !zones.Contains(k, pos)) continue;
            inside = true;
            int bit = BoxBit(zones, k, x, y);
            if (bit < 0 || !zones.FogBit(false, k, player, bit)) return Revealed(player, slot);
        }
        if (inside || Bit(_visible[player], c)) return true;
        var centre = new Vector2((x + 0.5f) * MapConstants.CellSize, (y + 0.5f) * MapConstants.CellSize);
        for (int k = 0; k < zones.End; k++)
        {
            if (!HidesFrom(zones, k, player) || !zones.Contains(k, centre)) continue;
            int bit = BoxBit(zones, k, x, y);
            if (bit >= 0 && zones.FogBit(true, k, player, bit)) return true;
        }
        return Revealed(player, slot);
    }

    /// <summary>Cell (<paramref name="x"/>, <paramref name="y"/>)'s bit in live zone <paramref name="k"/>'s fog record, or -1 outside its box.</summary>
    private int BoxBit(ZoneStore zones, int k, int x, int y)
    {
        ZoneBox(zones.Center[k], zones.Radius(k), out int x0, out int y0, out int x1, out int y1);
        if (x < x0 || x > x1 || y < y0 || y > y1) return -1;
        return (y - y0) * zones.BoxSide + (x - x0);
    }

    /// <summary>Clears the blocked marks <see cref="MarkBlocked"/> left outside the cells <see cref="Combine"/> rewrote (M4-4b-2).</summary>
    private void ClearBlocked(ZoneStore zones, int p, byte[] vis)
    {
        for (int k = 0; k < zones.End; k++)
        {
            if (!HidesFrom(zones, k, p)) continue;
            ZoneBox(zones.Center[k], zones.Radius(k), out int x0, out int y0, out int x1, out int y1);
            for (int y = y0; y <= y1; y++)
                for (int c = y * _width + x0, end = y * _width + x1; c <= end; c++)
                    vis[c] &= StateMask;
        }
    }

    /// <summary>The cells (inclusive, clamped onto the map) whose centres may lie within <paramref name="r"/> m of <paramref name="centre"/>.</summary>
    private void ZoneBox(Vector2 centre, float r, out int x0, out int y0, out int x1, out int y1)
    {
        x0 = Math.Max(0, (int)MathF.Floor((centre.X - r) / MapConstants.CellSize));
        y0 = Math.Max(0, (int)MathF.Floor((centre.Y - r) / MapConstants.CellSize));
        x1 = Math.Min(_width - 1, (int)MathF.Floor((centre.X + r) / MapConstants.CellSize));
        y1 = Math.Min(_height - 1, (int)MathF.Floor((centre.Y + r) / MapConstants.CellSize));
    }

    /// <summary>
    /// Whether one of the units in <paramref name="inside"/> that stands inside zone <paramref name="k"/> sees cell
    /// <paramref name="c"/> (at <paramref name="x"/>, <paramref name="y"/>) by the stamp's rule: within its circle now, and
    /// not above its level unless within its lip.
    /// </summary>
    private bool SeenFromInside(ZoneStore zones, int k, int c, int x, int y, int[] inside, int insideCount)
    {
        for (int n = 0; n < insideCount; n++)
        {
            int i = inside[n];
            Vector2 pos = _units.Position[i];
            if (!zones.Contains(k, pos)) continue;
            int m = UnitMask(i);
            if (m < 0) continue;
            int uc = CellOf(pos);
            int dx = x - uc % _width, dy = y - uc / _width;
            int d2 = dx * dx + dy * dy;
            if (d2 > _limits[m]) continue;
            if (MultiLevel && _levels[c] > _levels[uc] && d2 > _limits[_lipMask[m]]) continue;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Player <paramref name="p"/>'s last-known list after its fog update (M4-3b): every enemy building with a footprint cell
    /// visible now is recorded or refreshed in its slot's entry; an entry whose building is not seen now is kept, unless
    /// that building is gone (its slot free, or holding a newer one) and a cell of the remembered footprint is visible now,
    /// which drops it. One pass over the building slots; allocation-free.
    /// </summary>
    private void UpdateGhosts(int p)
    {
        ReadOnlySpan<bool> alive = _buildings.Alive;
        ReadOnlySpan<int> owner = _buildings.Owner;
        ReadOnlySpan<int> generation = _buildings.Generation;
        ReadOnlySpan<int> type = _buildings.TypeId;
        ReadOnlySpan<int> cell = _buildings.Cell;
        int head = p * _buildingSlots;
        for (int j = 0; j < _buildingSlots; j++)
        {
            ref BuildingGhost g = ref _ghosts[head + j];
            bool enemy = alive[j] && owner[j] != p;
            if (enemy && (SeesFootprint(p, type[j], cell[j]) || BuildingRevealed(p, j))) // BUG-0270: a revealed tower is seen
            {
                if (g.Generation == 0) _ghostCount[p]++;
                g = new BuildingGhost(generation[j], type[j], cell[j], owner[j]);
                continue;
            }
            if (g.Generation == 0 || (enemy && generation[j] == g.Generation)) continue; // nothing known, or still there unseen
            if (!SeesFootprint(p, g.TypeId, g.Cell)) continue; // gone, but the player hasn't looked yet
            g = default;
            _ghostCount[p]--;
        }
    }

    /// <summary>
    /// A viewer of mask <paramref name="m"/> in cell <paramref name="c"/>: its circle into its level's layer, and the lip
    /// into the top layer when a higher cell is that near. Skipped when the same or a bigger circle was stamped from the
    /// same cell in this update (a blob's units share cells): the biggest mask stamped from a cell, + 1, rides in the high
    /// bits of the player's visibility byte there until <see cref="Combine"/> (which rewrites every cell of the box,
    /// stamp centres included) clears it; nothing reads the bytes in between.
    /// </summary>
    private void StampViewer(byte[] vis, int c, int m)
    {
        if (vis[c] >> MarkShift > m) return;
        if (m < MaxMark) vis[c] = (byte)((vis[c] & StateMask) | ((m + 1) << MarkShift));
        int cx = c % _width, cy = c / _width, level = _levels[c];
        Stamp(level, cx, cy, _halfWidths[m]);
        if (!MultiLevel || level >= MapConstants.MaxLevel) return;
        int[] lip = _halfWidths[_lipMask[m]];
        if (HigherWithin(cx, cy, lip, level)) Stamp(MapConstants.MaxLevel, cx, cy, lip);
    }

    /// <summary>Adds a circle (row half-widths <paramref name="half"/>) around (cx, cy) to layer <paramref name="layer"/>: +1 at each row's first cell, -1 past its last.</summary>
    private void Stamp(int layer, int cx, int cy, int[] half)
    {
        int[] s = _scratch;
        int w = _width, r = half.Length >> 1, origin = layer * _levels.Length;
        int y0 = cy - r, y1 = cy + r;
        if (y0 < 0) y0 = 0;
        if (y1 >= _height) y1 = _height - 1;
        for (int y = y0; y <= y1; y++)
        {
            int h = half[y - cy + r];
            int x0 = cx - h, x1 = cx + h;
            if (x0 < 0) x0 = 0;
            if (x1 >= w) x1 = w - 1;
            int row = origin + y * w;
            s[row + x0]++;
            if (x1 + 1 < w) s[row + x1 + 1]--;
        }
        int xMin = cx - r, xMax = cx + r; // the widest row's span bounds every row's
        if (xMin < 0) xMin = 0;
        if (xMax >= w) xMax = w - 1;
        int b = 4 * layer;
        if (!_levelUsed[layer])
        {
            _levelUsed[layer] = true;
            _box[b] = xMin;
            _box[b + 1] = y0;
            _box[b + 2] = xMax;
            _box[b + 3] = y1;
            return;
        }
        if (xMin < _box[b]) _box[b] = xMin;
        if (y0 < _box[b + 1]) _box[b + 1] = y0;
        if (xMax > _box[b + 2]) _box[b + 2] = xMax;
        if (y1 > _box[b + 3]) _box[b + 3] = y1;
    }

    /// <summary>Whether any cell of the circle <paramref name="half"/> around (cx, cy) is above <paramref name="level"/>: else the lip adds nothing.</summary>
    private bool HigherWithin(int cx, int cy, int[] half, int level)
    {
        int r = half.Length >> 1;
        int y0 = cy - r, y1 = cy + r;
        if (y0 < 0) y0 = 0;
        if (y1 >= _height) y1 = _height - 1;
        for (int y = y0; y <= y1; y++)
        {
            int h = half[y - cy + r];
            int x0 = cx - h, x1 = cx + h;
            if (x0 < 0) x0 = 0;
            if (x1 >= _width) x1 = _width - 1;
            for (int c = y * _width + x0, end = y * _width + x1; c <= end; c++)
                if (_levels[c] > level) return true;
        }
        return false;
    }

    /// <summary>
    /// Player <paramref name="p"/>'s new fog from the stamped layers, over the box holding last update's visible cells and
    /// this update's stamps: covered cells its viewers may see become visible (the first sight sets the explored bit), the
    /// rest that were visible become explored. Then (BUG-0360, with <paramref name="blockers"/>: a zone hides cells from
    /// <paramref name="p"/>) each such zone records the cells of its box the stamps cover, and the stamped boxes are zeroed
    /// for the next player.
    /// </summary>
    private void Combine(int p, ZoneStore? blockers)
    {
        byte[] vis = _visibility[p];
        ulong[] bits = _explored[p];
        ulong[] seen = _visible[p];
        byte[] levels = _levels;
        int[] s = _scratch;
        int w = _width, n = levels.Length, n2 = 2 * n;
        int vb = 4 * p;
        // This update's stamped box: the union of the layers' boxes (empty: x0 = int.MaxValue).
        int nx0 = int.MaxValue, ny0 = 0, nx1 = 0, ny1 = 0;
        for (int layer = 0; layer < MapConstants.LevelCount; layer++)
        {
            if (!_levelUsed[layer]) continue;
            int b = 4 * layer;
            if (nx0 == int.MaxValue)
            {
                nx0 = _box[b];
                ny0 = _box[b + 1];
                nx1 = _box[b + 2];
                ny1 = _box[b + 3];
                continue;
            }
            if (_box[b] < nx0) nx0 = _box[b];
            if (_box[b + 1] < ny0) ny0 = _box[b + 1];
            if (_box[b + 2] > nx1) nx1 = _box[b + 2];
            if (_box[b + 3] > ny1) ny1 = _box[b + 3];
        }
        // The pass covers it and last update's box (its visible cells may have to turn explored).
        int ux0 = _visibleBox[vb], uy0 = _visibleBox[vb + 1], ux1 = _visibleBox[vb + 2], uy1 = _visibleBox[vb + 3];
        if (ux0 == int.MaxValue)
        {
            ux0 = nx0;
            uy0 = ny0;
            ux1 = nx1;
            uy1 = ny1;
        }
        else if (nx0 != int.MaxValue)
        {
            if (nx0 < ux0) ux0 = nx0;
            if (ny0 < uy0) uy0 = ny0;
            if (nx1 > ux1) ux1 = nx1;
            if (ny1 > uy1) uy1 = ny1;
        }
        for (int y = uy0; ux0 != int.MaxValue && y <= uy1; y++)
        {
            int run0 = 0, run1 = 0, run2 = 0;
            for (int c = y * w + ux0, end = y * w + ux1; c <= end; c++)
            {
                run0 += s[c];
                run1 += s[n + c];
                run2 += s[n2 + c];
                int level = levels[c];
                int raw = vis[c];
                int state = raw & StateMask; // without a stamp mark or a zone's blocked mark
                // The high-ground rule: a layer sees its own level and below; the top one (and the lip) every level.
                // M4-4b-2: a cell a zone hides (marked blocked) is uncovered whatever the layers say.
                if ((raw & BlockedBit) == 0 && (run2 > 0 || (run1 > 0 && level <= 1) || (run0 > 0 && level == 0)))
                {
                    if (state != VisionConstants.Visible)
                    {
                        seen[c >> 6] |= 1UL << (c & 63);
                        if (state == VisionConstants.Unexplored) bits[c >> 6] |= 1UL << (c & 63);
                    }
                    vis[c] = VisionConstants.Visible;
                }
                else if (state == VisionConstants.Visible)
                {
                    seen[c >> 6] &= ~(1UL << (c & 63));
                    vis[c] = VisionConstants.Explored;
                }
                else
                {
                    vis[c] = (byte)state;
                }
            }
        }
        _visibleBox[vb] = nx0;
        _visibleBox[vb + 1] = ny0;
        _visibleBox[vb + 2] = nx1;
        _visibleBox[vb + 3] = ny1;
        if (blockers != null) RecordCovered(blockers, p, nx0, ny0, nx1, ny1);
        for (int layer = 0; layer < MapConstants.LevelCount; layer++)
        {
            if (!_levelUsed[layer]) continue;
            _levelUsed[layer] = false;
            int b = 4 * layer, x0 = _box[b], x1 = _box[b + 2] + 1 < w ? _box[b + 2] + 1 : w - 1;
            for (int y = _box[b + 1]; y <= _box[b + 3]; y++)
                Array.Clear(s, layer * n + y * w + x0, x1 - x0 + 1);
        }
        _version[p]++;
    }

    /// <summary>
    /// Mixes the fog's state into a state hash: every player's explored bits, then every player's visible bits (64 cells
    /// a word; BUG-0215: combat reads them, and they follow from the positions at the last update, not the ones now), then
    /// the count and every reveal still in force at tick <paramref name="tick"/> (slot, player, end). The byte map,
    /// versions and boxes are derived.
    /// </summary>
    internal void AddToHash(ref StateHasher h, int tick)
    {
        for (int p = 0; p < _players; p++)
        {
            ulong[] bits = _explored[p];
            for (int k = 0; k < bits.Length; k++) h.AddWord(bits[k]);
        }
        for (int p = 0; p < _players; p++)
        {
            ulong[] bits = _visible[p];
            for (int k = 0; k < bits.Length; k++) h.AddWord(bits[k]);
        }
        int live = 0;
        for (int k = 0; k < _revealUntil.Length; k++)
            if (RevealLive(k, tick)) live++;
        // BUG-0270: the towers' reveals in force, only while any is (bit 30 of the count word flags them), so a match
        // without one hashes as before.
        int buildingLive = 0;
        for (int k = 0; k < _buildingRevealUntil.Length; k++)
            if (BuildingRevealLive(k, tick)) buildingLive++;
        h.Add(live | (buildingLive > 0 ? 1 << 30 : 0));
        if (live > 0) AddRevealsToHash(ref h, tick);
        if (buildingLive > 0)
        {
            h.Add(buildingLive);
            for (int k = 0; k < _buildingRevealUntil.Length; k++)
            {
                if (!BuildingRevealLive(k, tick)) continue;
                h.Add(k);
                h.Add(_buildingRevealUntil[k]);
            }
        }
        AddGhostsToHash(ref h);
    }

    /// <summary>Whether building reveal entry <paramref name="k"/> (slot x players + player) is in force at tick <paramref name="tick"/> on the building it was set on.</summary>
    private bool BuildingRevealLive(int k, int tick)
    {
        int slot = k / _players;
        return _buildingRevealUntil[k] > tick && _buildings.Alive[slot] && _buildingRevealGeneration[k] == _buildings.Generation[slot];
    }

    /// <summary>
    /// M4-3b: every player's last-known list, only when some player has an entry (so a match where no enemy building was
    /// ever seen hashes as before): each player's count, then each entry in use (slot and fields), slot order.
    /// </summary>
    private void AddGhostsToHash(ref StateHasher h)
    {
        int total = 0;
        for (int p = 0; p < _players; p++) total += _ghostCount[p];
        if (total == 0) return;
        for (int p = 0; p < _players; p++)
        {
            h.Add(_ghostCount[p]);
            int head = p * _buildingSlots;
            for (int j = 0; j < _buildingSlots; j++)
            {
                BuildingGhost g = _ghosts[head + j];
                if (g.Generation == 0) continue;
                h.Add(j);
                h.Add(g.Generation);
                h.Add(g.TypeId);
                h.Add(g.Cell);
                h.Add(g.Owner);
            }
        }
    }

    /// <summary>Every reveal in force at tick <paramref name="tick"/>: its entry index and end tick, entry order.</summary>
    private void AddRevealsToHash(ref StateHasher h, int tick)
    {
        for (int k = 0; k < _revealUntil.Length; k++)
        {
            if (!RevealLive(k, tick)) continue;
            h.Add(k);
            h.Add(_revealUntil[k]);
        }
    }

    /// <summary>Whether reveal entry <paramref name="k"/> (slot x players + player) is in force at tick <paramref name="tick"/> on the unit it was set on.</summary>
    private bool RevealLive(int k, int tick)
    {
        int slot = k / _players;
        return _revealUntil[k] > tick && _units.Alive[slot] && _revealGeneration[k] == _units.Generation[slot];
    }

    /// <summary>The visibility byte's state bits (0-2); the bits above hold a stamp mark during an update only.</summary>
    private const int StateMask = 3;

    /// <summary>Where a stamp mark starts in the visibility byte.</summary>
    private const int MarkShift = 2;

    /// <summary>
    /// Largest mark (5 bits, bits 2-6; M4-4b-2 took bit 7 for <see cref="BlockedBit"/>): masks from this index on are never
    /// skipped (no shipped data has that many radii; a skip is only a saving).
    /// </summary>
    private const int MaxMark = 31;

    /// <summary>M4-4b-2: the visibility byte's top bit marks a cell a zone hides during an update only (cleared by its end).</summary>
    private const byte BlockedBit = 0x80;

    /// <summary>The largest squared cell distance inside a sight of <paramref name="sight"/> m: (dx^2 + dy^2) x cell^2 &lt;= sight^2.</summary>
    private static int Limit(float sight, int maxLimit)
    {
        double cells = (double)sight / MapConstants.CellSize;
        double limit = Math.Floor(cells * cells);
        return limit >= maxLimit ? maxLimit : (int)limit;
    }

    private static void AddLimits(int[] limits, ref int count, float sight, int maxLimit)
    {
        if (!(sight > 0f)) return;
        limits[count++] = Limit(sight, maxLimit);
        limits[count++] = Math.Min(Limit(sight, maxLimit), Limit(VisionConstants.LipRadius, maxLimit));
    }

    private static int MaskOf(int[] limits, int distinct, float sight, int maxLimit) =>
        sight > 0f ? Array.BinarySearch(limits, 0, distinct, Limit(sight, maxLimit)) : -1;

    /// <summary>The half-width of each row (dy from -R to R) of the cells with dx^2 + dy^2 &lt;= <paramref name="limit"/>.</summary>
    private static int[] Circle(int limit)
    {
        int r = 0;
        while ((r + 1) * (r + 1) <= limit) r++;
        var half = new int[2 * r + 1];
        for (int dy = -r; dy <= r; dy++)
        {
            int h = 0;
            while ((h + 1) * (h + 1) + dy * dy <= limit) h++;
            half[dy + r] = h;
        }
        return half;
    }
}
