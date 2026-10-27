using System;
using System.Collections.Immutable;
using Rts.Sim.Data;
using Rts.Sim.Map;

namespace Rts.Sim.Save;

/// <summary>
/// The save header (M6-S2, docs/03 "Save/load and replays"): everything a load needs to build the <see cref="SimConfig"/>
/// before it reads a store, in a fixed order after the magic, the format version and the total length. Written from a
/// sim's config by <see cref="Write"/>; read and checked by <see cref="TryRead"/>, which returns the config.
/// </summary>
internal static class SaveHeader
{
    /// <summary>Writes the magic, version, a length placeholder (patched by <see cref="SaveWriter.Finish"/>) and the header fields of <paramref name="sim"/>.</summary>
    public static void Write(SaveWriter w, Simulation sim)
    {
        SimConfig c = sim.World.Config;
        GameData data = c.Data;
        foreach (byte b in SaveFormat.Magic) w.Write(b);
        w.Write(SaveFormat.CurrentVersion);
        w.Write(0L); // total length, patched at the end
        w.WriteString(SimInfo.Version);
        w.Write(data.ContentHash());
        w.Write(c.Seed);
        w.Write(c.PlayerCount);
        for (int p = 0; p < c.PlayerCount; p++)
        {
            // Every seat written out (the default ones too), the faction by its string id: ids are mapped to ints at load.
            PlayerSeat seat = c.SeatOf(p);
            w.WriteString(seat.Faction >= 0 ? data.Factions[seat.Faction].Key : "-");
            w.Write((byte)seat.Controller);
            w.Write((byte)seat.Difficulty);
        }
        w.Write(c.UnitCapacity);
        w.Write(c.CommandCapacity);
        w.Write(c.ResourceCapacity);
        w.Write(c.BuildingCapacity);
        w.Write(c.ProjectileCapacity);
        w.Write(c.ZoneCapacity);
        WriteMap(w, c.Map);
        w.Write(c.Combat);
        w.Write(c.CastWalkInBlockerRange);
        w.Write(c.CastWalkInPatienceSeconds);
        w.Write(c.StatsSampleTicks);
        w.Write(c.StatsSampleCapacity);
        w.Write(sim.TickNumber);
        // Format 3 (M6-S4): each seat's team (0-8), one byte per seat in player order, after the tick: the header's last
        // field, so every format-2 field before it keeps its offset (a header-only reader, ViewApi.SaveSummary, reads up to the
        // tick).
        for (int p = 0; p < c.PlayerCount; p++) w.Write((byte)c.SeatOf(p).Team);
    }

    /// <summary>Every <see cref="MapGenParams"/> field, in declaration order.</summary>
    private static void WriteMap(SaveWriter w, MapGenParams m)
    {
        w.Write(m.Width);
        w.Write(m.Height);
        w.Write(m.EdgeMargin);
        w.Write(m.Level1Plateaus);
        w.Write(m.Level1MinSize);
        w.Write(m.Level1MaxSize);
        w.Write(m.Level2Plateaus);
        w.Write(m.Level2MinSize);
        w.Write(m.Level2MaxSize);
        w.Write(m.Level2Inset);
        w.Write(m.RampWidth);
        w.Write(m.RampLength);
        w.Write(m.RampsPerPlateau);
        w.Write(m.RampTries);
        w.Write(m.MinPassableFraction);
        w.Write(m.MaxAttempts);
        w.Write(m.Forests);
        w.Write(m.ForestMinTrees);
        w.Write(m.ForestMaxTrees);
        w.Write(m.GoldMines);
        w.Write(m.MineSpacing);
        w.Write(m.StartLocations);
        w.Write(m.StartClearRadius);
        w.Write(m.StartEdgeMargin);
        w.Write(m.StartMineDistance);
        w.Write(m.StartForestDistance);
    }

    private static MapGenParams ReadMap(SaveReader r) => new()
    {
        Width = r.ReadInt32(),
        Height = r.ReadInt32(),
        EdgeMargin = r.ReadInt32(),
        Level1Plateaus = r.ReadInt32(),
        Level1MinSize = r.ReadInt32(),
        Level1MaxSize = r.ReadInt32(),
        Level2Plateaus = r.ReadInt32(),
        Level2MinSize = r.ReadInt32(),
        Level2MaxSize = r.ReadInt32(),
        Level2Inset = r.ReadInt32(),
        RampWidth = r.ReadInt32(),
        RampLength = r.ReadInt32(),
        RampsPerPlateau = r.ReadInt32(),
        RampTries = r.ReadInt32(),
        MinPassableFraction = r.ReadFloat(),
        MaxAttempts = r.ReadInt32(),
        Forests = r.ReadInt32(),
        ForestMinTrees = r.ReadInt32(),
        ForestMaxTrees = r.ReadInt32(),
        GoldMines = r.ReadInt32(),
        MineSpacing = r.ReadFloat(),
        StartLocations = r.ReadInt32(),
        StartClearRadius = r.ReadInt32(),
        StartEdgeMargin = r.ReadInt32(),
        StartMineDistance = r.ReadFloat(),
        StartForestDistance = r.ReadFloat(),
    };

    /// <summary>
    /// Reads the header after the fixed prefix (the caller checked magic, version, length and checksum): the data hash
    /// (<see cref="SaveError.DataMismatch"/> against <paramref name="data"/>), then every field, each in its range
    /// (<see cref="SaveError.InvalidHeader"/>, an AI seat whose faction has no <c>ai.json</c> too, and since format 3 a team
    /// outside 0 to <see cref="PlayerSeat.MaxTeam"/>). On success
    /// <paramref name="config"/> is the match's config and <paramref name="tick"/> the tick number the save was made at.
    /// </summary>
    public static SaveError TryRead(SaveReader r, GameData data, out SimConfig? config, out int tick)
    {
        config = null;
        tick = 0;
        string? simVersion = r.ReadString(); // informational, not checked (as the replay's)
        if (simVersion == null) return SaveError.Malformed;
        ulong dataHash = r.ReadUInt64();
        if (r.Failed) return SaveError.Malformed;
        if (dataHash != data.ContentHash()) return SaveError.DataMismatch;
        ulong seed = r.ReadUInt64();
        int players = r.ReadInt32();
        if (r.Failed) return SaveError.Malformed;
        if (players < 1 || players > SaveFormat.MaxPlayers) return SaveError.InvalidHeader;
        var seats = ImmutableArray.CreateBuilder<PlayerSeat>(players);
        for (int p = 0; p < players; p++)
        {
            string? id = r.ReadString();
            byte controller = r.ReadByte(), difficulty = r.ReadByte();
            if (r.Failed || id == null) return SaveError.Malformed;
            int faction = id == "-" ? -1 : data.FindFaction(id);
            if (faction < 0 && (id != "-" || data.Factions.Length > 0)) return SaveError.InvalidHeader;
            if (controller > (byte)SeatController.Ai || difficulty > (byte)AiDifficulty.Hard) return SaveError.InvalidHeader;
            seats.Add(new PlayerSeat(faction, (SeatController)controller, (AiDifficulty)difficulty));
        }
        int units = r.ReadInt32(), commands = r.ReadInt32(), resources = r.ReadInt32(), buildings = r.ReadInt32();
        int projectiles = r.ReadInt32(), zones = r.ReadInt32();
        MapGenParams map = ReadMap(r);
        bool combat = r.ReadBool();
        float blockerRange = r.ReadFloat(), patience = r.ReadFloat();
        int sampleTicks = r.ReadInt32(), sampleCapacity = r.ReadInt32();
        tick = r.ReadInt32();
        // Format 3 (M6-S4): the seats' teams, after the tick.
        for (int p = 0; p < players; p++)
        {
            byte team = r.ReadByte();
            if (r.Failed) return SaveError.Malformed;
            if (team > PlayerSeat.MaxTeam) return SaveError.InvalidHeader;
            seats[p] = seats[p] with { Team = team };
        }
        if (r.Failed) return SaveError.Malformed;
        // BUG-1040: the command capacity's range is the config's own (1 to SaveFormat.MaxCommandCapacity), so a header over
        // it is InvalidHeader before anything is built, as every other capacity out of range.
        if (!InCapacity(units, 1) || commands < 1 || commands > SaveFormat.MaxCommandCapacity || !InCapacity(resources, 1) || !InCapacity(buildings, 1)
            || !InCapacity(projectiles, 0) || !InCapacity(zones, 1) || tick < 0 || tick == int.MaxValue)
            return SaveError.InvalidHeader;
        var built = new SimConfig(seed, players, units, commands)
        {
            Data = data,
            Map = map,
            ResourceCapacity = resources,
            BuildingCapacity = buildings,
            ProjectileCapacity = projectiles,
            ZoneCapacity = zones,
            Combat = combat,
            CastWalkInBlockerRange = blockerRange,
            CastWalkInPatienceSeconds = patience,
            StatsSampleTicks = sampleTicks,
            StatsSampleCapacity = sampleCapacity,
            Seats = seats.MoveToImmutable(),
        };
        // An AI seat's faction needs a loaded ai.json, which the config's check asks (M6-S3: AI seats load).
        if (!Valid(built)) return SaveError.InvalidHeader;
        config = built;
        return SaveError.None;
    }

    /// <summary>
    /// BUG-0960 item 1: a lower bound on the bytes the body after the heightmap must hold for <paramref name="config"/>'s
    /// capacities on a map of <paramref name="cells"/> cells, so a header asking for huge stores is refused before
    /// <c>World.ForLoad</c> allocates them. Each term is below what its section writes: the unit store writes over 40 bytes a
    /// slot (positions, velocity, facings, owner, type, before the rest), the building and resource stores over 16, the nav
    /// grid 2 a cell (flags and cost); BUG-0990: the projectile store 68 a slot (four vectors, four ints, two handles, three
    /// flags and a level), counted as 48, and the zone store 26 (alive, owner, centre, ability, ticks, radius, blocks-vision) plus its
    /// per-player boxes, counted as 24. The command capacity is not a length the body holds:
    /// the header's range check (<see cref="SaveFormat.MaxCommandCapacity"/>, BUG-1040) bounds it instead.
    /// </summary>
    public static long MinBodyBytes(SimConfig config, int cells) =>
        32L * config.UnitCapacity + 16L * config.BuildingCapacity + 16L * config.ResourceCapacity + 2L * cells
        + 48L * config.ProjectileCapacity + 24L * config.ZoneCapacity;

    private static bool InCapacity(int value, int min) => value >= min && value <= SaveFormat.MaxCapacity;

    /// <summary>The config's own range checks (<see cref="SimConfig.Validate"/>, every map parameter too), as a bool.</summary>
    private static bool Valid(SimConfig config)
    {
        try
        {
            config.Validate();
            return true;
        }
        catch (ArgumentException)
        {
            // Validate reports a bad value by throwing ArgumentException (or a subclass); the save answers InvalidHeader.
            return false;
        }
    }
}
