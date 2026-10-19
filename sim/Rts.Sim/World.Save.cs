using System;
using Rts.Sim.Abilities;
using Rts.Sim.Combat;
using Rts.Sim.Determinism;
using Rts.Sim.Map;
using Rts.Sim.Save;

namespace Rts.Sim;

// M6-S2: the world in a save (docs/03 "Save/load and replays"). The heightmap goes first, since the load builds the world
// on it; then the world's own fields, every RNG stream, and every store whole, in a fixed order. Per-tick scratch (the
// movement, combat and production buffers, all rewritten before they are read in a tick and documented "derived, not
// hashed") is not stored; the scratch epoch is, so a loaded pass numbers its answers as the unsaved one does. Nor is the
// never-seal check's memo (SealCheck, BUG-0096): a cache of an answer that follows from the grid alone, which reads between
// ticks (CanPlace from a view) also write, so storing it would make a save depend on what the view asked.
public sealed partial class World
{
    /// <summary>A world for a load (M6-S2): built on the saved <paramref name="map"/> with no placer run, every store still to be read.</summary>
    internal static World ForLoad(SimConfig config, Heightmap map) => new(config, map, placeResources: false);

    /// <summary>Writes the terrain: size, levels, exact elevations and start locations (M6-S2: stored, not re-generated).</summary>
    internal static void WriteHeightmap(SaveWriter w, Heightmap map)
    {
        w.Tag(SaveFormat.TagHeightmap);
        w.Write(map.Width);
        w.Write(map.Height);
        w.WriteSpan(map.Levels);
        w.WriteSpan(map.Elevations);
        w.WriteSpan(map.StartLocations);
    }

    /// <summary>Reads what <see cref="WriteHeightmap"/> wrote; null (the reader failed) on a size past 1,024 a side or terrain the heightmap's own checks refuse.</summary>
    internal static Heightmap? ReadHeightmap(SaveReader r)
    {
        if (!r.Expect(SaveFormat.TagHeightmap)) return null;
        int width = r.ReadInt32In(1, 1024), height = r.ReadInt32In(1, 1024);
        // BUG-0960 item 1: the levels and elevations (5 bytes a cell, after their lengths) must be there before they are allocated.
        if (r.Failed || !r.Check(r.Remaining >= 5L * width * height + 8)) return null;
        var levels = new byte[width * height];
        var elevations = new float[width * height];
        r.ReadArray(levels);
        r.ReadArray(elevations);
        StartLocation[] locations = r.ReadNewArray<StartLocation>(SaveFormat.MaxPlayers);
        if (r.Failed) return null;
        try
        {
            return new Heightmap(width, height, levels, elevations, locations);
        }
        catch (ArgumentException)
        {
            // The heightmap refuses a level / elevation pair or start location it can't hold by throwing; a load says Malformed.
            r.Fail();
            return null;
        }
    }

    /// <summary>Writes the world's fields, every RNG stream and every store, after the heightmap (M6-S2).</summary>
    internal void WriteTo(SaveWriter w)
    {
        w.Tag(SaveFormat.TagWorld);
        w.Write(HasGeneratedMap);
        w.Write(ResourcePlacement.Forests);
        w.Write(ResourcePlacement.Trees);
        w.Write(ResourcePlacement.Mines);
        w.Write(SeenBlockVersion);
        w.Write(PlugEpoch);
        w.Write(SurroundRuleOn);
        w.Write(FlankRuleOn);
        w.Write(_matchOverLatched);

        w.Tag(SaveFormat.TagRng);
        w.Write(_rngs.Length);
        foreach (SimRng rng in _rngs)
        {
            w.Write(rng.State);
            w.Write(rng.Increment);
        }

        NavGrid.WriteTo(w);
        Resources.WriteTo(w);
        Buildings.WriteTo(w);
        Units.WriteTo(w);
        _ledger.WriteTo(w);
        Techs.WriteTo(w);
        Projectiles.WriteTo(w);
        Zones.WriteTo(w);
        Fog.WriteTo(w);
        Spatial.WriteTo(w);
        FlowFields.WriteTo(w);

        w.Tag(SaveFormat.TagSurround);
        w.WriteArray(SurroundClaim);
        w.WriteArray(SurroundHolder);

        w.Tag(SaveFormat.TagStealth);
        w.WriteArray(_detectedBy);

        WriteEvents(w);
    }

    /// <summary>
    /// The last tick's event lists (deaths, landings, cast moments, defeats): <c>Tick()</c> clears them at its start, but a
    /// view reads them between ticks, so a load at tick T shows tick T's events as the unsaved sim does.
    /// </summary>
    private void WriteEvents(SaveWriter w)
    {
        w.Tag(SaveFormat.TagEvents);
        w.Write(DeathCount);
        foreach (DeathEvent e in Deaths)
        {
            w.Write(e.Victim);
            w.Write(e.IsBuilding);
            w.Write(e.VictimType);
            w.Write(e.VictimOwner);
            w.Write(e.KillerOwner);
            w.Write(e.Position);
            w.Write(e.Expired);
            w.Write(e.Collapsed);
        }
        w.Write(ImpactCount);
        foreach (ProjectileImpact e in Impacts)
        {
            w.Write(e.Position);
            w.Write(e.ProjectileTypeId);
            w.Write(e.Owner);
            w.Write(e.Hit);
        }
        w.Write(AbilityEventCount);
        foreach (AbilityEvent e in AbilityEvents)
        {
            w.Write(e.Caster);
            w.Write(e.Owner);
            w.Write(e.Ability);
            w.Write(e.Point);
            w.Write(e.Resolved);
            w.Write(e.Dropped);
        }
        w.Write(DefeatEventCount);
        foreach (DefeatEvent e in DefeatEvents)
        {
            w.Write(e.Player);
            w.Write(e.Tick);
        }
    }

    /// <summary>Reads what <see cref="WriteTo"/> wrote into this world, built by <see cref="ForLoad"/> from the same header and heightmap.</summary>
    internal void ReadFrom(SaveReader r)
    {
        if (!r.Expect(SaveFormat.TagWorld)) return;
        HasGeneratedMap = r.ReadBool();
        int forests = r.ReadInt32(), trees = r.ReadInt32(), mines = r.ReadInt32();
        ResourcePlacement = new ResourcePlacement(forests, trees, mines);
        SeenBlockVersion = r.ReadInt32();
        PlugEpoch = r.ReadInt64();
        SurroundRuleOn = r.ReadBool();
        FlankRuleOn = r.ReadBool();
        _matchOverLatched = r.ReadBool();

        if (!r.Expect(SaveFormat.TagRng) || !r.ExpectLength(_rngs.Length)) return;
        for (int s = 0; s < _rngs.Length; s++)
        {
            ulong state = r.ReadUInt64(), increment = r.ReadUInt64();
            if (!r.Check(SimRng.IsValidIncrement(increment))) return;
            _rngs[s] = SimRng.FromWords(state, increment);
        }

        int cells = NavGrid.Width * NavGrid.Height;
        NavGrid.ReadFrom(r);
        Resources.ReadFrom(r, cells);
        Buildings.ReadFrom(r, Config.PlayerCount, cells);
        Units.ReadFrom(r, Config.PlayerCount, Data.Units.Length, Data.Statuses.Length);
        _ledger.ReadFrom(r);
        Techs.ReadFrom(r);
        Projectiles.ReadFrom(r, Data.Projectiles.Length, Data.Units.Length, Data.Buildings.Length);
        Zones.ReadFrom(r);
        Fog.ReadFrom(r);
        Spatial.ReadFrom(r);
        FlowFields.ReadFrom(r);
        if (r.Failed) return;

        if (!r.Expect(SaveFormat.TagSurround)) return;
        r.ReadArray(SurroundClaim);
        r.ReadArray(SurroundHolder);
        if (!r.Expect(SaveFormat.TagStealth)) return;
        r.ReadArray(_detectedBy);
        ReadEvents(r);
    }

    private void ReadEvents(SaveReader r)
    {
        if (!r.Expect(SaveFormat.TagEvents)) return;
        int deaths = r.ReadInt32In(0, _deaths.Length);
        for (int i = 0; i < deaths && !r.Failed; i++)
        {
            var victim = r.ReadHandle();
            bool isBuilding = r.ReadBool();
            int type = r.ReadInt32(), victimOwner = r.ReadInt32(), killerOwner = r.ReadInt32();
            var position = r.ReadVector2();
            bool expired = r.ReadBool(), collapsed = r.ReadBool();
            _deaths[i] = new DeathEvent(victim, isBuilding, type, victimOwner, killerOwner, position, expired, collapsed);
        }
        DeathCount = deaths;
        int impacts = r.ReadInt32In(0, _impacts.Length);
        for (int i = 0; i < impacts && !r.Failed; i++)
        {
            var position = r.ReadVector2();
            int type = r.ReadInt32(), owner = r.ReadInt32();
            _impacts[i] = new ProjectileImpact(position, type, owner, r.ReadBool());
        }
        ImpactCount = impacts;
        int casts = r.ReadInt32In(0, _abilityEvents.Length);
        for (int i = 0; i < casts && !r.Failed; i++)
        {
            var caster = r.ReadHandle();
            int owner = r.ReadInt32(), ability = r.ReadInt32();
            var point = r.ReadVector2();
            bool resolved = r.ReadBool(), dropped = r.ReadBool();
            _abilityEvents[i] = new AbilityEvent(caster, owner, ability, point, resolved, dropped);
        }
        AbilityEventCount = casts;
        int defeats = r.ReadInt32In(0, _defeatEvents.Length);
        for (int i = 0; i < defeats && !r.Failed; i++)
        {
            int player = r.ReadInt32();
            _defeatEvents[i] = new DefeatEvent(player, r.ReadInt32());
        }
        DefeatEventCount = defeats;
    }
}
