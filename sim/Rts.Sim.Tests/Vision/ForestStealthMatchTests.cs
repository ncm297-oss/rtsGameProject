using System.Buffers.Binary;
using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M8-1 criterion 4: determinism, saves and recordings with a forest-stealth faction. A scripted match on seed 3's generated
/// map (its own forests) on the <see cref="ForestScenes.Data3"/> copy: Malazan (the bonus) spawned along a forest edge,
/// Whirlwind (Raiders, Desert Archers, Priests with their 10 m detectors, Camp Followers) a little way off, both sides
/// ordered through commands every few seconds, so units hide, are found, strike from the trees, are hit, wait and hide again.
/// </summary>
public class ForestStealthMatchTests
{
    private readonly ITestOutputHelper _out;

    public ForestStealthMatchTests(ITestOutputHelper output) => _out = output;

    private static GameData D => ForestScenes.Data3;

    private const ulong Seed = 3;

    private static readonly string[] Malazan = { "malazan_heavy_infantry", "malazan_crossbowman", "malazan_laborer", "malazan_catapult", "malazan_cadre_mage" };
    private static readonly string[] Whirlwind = { "whirlwind_raider", "whirlwind_desert_archer", "whirlwind_priest", "whirlwind_camp_follower" };

    /// <summary>The open cells beside a standing tree (the forest edge), in cell order.</summary>
    private static List<int> Edge(World w)
    {
        NavGrid g = w.NavGrid;
        var edge = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
            if (g.IsPassable(c % g.Width, c / g.Width) && Abilities.ForestStealthSystem.BesideTree(w, g.CellCenter(c % g.Width, c / g.Width))) edge.Add(c);
        return edge;
    }

    /// <summary>The scene, all through commands (so a recorder sees it): 20 Malazan units on the forest edge cells nearest a picked edge cell, 20 Whirlwind units on open cells 10-16 cells from it.</summary>
    internal static Simulation NewSim(ReplayRecorder?[] recorder)
    {
        var sim = new Simulation(new SimConfig(Seed, 2, 96, 1024) { Data = D, Map = MapGenParams.Default with { Forests = 12, GoldMines = 4 } });
        if (recorder.Length > 0) recorder[0] = new ReplayRecorder(sim, checkpointInterval: 50);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        List<int> edge = Edge(w);
        Assert.True(edge.Count > 100, $"seed {Seed}'s map has {edge.Count} forest edge cells");
        int anchor = edge[edge.Count / 2];
        List<int> near = edge.OrderBy(c => D2(g, anchor, c)).ThenBy(c => c).Take(20).ToList();
        for (int k = 0; k < near.Count; k++)
            sim.Enqueue(Command.SpawnUnit(0, D.FindUnit(Malazan[k % Malazan.Length]), g.CellCenter(near[k] % g.Width, near[k] / g.Width)));
        var rng = new SimRng(Seed, 811);
        Reinforce(sim, Ring(g, anchor), 20, ref rng);
        return sim;
    }

    private static int D2(NavGrid g, int a, int c) => (c % g.Width - a % g.Width) * (c % g.Width - a % g.Width) + (c / g.Width - a / g.Width) * (c / g.Width - a / g.Width);

    /// <summary>The open cells 10-16 cells from <paramref name="anchor"/>, in cell order: where the Whirlwind arrive.</summary>
    private static List<int> Ring(NavGrid g, int anchor)
    {
        var ring = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
            if (g.IsPassable(c % g.Width, c / g.Width) && D2(g, anchor, c) >= 100 && D2(g, anchor, c) <= 256) ring.Add(c);
        return ring;
    }

    /// <summary><paramref name="count"/> Whirlwind units (the four types in turn) spawned by command on random <paramref name="ring"/> cells.</summary>
    private static void Reinforce(Simulation sim, List<int> ring, int count, ref SimRng rng)
    {
        NavGrid g = sim.World.NavGrid;
        for (int k = 0; k < count; k++)
        {
            int c = ring[rng.NextInt(0, ring.Count)];
            sim.Enqueue(Command.SpawnUnit(1, D.FindUnit(Whirlwind[k % Whirlwind.Length]), g.CellCenter(c % g.Width, c / g.Width)));
        }
    }

    /// <summary>Every 500 ticks (from tick 250) 8 more Whirlwind units arrive, so the fight goes on all match.</summary>
    private static void Reinforcements(Simulation sim, ref SimRng rng)
    {
        if (sim.TickNumber % 500 != 250) return;
        World w = sim.World;
        List<int> edge = Edge(w);
        Reinforce(sim, Ring(w.NavGrid, edge[edge.Count / 2]), 8, ref rng);
    }

    /// <summary>
    /// Whirlwind reinforcements every 500 ticks (<see cref="Reinforcements"/>). Every 120 ticks each Whirlwind unit attack-moves to a random Malazan unit (a Camp Follower walks there); every 160
    /// ticks each Malazan unit, with chance 1/3, attack-moves to a random Whirlwind unit, else walks to a random forest edge
    /// cell near where it stands. All through commands, from <paramref name="rng"/>.
    /// </summary>
    internal static void Orders(Simulation sim, ref SimRng rng)
    {
        Reinforcements(sim, ref rng);
        int t = sim.TickNumber;
        if (t % 120 != 7 && t % 160 != 11) return;
        UnitStore u = sim.World.Units;
        NavGrid g = sim.World.NavGrid;
        int follower = D.FindUnit("whirlwind_camp_follower");
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            var h = new EntityHandle(i, u.Generation[i]);
            int owner = u.Owner[i];
            if ((owner == 1) != (t % 120 == 7)) continue;
            Vector2 to = u.Position[i];
            for (int tries = 0; tries < 8; tries++)
            {
                int j = rng.NextInt(0, u.Capacity);
                if (u.Alive[j] && u.Owner[j] != owner) { to = u.Position[j]; break; }
            }
            if (owner == 1)
            {
                sim.Enqueue(u.TypeId[i] == follower ? Command.Move(1, h, to) : Command.AttackMove(1, h, to));
                continue;
            }
            if (rng.NextInt(0, 3) == 0)
            {
                sim.Enqueue(Command.AttackMove(0, h, to));
                continue;
            }
            // Back into the trees: an edge cell within 6 cells, or stay.
            g.WorldToCell(u.Position[i], out int cx, out int cy);
            for (int tries = 0; tries < 12; tries++)
            {
                int x = cx + rng.NextInt(-6, 7), y = cy + rng.NextInt(-6, 7);
                if (!g.InBounds(x, y) || !g.IsPassable(x, y) || !ForestStealthSystem.BesideTree(sim.World, g.CellCenter(x, y))) continue;
                sim.Enqueue(Command.Move(0, h, g.CellCenter(x, y)));
                break;
            }
        }
    }

    /// <summary>What a run saw: forest-held unit-ticks, lets-go by hp loss, reveals of a forest unit.</summary>
    private sealed record Counts(int HeldTicks, int HpLetGo, int Reveals);

    private static Counts Count(Simulation sim, byte[] heldBefore, int[] damagedBefore, Counts c)
    {
        UnitStore u = sim.World.Units;
        int held = 0, letGo = 0, reveals = 0, revealed = D.FindStatus("revealed");
        int now = sim.TickNumber - 1;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            if (u.Forest!.Held[i] != 0) held++;
            if (heldBefore[i] != 0 && u.Forest!.Held[i] == 0 && u.LastDamagedTick[i] == now && damagedBefore[i] != now) letGo++;
            int r = u.Statuses.IndexOf(i, revealed);
            if (u.Owner[i] == 0 && r >= 0 && u.Statuses.TicksRemaining[r] == D.Rules.RevealTicks) reveals++;
        }
        return new Counts(c.HeldTicks + held, c.HpLetGo + letGo, c.Reveals + reveals);
    }

    /// <summary>
    /// Two runs of the 3,000-tick match are hash-equal every tick; units were held, let go by hp loss and revealed; the
    /// recording of the first plays back to its end.
    /// </summary>
    [Fact]
    public void TwoRunsOf3000Ticks_AreHashEqualEveryTick_AndTheRecordingReplays()
    {
        var rec = new ReplayRecorder?[1];
        Simulation a = NewSim(rec), b = NewSim(Array.Empty<ReplayRecorder?>());
        var rngA = new SimRng(Seed, 812);
        var rngB = new SimRng(Seed, 812);
        UnitStore u = a.World.Units;
        var heldBefore = new byte[u.Capacity];
        var damagedBefore = new int[u.Capacity];
        var counts = new Counts(0, 0, 0);
        for (int t = 0; t < 3000; t++)
        {
            Orders(a, ref rngA);
            Orders(b, ref rngB);
            Array.Copy(u.Forest!.Held, heldBefore, u.Capacity);
            Array.Copy(u.LastDamagedTick, damagedBefore, u.Capacity);
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"twins differ after tick {t}");
            counts = Count(a, heldBefore, damagedBefore, counts);
        }
        _out.WriteLine($"{counts.HeldTicks} held unit-ticks, {counts.HpLetGo} lets-go by hp loss, {counts.Reveals} reveals of a forest unit; Malazan kills {a.World.Kills[0]}, Whirlwind kills {a.World.Kills[1]}");
        Assert.True(counts.HeldTicks > 1000, $"only {counts.HeldTicks} held unit-ticks");
        Assert.True(counts.HpLetGo > 0, "no forest unit was let go by an hp loss");
        Assert.True(counts.Reveals > 0, "no forest unit was revealed");
        Replay replay = rec[0]!.ToReplay();
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(replay), out Replay? back));
        ReplayResult result = ReplayPlayer.Run(back!, D);
        Assert.True(result.Ok, $"replay {result.Error} at tick {result.Tick}");
        Assert.Equal(3000, result.TicksRun);
    }

    /// <summary>
    /// Saved at the first tick past 400 with a unit held and another of the faction beside a tree but still waiting (its last
    /// landed attack or hp loss inside the delay), the save loads to the same hash and bytes, and the loaded match and the
    /// unsaved one stay hash-equal for 1,000 ticks under the same orders.
    /// </summary>
    [Fact]
    public void ASaveWhileHiddenAndMidWait_LoadsAndMatchesTheUnsavedRunFor1000Ticks()
    {
        Simulation sim = NewSim(Array.Empty<ReplayRecorder?>());
        var rng = new SimRng(Seed, 812);
        UnitStore u = sim.World.Units;
        int delay = D.Factions[D.FindFaction("malazan")].ForestStealthDelayTicks;
        bool found = false;
        while (!found && sim.TickNumber < 3000)
        {
            Orders(sim, ref rng);
            sim.Tick();
            if (sim.TickNumber < 400) continue;
            int now = sim.TickNumber;
            bool held = false, waiting = false;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i] || u.Owner[i] != 0) continue;
                if (u.Forest!.Held[i] != 0) held = true;
                else if (ForestStealthSystem.BesideTree(sim.World, u.Position[i])
                    && ((u.Forest!.LastAttackTick[i] >= 0 && now - u.Forest!.LastAttackTick[i] < delay) || (u.LastDamagedTick[i] >= 0 && now - u.LastDamagedTick[i] < delay)))
                    waiting = true;
            }
            found = held && waiting;
        }
        Assert.True(found, "no tick with a unit held and another waiting");
        _out.WriteLine($"saved at tick {sim.TickNumber}");
        byte[] bytes = SaveTestRun.Save(sim);
        Simulation loaded = SaveTestRun.Load(D, bytes);
        Assert.Equal(sim.StateHash(), loaded.StateHash());
        Assert.Equal(bytes, SaveTestRun.Save(loaded));
        Assert.Equal(u.Forest!.Held, loaded.World.Units.Forest!.Held);
        Assert.Equal(u.Forest.LastAttackTick, loaded.World.Units.Forest.LastAttackTick);
        var rngLoaded = rng;
        for (int k = 0; k < 1000; k++)
        {
            Orders(sim, ref rng);
            Orders(loaded, ref rngLoaded);
            sim.Tick();
            loaded.Tick();
            Assert.True(sim.StateHash() == loaded.StateHash(), $"loaded run differs {k + 1} ticks after the load");
        }
    }

    /// <summary>With the block in the data, each forest timer is state: changing one changes the state hash; without it, there is none.</summary>
    [Fact]
    public void ChangingAForestTimer_ChangesTheStateHash_WhenTheDataHasTheBlock()
    {
        Simulation sim = ForestScenes.Scene(D, combat: false);
        ForestScenes.Tree(sim, 20, 20);
        EntityHandle hidden = CombatScenes.Place(sim, 0, D.FindUnit("malazan_laborer"), ForestScenes.At(sim, 21, 20));
        EntityHandle open = CombatScenes.Place(sim, 0, D.FindUnit("malazan_laborer"), ForestScenes.At(sim, 30, 20));
        sim.Tick();
        UnitStore u = sim.World.Units;
        Assert.True(ForestScenes.Held(sim, hidden));
        ulong h0 = sim.StateHash();
        u.Forest!.LastAttackTick[open.Index] = 0;
        Assert.NotEqual(h0, sim.StateHash());
        u.Forest!.LastAttackTick[open.Index] = -1;
        Assert.Equal(h0, sim.StateHash());
        u.Forest!.LastAttackTick[hidden.Index] = 0;
        Assert.NotEqual(h0, sim.StateHash());
        u.Forest!.LastAttackTick[hidden.Index] = -1;
        u.Forest!.Held[hidden.Index] = 0;
        Assert.NotEqual(h0, sim.StateHash());
        u.Forest!.Held[hidden.Index] = 1;
        u.LastDamagedTick[open.Index] = 0; // recorded for the faction now (the regen gate widened)
        Assert.NotEqual(h0, sim.StateHash());
        u.LastDamagedTick[open.Index] = -1;
        Assert.Equal(h0, sim.StateHash());

        Simulation plain = CombatScenes.Flat();
        Assert.Null(plain.World.Units.Forest);
    }

    /// <summary>The unit store's forest part, <c>FRST</c>: right after the regeneration part, before the ledger's <c>LEDR</c>, exactly when the data has the block.</summary>
    private static int ForestPart(byte[] save, int capacity)
    {
        int bytes = ForestBytes(capacity);
        for (int at = save.AsSpan().IndexOf("FRST"u8); at >= 0;)
        {
            if (at + bytes + 4 <= save.Length && save.AsSpan(at + bytes, 4).SequenceEqual("LEDR"u8)) return at;
            int next = save.AsSpan(at + 1).IndexOf("FRST"u8);
            at = next < 0 ? -1 : at + 1 + next;
        }
        return -1;
    }

    private static int ForestBytes(int capacity) => 4 + (4 + 4 * capacity) + (4 + capacity);

    /// <summary>The ledger's tag right after the unit store's regeneration part (where a forest part goes).</summary>
    private static int LedgerAfterRegen(byte[] save, int capacity)
    {
        int regenBytes = 4 + 2 * (4 + 4 * capacity) + (4 + capacity) + 4;
        for (int at = save.AsSpan().IndexOf("LEDR"u8); at >= 0;)
        {
            if (at - regenBytes >= 0 && save.AsSpan(at - regenBytes, 4).SequenceEqual("REGN"u8)) return at;
            int next = save.AsSpan(at + 1).IndexOf("LEDR"u8);
            at = next < 0 ? -1 : at + 1 + next;
        }
        return -1;
    }

    private static byte[] Splice(byte[] save, int at, int remove, byte[] insert)
    {
        byte[] result = new byte[save.Length - remove + insert.Length];
        save.AsSpan(0, at).CopyTo(result);
        insert.CopyTo(result.AsSpan(at));
        save.AsSpan(at + remove).CopyTo(result.AsSpan(at + insert.Length));
        BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(SaveFormat.LengthOffset), result.Length);
        return SaveBytes.Rechecksum(result);
    }

    [Fact]
    public void AForgedSave_ThePartWithoutTheBlock_OrTheBlockWithoutThePart_IsRefused()
    {
        // The data has the block, the save's part is cut out.
        Simulation forest = ForestScenes.Scene(D, combat: false);
        ForestScenes.Tree(forest, 20, 20);
        CombatScenes.Place(forest, 0, D.FindUnit("malazan_laborer"), ForestScenes.At(forest, 21, 20));
        forest.Tick();
        int cap = forest.World.Units.Capacity;
        byte[] withPart = SaveTestRun.Save(forest);
        int part = ForestPart(withPart, cap);
        Assert.True(part > 0, "no FRST part before LEDR");
        Assert.Equal(SaveError.None, SaveTestRun.TryLoad(D, withPart));
        Assert.Equal(SaveError.Malformed, SaveTestRun.TryLoad(D, Splice(withPart, part, ForestBytes(cap), Array.Empty<byte>())));

        // The data has no block, the save gains a well-formed part.
        Simulation plain = CombatScenes.Flat();
        CombatScenes.Place(plain, 0, CombatScenes.Laborer, CombatScenes.At(plain, 10, 10));
        plain.Tick();
        int pcap = plain.World.Units.Capacity;
        byte[] without = SaveTestRun.Save(plain);
        Assert.Equal(-1, ForestPart(without, pcap));
        byte[] forged = new byte[ForestBytes(pcap)];
        "FRST"u8.CopyTo(forged);
        BinaryPrimitives.WriteInt32LittleEndian(forged.AsSpan(4), pcap);
        for (int i = 0; i < pcap; i++) BinaryPrimitives.WriteInt32LittleEndian(forged.AsSpan(8 + 4 * i), -1);
        BinaryPrimitives.WriteInt32LittleEndian(forged.AsSpan(8 + 4 * pcap), pcap);
        int ledger = LedgerAfterRegen(without, pcap);
        Assert.True(ledger > 0);
        Assert.Equal(SaveError.Malformed, SaveTestRun.TryLoad(TestSim.Data, Splice(without, ledger, 0, forged)));
    }

    /// <summary>The part's values are held to the rest: a held unit without its entry, an excepted or another seat's unit held, a last attack in the future, a free slot's state, a held byte above 1.</summary>
    [Fact]
    public void AForgedForestPart_IsRefused_NeverTrusted()
    {
        Simulation sim = ForestScenes.Scene(D, combat: false);
        ForestScenes.Tree(sim, 20, 20);
        EntityHandle h = CombatScenes.Place(sim, 0, D.FindUnit("malazan_laborer"), ForestScenes.At(sim, 21, 20));
        EntityHandle cat = CombatScenes.Place(sim, 0, D.FindUnit("malazan_catapult"), ForestScenes.At(sim, 19, 20));
        EntityHandle raider = CombatScenes.Place(sim, 1, D.FindUnit("whirlwind_raider"), ForestScenes.At(sim, 20, 19));
        sim.Tick();
        int cap = sim.World.Units.Capacity;
        byte[] good = SaveTestRun.Save(sim);
        int part = ForestPart(good, cap);
        int attackAt(int slot) => part + 8 + 4 * slot;
        int heldAt(int slot) => part + 8 + 4 * cap + 4 + slot;
        Assert.Equal(1, good[heldAt(h.Index)]);
        Assert.Equal(SaveError.None, SaveTestRun.TryLoad(D, good));

        byte[] Forge(Action<byte[]> edit)
        {
            byte[] copy = (byte[])good.Clone();
            edit(copy);
            return SaveBytes.Rechecksum(copy);
        }
        int free = cap - 1;
        Assert.False(sim.World.Units.Alive[free]);
        var cases = new (string Name, byte[] Bytes)[]
        {
            ("the catapult (an excepted slot) held", Forge(b => b[heldAt(cat.Index)] = 1)),
            ("the raider (no forest seat) held", Forge(b => b[heldAt(raider.Index)] = 1)),
            ("the raider with a last attack", Forge(b => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(attackAt(raider.Index)), 0))),
            ("a last attack after the save's tick", Forge(b => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(attackAt(h.Index)), sim.TickNumber + 1))),
            ("a held byte of 2", Forge(b => b[heldAt(h.Index)] = 2)),
            ("a free slot held", Forge(b => b[heldAt(free)] = 1)),
            ("a free slot with a last attack", Forge(b => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(attackAt(free)), 0))),
        };
        foreach ((string name, byte[] bytes) in cases)
            Assert.True(SaveTestRun.TryLoad(D, bytes) == SaveError.Malformed, name);
    }
}
