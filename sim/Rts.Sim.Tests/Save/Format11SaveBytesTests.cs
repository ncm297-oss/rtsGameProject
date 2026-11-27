using System.Buffers.Binary;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Save;

/// <summary>
/// M6-S14 (the save-format rule of the brief; <see cref="StillStealthSaveBytesTests"/> is the model): format 11 is format 10
/// byte for byte but for new values at the end of two AI parts, each seat's <c>AIST</c> block (the blind re-scouts' flag and
/// count, the rally point's memory: 14 bytes) and <c>HUNT</c> block (the hunt's find: 13 bytes), and the header's version.
/// <see cref="AsFormat10"/> cuts exactly those values out (located by writing each seat's blocks again, so a value written
/// anywhere else would not be found) and writes version 10. In-session (docs/03 "Implementation (M6-S14)") the base f40cb16e
/// wrote the same scenes, and its saves equalled these images with the header's data hash put back (the branch's data
/// carries <c>ai.minWorkers</c>); a save literal is not pinned here (rules 49 / 73).
/// </summary>
public class Format11SaveBytesTests(ITestOutputHelper output)
{
    /// <summary>The bytes M6-S14 appended to each seat's <c>AIST</c> block: <c>BlindScoutOut</c>, <c>BlindScoutsLost</c>, <c>RallyKept</c>, <c>HasRallyKept</c>.</summary>
    internal const int AistTail = 1 + 4 + 8 + 1;

    /// <summary>The bytes M6-S14 appended to each seat's <c>HUNT</c> block: <c>WaveFromHunt</c>, <c>HuntFindTick</c>, <c>HuntFindPoint</c>.</summary>
    internal const int HuntTail = 1 + 4 + 8;

    /// <summary>
    /// <paramref name="save"/> (of <paramref name="sim"/>, taken between ticks) with every seat's M6-S14 tail values cut from
    /// its <c>AIST</c> and <c>HUNT</c> blocks and the header's version set to 10, the length and checksum fixed: the bytes the
    /// format-10 writer laid out. Each seat's blocks are written again and found in place first, and each tail asserted to
    /// be the board's values.
    /// </summary>
    internal static byte[] AsFormat10(Simulation sim, byte[] save)
    {
        var cuts = new List<(int At, int Length)>();
        int aist = save.AsSpan().LastIndexOf("AIST"u8);
        Assert.True(aist > 0, "no AIST part");
        int at = aist + 4;
        int seats = BinaryPrimitives.ReadInt32LittleEndian(save.AsSpan(at));
        at += 4;
        for (int p = 0; p < sim.World.Config.PlayerCount; p++)
        {
            AiPlayer? ai = sim.AiOf(p);
            if (ai == null) continue;
            Assert.Equal(p, BinaryPrimitives.ReadInt32LittleEndian(save.AsSpan(at)));
            at += 4;
            var w = new SaveWriter();
            ai.WriteTo(w);
            byte[] block = w.Raw();
            Assert.True(save.AsSpan(at, block.Length).SequenceEqual(block), $"seat {p}: its AIST block is not where the writer puts it");
            AssertTail(block, ai.Board, hunt: false);
            cuts.Add((at + block.Length - AistTail, AistTail));
            at += block.Length;
            seats--;
        }
        Assert.Equal(0, seats);
        Assert.True(save.AsSpan(at, 4).SequenceEqual("HUNT"u8), "HUNT does not follow AIST");
        at += 8;
        for (int p = 0; p < sim.World.Config.PlayerCount; p++)
        {
            AiPlayer? ai = sim.AiOf(p);
            if (ai == null) continue;
            Assert.Equal(p, BinaryPrimitives.ReadInt32LittleEndian(save.AsSpan(at)));
            at += 4;
            var w = new SaveWriter();
            ai.WriteHuntTo(w);
            byte[] block = w.Raw();
            Assert.True(save.AsSpan(at, block.Length).SequenceEqual(block), $"seat {p}: its HUNT block is not where the writer puts it");
            AssertTail(block, ai.Board, hunt: true);
            cuts.Add((at + block.Length - HuntTail, HuntTail));
            at += block.Length;
        }
        Assert.True(save.AsSpan(at, 4).SequenceEqual("CTRM"u8), "CTRM does not follow HUNT");
        var image = new List<byte>(save.Length);
        int from = 0;
        foreach ((int cut, int length) in cuts)
        {
            image.AddRange(save.AsSpan(from, cut - from).ToArray());
            from = cut + length;
        }
        image.AddRange(save.AsSpan(from).ToArray());
        byte[] bytes = image.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(SaveFormat.MagicLength), 10);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(SaveFormat.LengthOffset), bytes.Length);
        return SaveBytes.Rechecksum(bytes);
    }

    /// <summary>The last bytes of a seat's block are the board's M6-S14 values, in the writer's order.</summary>
    private static void AssertTail(byte[] block, Blackboard b, bool hunt)
    {
        var w = new SaveWriter();
        if (hunt)
        {
            w.Write(b.WaveFromHunt);
            w.Write(b.HuntFindTick);
            w.Write(b.HuntFindPoint);
        }
        else
        {
            w.Write(b.BlindScoutOut);
            w.Write(b.BlindScoutsLost);
            w.Write(b.RallyKept);
            w.Write(b.HasRallyKept);
        }
        byte[] tail = w.Raw();
        Assert.Equal(hunt ? HuntTail : AistTail, tail.Length);
        Assert.True(block.AsSpan(block.Length - tail.Length).SequenceEqual(tail), "the block does not end with the M6-S14 values");
    }

    /// <summary>A save with no AI seat holds an empty <c>AIST</c> part, no <c>HUNT</c> or <c>CTRM</c>, and the current version (11): format 10's bytes but for the version (in-session: equal).</summary>
    [Fact]
    public void ASaveWithNoAiSeat_HasNoAiPart_AndTheCurrentVersion()
    {
        byte[] save = SaveBytes.Small();
        Assert.Equal(11, SaveFormat.CurrentVersion);
        Assert.Equal(SaveFormat.CurrentVersion, BinaryPrimitives.ReadInt32LittleEndian(save.AsSpan(SaveFormat.MagicLength)));
        int aist = SaveBytes.AiSection(save);
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(save.AsSpan(aist + 4))); // the AIST tag with no seat in it
        Assert.True(save.AsSpan(aist + 8, 4).SequenceEqual("END!"u8), "a part follows the empty AIST");
    }

    /// <summary>
    /// An AI save is the format-10 layout plus the tails: on the small one-seat AI sim, a two-seat AI match at 3,000 ticks and
    /// the M6-S14 scenes with every new value away from its default (a resumed hunt, a blind re-scout counted, a kept rally
    /// point), every seat's blocks are found where the writer puts them, each ends with the board's new values, and cutting
    /// them leaves exactly <c>27 x seats</c> bytes fewer.
    /// </summary>
    [Fact]
    public void AnAiSave_IsTheFormat10Layout_PlusTheTails()
    {
        var scenes = new List<(string Name, Simulation Sim)> { ("small AI", SaveBytes.SmallAiSim()) };
        Simulation match = AiScenes.Generated(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai), 2, record: false, out _);
        for (int t = 0; t < 3000; t++) match.Tick();
        scenes.Add(("two AI seats at 3,000", match));
        Simulation blind = M6S12AiRulesQaTests.BlindScene(keepMines: false);
        while (blind.AiOf(0)!.Board.ScoutPhase != 1 && blind.TickNumber < 2000) blind.Tick();
        scenes.Add(("a blind re-scout out", blind));
        foreach ((string name, Simulation sim) in scenes)
        {
            byte[] save = SaveTestRun.Save(sim);
            byte[] image = AsFormat10(sim, save);
            int seats = 0;
            for (int p = 0; p < sim.World.Config.PlayerCount; p++) if (sim.AiOf(p) != null) seats++;
            output.WriteLine($"{name}: {save.Length} bytes, the format-10 image {image.Length} ({seats} seats)");
            Assert.Equal(save.Length - seats * (AistTail + HuntTail), image.Length);
            Assert.Equal(10, BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(SaveFormat.MagicLength)));
        }
        Assert.True(blind.AiOf(0)!.Board.BlindScoutOut);
    }

    /// <summary>
    /// Criterion 14: a save from before M6-S14 (format 10) stops loading. The format-10 image of a save made here (what the
    /// M8-4 build wrote for it) and a format-11 save with only its version set to 10 are both refused
    /// <see cref="SaveError.FormatVersionMismatch"/>; the format-11 save loads.
    /// </summary>
    [Fact]
    public void AFormat10Save_IsRefused_FormatVersionMismatch()
    {
        Simulation sim = SaveBytes.SmallAiSim();
        byte[] save = SaveTestRun.Save(sim);
        Assert.Equal(SaveError.None, SaveTestRun.TryLoad(TestSim.Data, save));
        Assert.Equal(SaveError.FormatVersionMismatch, SaveTestRun.TryLoad(TestSim.Data, AsFormat10(sim, save)));
        byte[] relabelled = (byte[])save.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(relabelled.AsSpan(SaveFormat.MagicLength), 10);
        Assert.Equal(SaveError.FormatVersionMismatch, SaveTestRun.TryLoad(TestSim.Data, SaveBytes.Rechecksum(relabelled)));
    }

    /// <summary>
    /// The shipped data with <c>ai.minWorkers</c> written 1 (the M6-SH2 rule): the data the in-session comparison against the
    /// base ran the AI scenes on, so the floor sends nothing the base would not.
    /// </summary>
    internal static GameData FloorOne() => AiScenes.DataWith(dir => dir.EditJson("common/rules.json", r => r["ai"]!["minWorkers"] = 1));
}
