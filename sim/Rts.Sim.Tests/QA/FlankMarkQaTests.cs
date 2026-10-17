using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M5-P2 fix round 1 (session 2026-10-17-0554): the per-unit flank mark (<see cref="UnitStore.Flank"/>) that the round
/// added to the state. It must be in <see cref="Simulation.StateHash"/> (state that changes later ticks), leave the hash of a
/// unit that never flanked alone, start at 0 for a new unit in a reused slot, and twin runs that write it must hash equal.
/// </summary>
[Collection(SerialCollection.Name)]
public class FlankMarkQaTests
{
    private readonly ITestOutputHelper _out;

    public FlankMarkQaTests(ITestOutputHelper output) => _out = output;

    /// <summary>A planted own line of <paramref name="count"/> Crossbowmen and one Heavy Infantry behind it with a Move through, re-issued every 3 ticks 2 m along the line (the dev's BUG-0883 spam shape).</summary>
    private static (Simulation Sim, EntityHandle Walker) LineScene(int count, ulong seed)
    {
        var sim = new Simulation(TestSim.Config(seed, 2, 2 * count + 8, 16 * count + 64), LocalMovementTests.Flat(80));
        UnitStore u = sim.World.Units;
        TestSim.KeepIn(sim, 1);
        var mid = new Vector2(80f, 80f);
        var row = new EntityHandle[count];
        for (int k = 0; k < count; k++)
        {
            Vector2 p = mid + new Vector2(0f, k - (count - 1) / 2f);
            EntityHandle t = Place(sim, 1, Raider, p + new Vector2(14.5f, 0f));
            u.Hold[t.Index] = true;
            Spot(sim, 0, t);
            row[k] = Place(sim, 0, Crossbowman, p);
            sim.Enqueue(Command.Attack(0, row[k], t, false));
        }
        EntityHandle w = Place(sim, 0, HeavyInfantry, mid + new Vector2(-3f, 0.2f));
        return (sim, w);
    }

    private static void Topped(Simulation sim)
    {
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i]) u.Hp[i] = sim.World.Data.Units[u.TypeId[i]].Hp;
        sim.Tick();
    }

    private static ulong[] Run(ulong seed, int ticks, out int mark)
    {
        (Simulation sim, EntityHandle w) = LineScene(24, seed);
        var hashes = new ulong[ticks];
        for (int t = 0; t < ticks; t++)
        {
            if (t == 40) sim.Enqueue(Command.Move(0, w, new Vector2(85f, 80f)));
            else if (t > 40 && t % 3 == 0) sim.Enqueue(Command.Move(0, w, new Vector2(85f, 80f + (t / 3 % 2 == 1 ? 2f : 0f))));
            Topped(sim);
            hashes[t] = sim.StateHash();
        }
        mark = sim.World.Units.Flank[w.Index];
        return hashes;
    }

    /// <summary>Two runs of the spam scene hash equal every tick, the walker's mark is written, and another seed parts.</summary>
    [Fact]
    public void LineSpamScene_TwinRuns_HashEqualEveryTick_MarkWritten()
    {
        ulong[] a = Run(7, 400, out int markA);
        ulong[] b = Run(7, 400, out int markB);
        for (int t = 0; t < a.Length; t++) Assert.True(a[t] == b[t], $"tick {t}: {a[t]:X16} vs {b[t]:X16}");
        Assert.Equal(markA, markB);
        Assert.NotEqual(0, markA);
        ulong[] c = Run(8, 400, out _);
        _out.WriteLine($"mark {markA}, final hash {a[^1]:X16}, seed 8 {c[^1]:X16}");
        Assert.NotEqual(a[^1], c[^1]);
    }

    /// <summary>The mark is hashed: changing it changes the hash, and putting it back restores the hash.</summary>
    [Fact]
    public void FlankMark_IsInTheStateHash()
    {
        (Simulation sim, EntityHandle w) = LineScene(4, 7);
        for (int t = 0; t < 5; t++) Topped(sim);
        UnitStore u = sim.World.Units;
        Assert.Equal(0, u.Flank[w.Index]);
        ulong before = sim.StateHash();
        u.Flank[w.Index] = (sim.World.TickNumber + 1) * 128 + 2 * 12 + 1;
        ulong marked = sim.StateHash();
        u.Flank[w.Index] = (sim.World.TickNumber + 1) * 128 + 2 * 12; // the side alone differs
        ulong otherSide = sim.StateHash();
        u.Flank[w.Index] = 0;
        Assert.NotEqual(before, marked);
        Assert.NotEqual(marked, otherSide);
        Assert.Equal(before, sim.StateHash());
    }

    /// <summary>A unit that flanked dies; the unit spawned into its freed slot starts with no flank mark.</summary>
    [Fact]
    public void FlankMark_ResetForANewUnitInAReusedSlot()
    {
        (Simulation sim, EntityHandle w) = LineScene(24, 7);
        UnitStore u = sim.World.Units;
        for (int t = 0; t < 40; t++) Topped(sim);
        sim.Enqueue(Command.Move(0, w, new Vector2(85f, 80f)));
        for (int t = 0; t < 60 && u.Flank[w.Index] == 0; t++) Topped(sim);
        Assert.NotEqual(0, u.Flank[w.Index]);
        int slot = w.Index;
        u.Free(w); // the store's free, as a death frees the slot
        Assert.False(u.Alive[slot]);
        // Spawn until the freed slot is reused (free-list order is the store's business).
        EntityHandle n = default;
        for (int k = 0; k < 4 && n.Index != slot; k++) n = Place(sim, 0, HeavyInfantry, new Vector2(40f + k, 40f));
        Assert.Equal(slot, n.Index);
        Assert.Equal(0, u.Flank[n.Index]);
    }
}
