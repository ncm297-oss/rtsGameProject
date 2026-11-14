using System.Buffers.Binary;
using Rts.Sim.Abilities;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Save;
using Rts.Sim.Tests.QA;

namespace Rts.Sim.Tests.Save;

/// <summary>
/// M7-H2 (BUG-1220): the status part's derived counts are held to the entries at load, as the <c>REGN</c> part's are
/// (BUG-1165): each slot's stealth count is its stealthed entries, its blind the one the blind rule picks from them, and the
/// three totals (<c>UnitsWithStatuses</c>, <c>BlindedUnits</c>, <c>StealthedUnits</c>) the slots that count one. A forged
/// count is <see cref="SaveError.Malformed"/>; the honest save loads. No format change: the offsets are the M7-H1 QA
/// helper's (<see cref="M7H1QaTests.Regn"/>), which reads the status part just before the <c>REGN</c> tag.
/// </summary>
public class StatusCountSaveTests
{
    /// <summary>Three Malazan Heavy Infantry on shipped data: one stealthed, one blinded, one plain; saved at tick 3.</summary>
    private static (GameData Data, byte[] Bytes, int Cap, int Stealthed, int Blinded, int Plain) Scene()
    {
        GameData d = TestSim.Data;
        Simulation sim = RegenTests.Scene(d);
        int heavy = d.FindUnit("malazan_heavy_infantry");
        EntityHandle s = CombatScenes.Place(sim, 0, heavy, CombatScenes.At(sim, 10, 10));
        EntityHandle b = CombatScenes.Place(sim, 0, heavy, CombatScenes.At(sim, 20, 10));
        EntityHandle p = CombatScenes.Place(sim, 0, heavy, CombatScenes.At(sim, 30, 10));
        sim.Tick();
        StatusSystem.Apply(sim.World, s.Index, d.FindStatus("stealthed"), 1f, 400, 0);
        StatusSystem.Apply(sim.World, b.Index, d.FindStatus("blinded"), 1f, 400, 1);
        sim.Tick();
        sim.Tick();
        StatusStore st = sim.World.Units.Statuses;
        Assert.True(st.HasStealth(s.Index) && st.BlindOf(b.Index) == d.FindStatus("blinded") && st.Count[p.Index] == 0, "precondition: the scene's statuses");
        Assert.Equal((2, 1, 1), (st.UnitsWithStatuses, st.BlindedUnits, st.StealthedUnits));
        return (d, SaveTestRun.Save(sim), sim.World.Units.Capacity, s.Index, b.Index, p.Index);
    }

    private static int R32(byte[] b, int at) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at));

    private static void W32(byte[] b, int at, int v) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(at), v);

    [Theory]
    [InlineData("honest", true)]
    [InlineData("stealth: a plain slot counts one, the total follows", false)]
    [InlineData("stealth: the stealthed slot counts none, the total follows", false)]
    [InlineData("stealth: the stealthed slot counts two", false)]
    [InlineData("stealth: the total +1", false)]
    [InlineData("stealth: the total -1", false)]
    [InlineData("blind: a plain slot holds the blind, the total follows", false)]
    [InlineData("blind: the blinded slot holds none, the total follows", false)]
    [InlineData("blind: the blinded slot holds a slow's id", false)]
    [InlineData("blind: the total +1", false)]
    [InlineData("blind: the total -1", false)]
    [InlineData("units with statuses: the total +1", false)]
    [InlineData("units with statuses: the total -1", false)]
    public void AForgedStealthOrBlindCount_IsRefused(string forge, bool loads)
    {
        (GameData d, byte[] bytes, int cap, int stealthed, int blinded, int plain) = Scene();
        M7H1QaTests.Regn o = M7H1QaTests.Regn.Find(bytes, cap);
        // After the five per-entry arrays: _blind (int per slot), then _stealth (a byte per slot), each with its length
        // prefix, then UnitsWithStatuses, BlindedUnits, StealthedUnits.
        int withStatuses = o.UnitsWithStatuses, blindedUnits = withStatuses + 4, stealthedUnits = withStatuses + 8;
        int stealth = withStatuses - cap, blind = stealth - 4 - 4 * cap;
        Assert.Equal(cap, R32(bytes, stealth - 4));
        Assert.Equal(cap, R32(bytes, blind - 4));
        Assert.Equal(1, bytes[stealth + stealthed]);
        Assert.Equal(d.FindStatus("blinded") + 1, R32(bytes, blind + 4 * blinded));
        Assert.Equal((2, 1, 1), (R32(bytes, withStatuses), R32(bytes, blindedUnits), R32(bytes, stealthedUnits)));
        byte[] b = (byte[])bytes.Clone();
        switch (forge)
        {
            case "honest": break;
            case "stealth: a plain slot counts one, the total follows": b[stealth + plain] = 1; W32(b, stealthedUnits, 2); break;
            case "stealth: the stealthed slot counts none, the total follows": b[stealth + stealthed] = 0; W32(b, stealthedUnits, 0); break;
            case "stealth: the stealthed slot counts two": b[stealth + stealthed] = 2; break;
            case "stealth: the total +1": W32(b, stealthedUnits, 2); break;
            case "stealth: the total -1": W32(b, stealthedUnits, 0); break;
            case "blind: a plain slot holds the blind, the total follows": W32(b, blind + 4 * plain, d.FindStatus("blinded") + 1); W32(b, blindedUnits, 2); break;
            case "blind: the blinded slot holds none, the total follows": W32(b, blind + 4 * blinded, 0); W32(b, blindedUnits, 0); break;
            case "blind: the blinded slot holds a slow's id": W32(b, blind + 4 * blinded, d.FindStatus("slowed") + 1); break;
            case "blind: the total +1": W32(b, blindedUnits, 2); break;
            case "blind: the total -1": W32(b, blindedUnits, 0); break;
            case "units with statuses: the total +1": W32(b, withStatuses, 3); break;
            case "units with statuses: the total -1": W32(b, withStatuses, 1); break;
            default: throw new ArgumentException(forge);
        }
        Assert.Equal(loads ? SaveError.None : SaveError.Malformed, SaveTestRun.TryLoad(d, SaveBytes.Rechecksum(b)));
    }
}
