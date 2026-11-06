using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using Rts.Sim.Map;

namespace Rts.Sim.Data;

// M6-S7: the hand-made maps, game/data/maps/*.json (docs/03 "Implementation (M6-S7)"). Load time only, so it allocates
// freely; output in file order (the files' names in ordinal order), never a Dictionary's.
public static partial class DataLoader
{
    /// <summary>
    /// Every <c>maps/*.json</c> under <paramref name="dataDir"/> in ordinal order of the file names (the folder is optional:
    /// none means no hand-made maps). Each is checked whole (<see cref="BuildMap"/>); a map with an error is left out, and
    /// a repeated id is an error at the second file's <c>id</c>. The node checks need <c>rules.json</c> and
    /// <c>resources.json</c>; when either failed to load, the maps are read for their own fields only.
    /// </summary>
    private static MapDef[] BuildMaps(Checker c, string dataDir, RulesDef? rules, ResourceDef[]? resources)
    {
        string dir = Path.Combine(dataDir, "maps");
        if (!Directory.Exists(dir)) return Array.Empty<MapDef>();
        string[] files = Directory.GetFiles(dir, "*.json");
        for (int i = 0; i < files.Length; i++) files[i] = Path.GetFileName(files[i]);
        Array.Sort(files, StringComparer.Ordinal); // file order: the maps' order in GameData.Maps and in the hash
        if (files.Length > DataLimits.MaxMaps)
        {
            c.CurrentFile = "maps/";
            c.Error("", $"{files.Length} map files, above the maximum {DataLimits.MaxMaps}");
            return Array.Empty<MapDef>();
        }
        var maps = new List<MapDef>();
        var ids = new List<string>();
        foreach (string file in files)
        {
            MapJson? j = c.Read($"maps/{file}", DataJsonContext.Default.MapJson);
            if (j == null) continue;
            int before = c.Errors.Count;
            MapDef? map = BuildMap(c, j, maps.Count, rules, resources);
            if (map != null && ids.Contains(map.Key))
            {
                c.Error("id", $"duplicate map id '{map.Key}'");
                continue;
            }
            if (map == null || c.Errors.Count != before) continue;
            ids.Add(map.Key);
            maps.Add(map);
        }
        return maps.ToArray();
    }

    /// <summary>
    /// M6-S8: checks one map file anywhere on disk (the <c>mirror-map</c> tool's output) exactly as <see cref="LoadAll"/>
    /// checks a <c>maps/*.json</c>, against <c>common/rules.json</c> and <c>common/resources.json</c> under
    /// <paramref name="dataDir"/> (the other data files and the shipped maps are not read, so a broken shipped copy of the
    /// same map doesn't block its rebuild). Every error found; empty when the file would load.
    /// </summary>
    public static IReadOnlyList<DataError> CheckMapFile(string dataDir, string mapPath)
    {
        var c = new Checker(dataDir);
        RulesJson? rulesJson = c.Read("common/rules.json", DataJsonContext.Default.RulesJson);
        RulesDef? rules = rulesJson == null ? null : BuildRules(c, rulesJson);
        ResourceFileJson? resourcesJson = c.Read("common/resources.json", DataJsonContext.Default.ResourceFileJson);
        ResourceDef[]? resources = resourcesJson == null ? null : BuildResources(c, resourcesJson);
        if (c.Errors.Count > 0) return c.Errors;
        MapJson? j = c.Read(Path.GetFullPath(mapPath), DataJsonContext.Default.MapJson); // a rooted path: Path.Combine keeps it
        if (j != null) BuildMap(c, j, 0, rules, resources);
        return c.Errors;
    }

    /// <summary>
    /// One map file, checked in stages so one mistake is one error: its own fields (id, names, players 2-4, size 128 or
    /// 256: nothing else is read without a size); the text rows, ramps, starts, mines and forests as written (each on the
    /// map, the rows of the size, levels 0-2, trees 'T' or '.'); then, all of those clean, the terrain built (each ramp a
    /// slope of one level up to the next, long enough for 30 degrees) and the generator's contract: each start on open, flat
    /// ground of one level (the start site disc, <see cref="MapGenParams.StartSiteRadius"/>), every start on one level and
    /// at least half the map side from every other (<see cref="MapGenerator.Apart"/>), each node on open ground, touching no
    /// mine and off every start's clear disc, each start with <c>rules.json</c> <c>startMines.count</c> mines within
    /// <see cref="MapGenParams.StartMineDistance"/> and a tree within <see cref="MapGenParams.StartForestDistance"/> (the
    /// generator's defaults), and the nodes leaving every passable cell reachable from every other. Null on any error.
    /// </summary>
    private static MapDef? BuildMap(Checker c, MapJson j, int index, RulesDef? rules, ResourceDef[]? resources)
    {
        int before = c.Errors.Count;
        string id = c.Id(j.Id, "id");
        if (id.Length > DataLimits.MaxMapIdLength)
            c.Error("id", $"id '{id}' is longer than {DataLimits.MaxMapIdLength} characters");
        string name = c.Text(j.DisplayName, "displayName");
        string description = c.Text(j.Description, "description");
        int players = 0;
        if (j.Players == null) c.Error("players", "missing required field");
        else if (j.Players < DataLimits.MinMapPlayers || j.Players > DataLimits.MaxMapPlayers)
            c.Error("players", $"{j.Players} is outside {DataLimits.MinMapPlayers}-{DataLimits.MaxMapPlayers}");
        else players = j.Players.Value;
        int size = 0;
        if (j.Size == null) c.Error("size", "missing required field");
        else if (!DataLimits.MapSizes.Contains(j.Size.Value))
            c.Error("size", $"{j.Size} is not a map size ({string.Join(" or ", DataLimits.MapSizes)} cells)");
        else size = j.Size.Value;
        if (size == 0 || players == 0) return null; // the rest is read against the side and the player count

        byte[]? levels = ReadTerrain(c, j.Terrain, size);
        var ramps = new List<MapRampDef>();
        for (int i = 0; j.Ramps != null && i < j.Ramps.Count; i++)
            if (ReadRamp(c, j.Ramps[i], $"ramps[{i}]", size) is MapRampDef r) ramps.Add(r);
        var starts = new List<int>();
        if (c.Obj(j.Starts, "starts") != null)
        {
            if (j.Starts!.Count != players) c.Error("starts", $"{j.Starts.Count} start locations for {players} players");
            for (int i = 0; i < j.Starts.Count; i++)
                if (ReadCell(c, j.Starts[i], $"starts[{i}]", size, out int cell)) starts.Add(cell);
        }
        var mines = new List<(int Cell, int Gold, string Path)>();
        if (c.Obj(j.Mines, "mines") != null)
            for (int i = 0; i < j.Mines!.Count; i++)
            {
                string p = $"mines[{i}]";
                MapMineJson? m = c.Obj(j.Mines[i], p);
                if (m == null) continue;
                bool on = ReadCell(c, new MapCellJson { X = m.X, Y = m.Y }, p, size, out int cell);
                int gold = c.Int(m.Gold, p + ".gold", 1);
                if (on && gold > 0) mines.Add((cell, gold, p));
            }
        var trees = new List<(int Cell, string Path)>();
        int forests = 0;
        if (c.Obj(j.Forests, "forests") != null)
            for (int f = 0; f < j.Forests!.Count; f++)
            {
                if (ReadForest(c, j.Forests[f], $"forests[{f}]", size, trees)) forests++;
            }
        if (c.Errors.Count != before || levels == null || rules == null || resources == null) return null;

        // The terrain: levels and ramps into a heightmap (the heightmap's own checks back the ramp rule up).
        var elevations = new float[size * size];
        for (int i = 0; i < elevations.Length; i++) elevations[i] = levels[i] * MapConstants.LevelHeight;
        for (int i = 0; i < ramps.Count; i++)
            CutRamp(c, ramps, i, levels, elevations, size);
        if (c.Errors.Count != before) return null;
        Heightmap terrain;
        try
        {
            terrain = new Heightmap(size, size, levels, elevations);
        }
        catch (ArgumentException e)
        {
            c.Error("ramps", e.Message);
            return null;
        }
        var nav = new NavGrid(terrain);

        StartLocation[] locations = CheckStarts(c, starts, nav, size);
        if (c.Errors.Count != before) return null;

        int mineType = FirstOfKind(resources, ResourceKind.Gold), treeType = FirstOfKind(resources, ResourceKind.Wood);
        var occupied = new byte[size * size]; // 1 a mine's cell, 2 a tree's
        var nodes = ImmutableArray.CreateBuilder<MapNodeDef>(mines.Count + trees.Count);
        MapGenParams contract = MapGenParams.Default;
        foreach ((int cell, int gold, string p) in mines)
        {
            ResourceDef def = resources[mineType];
            if (!PlaceNode(c, p, nav, locations, contract.StartClearRadius, occupied, cell, def.FootprintWidth, def.FootprintHeight, mine: true)) continue;
            nodes.Add(new MapNodeDef(mineType, cell, gold));
        }
        foreach ((int cell, string p) in trees)
        {
            if (!PlaceNode(c, p, nav, locations, contract.StartClearRadius, occupied, cell, 1, 1, mine: false)) continue;
            nodes.Add(new MapNodeDef(treeType, cell, rules.TreeWood));
        }
        if (c.Errors.Count != before) return null;
        CheckStartNodes(c, locations, nodes, resources, rules, size, contract);
        if (c.Errors.Count != before) return null;
        if (!Connected(nav, occupied))
        {
            c.Error("", "the mines and forests cut the map in two: some open ground can't be reached from the rest");
            return null;
        }
        var startCells = ImmutableArray.CreateBuilder<int>(locations.Length);
        foreach (StartLocation st in locations) startCells.Add(st.Cell);
        var rampDefs = ImmutableArray.CreateBuilder<MapRampDef>(ramps.Count);
        rampDefs.AddRange(ramps);
        return new MapDef
        {
            Id = index,
            Key = id,
            DisplayName = name,
            Description = description,
            Players = players,
            Size = size,
            Levels = ImmutableArray.Create(levels),
            Ramps = rampDefs.MoveToImmutable(),
            Starts = startCells.MoveToImmutable(),
            Nodes = nodes.MoveToImmutable(),
            ForestCount = forests,
            Terrain = terrain.WithStartLocations(locations),
        };
    }

    /// <summary>The text heightmap: <paramref name="size"/> rows of <paramref name="size"/> characters, each <c>0</c>, <c>1</c> or <c>2</c>; null (after one error per bad row, or one for the row count) when it isn't.</summary>
    private static byte[]? ReadTerrain(Checker c, List<string?>? rows, int size)
    {
        if (c.Obj(rows, "terrain") == null) return null;
        if (rows!.Count != size)
        {
            c.Error("terrain", $"{rows.Count} rows, expected {size} (one per row of cells, north first)");
            return null;
        }
        var levels = new byte[size * size];
        bool ok = true;
        for (int y = 0; y < size; y++)
        {
            string p = $"terrain[{y}]";
            string? row = rows[y];
            if (row == null || row.Length != size)
            {
                c.Error(p, $"{row?.Length ?? 0} cells, expected {size}");
                ok = false;
                continue;
            }
            for (int x = 0; x < size; x++)
            {
                char ch = row[x];
                if (ch < '0' || ch > (char)('0' + MapConstants.MaxLevel))
                {
                    c.Error(p, $"unknown level '{ch}' at column {x} (levels are 0 to {MapConstants.MaxLevel})");
                    ok = false;
                    break;
                }
                levels[y * size + x] = (byte)(ch - '0');
            }
        }
        return ok ? levels : null;
    }

    /// <summary>A cell given as <c>x</c>, <c>y</c>: both required, inside the map.</summary>
    private static bool ReadCell(Checker c, MapCellJson? j, string p, int size, out int cell)
    {
        cell = -1;
        if (c.Obj(j, p) == null) return false;
        if (j!.X == null || j.Y == null)
        {
            c.Error(j.X == null ? p + ".x" : p + ".y", "missing required field");
            return false;
        }
        if (j.X < 0 || j.X >= size || j.Y < 0 || j.Y >= size)
        {
            c.Error(p, $"({j.X}, {j.Y}) is off the {size} x {size} map");
            return false;
        }
        cell = j.Y.Value * size + j.X.Value;
        return true;
    }

    /// <summary>A ramp's fields: a rectangle inside the map's border ring, and an <c>up</c> side.</summary>
    private static MapRampDef? ReadRamp(Checker c, MapRampJson? j, string p, int size)
    {
        if (c.Obj(j, p) == null) return null;
        int before = c.Errors.Count;
        int x = c.Int(j!.X, p + ".x", 0), y = c.Int(j.Y, p + ".y", 0), w = c.Int(j.Width, p + ".width", 1), h = c.Int(j.Height, p + ".height", 1);
        string up = c.Text(j.Up, p + ".up");
        int dir = DataLimits.MapDirectionIds.IndexOf(up);
        if (up.Length > 0 && dir < 0) c.Error(p + ".up", $"unknown side '{up}' (expected one of {string.Join(", ", DataLimits.MapDirectionIds)})");
        if (c.Errors.Count != before) return null;
        if (x < 1 || y < 1 || x + w > size - 1 || y + h > size - 1)
        {
            c.Error(p, $"the ramp ({x}, {y}) {w} x {h} leaves the map or touches its border ring");
            return null;
        }
        return new MapRampDef(x, y, w, h, (MapDirection)dir);
    }

    /// <summary>A forest: an anchor cell and rows of <c>T</c> (a tree) and <c>.</c> (none); its trees go to <paramref name="trees"/>. False on an error or no tree.</summary>
    private static bool ReadForest(Checker c, MapForestJson? j, string p, int size, List<(int, string)> trees)
    {
        if (c.Obj(j, p) == null) return false;
        if (!ReadCell(c, new MapCellJson { X = j!.X, Y = j.Y }, p, size, out int anchor) || c.Obj(j.Rows, p + ".rows") == null) return false;
        int ax = anchor % size, ay = anchor / size, count = 0;
        var mine = new List<(int, string)>();
        for (int r = 0; r < j.Rows!.Count; r++)
        {
            string rp = $"{p}.rows[{r}]";
            string row = j.Rows[r] ?? "";
            for (int k = 0; k < row.Length; k++)
            {
                if (row[k] == '.') continue;
                if (row[k] != 'T')
                {
                    c.Error(rp, $"'{row[k]}' at column {k}: a forest row holds 'T' (a tree) and '.' (none)");
                    return false;
                }
                int x = ax + k, y = ay + r;
                if (x >= size || y >= size)
                {
                    c.Error(rp, $"the tree at ({x}, {y}) is off the {size} x {size} map");
                    return false;
                }
                mine.Add((y * size + x, $"{rp}[{k}]"));
                count++;
            }
        }
        if (count == 0)
        {
            c.Error(p + ".rows", "a forest with no tree");
            return false;
        }
        trees.AddRange(mine);
        return true;
    }

    /// <summary>
    /// Ramp <paramref name="i"/> into the elevations: every cell on one level below the top, the row (column) past its up end
    /// on the next level and the one past its foot on its own, long enough that the slope stays within 30 degrees, and no
    /// other ramp on its cells. Each cell's height steps evenly between the levels, as the generator's ramps.
    /// </summary>
    private static void CutRamp(Checker c, List<MapRampDef> ramps, int i, byte[] levels, float[] elevations, int size)
    {
        MapRampDef r = ramps[i];
        string p = $"ramps[{i}]";
        bool vertical = r.Up is MapDirection.North or MapDirection.South;
        int length = vertical ? r.Height : r.Width, across = vertical ? r.Width : r.Height;
        int lower = levels[r.Y * size + r.X];
        if (lower >= MapConstants.MaxLevel)
        {
            c.Error(p, $"the ramp stands on level {lower}: nothing above it to climb to");
            return;
        }
        if (MapConstants.LevelHeight / ((length + 1) * MapConstants.CellSize) > MapConstants.MaxRampSlope)
        {
            c.Error(p, $"a ramp {length} cells long is steeper than 30 degrees (at least {MinRampLength()} cells along the slope)");
            return;
        }
        for (int k = 0; k < i; k++)
        {
            MapRampDef q = ramps[k];
            if (r.X < q.X + q.Width && q.X < r.X + r.Width && r.Y < q.Y + q.Height && q.Y < r.Y + r.Height)
            {
                c.Error(p, $"overlaps ramps[{k}]");
                return;
            }
        }
        for (int y = r.Y; y < r.Y + r.Height; y++)
            for (int x = r.X; x < r.X + r.Width; x++)
                if (levels[y * size + x] != lower)
                {
                    c.Error(p, $"cell ({x}, {y}) is on level {levels[y * size + x]}, the ramp's foot on {lower}: a ramp is one level's cells");
                    return;
                }
        // (dx, dy) one step up the slope; the mouth is the line past the up end, the landing the line past the foot.
        (int dx, int dy) = r.Up switch { MapDirection.North => (0, -1), MapDirection.South => (0, 1), MapDirection.East => (1, 0), _ => (-1, 0) };
        for (int a = 0; a < across; a++)
        {
            // k = 1 is the cell next to the mouth, k = length the one at the foot.
            for (int k = 1; k <= length + 1; k++)
            {
                (int x, int y) = RampCell(r, a, k, length, dx, dy);
                if (k <= length)
                {
                    float rise = (float)(length + 1 - k) / (length + 1);
                    elevations[y * size + x] = (lower + rise) * MapConstants.LevelHeight;
                    continue;
                }
                int footX = x, footY = y; // k = length + 1: the landing below the foot
                if (levels[footY * size + footX] != lower)
                {
                    c.Error(p, $"the ground below the ramp's foot ({footX}, {footY}) is on level {levels[footY * size + footX]}, not {lower}");
                    return;
                }
            }
            (int mx, int my) = RampCell(r, a, 0, length, dx, dy);
            if (levels[my * size + mx] != lower + 1)
            {
                c.Error(p, $"the ground past the ramp's top ({mx}, {my}) is on level {levels[my * size + mx]}, not {lower + 1}");
                return;
            }
        }
    }

    /// <summary>The cell at <paramref name="k"/> steps down the ramp from its mouth (0 the mouth, 1 the top ramp cell, <paramref name="length"/> + 1 the landing), <paramref name="a"/> cells across.</summary>
    private static (int X, int Y) RampCell(MapRampDef r, int a, int k, int length, int dx, int dy)
    {
        // The top cell row (column) is the one on the up side.
        int topX = dx > 0 ? r.X + r.Width - 1 : r.X, topY = dy > 0 ? r.Y + r.Height - 1 : r.Y;
        int ax = dx == 0 ? 1 : 0, ay = dy == 0 ? 1 : 0;
        return (topX + a * ax - (k - 1) * dx, topY + a * ay - (k - 1) * dy);
    }

    /// <summary>The fewest cells along a ramp's slope that keep it within 30 degrees.</summary>
    private static int MinRampLength()
    {
        int n = 1;
        while (MapConstants.LevelHeight / ((n + 1) * MapConstants.CellSize) > MapConstants.MaxRampSlope) n++;
        return n;
    }

    /// <summary>
    /// The generator's start rules on the built terrain: each start's site disc open, flat ground (so one level); every start
    /// on start 0's level; each at least half the map side from every earlier one (and their discs apart). One error per bad
    /// start, at its path, and a start that fails one rule isn't judged by the next.
    /// </summary>
    private static StartLocation[] CheckStarts(Checker c, List<int> starts, NavGrid nav, int size)
    {
        int rs = MapGenParams.Default.StartSiteRadius;
        var locations = new StartLocation[starts.Count];
        var valid = new bool[starts.Count];
        int first = -1; // the first start that passed: the others are held to its level
        Span<int> pair = stackalloc int[2];
        Span<int> pairY = stackalloc int[2];
        for (int i = 0; i < starts.Count; i++)
        {
            string p = $"starts[{i}]";
            int cx = starts[i] % size, cy = starts[i] / size;
            locations[i] = new StartLocation(starts[i], nav.LevelAt(cx, cy));
            if (!SiteOpen(nav, cx, cy, rs))
            {
                c.Error(p, $"({cx}, {cy}): the ground within {rs} cells is not open and flat (a cliff, ramp, border, sealed or other-level cell)");
                continue;
            }
            if (first >= 0 && locations[i].Level != locations[first].Level)
            {
                c.Error(p, $"on level {locations[i].Level}, starts[{first}] on level {locations[first].Level}: every start stands on one level");
                continue;
            }
            bool apart = true;
            for (int k = 0; k < i && apart; k++)
            {
                if (!valid[k]) continue;
                pair[0] = starts[k] % size; pairY[0] = starts[k] / size;
                pair[1] = cx; pairY[1] = cy;
                if (MapGenerator.Apart(size, size, rs, 2, pair, pairY)) continue;
                c.Error(p, $"too close to starts[{k}]: starts stand at least half the map side ({size / 2} cells) apart");
                apart = false;
            }
            if (!apart) continue;
            valid[i] = true;
            if (first < 0) first = i;
        }
        return locations;
    }

    /// <summary>Whether every cell within <paramref name="r"/> cells of (cx, cy) is inside the map and open ground (<see cref="ResourcePlacer.IsOpenGround"/>).</summary>
    private static bool SiteOpen(NavGrid nav, int cx, int cy, int r)
    {
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (dx * dx + dy * dy > r * r) continue;
                int x = cx + dx, y = cy + dy;
                if (x <= 0 || y <= 0 || x >= nav.Width - 1 || y >= nav.Height - 1 || !ResourcePlacer.IsOpenGround(nav, x, y)) return false;
            }
        return true;
    }

    /// <summary>
    /// One node's footprint: every cell on the map, open ground (<see cref="ResourcePlacer.IsOpenGround"/>) of one level, off
    /// every start's clear disc, on no other node; a mine touches no node (8-neighbour) and a tree no mine, so workers reach
    /// a mine from every side. Marks its cells taken.
    /// </summary>
    private static bool PlaceNode(Checker c, string p, NavGrid nav, StartLocation[] starts, int clear, byte[] occupied, int anchor, int fw, int fh,
        bool mine)
    {
        int size = nav.Width, ax = anchor % size, ay = anchor / size, level = nav.LevelAt(ax, ay);
        for (int y = ay; y < ay + fh; y++)
            for (int x = ax; x < ax + fw; x++)
            {
                if (x >= size || y >= size || !ResourcePlacer.IsOpenGround(nav, x, y) || nav.LevelAt(x, y) != level)
                {
                    c.Error(p, $"cell ({x}, {y}) is not open, flat ground on the node's level (a cliff, ramp, border or sealed cell is near, or it is off the map)");
                    return false;
                }
                if (occupied[y * size + x] != 0)
                {
                    c.Error(p, $"cell ({x}, {y}) is already a node's");
                    return false;
                }
                for (int s = 0; s < starts.Length; s++)
                {
                    int sx = starts[s].Cell % size, sy = starts[s].Cell / size;
                    if ((x - sx) * (x - sx) + (y - sy) * (y - sy) > clear * clear) continue;
                    c.Error(p, $"cell ({x}, {y}) is within {clear} cells of starts[{s}]: a start's clear ground holds no node");
                    return false;
                }
                for (int ny = y - 1; ny <= y + 1; ny++)
                    for (int nx = x - 1; nx <= x + 1; nx++)
                    {
                        byte other = nx >= 0 && ny >= 0 && nx < size && ny < size ? occupied[ny * size + nx] : (byte)0;
                        if (other == 0 || (!mine && other == 2)) continue;
                        c.Error(p, mine ? $"the mine touches another node at ({nx}, {ny}): a mine needs open ground on every side"
                            : $"the tree touches a mine at ({nx}, {ny}): a mine needs open ground on every side");
                        return false;
                    }
            }
        for (int y = ay; y < ay + fh; y++)
            for (int x = ax; x < ax + fw; x++)
            {
                occupied[y * size + x] = mine ? (byte)1 : (byte)2;
            }
        return true;
    }

    /// <summary>
    /// Each start has <c>startMines.count</c> mines whose centre lies within <see cref="MapGenParams.StartMineDistance"/> of
    /// the start's centre cell, and a tree within <see cref="MapGenParams.StartForestDistance"/> (centre to centre): the
    /// generator's start contract (its defaults), one error per start.
    /// </summary>
    private static void CheckStartNodes(Checker c, StartLocation[] starts, ImmutableArray<MapNodeDef>.Builder nodes, ResourceDef[] resources,
        RulesDef rules, int size, MapGenParams contract)
    {
        float mine2 = contract.StartMineDistance * contract.StartMineDistance, tree2 = contract.StartForestDistance * contract.StartForestDistance;
        for (int s = 0; s < starts.Length; s++)
        {
            float sx = (starts[s].Cell % size + 0.5f) * MapConstants.CellSize, sy = (starts[s].Cell / size + 0.5f) * MapConstants.CellSize;
            int mines = 0;
            bool tree = false;
            foreach (MapNodeDef n in nodes)
            {
                ResourceDef def = resources[n.TypeId];
                float nx = (n.Cell % size + def.FootprintWidth * 0.5f) * MapConstants.CellSize - sx;
                float ny = (n.Cell / size + def.FootprintHeight * 0.5f) * MapConstants.CellSize - sy;
                float d2 = nx * nx + ny * ny;
                if (def.Resource == ResourceKind.Gold && d2 <= mine2) mines++;
                else if (def.Resource == ResourceKind.Wood && d2 <= tree2) tree = true;
            }
            if (mines < rules.StartMineCount)
                c.Error($"starts[{s}]", $"{mines} gold mine(s) within {contract.StartMineDistance} m of the start; it needs {rules.StartMineCount} (rules.json startMines.count)");
            else if (!tree)
                c.Error($"starts[{s}]", $"no tree within {contract.StartForestDistance} m of the start; it needs a forest beside it");
        }
    }

    /// <summary>Whether every passable cell no node covers reaches every other (4-neighbour steps, a flood from the first such cell).</summary>
    private static bool Connected(NavGrid nav, byte[] occupied)
    {
        int w = nav.Width, n = w * nav.Height, open = 0, first = -1;
        for (int i = 0; i < n; i++)
        {
            if (!nav.IsPassable(i % w, i / w) || occupied[i] != 0) continue;
            open++;
            if (first < 0) first = i;
        }
        if (first < 0) return true;
        var seen = new bool[n];
        var queue = new int[open];
        int head = 0, tail = 0;
        queue[tail++] = first;
        seen[first] = true;
        while (head < tail)
        {
            int i = queue[head++], x = i % w, y = i / w;
            for (int d = 0; d < 4; d++)
            {
                int nx = x + (d == 0 ? -1 : d == 1 ? 1 : 0), ny = y + (d == 2 ? -1 : d == 3 ? 1 : 0);
                if (!nav.IsPassable(nx, ny)) continue; // off the map is not passable
                int j = ny * w + nx;
                if (seen[j] || occupied[j] != 0) continue;
                seen[j] = true;
                queue[tail++] = j;
            }
        }
        return tail == open;
    }

    /// <summary>The lowest resource type id yielding <paramref name="kind"/> (the generator's choice of mine and tree type); the loader requires one of each.</summary>
    private static int FirstOfKind(ResourceDef[] resources, ResourceKind kind)
    {
        for (int i = 0; i < resources.Length; i++)
            if (resources[i] != null && resources[i].Resource == kind) return i;
        return 0;
    }
}
