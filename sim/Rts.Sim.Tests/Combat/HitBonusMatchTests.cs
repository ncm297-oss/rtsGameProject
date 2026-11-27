using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M8-3 criterion 10 and the invariant fuzz, on the <see cref="HitBonusScenes"/> copy: a three-player brawl on a flat
/// 64-cell map, each side (Malazan, Whirlwind, Teblor) 24 units round its corner, half of them of a type with a field (the
/// first-shot bonus, the pack passive or both), the rest of types with neither; random move, attack-move, attack, stop and
/// hold orders (M8-2's stream, <see cref="StillStealthMatchTests.Orders"/>), and the Heavy Infantry's 60 s test stealth cast
/// now and then, so units hide (stand-still, cast), strike from hiding, are revealed, gather into packs and die.
/// </summary>
public class HitBonusMatchTests
{
    private readonly ITestOutputHelper _out;

    public HitBonusMatchTests(ITestOutputHelper output) => _out = output;

    private static GameData D => HitBonusScenes.Data;

    // Per side: the field types first (Malazan: first shot, first shot + pack, first shot with splash; Whirlwind: first shot
    // + stand-still, pack; Teblor: first shot + stand-still, pack), then types with neither field.
    private static readonly string[][] Fields =
    {
        new[] { "malazan_crossbowman", "malazan_heavy_infantry", "malazan_cadre_mage" },
        new[] { "whirlwind_desert_archer", "whirlwind_raider" },
        new[] { "teblor_javelin_thrower", "teblor_war_dog" },
    };

    private static readonly string[][] Others =
    {
        new[] { "malazan_laborer", "malazan_wickan_lancer", "malazan_sapper" },
        new[] { "whirlwind_priest", "whirlwind_horse_raider", "whirlwind_zealot" },
        new[] { "teblor_warrior", "teblor_shaman", "teblor_gatherer" },
    };

    private static readonly Vector2[] Corners = { new(30f, 30f), new(98f, 30f), new(64f, 98f) };

    internal static Simulation NewSim(ulong seed)
    {
        var sim = new Simulation(new SimConfig(seed, 3, 160, 2048) { Data = D }, LocalMovementTests.Flat(64));
        var rng = new SimRng(seed, 931);
        for (int p = 0; p < 3; p++)
            for (int k = 0; k < 24; k++)
            {
                // Packs stand close: field units within a 16 m square of the corner, the rest within 24 m.
                int spread = k % 2 == 0 ? 8 : 12;
                Vector2 at = Corners[p] + new Vector2(rng.NextInt(-spread, spread + 1), rng.NextInt(-spread, spread + 1));
                string type = k % 2 == 0 ? Fields[p][rng.NextInt(0, Fields[p].Length)] : Others[p][rng.NextInt(0, Others[p].Length)];
                CombatScenes.Place(sim, p, D.FindUnit(type), at);
            }
        return sim;
    }

    /// <summary>M8-2's order stream, plus: every 25 ticks (from tick 5) each live Heavy Infantry casts its test stealth with chance 1/6.</summary>
    internal static void Orders(Simulation sim, ref SimRng rng, List<Command> into)
    {
        StillStealthMatchTests.Orders(sim, ref rng, into);
        if (sim.TickNumber % 25 != 5) return;
        UnitStore u = sim.World.Units;
        int infantry = D.FindUnit("malazan_heavy_infantry");
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.TypeId[i] == infantry && rng.NextInt(0, 6) == 0)
                into.Add(Command.UseAbility(u.Owner[i], new EntityHandle(i, u.Generation[i]), 0, u.Position[i]));
    }

    /// <summary>What a run saw: hits from hiding (factor with the first-shot term), pack hits (a count of 1 or more), melee hits checked exactly.</summary>
    private sealed class Seen
    {
        internal int StealthedFieldUnitTicks, PackedUnitTicks, CheckedHits, PackHits, HiddenBlows, Deaths;
    }

    /// <summary>
    /// The invariant, after every tick: (1) for every live unit, <see cref="HitFactor.Of"/> (the grid, phase 1's positions)
    /// equals <see cref="HitBonusScenes.Oracle"/> (every slot walked, today's positions); for a type with neither field it is
    /// the status factor alone. (2) Every melee blow struck this tick (the hit queue phase 10 filled, read back from
    /// <see cref="World.Hits"/>) deals the base damage times the factors that held at the strike: by a type with neither
    /// field, the base damage at its status factor (no status in this run carries a damage bonus, so 1); by a pack type
    /// without the first-shot field, the base x the pack factor counted over the slots live at the strike (those alive now
    /// and those that died in this tick's phase 11, positions unchanged since phase 9); by the Heavy Infantry (both
    /// fields), the base x the pack factor, or that x 1.5 when it was hidden at the strike.
    /// </summary>
    private static void AssertInvariant(Simulation sim, Seen seen)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        int tick = sim.TickNumber - 1;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            UnitDef def = D.Units[u.TypeId[i]];
            float of = HitFactor.Of(w, i), oracle = HitBonusScenes.Oracle(w, i);
            Assert.True(of == oracle, $"tick {tick}: slot {i} ({def.Key}) factor {of} against the oracle's {oracle}");
            if (def.NearbyPassive < 0 && def.Attack.FromStealthBonus == 0f)
                Assert.True(of == Abilities.StatusSystem.DamageFactor(w, i), $"tick {tick}: slot {i} ({def.Key}) has no field but a factor {of}");
            if (def.Attack.FromStealthBonus > 0f && w.IsStealthed(i)) seen.StealthedFieldUnitTicks++;
            if (def.NearbyPassive >= 0 && HitFactor.PackCount(w, i, def.Passives[def.NearbyPassive]) > 0) seen.PackedUnitTicks++;
        }
        seen.Deaths += w.Deaths.Length;
        for (int k = 0; k < w.Hits.Length; k++)
        {
            PendingHit hit = w.Hits[k];
            if (hit.Attacker.Generation == 0) break; // the queue is cleared before each tick: the rest is unused
            int a = hit.Attacker.Index;
            UnitDef def = D.Units[u.TypeId[a]]; // a free keeps the slot's type and position (CombatSystem.Resolve)
            AttackDef attack = def.Attack;
            int Base(float f) => hit.IsBuilding ? CombatSystem.DamageToBuilding(w, attack, 0, hit.Victim.Index, f) : CombatSystem.DamageToUnit(w, attack, 0, hit.Victim.Index, f);
            float pack = def.NearbyPassive < 0 ? 1f : PackAtStrike(w, a, hit.AttackerOwner, def.Passives[def.NearbyPassive]);
            if (pack > 1f) seen.PackHits++;
            if (attack.FromStealthBonus == 0f)
                Assert.True(hit.Damage == Base(pack), $"tick {tick}: {def.Key} slot {a} struck {hit.Damage}, expected {Base(pack)} (pack {pack})");
            else
            {
                Assert.True(hit.Damage == Base(pack) || hit.Damage == Base(pack * (1f + attack.FromStealthBonus)),
                    $"tick {tick}: {def.Key} slot {a} struck {hit.Damage}, expected {Base(pack)} or {Base(pack * (1f + attack.FromStealthBonus))}");
                if (hit.Damage != Base(pack)) seen.HiddenBlows++;
            }
            seen.CheckedHits++;
        }
    }

    /// <summary>The pack factor at the strike (phase 10): the owner's listed units live then (alive now, or killed in this tick's phase 11) within the radius, at most the cap.</summary>
    private static float PackAtStrike(World w, int a, int owner, UnitPassive p)
    {
        UnitStore u = w.Units;
        Vector2 at = u.Position[a];
        float r2 = p.Radius * p.Radius;
        int n = 0;
        for (int j = 0; j < u.Capacity; j++)
            if (j != a && u.Alive[j] && u.Owner[j] == owner && p.Units.Contains(u.TypeId[j]) && Vector2.DistanceSquared(u.Position[j], at) <= r2) n++;
        foreach (DeathEvent d in w.Deaths)
            if (!d.IsBuilding && d.Victim.Index != a && d.VictimOwner == owner && p.Units.Contains(d.VictimType) && Vector2.DistanceSquared(d.Position, at) <= r2) n++;
        n = Math.Min(n, p.MaxStacks);
        return n == 0 ? 1f : 1f + n * p.DamageBonus;
    }

    /// <summary>
    /// Criterion 10 (first row) and the invariant fuzz: two runs per seed are hash-equal every tick over 2,000 ticks, and
    /// the invariant holds after every tick of the first. Every seed has field units hidden, packed units and checked blows.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    public void Twins_AreHashEqualEveryTick_Over2000Ticks_AndTheInvariantHolds(ulong seed)
    {
        Simulation a = NewSim(seed), b = NewSim(seed);
        var rng = new SimRng(seed, 932);
        var commands = new List<Command>();
        var seen = new Seen();
        for (int t = 0; t < 2000; t++)
        {
            Orders(a, ref rng, commands);
            foreach (Command c in commands)
            {
                a.Enqueue(c);
                b.Enqueue(c);
            }
            Array.Clear(a.World.Hits);
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {t}");
            AssertInvariant(a, seen);
        }
        _out.WriteLine($"seed {seed}: {seen.StealthedFieldUnitTicks} hidden field-unit ticks, {seen.PackedUnitTicks} packed unit-ticks, {seen.CheckedHits} blows checked ({seen.PackHits} with a pack, {seen.HiddenBlows} from hiding), {seen.Deaths} deaths; kills {string.Join("/", a.World.Kills.ToArray())}");
        Assert.True(seen.StealthedFieldUnitTicks > 100, $"only {seen.StealthedFieldUnitTicks} hidden field-unit ticks");
        Assert.True(seen.PackedUnitTicks > 100, $"only {seen.PackedUnitTicks} packed unit-ticks");
        Assert.True(seen.PackHits > 0, "no blow with a pack");
        Assert.True(seen.Deaths > 10, $"only {seen.Deaths} deaths");
    }

    /// <summary>
    /// Criterion 10 (second row): seed 2's run uninterrupted beside a copy saved and loaded at every 50th tick (the loaded sim
    /// goes on in its place), hash-equal every tick over 2,000 ticks; each save is format 10 and re-saves to the same bytes.
    /// </summary>
    [Fact]
    public void ASaveAtEvery50thTick_LoadedAndContinued_EqualsTheUninterruptedRun()
    {
        Simulation reference = NewSim(2), chained = NewSim(2);
        var rng = new SimRng(2, 932);
        var commands = new List<Command>();
        int saves = 0;
        for (int t = 0; t < 2000; t++)
        {
            if (t > 0 && t % 50 == 0)
            {
                byte[] bytes = SaveTestRun.Save(chained);
                Assert.Equal(SaveFormat.CurrentVersion, System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(SaveFormat.MagicLength)));
                Simulation loaded = SaveTestRun.Load(D, bytes);
                Assert.True(bytes.AsSpan().SequenceEqual(SaveTestRun.Save(loaded)), $"save -> load -> save differs at {t}");
                Assert.Equal(chained.StateHash(), loaded.StateHash());
                chained = loaded;
                saves++;
            }
            Orders(reference, ref rng, commands);
            foreach (Command c in commands)
            {
                reference.Enqueue(c);
                chained.Enqueue(c);
            }
            reference.Tick();
            chained.Tick();
            Assert.True(chained.StateHash() == reference.StateHash(), $"the chained run differs after tick {t}");
        }
        Assert.Equal(39, saves);
        Assert.Equal(11, SaveFormat.CurrentVersion); // M6-S14: format 11 (was 10)
    }

    /// <summary>
    /// No stored state: working out the factors (a hidden unit in a pack, the grid queried) writes nothing the state hash or a
    /// save reads, so the hash and the bytes are the same before and after; the save format stays 10 and the replay format 9.
    /// </summary>
    [Fact]
    public void TheRules_AddNoStateHashBit_AndNoSavePart()
    {
        Simulation sim = HitBonusScenes.Scene();
        EntityHandle a = HitBonusScenes.Place(sim, 0, "malazan_heavy_infantry", HitBonusScenes.At(sim, 20, 20));
        HitBonusScenes.Place(sim, 0, "malazan_heavy_infantry", HitBonusScenes.At(sim, 22, 20));
        HitBonusScenes.Hide(sim, a);
        sim.Tick();
        ulong h0 = sim.StateHash();
        byte[] s0 = SaveTestRun.Save(sim);
        Assert.True(sim.World.HitBonusInData);
        for (int k = 0; k < 3; k++) Assert.Equal(1.5f * 1.1f, HitFactor.Of(sim.World, a.Index));
        Assert.Equal(h0, sim.StateHash());
        Assert.True(s0.AsSpan().SequenceEqual(SaveTestRun.Save(sim)));
        Assert.Equal(11, SaveFormat.CurrentVersion); // M6-S14: format 11 (was 10)
        Assert.Equal(9, Replays.Replay.CurrentFormatVersion);
    }
}
