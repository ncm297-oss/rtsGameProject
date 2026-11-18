using System.Buffers.Binary;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;
using static Rts.Sim.Tests.StillScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M8-2 criterion 10, and the invariant fuzz: determinism, saves and the state hash with the stand-still stealth. A
/// three-player brawl on a flat 64-cell map on the <see cref="StillScenes.Still"/> copy: each side (Malazan, Whirlwind,
/// Teblor) 24 units, a third of a type with the field, spread round its corner; random move, attack-move, attack, stop and
/// hold orders from a seeded stream, so field units walk, stop, hide, are found, strike, are hit, are shoved, wait and hide
/// again.
/// </summary>
public class StillStealthMatchTests
{
    private readonly ITestOutputHelper _out;

    public StillStealthMatchTests(ITestOutputHelper output) => _out = output;

    private static GameData D => Still;

    private static readonly string[][] Sides =
    {
        new[] { "malazan_crossbowman", "malazan_heavy_infantry", "malazan_laborer", "malazan_wickan_lancer", "malazan_cadre_mage", "malazan_sapper" },
        new[] { "whirlwind_desert_archer", "whirlwind_raider", "whirlwind_priest", "whirlwind_horse_raider", "whirlwind_camp_follower", "whirlwind_zealot" },
        new[] { "teblor_javelin_thrower", "teblor_warrior", "teblor_shaman", "teblor_destrier_rider", "teblor_gatherer", "teblor_war_dog" },
    };

    private static readonly Vector2[] Corners = { new(30f, 30f), new(98f, 30f), new(64f, 98f) };

    /// <summary>The scene: 24 units a side round its corner (cell size 2 m: a 128 m map), all spawned before the first tick.</summary>
    internal static Simulation NewSim(ulong seed)
    {
        var sim = new Simulation(new SimConfig(seed, 3, 160, 2048) { Data = D }, LocalMovementTests.Flat(64));
        var rng = new SimRng(seed, 901);
        for (int p = 0; p < 3; p++)
            for (int k = 0; k < 24; k++)
            {
                Vector2 at = Corners[p] + new Vector2(rng.NextInt(-12, 13), rng.NextInt(-12, 13));
                // A third of each side is its field type (the first of its list).
                string type = k % 3 == 0 ? Sides[p][0] : Sides[p][1 + rng.NextInt(0, Sides[p].Length - 1)];
                CombatScenes.Place(sim, p, D.FindUnit(type), at);
            }
        return sim;
    }

    /// <summary>
    /// Every 25 ticks (from tick 5) each live unit, with chance 1/4, gets a random order: a move to a random point near its
    /// corner or the middle, an attack-move to a random enemy's position, an attack on a random enemy, a stop, or a hold.
    /// Drawn from <paramref name="rng"/> over <paramref name="sim"/>'s state; returned so a twin gets the same commands.
    /// </summary>
    internal static void Orders(Simulation sim, ref SimRng rng, List<Command> into)
    {
        into.Clear();
        if (sim.TickNumber % 25 != 5) return;
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || rng.NextInt(0, 4) != 0) continue;
            var h = new EntityHandle(i, u.Generation[i]);
            int p = u.Owner[i];
            int enemy = -1;
            for (int tries = 0; tries < 8 && enemy < 0; tries++)
            {
                int j = rng.NextInt(0, u.Capacity);
                if (u.Alive[j] && u.Owner[j] != p) enemy = j;
            }
            switch (rng.NextInt(0, 6))
            {
                case 0:
                case 1:
                    Vector2 c = rng.NextInt(0, 3) == 0 ? new Vector2(64f, 64f) : Corners[p];
                    into.Add(Command.Move(p, h, c + new Vector2(rng.NextInt(-14, 15), rng.NextInt(-14, 15))));
                    break;
                case 2:
                    if (enemy >= 0) into.Add(Command.AttackMove(p, h, u.Position[enemy]));
                    break;
                case 3:
                    if (enemy >= 0) into.Add(Command.Attack(p, h, new EntityHandle(enemy, u.Generation[enemy]), isBuilding: false));
                    break;
                case 4:
                    into.Add(Command.Stop(p, h));
                    break;
                default:
                    into.Add(Command.HoldPosition(p, h));
                    break;
            }
        }
    }

    /// <summary>What a run saw: held unit-ticks, lets-go by hp loss, lets-go by a move, reveals of a field unit, hp losses of a field unit.</summary>
    private sealed record Counts(int HeldTicks, int HpLetGo, int MoveLetGo, int Reveals, int Hurt)
    {
        internal Counts Add(Simulation sim, byte[] heldBefore)
        {
            UnitStore u = sim.World.Units;
            int held = 0, hp = 0, moved = 0, reveals = 0, hurt = 0, revealed = D.FindStatus("revealed");
            int now = sim.TickNumber - 1;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                if (u.Still!.Held[i] != 0) held++;
                if (u.Still.LastHurtTick[i] == now) hurt++;
                if (heldBefore[i] != 0 && u.Still!.Held[i] == 0)
                {
                    if (u.Still.LastHurtTick[i] == now) hp++;
                    else if (u.Still.MovedTick[i] == now - 1) moved++;
                }
                int r = u.Statuses.IndexOf(i, revealed);
                if (D.Units[u.TypeId[i]].HasStillStealth && r >= 0 && u.Statuses.TicksRemaining[r] == D.Rules.RevealTicks) reveals++;
            }
            return new Counts(HeldTicks + held, HpLetGo + hp, MoveLetGo + moved, Reveals + reveals, Hurt + hurt);
        }
    }

    /// <summary>
    /// Criterion 10 (first row) and the invariant fuzz: two runs per seed are hash-equal every tick over 2,000 ticks; after
    /// every tick of the first, a unit is held only when all three counts have passed and no entry outlives its holders
    /// (<see cref="StillScenes.AssertInvariant"/>). Every seed holds, lets go by a move, hurts field units and reveals one.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    public void Twins_AreHashEqualEveryTick_Over2000Ticks_AndTheInvariantHolds(ulong seed)
    {
        Simulation a = NewSim(seed), b = NewSim(seed);
        var rng = new SimRng(seed, 902);
        var commands = new List<Command>();
        UnitStore u = a.World.Units;
        var heldBefore = new byte[u.Capacity];
        var counts = new Counts(0, 0, 0, 0, 0);
        for (int t = 0; t < 2000; t++)
        {
            Orders(a, ref rng, commands);
            foreach (Command c in commands)
            {
                a.Enqueue(c);
                b.Enqueue(c);
            }
            Array.Copy(u.Still!.Held, heldBefore, u.Capacity);
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {t}");
            AssertInvariant(a, timed: null);
            counts = counts.Add(a, heldBefore);
        }
        _out.WriteLine($"seed {seed}: {counts.HeldTicks} held unit-ticks, {counts.MoveLetGo} lets-go by a move, {counts.HpLetGo} by hp loss, {counts.Reveals} reveals, {counts.Hurt} field-unit hp losses; kills {string.Join("/", a.World.Kills.ToArray())}");
        Assert.True(counts.HeldTicks > 1000, $"only {counts.HeldTicks} held unit-ticks");
        Assert.True(counts.MoveLetGo > 0, "no field unit was let go by a move");
        // A held unit hit is rarer (it must be found first): each seed has field units hurt, and the four seeds together
        // let go by an hp loss (the scene rows pin the moment: StillStealthTests.C5_*).
        Assert.True(counts.Hurt > 0, "no field unit lost hit points");
        Assert.True(counts.Reveals > 0, "no field unit was revealed");
    }

    private const string ChainChildVariable = "RTS_M82_SAVE_CHAIN_CHILD";
    private const string ChainLine = "M82-SAVE-CHAIN ";

    /// <summary>
    /// The chain: seed 2's run uninterrupted beside a copy saved and loaded at every 50th tick (the loaded sim goes on in its
    /// place), the hashes compared every tick over 1,500 ticks and the save -> load -> save bytes compared at each save; the
    /// digest folds every tick's hash and every save's length.
    /// </summary>
    private ulong Chain(out int savesWhileHeld)
    {
        Simulation reference = NewSim(2), chained = NewSim(2);
        var rng = new SimRng(2, 902);
        var commands = new List<Command>();
        var h = new StateHasher();
        savesWhileHeld = 0;
        for (int t = 0; t < 1500; t++)
        {
            if (t > 0 && t % 50 == 0)
            {
                byte[] bytes = SaveTestRun.Save(chained);
                Assert.True(bytes.AsSpan().IndexOf("STIL"u8) > 0, "no STIL part");
                Simulation loaded = SaveTestRun.Load(D, bytes);
                Assert.True(bytes.AsSpan().SequenceEqual(SaveTestRun.Save(loaded)), $"save -> load -> save differs at {t}");
                Assert.Equal(chained.StateHash(), loaded.StateHash());
                if (Array.IndexOf(chained.World.Units.Still!.Held, (byte)1) >= 0) savesWhileHeld++;
                chained = loaded;
                h.Add(bytes.Length);
            }
            Orders(reference, ref rng, commands);
            foreach (Command c in commands)
            {
                reference.Enqueue(c);
                chained.Enqueue(c);
            }
            reference.Tick();
            chained.Tick();
            ulong hash = reference.StateHash();
            Assert.True(chained.StateHash() == hash, $"the chained run differs after tick {t}");
            h.Add(hash);
        }
        return h.Value;
    }

    /// <summary>
    /// Criterion 10 (second row): a save at every 50th tick, loaded and continued, equals the uninterrupted run, and this
    /// process and a fresh child <c>dotnet test</c> of this row fold the same digest.
    /// </summary>
    [Fact]
    public void ASaveAtEvery50thTick_LoadedAndContinued_EqualsTheUninterruptedRun_InTwoProcesses()
    {
        ulong digest = Chain(out int held);
        if (Environment.GetEnvironmentVariable(ChainChildVariable) == "1")
        {
            _out.WriteLine($"{ChainLine}{digest:X16}");
            return;
        }
        Assert.True(held >= 10, $"only {held} of 29 saves had a unit held");
        string child = MapFileTests.RunChild(typeof(StillStealthMatchTests), nameof(ASaveAtEvery50thTick_LoadedAndContinued_EqualsTheUninterruptedRun_InTwoProcesses), ChainChildVariable, ChainLine);
        _out.WriteLine($"this process {digest:X16} ({held} saves with a unit held); the child {child}");
        Assert.Equal($"{digest:X16}", child);
    }

    /// <summary>With the field in the data, every part of a unit's state is state: changing any one changes the hash; without the field there is none.</summary>
    [Fact]
    public void ChangingAnyStillField_ChangesTheStateHash_WhenTheDataHasTheField()
    {
        Simulation sim = Scene(D, combat: false);
        EntityHandle hidden = CombatScenes.Place(sim, 0, D.FindUnit("malazan_laborer"), At(sim, 20, 20));
        EntityHandle other = CombatScenes.Place(sim, 0, D.FindUnit("malazan_wickan_lancer"), At(sim, 30, 20)); // no field: default
        Ticks(sim, 61);
        var st = sim.World.Units.Still!;
        Assert.True(Held(sim, hidden));
        Assert.False(st.HasState(other.Index));
        ulong h0 = sim.StateHash();
        void Moves(Action change, Action back)
        {
            change();
            Assert.NotEqual(h0, sim.StateHash());
            back();
            Assert.Equal(h0, sim.StateHash());
        }
        Moves(() => st.MovedTick[hidden.Index]++, () => st.MovedTick[hidden.Index]--);
        Vector2 at = st.StillAt[hidden.Index];
        Moves(() => st.StillAt[hidden.Index] += new Vector2(0.5f, 0f), () => st.StillAt[hidden.Index] = at);
        Moves(() => st.LastAttackTick[hidden.Index] = 3, () => st.LastAttackTick[hidden.Index] = -1);
        Moves(() => st.LastHurtTick[hidden.Index] = 3, () => st.LastHurtTick[hidden.Index] = -1);
        Moves(() => st.Held[hidden.Index] = 0, () => st.Held[hidden.Index] = 1);
        Moves(() => st.LastHurtTick[other.Index] = 3, () => st.LastHurtTick[other.Index] = -1);
        Assert.Null(CombatScenes.Flat().World.Units.Still);
    }

    // ---------- forged saves ----------

    private static int StillBytes(int capacity) => 4 + (4 + 4 * capacity) + (4 + 8 * capacity) + 2 * (4 + 4 * capacity) + (4 + capacity);

    /// <summary>The unit store's stand-still part, <c>STIL</c>: right before the ledger's <c>LEDR</c>, exactly when the data has the field.</summary>
    private static int StillPart(byte[] save, int capacity)
    {
        int bytes = StillBytes(capacity);
        for (int at = save.AsSpan().IndexOf("STIL"u8); at >= 0;)
        {
            if (at + bytes + 4 <= save.Length && save.AsSpan(at + bytes, 4).SequenceEqual("LEDR"u8)) return at;
            int next = save.AsSpan(at + 1).IndexOf("STIL"u8);
            at = next < 0 ? -1 : at + 1 + next;
        }
        return -1;
    }

    /// <summary>The ledger's tag right after the unit store's regeneration part (where a stand-still part goes on data without the forest).</summary>
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
    public void AForgedSave_ThePartWithoutTheField_OrTheFieldWithoutThePart_IsRefused()
    {
        Simulation still = Scene(D, combat: false);
        CombatScenes.Place(still, 0, D.FindUnit("malazan_laborer"), At(still, 21, 20));
        Ticks(still, 61);
        int cap = still.World.Units.Capacity;
        byte[] withPart = SaveTestRun.Save(still);
        int part = StillPart(withPart, cap);
        Assert.True(part > 0, "no STIL part before LEDR");
        Assert.Equal(SaveFormat.CurrentVersion, BinaryPrimitives.ReadInt32LittleEndian(withPart.AsSpan(SaveFormat.MagicLength)));
        Assert.Equal(SaveError.None, SaveTestRun.TryLoad(D, withPart));
        Assert.Equal(SaveError.Malformed, SaveTestRun.TryLoad(D, Splice(withPart, part, StillBytes(cap), Array.Empty<byte>())));

        // The data has no field, the save gains a well-formed (all-default) part.
        Simulation plain = CombatScenes.Flat();
        CombatScenes.Place(plain, 0, CombatScenes.Laborer, CombatScenes.At(plain, 10, 10));
        plain.Tick();
        int pcap = plain.World.Units.Capacity;
        byte[] without = SaveTestRun.Save(plain);
        Assert.Equal(-1, StillPart(without, pcap));
        var empty = new StillStealthStoreBytes(pcap);
        int ledger = LedgerAfterRegen(without, pcap);
        Assert.True(ledger > 0);
        Assert.Equal(SaveError.Malformed, SaveTestRun.TryLoad(TestSim.Data, Splice(without, ledger, 0, empty.Bytes)));
    }

    /// <summary>A well-formed all-default <c>STIL</c> part for <c>capacity</c> slots.</summary>
    private sealed class StillStealthStoreBytes
    {
        internal readonly byte[] Bytes;

        internal StillStealthStoreBytes(int capacity)
        {
            Bytes = new byte[StillBytes(capacity)];
            "STIL"u8.CopyTo(Bytes);
            int at = 4;
            void Ints(int value)
            {
                BinaryPrimitives.WriteInt32LittleEndian(Bytes.AsSpan(at), capacity);
                at += 4;
                for (int i = 0; i < capacity; i++, at += 4) BinaryPrimitives.WriteInt32LittleEndian(Bytes.AsSpan(at), value);
            }
            Ints(-1); // MovedTick
            BinaryPrimitives.WriteInt32LittleEndian(Bytes.AsSpan(at), capacity);
            at += 4 + 8 * capacity; // StillAt, zeros
            Ints(-1); // LastAttackTick
            Ints(-1); // LastHurtTick
            BinaryPrimitives.WriteInt32LittleEndian(Bytes.AsSpan(at), capacity); // Held, zeros
        }
    }

    /// <summary>
    /// The part's values are held to the rest: state on a type without the field, on a summoned unit or a free slot; a held
    /// unit without its entry; a tick after the save's; a held byte of 2.
    /// </summary>
    [Fact]
    public void AForgedStillPart_IsRefused_NeverTrusted()
    {
        Simulation sim = Scene(D, combat: false);
        UnitStore u = sim.World.Units;
        EntityHandle h = CombatScenes.Place(sim, 0, D.FindUnit("malazan_laborer"), At(sim, 21, 20));
        EntityHandle lancer = CombatScenes.Place(sim, 0, D.FindUnit("malazan_wickan_lancer"), At(sim, 25, 20));
        Assert.True(u.TrySpawn(0, D.FindUnit("malazan_crossbowman"), D.Units[D.FindUnit("malazan_crossbowman")], At(sim, 30, 20), 100_000, out EntityHandle summoned));
        Ticks(sim, 61);
        Assert.True(Held(sim, h));
        int cap = u.Capacity;
        byte[] good = SaveTestRun.Save(sim);
        int part = StillPart(good, cap);
        int movedAt(int slot) => part + 8 + 4 * slot;
        int attackAt(int slot) => part + 8 + 4 * cap + 4 + 8 * cap + 4 + 4 * slot;
        int hurtAt(int slot) => part + 8 + 4 * cap + 4 + 8 * cap + 4 + 4 * cap + 4 + 4 * slot;
        int heldAt(int slot) => part + 8 + 4 * cap + 4 + 8 * cap + 2 * (4 + 4 * cap) + 4 + slot;
        Assert.Equal(1, good[heldAt(h.Index)]);
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(good.AsSpan(movedAt(h.Index))));
        Assert.Equal(SaveError.None, SaveTestRun.TryLoad(D, good));

        byte[] Forge(Action<byte[]> edit)
        {
            byte[] copy = (byte[])good.Clone();
            edit(copy);
            return SaveBytes.Rechecksum(copy);
        }
        void Int(byte[] b, int at, int v) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(at), v);
        int free = cap - 1;
        Assert.False(u.Alive[free]);
        var cases = new (string Name, byte[] Bytes)[]
        {
            ("the lancer (no field) held", Forge(b => b[heldAt(lancer.Index)] = 1)),
            ("the lancer with a moved tick", Forge(b => Int(b, movedAt(lancer.Index), 0))),
            ("the summoned unit with a last attack", Forge(b => Int(b, attackAt(summoned.Index), 0))),
            ("a moved tick after the save's tick", Forge(b => Int(b, movedAt(h.Index), sim.TickNumber + 1))),
            ("a last hp loss after the save's tick", Forge(b => Int(b, hurtAt(h.Index), sim.TickNumber + 1))),
            ("a moved tick below -1", Forge(b => Int(b, movedAt(h.Index), -2))),
            ("a held byte of 2", Forge(b => b[heldAt(h.Index)] = 2)),
            ("a free slot held", Forge(b => b[heldAt(free)] = 1)),
            ("a free slot with a moved tick", Forge(b => Int(b, movedAt(free), 0))),
        };
        foreach ((string name, byte[] bytes) in cases)
            Assert.True(SaveTestRun.TryLoad(D, bytes) == SaveError.Malformed, name);
    }

    /// <summary>A held unit whose entry is gone (cut from the status store) is refused: the hold needs its held tick.</summary>
    [Fact]
    public void AForgedSave_AHeldUnitWithoutItsEntry_IsRefused()
    {
        Simulation sim = Scene(D, combat: false);
        UnitStore u = sim.World.Units;
        EntityHandle h = CombatScenes.Place(sim, 0, D.FindUnit("malazan_laborer"), At(sim, 21, 20));
        Ticks(sim, 61);
        Assert.True(Held(sim, h));
        // Take the entry away behind the rule's back (as a forged file would), then save: the load must refuse it.
        int at = u.Statuses.IndexOf(h.Index, Stealthed(D));
        Abilities.StatusSystem.RemoveEntry(sim.World, h.Index, at);
        Assert.True(Held(sim, h));
        using var stream = new MemoryStream();
        Assert.True(sim.TrySave(stream, out _));
        Assert.Equal(SaveError.Malformed, SaveTestRun.TryLoad(D, stream.ToArray()));
    }
}
