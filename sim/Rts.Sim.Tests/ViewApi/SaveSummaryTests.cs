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
