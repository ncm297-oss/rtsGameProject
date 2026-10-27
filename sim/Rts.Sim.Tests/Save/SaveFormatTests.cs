using System.Buffers.Binary;
using System.Text;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.Save;

namespace Rts.Sim.Tests.Save;

/// <summary>
/// M6-S2 criterion 1: the save format as docs/03 "Save/load and replays" tables it: the header's fields in order, the
/// version, the total length, the trailing FNV-1a 64 checksum, the first section; and the flow-field section's one
/// encoding choice (costs rebuilt from the directions only when that is bit-exact).
/// </summary>
public class SaveFormatTests
{
    [Fact]
    public void CurrentVersion_Is3()
    {
        // M6-S3: the AI seats' section bumped the format to 2 (rule 36); M6-S4: a team byte per seat to 3. Older files are
        // refused, not read.
        Assert.Equal(3, SaveFormat.CurrentVersion);
    }

    /// <summary>M6-S4: each seat's team is one byte, in player order, after the tick (0 for no team); every format-2 field keeps its offset.</summary>
    [Fact]
    public void Header_HoldsEachSeatsTeam_AfterTheTick()
    {
        var seats = System.Collections.Immutable.ImmutableArray.Create(new PlayerSeat(0, SeatController.Human, AiDifficulty.Normal, 3),
            new PlayerSeat(1, SeatController.Ai, AiDifficulty.Hard, 8), new PlayerSeat(0, SeatController.Human, AiDifficulty.Easy));
        var sim = new Simulation(TestSim.Config(3, 3, 64, 64) with { Seats = seats });
        byte[] bytes = SaveTestRun.Save(sim);
        using var r = new BinaryReader(new MemoryStream(bytes), Encoding.ASCII);
        r.ReadBytes(8);
        Assert.Equal(3, r.ReadInt32());
        r.ReadInt64();
        ReadString(r);
        r.ReadUInt64();
        r.ReadUInt64();
        Assert.Equal(3, r.ReadInt32());
        for (int p = 0; p < 3; p++)
        {
            Assert.Equal(TestSim.Data.Factions[seats[p].Faction].Key, ReadString(r));
            Assert.Equal((byte)seats[p].Controller, r.ReadByte());
            Assert.Equal((byte)seats[p].Difficulty, r.ReadByte());
        }
        Assert.Equal(64, r.ReadInt32()); // the unit capacity follows the seats, as in format 2
        r.ReadBytes(4 * 5 + 4 * 26 + 1 + 4 * 4); // the other capacities, the map, combat, the cast floats, the stats ints
        Assert.Equal(0, r.ReadInt32()); // the tick
        Assert.Equal(new byte[] { 3, 8, 0 }, r.ReadBytes(3));
        Assert.Equal("HMAP"u8.ToArray(), r.ReadBytes(4));
    }

    [Fact]
    public void TheAiSection_ComesAfterTheCommands_AndHoldsEachAiSeatInPlayerOrder()
    {
        // A human-only save: the tag and a seat count of 0 right before END!.
        byte[] human = SaveBytes.Small();
        int body = human.Length - SaveFormat.ChecksumLength;
        Assert.Equal("AIST"u8.ToArray(), human.AsSpan(body - 12, 4).ToArray());
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(human.AsSpan(body - 8)));

        // Human, AI, AI: the count, then player 1's entry (its player, its pick) and later player 2's.
        var seats = System.Collections.Immutable.ImmutableArray.Create(new PlayerSeat(0, SeatController.Human, AiDifficulty.Normal),
            new PlayerSeat(1, SeatController.Ai, AiDifficulty.Hard), new PlayerSeat(0, SeatController.Ai, AiDifficulty.Easy));
        var sim = new Simulation(TestSim.Config(3, 3, 64, 64) with { Seats = seats });
        byte[] bytes = SaveTestRun.Save(sim);
        int at = SaveBytes.AiSection(bytes);
        Assert.True(bytes.AsSpan(0, at).LastIndexOf("CMDS"u8) > 0);
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + 4)));
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + 8)));
        Assert.Equal(sim.AiOpening(1), BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + 12)));
        Assert.Equal(sim.AiOf(1)!.Rng.State, BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(at + 16)));
        Assert.Equal(sim.AiOf(1)!.Rng.Increment, BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(at + 24)));
        Assert.Equal("END!"u8.ToArray(), bytes.AsSpan(bytes.Length - SaveFormat.ChecksumLength - 4, 4).ToArray());
    }

    [Fact]
    public void Header_HoldsEveryFieldInTheTablesOrder()
    {
        // A generated match with non-default config values, so each field is told apart from its neighbours.
        var config = new SimConfig(0xDEADBEEF12345678UL, 2, 300, 700)
        {
            Data = TestSim.Data, ResourceCapacity = 3000, BuildingCapacity = 200, ProjectileCapacity = 40, ZoneCapacity = 30,
            Map = MapGenParams.Default with { Forests = 3, GoldMines = 2 }, Combat = false, CastWalkInBlockerRange = 2.25f,
            CastWalkInPatienceSeconds = 4.5f, StatsSampleTicks = 150, StatsSampleCapacity = 77,
        };
        var sim = new Simulation(config);
        sim.Tick();
        sim.Tick();
        byte[] bytes = SaveTestRun.Save(sim);

        using var r = new BinaryReader(new MemoryStream(bytes), Encoding.ASCII);
        Assert.Equal("RTSSAVE\0"u8.ToArray(), r.ReadBytes(8));
        Assert.Equal(SaveFormat.CurrentVersion, r.ReadInt32());
        Assert.Equal(bytes.Length, r.ReadInt64());
        Assert.Equal(SimInfo.Version, ReadString(r));
        Assert.Equal(TestSim.Data.ContentHash(), r.ReadUInt64());
        Assert.Equal(config.Seed, r.ReadUInt64());
        Assert.Equal(2, r.ReadInt32());
        for (int p = 0; p < 2; p++)
        {
            Assert.Equal(TestSim.Data.Factions[p % 2].Key, ReadString(r));
            Assert.Equal((byte)SeatController.Human, r.ReadByte());
            Assert.Equal((byte)AiDifficulty.Normal, r.ReadByte());
        }
        Assert.Equal(new[] { 300, 700, 3000, 200, 40, 30 }, Enumerable.Range(0, 6).Select(_ => r.ReadInt32()).ToArray());
        MapGenParams m = config.Map;
        object[] map =
        {
            m.Width, m.Height, m.EdgeMargin, m.Level1Plateaus, m.Level1MinSize, m.Level1MaxSize, m.Level2Plateaus, m.Level2MinSize,
            m.Level2MaxSize, m.Level2Inset, m.RampWidth, m.RampLength, m.RampsPerPlateau, m.RampTries, m.MinPassableFraction,
            m.MaxAttempts, m.Forests, m.ForestMinTrees, m.ForestMaxTrees, m.GoldMines, m.MineSpacing, m.StartLocations,
            m.StartClearRadius, m.StartEdgeMargin, m.StartMineDistance, m.StartForestDistance,
        };
        foreach (object field in map)
        {
            if (field is float f) Assert.Equal(BitConverter.SingleToInt32Bits(f), r.ReadInt32());
            else Assert.Equal((int)field, r.ReadInt32());
        }
        Assert.Equal(0, r.ReadByte()); // combat off
        Assert.Equal(BitConverter.SingleToInt32Bits(2.25f), r.ReadInt32());
        Assert.Equal(BitConverter.SingleToInt32Bits(4.5f), r.ReadInt32());
        Assert.Equal(150, r.ReadInt32());
        Assert.Equal(77, r.ReadInt32());
        Assert.Equal(2, r.ReadInt32()); // the tick
        Assert.Equal(new byte[] { 0, 0 }, r.ReadBytes(2)); // M6-S4: each seat's team, none
        Assert.Equal("HMAP"u8.ToArray(), r.ReadBytes(4)); // the first section: the stored heightmap
        Assert.Equal(config.Map.Width, r.ReadInt32());
        Assert.Equal(config.Map.Height, r.ReadInt32());
    }

    private static string ReadString(BinaryReader r) => Encoding.ASCII.GetString(r.ReadBytes(r.ReadInt32()));

    [Fact]
    public void Checksum_IsFnv1a64OfEveryByteBeforeIt_AndTheBodyEndsWithTheEndTag()
    {
        byte[] bytes = SaveBytes.Small();
        int body = bytes.Length - 8;
        ulong h = 14695981039346656037UL;
        for (int i = 0; i < body; i++) h = unchecked((h ^ bytes[i]) * 1099511628211UL);
        Assert.Equal(h, BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(body)));
        Assert.Equal("END!"u8.ToArray(), bytes.AsSpan(body - 4, 4).ToArray());
    }

    [Fact]
    public void ASave_IsTheSameBytesEveryTime_AndOneTickLaterItDiffers()
    {
        Simulation sim = SaveBytes.SmallSim();
        byte[] a = SaveTestRun.Save(sim), b = SaveTestRun.Save(sim);
        Assert.Equal(a, b);
        sim.Tick();
        Assert.NotEqual(a, SaveTestRun.Save(sim));
    }

    [Fact]
    public void FlowFields_OnTheMenuMap_AreStoredWithoutTheirCosts_AndReadBackBitForBit()
    {
        var sim = new Simulation(TestSim.Config(3, 2, 64, 64) with { Map = MapGenParams.Default with { Forests = 12, GoldMines = 8 } });
        NavGrid g = sim.World.NavGrid;
        var stack = new int[g.Width * g.Height];
        var state = new byte[g.Width * g.Height];
        for (int c = 0; c < g.Width * g.Height; c += 97)
        {
            FlowField field = FlowField.Build(g, c);
            var w = new SaveWriter();
            field.WriteTo(w);
            byte[] bytes = w.Raw();
            // Keys and versions (4 ints), the mode byte, the direction array (length + bytes); no cost array after it.
            int mode = 4 * 4;
            Assert.Equal(0, bytes[mode]);
            Assert.Equal(mode + 1 + 4 + g.Width * g.Height, bytes.Length);
            var back = new FlowField(g.Width, g.Height);
            var r = new SaveReader(bytes, 0, bytes.Length);
            back.ReadFrom(r, stack, state);
            Assert.False(r.Failed);
            AssertSameField(field, back, g.Width * g.Height);
        }
    }

    [Fact]
    public void AFieldWhoseCostsDontFollowItsDirections_IsStoredRaw_AndStillReadBackBitForBit()
    {
        var sim = new Simulation(TestSim.Config(3, 1, 8, 8));
        NavGrid g = sim.World.NavGrid;
        FlowField field = FlowField.Build(g, MoveScenario.CentralCell(g));
        // Bend one reached cell's cost by an ulp: no direction chain gives it any more.
        int cell = Enumerable.Range(0, g.Width * g.Height).First(c => field.DirectionAt(c) != FlowField.NoDirection);
        field.SetCostForTests(cell, BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(field.CostAt(cell)) + 1));
        var w = new SaveWriter();
        field.WriteTo(w);
        byte[] bytes = w.Raw();
        Assert.Equal(1, bytes[16]);
        var back = new FlowField(g.Width, g.Height);
        var r = new SaveReader(bytes, 0, bytes.Length);
        back.ReadFrom(r, new int[g.Width * g.Height], new byte[g.Width * g.Height]);
        Assert.False(r.Failed);
        AssertSameField(field, back, g.Width * g.Height);
    }

    private static void AssertSameField(FlowField a, FlowField b, int cells)
    {
        Assert.Equal((a.RequestedCell, a.TargetCell, a.Version, a.BlockVersion), (b.RequestedCell, b.TargetCell, b.Version, b.BlockVersion));
        for (int c = 0; c < cells; c++)
        {
            Assert.Equal(a.DirectionAt(c), b.DirectionAt(c));
            Assert.Equal(BitConverter.SingleToInt32Bits(a.CostAt(c)), BitConverter.SingleToInt32Bits(b.CostAt(c)));
        }
    }
}
