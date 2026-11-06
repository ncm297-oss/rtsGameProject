using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.Map;

/// <summary>
/// M6-S7: a hand-made map's resource nodes onto a fresh world (docs/03 "Implementation (M6-S7)"). Runs once at world
/// construction, in place of the resource placer: the loader already checked every node against the terrain, so each
/// spawns where the file put it, in file order (mines, then each forest's trees row by row), and nothing draws from any
/// random stream. Allocates nothing past the store.
/// </summary>
internal static class MapFile
{
    /// <summary>Spawns every node of <paramref name="map"/> into <paramref name="store"/> (sized for them: <see cref="SimConfig.Validate"/>); returns what was placed.</summary>
    internal static ResourcePlacement Place(MapDef map, ResourceStore store, GameData data)
    {
        int mines = 0, trees = 0;
        foreach (MapNodeDef n in map.Nodes)
        {
            if (!store.Spawn(n.TypeId, n.Cell, n.Amount, out _)) continue; // the loader and the config rule this out
            if (data.Resources[n.TypeId].Resource == ResourceKind.Gold) mines++;
            else trees++;
        }
        return new ResourcePlacement(map.ForestCount, trees, mines);
    }
}
