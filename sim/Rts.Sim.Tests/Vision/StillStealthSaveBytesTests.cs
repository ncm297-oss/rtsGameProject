using System.Buffers.Binary;
using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;

namespace Rts.Sim.Tests;

/// <summary>
/// M8-2 criterion 10 (third row): a save made on data without <c>stillStealth</c> is byte for byte what the code before M8-2
/// wrote. QA checked it against the base (10acb53) with SHA-256 digests; as permanent pins those moved with every shipped
/// number and every combat change (BUG-1580), so these rows prove it without depending on the shipped numbers. The M8-2
/// code changes a save only through its <c>STIL</c> part, written exactly when <see cref="Entities.UnitStore.Still"/>
/// exists, so: (a) on data without the field there is no store and no <c>STIL</c> tag; (b) on the same data plus the field
/// on a unit type the match never fields (store allocated, the pass and every M8-2 hook running), the save is the first one
/// plus a <c>STIL</c> part between <c>REGN</c>/<c>FRST</c> and <c>LEDR</c>, and nothing else: cut that part out, put back the
/// header's data hash, fix the length and checksum, and the bytes are equal. Run on the shipped data and on the M8-1 forest copy.
/// </summary>
public class StillStealthSaveBytesTests
{
    /// <summary>A unit type the brawl never fields (it places Malazan Heavy Infantry, Whirlwind Raiders and a Malazan Laborer).</summary>
    private const string Unfielded = "teblor_javelin_thrower";

    /// <summary>A 300-tick brawl of 12 a side on a flat map, a Laborer standing aside, saved after the last tick.</summary>
    private static (byte[] Bytes, Simulation Sim) BrawlSave(GameData data, bool tree)
    {
        var sim = new Simulation(new SimConfig(5, 2, 96, 800) { Data = data }, LocalMovementTests.Flat(48));
        if (tree) ForestScenes.Tree(sim, 10, 10);
        CombatScenes.Place(sim, 0, data.FindUnit("malazan_laborer"), CombatScenes.At(sim, 11, 10));
        CombatScenes.FlatBrawl(sim, 12, new Vector2(24f, 24f), 12f);
        for (int t = 0; t < 300; t++) sim.Tick();
        return (SaveTestRun.Save(sim), sim);
    }

    /// <summary>
    /// <paramref name="with"/> (a save on the data plus the field) as it would be without the field: its one <c>STIL</c> part
    /// (up to the <c>LEDR</c> tag the world writes next) cut out, the header's data hash set to <paramref name="dataHash"/>,
    /// the total length and the checksum fixed.
    /// </summary>
    private static byte[] WithoutStillPart(byte[] with, ulong dataHash)
    {
        int start = with.AsSpan().IndexOf("STIL"u8);
        Assert.True(start > 0, "no STIL part");
        Assert.Equal(-1, with.AsSpan(start + 4).IndexOf("STIL"u8));
        int end = start + with.AsSpan(start).IndexOf("LEDR"u8);
        Assert.True(end > start, "no LEDR tag after the STIL part");
        byte[] cut = new byte[with.Length - (end - start)];
        with.AsSpan(0, start).CopyTo(cut);
        with.AsSpan(end).CopyTo(cut.AsSpan(start));
        BinaryPrimitives.WriteUInt64LittleEndian(cut.AsSpan(SaveBytes.HeaderOffsets(cut).DataHash), dataHash);
        BinaryPrimitives.WriteInt64LittleEndian(cut.AsSpan(SaveFormat.LengthOffset), cut.Length);
        return SaveBytes.Rechecksum(cut);
    }

    private static void AssertTheFieldAddsOnlyItsPart(GameData without, GameData with, bool tree)
    {
        Assert.False(without.AnyStillStealth());
        Assert.True(with.AnyStillStealth());
        Assert.True(with.FindUnit(Unfielded) >= 0);
        Assert.Equal(without.Units.Length, with.Units.Length);
        Assert.NotEqual(without.ContentHash(), with.ContentHash());

        (byte[] a, Simulation simA) = BrawlSave(without, tree);
        (byte[] b, Simulation simB) = BrawlSave(with, tree);

        // (a) No field: no store, no part.
        Assert.Null(simA.World.Units.Still);
        Assert.Equal(-1, a.AsSpan().IndexOf("STIL"u8));
        Assert.Equal(without.ContentHash(), BinaryPrimitives.ReadUInt64LittleEndian(a.AsSpan(SaveBytes.HeaderOffsets(a).DataHash)));

        // (b) The field on a type nobody fields: the store and the pass run, no unit leaves the default, the match is the same.
        Entities.UnitStore ub = simB.World.Units;
        Assert.NotNull(ub.Still);
        int live = 0;
        for (int i = 0; i < ub.Capacity; i++)
        {
            Assert.False(ub.Still!.HasState(i));
            if (!ub.Alive[i]) continue;
            live++;
            Assert.NotEqual(with.FindUnit(Unfielded), ub.TypeId[i]);
        }
        Assert.True(live > 0);
        Assert.Equal(with.ContentHash(), BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(SaveBytes.HeaderOffsets(b).DataHash)));
        Assert.True(b.Length > a.Length);
        Assert.Equal(a, WithoutStillPart(b, without.ContentHash()));
    }

    [Fact]
    public void ASaveOnTheShippedData_IsTheBytesMainWrites()
    {
        GameData with = AiScenes.DataWith(dir => dir.SetUnitField("teblor", Unfielded, "stillStealth", StillScenes.Block(3)));
        AssertTheFieldAddsOnlyItsPart(TestSim.Data, with, tree: false);
    }

    [Fact]
    public void ASaveOnTheForestCopy_IsTheBytesMainWrites()
    {
        GameData with = ForestScenes.DataWith(3, dir => dir.SetUnitField("teblor", Unfielded, "stillStealth", StillScenes.Block(3)));
        Assert.Equal(ForestScenes.Data3.Units.Length, with.Units.Length);
        // The forest copy writes its FRST part in both saves; only STIL differs.
        AssertTheFieldAddsOnlyItsPart(ForestScenes.Data3, with, tree: true);
    }
}
