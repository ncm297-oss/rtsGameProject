using System.Buffers.Binary;
using Rts.Sim.Data;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>
/// M6-V16: <see cref="SaveSummary"/> reads format 3's team bytes (M6-S4: one per seat after the tick, the header's last field)
/// into <see cref="SaveSummary.Teams"/> and each seat's team, so the Load Game list shows a save's teams; a header before
/// format 3 has none and reads every team as 0; a team byte past <see cref="PlayerSeat.MaxTeam"/> is no save header.
/// </summary>
public class SaveSummaryTests
{
    [Fact]
    public void AFormat3SaveOfA2v2_ReadsItsTeams_InSeatOrder()
    {
        Simulation sim = AiSaveScenes.TwoVersusTwo(1).Build();
        for (int t = 0; t < 40; t++) sim.Tick();
        byte[] bytes = SaveTestRun.Save(sim);
        Assert.True(SaveSummary.TryParse(bytes, out SaveSummary? s));
        Assert.Equal(SaveFormat.MaplessVersion, s!.FormatVersion); // M6-S7: a generated map's save is format 4
        Assert.True(s.FormatVersion >= SaveSummary.FirstTeamVersion);
        Assert.Equal(new[] { 1, 2, 1, 2 }, s.Teams.ToArray());
        Assert.Equal(new[] { 1, 2, 1, 2 }, s.Seats.Select(seat => seat.Team).ToArray());
        for (int p = 0; p < 4; p++) Assert.Equal(sim.World.TeamOf(p), s.Teams[p]);
        Assert.Equal(40, s.Tick);
    }

    [Fact]
    public void AFreeForAllSave_ReadsEveryTeamAs0()
    {
        byte[] bytes = SaveTestRun.Save(SaveScenes.Menu(2).Build());
        Assert.True(SaveSummary.TryParse(bytes, out SaveSummary? s));
        Assert.True(s!.Teams.Length == s.Seats.Length && s.Teams.All(t => t == 0) && s.Seats.All(seat => seat.Team == 0));
    }

    [Fact]
    public void AHeaderBeforeFormat3_HasNoTeamBytes_AndReadsTeam0()
    {
        Simulation sim = AiSaveScenes.TwoVersusTwo(1).Build();
        byte[] three = SaveTestRun.Save(sim);
        // The same header as format 2 wrote it: the version 2 and no team bytes (the body after them is never read).
        int teamsAt = TeamsAt(three);
        byte[] two = three.Take(teamsAt).Concat(three.Skip(teamsAt + 4)).ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(two.AsSpan(SaveFormat.MagicLength), 2);
        Assert.True(SaveSummary.TryParse(two, out SaveSummary? s));
        Assert.Equal(2, s!.FormatVersion);
        Assert.Equal(new[] { 0, 0, 0, 0 }, s.Teams.ToArray());
        Assert.True(s.Seats.All(seat => seat.Team == 0));
        // Format 3 cut just before its team bytes: no header.
        Assert.False(SaveSummary.TryParse(three.AsSpan(0, teamsAt + 3), out _));
        Assert.True(SaveSummary.TryParse(three.AsSpan(0, teamsAt + 4), out _));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(8, true)]
    [InlineData(9, false)]
    [InlineData(255, false)]
    public void ATeamByte_Past8_IsNoHeader(byte team, bool reads)
    {
        byte[] bytes = SaveTestRun.Save(AiSaveScenes.TwoVersusTwo(1).Build());
        bytes[TeamsAt(bytes) + 3] = team;
        Assert.Equal(reads, SaveSummary.TryParse(bytes, out SaveSummary? s));
        if (reads) Assert.Equal(team, s!.Teams[3]);
    }

    // ---- M6-V18: format 5's map id ----

    private static Simulation RarakuSim()
    {
        var sim = new Simulation(TestSim.Config(7, 2, 64, 64) with { MapId = "raraku" });
        sim.Tick();
        return sim;
    }

    /// <summary>M6-V18: a save of a match on a map file (format 5) names the map: <see cref="SaveSummary.MapId"/> is the header's id.</summary>
    [Fact]
    public void AFormat5Save_ReadsItsMapId()
    {
        byte[] bytes = SaveTestRun.Save(RarakuSim());
        Assert.True(SaveSummary.TryParse(bytes, out SaveSummary? s));
        Assert.Equal(SaveFormat.CurrentVersion, s!.FormatVersion);
        Assert.Equal("raraku", s.MapId);
        Assert.Equal(StartingResourcesDef.StandardId, s.StartingResources);
        Assert.Equal(128, s.MapWidth);
        Assert.Equal(1, s.Tick);
    }

    /// <summary>M6-V18: a generated map's save (format 4, <see cref="SaveFormat.MaplessVersion"/>) and older headers have no map id: "".</summary>
    [Fact]
    public void AFormat4Save_HasNoMapId()
    {
        byte[] bytes = SaveTestRun.Save(SaveScenes.Menu(2).Build());
        Assert.True(SaveSummary.TryParse(bytes, out SaveSummary? s));
        Assert.Equal(SaveFormat.MaplessVersion, s!.FormatVersion);
        Assert.Equal("", s.MapId);
        Assert.Equal(StartingResourcesDef.StandardId, s.StartingResources);
        // A format-4 header relabelled 5: the bytes after the preset are the heightmap's tag, no string; still listed, no id.
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(SaveFormat.MagicLength), 5);
        Assert.True(SaveSummary.TryParse(bytes, out SaveSummary? relabelled));
        Assert.Equal("", relabelled!.MapId);
    }

    /// <summary>
    /// M6-V18: a format-5 header cut anywhere from the team bytes on lists with <c>""</c> for what's missing (never throws):
    /// before the preset (both ""), inside the map id's length or bytes (the id ""), right after the id (read).
    /// </summary>
    [Fact]
    public void AFormat5Header_CutBeforeOrInsideItsMapId_ListsWithNoMapId()
    {
        byte[] bytes = SaveTestRun.Save(RarakuSim());
        int preset = TeamsAt(bytes) + 2;
        int map = preset + 4 + StartingResourcesDef.StandardId.Length;
        Assert.Equal("raraku", SaveBytes.StringAt(bytes, map));
        for (int cut = TeamsAt(bytes) + 2; cut <= map + 4 + "raraku".Length; cut++)
        {
            Assert.True(SaveSummary.TryParse(bytes.AsSpan(0, cut), out SaveSummary? s), $"cut at {cut}");
            Assert.Equal(cut >= map ? StartingResourcesDef.StandardId : "", s!.StartingResources);
            Assert.Equal(cut == map + 4 + "raraku".Length ? "raraku" : "", s.MapId);
        }
        // A forged length (negative, past the bytes, past the string cap) or bad UTF-8: no id, no throw.
        foreach (int length in new[] { -1, 1 << 30, SaveFormat.MaxString + 1 })
        {
            byte[] forged = (byte[])bytes.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(forged.AsSpan(map), length);
            Assert.True(SaveSummary.TryParse(forged, out SaveSummary? s));
            Assert.Equal("", s!.MapId);
        }
        byte[] notUtf8 = (byte[])bytes.Clone();
        notUtf8[map + 4] = 0xFF;
        Assert.True(SaveSummary.TryParse(notUtf8, out SaveSummary? bad));
        Assert.Equal("", bad!.MapId);
    }

    /// <summary>M6-V18: reading a summary (with its map id) changes nothing in the match: hash, tick and the save's bytes are the same after.</summary>
    [Fact]
    public void ReadingTheMapId_ChangesNothing()
    {
        Simulation sim = RarakuSim();
        ulong hash = sim.StateHash();
        int tick = sim.TickNumber;
        byte[] before = SaveTestRun.Save(sim);
        for (int i = 0; i < 3; i++)
        {
            Assert.True(SaveSummary.TryParse(before, out SaveSummary? s));
            Assert.Equal("raraku", s!.MapId);
            using var stream = new MemoryStream(before);
            Assert.True(SaveSummary.TryRead(stream, out _));
        }
        Assert.Equal(hash, sim.StateHash());
        Assert.Equal(tick, sim.TickNumber);
        Assert.Equal(before, SaveTestRun.Save(sim));
    }

    // The offset of the first team byte: past the seats (an id string, controller and difficulty each), the six capacities,
    // the 26 map fields, combat, the two cast floats, the two stats ints and the tick.
    internal static int TeamsAt(byte[] bytes)
    {
        SaveBytes.Offsets o = SaveBytes.HeaderOffsets(bytes);
        int players = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(o.Players));
        int at = o.Players + 4;
        for (int p = 0; p < players; p++) at += 4 + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at)) + 2;
        return at + 4 * 6 + 4 * 26 + 1 + 4 + 4 + 4 + 4 + 4;
    }
}
