using System.Buffers.Binary;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M6-V14: <see cref="SaveSummary"/> (the Load Game list's header reader, a view-track ViewApi addition) reads exactly
/// what <see cref="Simulation.TrySave"/> wrote (seed, seats, map width, tick, version) for every save scene at several
/// ticks, is read-only (the sim's hash, tick and save bytes are unchanged by a read; it reads at most
/// <see cref="SaveSummary.MaxHeaderBytes"/>), and never throws on damaged bytes: every truncation of a real header, a
/// flipped byte at every header offset, forged lengths and seat bytes, random junk.
/// </summary>
public class SaveSummaryQaTests
{
    public static IEnumerable<object[]> Names() => SaveScenes.Names();

    [Theory]
    [MemberData(nameof(Names))]
    public void TheSummary_MatchesTheSavedConfig_AtSeveralTicks_AndReadingItChangesNothing(string scene)
    {
        SaveScene s = SaveScenes.Named(scene);
        Simulation sim = s.Build();
        var commands = new List<Command>();
        int[] at = { 0, 1, 137 };
        foreach (int tick in at)
        {
            while (sim.TickNumber < tick)
            {
                commands.Clear();
                s.Script(sim, commands);
                foreach (Command c in commands) sim.Enqueue(c);
                sim.Tick();
            }
            ulong hash = sim.StateHash();
            byte[] bytes = SaveTestRun.Save(sim);
            using (var stream = new MemoryStream(bytes, writable: false))
            {
                Assert.True(SaveSummary.TryRead(stream, out SaveSummary? summary), $"{scene} @ {tick}: the header did not read");
                Assert.True(stream.Position <= SaveSummary.MaxHeaderBytes, $"{scene} @ {tick}: read {stream.Position} bytes");
                SimConfig c = sim.World.Config;
                Assert.Equal(SaveFormat.CurrentVersion, summary!.FormatVersion);
                Assert.Equal(c.Seed, summary.Seed);
                Assert.Equal(c.Map.Width, summary.MapWidth);
                Assert.Equal(sim.TickNumber, summary.Tick);
                Assert.Equal(c.PlayerCount, summary.Seats.Length);
                for (int p = 0; p < c.PlayerCount; p++)
                {
                    PlayerSeat seat = c.SeatOf(p);
                    string id = seat.Faction >= 0 ? c.Data.Factions[seat.Faction].Key : "-";
                    Assert.Equal(id, summary.Seats[p].Faction);
                    Assert.Equal(seat.Controller, summary.Seats[p].Controller);
                    Assert.Equal(seat.Difficulty, summary.Seats[p].Difficulty);
                }
            }
            // Read-only: the sim is where it was, and saves the same bytes again.
            Assert.Equal(hash, sim.StateHash());
            Assert.Equal(bytes, SaveTestRun.Save(sim));
        }
    }

    [Fact]
    public void EveryTruncation_OfARealHeader_IsFalse_AndNeverThrows()
    {
        byte[] good = SaveTestRun.Save(SaveScenes.Menu(3).Build());
        Assert.True(SaveSummary.TryParse(good, out _));
        int headerEnd = HeaderEnd(good);
        for (int n = 0; n < headerEnd; n++)
            Assert.False(SaveSummary.TryParse(good.AsSpan(0, n), out SaveSummary? s) || s != null, $"length {n} of a {headerEnd}-byte header read");
        // Past the header the body is not read: a cut body still lists (Load refuses it as Truncated).
        Assert.True(SaveSummary.TryParse(good.AsSpan(0, headerEnd), out _));
    }

    [Fact]
    public void AFlippedByte_AtEveryHeaderOffset_NeverThrows_AndNeverInventsAnOutOfRangeSeat()
    {
        byte[] good = SaveTestRun.Save(SaveScenes.Menu(3).Build());
        int headerEnd = HeaderEnd(good);
        for (int i = 0; i < headerEnd; i++)
        {
            foreach (byte mask in new byte[] { 0x01, 0x80, 0xFF })
            {
                byte[] bad = (byte[])good.Clone();
                bad[i] ^= mask;
                if (!SaveSummary.TryParse(bad, out SaveSummary? s)) continue;
                Assert.True(s!.Seats.Length is >= 1 and <= 16 && s.Tick >= 0 && s.MapWidth >= 1, $"offset {i} mask {mask:x2}: {s}");
                foreach (var seat in s.Seats)
                    Assert.True(seat.Faction.Length > 0 && (byte)seat.Controller <= (byte)SeatController.Ai && (byte)seat.Difficulty <= (byte)AiDifficulty.Hard,
                        $"offset {i} mask {mask:x2}: seat {seat}");
            }
        }
    }

    [Fact]
    public void ForgedLengths_AndPlayerCounts_AreFalse()
    {
        byte[] good = SaveTestRun.Save(SaveScenes.Menu(3).Build());
        SaveBytes.Offsets o = SaveBytes.HeaderOffsets(good);
        foreach (int players in new[] { 0, -1, 17, int.MaxValue, int.MinValue })
            Assert.False(SaveSummary.TryParse(With(good, o.Players, players), out _), $"players {players}");
        foreach (int length in new[] { -1, 257, int.MaxValue, int.MinValue, good.Length })
        {
            Assert.False(SaveSummary.TryParse(With(good, o.SimVersion, length), out _), $"sim version length {length}");
            Assert.False(SaveSummary.TryParse(With(good, o.Seat0Faction, length), out _), $"seat 0 id length {length}");
        }
        Assert.False(SaveSummary.TryParse(With(good, o.Seat0Faction, 0), out _), "an empty faction id");
        byte[] ai = (byte[])good.Clone();
        ai[o.Seat0Controller] = 2; // past SeatController.Ai
        Assert.False(SaveSummary.TryParse(ai, out _), "controller 2");
        ai[o.Seat0Controller] = 0;
        ai[o.Seat0Controller + 1] = 3; // past AiDifficulty.Hard
        Assert.False(SaveSummary.TryParse(ai, out _), "difficulty 3");
        Assert.False(SaveSummary.TryParse(With(good, 8, 0), out _), "format version 0");
        Assert.True(SaveSummary.TryParse(With(good, 8, 99), out SaveSummary? v99) && v99!.FormatVersion == 99, "version 99 lists (Load refuses it)");
    }

    [Fact]
    public void RandomJunk_WithAndWithoutTheMagic_NeverThrows()
    {
        var rng = new System.Random(1236); // test-only junk generator, not sim code
        byte[] magic = SaveFormat.Magic.ToArray();
        for (int k = 0; k < 4000; k++)
        {
            var junk = new byte[rng.Next(0, 9000)];
            rng.NextBytes(junk);
            if (k % 2 == 0 && junk.Length >= magic.Length) magic.CopyTo(junk, 0);
            if (k % 4 == 0 && junk.Length >= 12) BinaryPrimitives.WriteInt32LittleEndian(junk.AsSpan(magic.Length), 1);
            SaveSummary.TryParse(junk, out SaveSummary? s);
            if (s != null) Assert.True(s.Seats.Length is >= 1 and <= 16 && s.Tick >= 0 && s.MapWidth >= 1, $"junk {k}: {s}");
            using var stream = new MemoryStream(junk, writable: false);
            SaveSummary.TryRead(stream, out _);
        }
    }

    [Fact]
    public void TryRead_OnAThrowingOrClosedStream_IsFalse()
    {
        var closed = new MemoryStream(SaveTestRun.Save(SaveScenes.Menu(3).Build()));
        closed.Dispose();
        Assert.False(SaveSummary.TryRead(closed, out SaveSummary? s));
        Assert.Null(s);
    }

    // The offset just past the header's tick field: the first byte the summary never reads.
    private static int HeaderEnd(byte[] bytes)
    {
        SaveBytes.Offsets o = SaveBytes.HeaderOffsets(bytes);
        int players = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(o.Players));
        int at = o.Players + 4;
        for (int p = 0; p < players; p++) at += 4 + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at)) + 2;
        return at + 4 * 6 + 4 * 26 + 1 + 4 + 4 + 4 + 4 + 4;
    }

    private static byte[] With(byte[] bytes, int at, int value)
    {
        byte[] copy = (byte[])bytes.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(at), value);
        return copy;
    }
}
