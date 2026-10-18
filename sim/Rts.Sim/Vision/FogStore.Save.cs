using Rts.Sim.Save;

namespace Rts.Sim.Vision;

// M6-S2: the fog in a save, whole: per player the byte map (the next update's transitions read it), the explored and visible
// bits, the update count; the unit and building reveals; the last-known building lists and node memories (not hashed, but
// what a player knows); the last update's stamped boxes and the near-blocker mask with the zone version it was built at.
public sealed partial class FogStore
{
    /// <summary>Writes every player's fog, the reveals, the last-known lists and node memory, and the update bookkeeping (M6-S2).</summary>
    internal void WriteTo(SaveWriter w)
    {
        w.Tag(SaveFormat.TagFog);
        w.Write(_players);
        for (int p = 0; p < _players; p++)
        {
            w.WriteArray(_visibility[p]);
            w.WriteArray(_explored[p]);
            w.WriteArray(_visible[p]);
        }
        w.WriteArray(_version);
        w.WriteArray(_revealUntil);
        w.WriteArray(_revealGeneration);
        w.WriteArray(_buildingRevealUntil);
        w.WriteArray(_buildingRevealGeneration);
        // A ghost carries a bool (padding after it), so it goes field by field.
        w.Write(_ghosts.Length);
        foreach (BuildingGhost g in _ghosts)
        {
            w.Write(g.Generation);
            w.Write(g.TypeId);
            w.Write(g.Cell);
            w.Write(g.Owner);
            w.Write(g.Site);
        }
        w.WriteArray(_ghostCount);
        w.WriteArray(_nodes);
        w.WriteArray(_visibleBox);
        w.WriteArray(_nearBlocker);
        w.Write(_nearBlockerVersion);
    }

    /// <summary>Reads what <see cref="WriteTo"/> wrote into a fog of the same players and map; checks every known ghost's and node's type and cell.</summary>
    internal void ReadFrom(SaveReader r)
    {
        if (!r.Expect(SaveFormat.TagFog) || !r.Check(r.ReadInt32() == _players)) return;
        for (int p = 0; p < _players; p++)
        {
            r.ReadByteEnums(_visibility[p], VisionConstants.Visible);
            r.ReadArray(_explored[p]);
            r.ReadArray(_visible[p]);
        }
        r.ReadArray(_version);
        r.ReadArray(_revealUntil);
        r.ReadArray(_revealGeneration);
        r.ReadArray(_buildingRevealUntil);
        r.ReadArray(_buildingRevealGeneration);
        if (!r.ExpectLength(_ghosts.Length)) return;
        int cells = _levels.Length;
        for (int k = 0; k < _ghosts.Length && !r.Failed; k++)
        {
            int generation = r.ReadInt32(), type = r.ReadInt32(), cell = r.ReadInt32(), owner = r.ReadInt32();
            bool site = r.ReadBool();
            if (generation != 0 && !r.Check((uint)type < (uint)_data.Buildings.Length && (uint)cell < (uint)cells && (uint)owner < (uint)_players)) return;
            _ghosts[k] = new BuildingGhost(generation, type, cell, owner, site);
        }
        r.ReadArray(_ghostCount);
        r.ReadArray(_nodes);
        r.ReadArray(_visibleBox);
        r.ReadArray(_nearBlocker);
        _nearBlockerVersion = r.ReadInt32();
        if (r.Failed) return;
        foreach (NodeMemory m in _nodes)
            if (m.Generation != 0 && !r.Check((uint)m.TypeId < (uint)_data.Resources.Length && (uint)m.Cell < (uint)cells)) return;
    }
}
