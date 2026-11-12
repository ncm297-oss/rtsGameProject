using System.Buffers.Binary;
using Rts.Sim.Abilities;
using Rts.Sim.Ai;
using Rts.Sim.Combat;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Save;
using Rts.Sim.Tests.QA;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA M7-H1 (session 2026-11-11-1317), BUG-1165 attacked: 10,000 seeded mutations of a save's <c>REGN</c> part (one to three
/// fields each: a live or free slot's last damage tick, its accumulator, its regenerating count, the total; values from the
/// edges and at random), checksum recomputed. No load may throw; a load that succeeds must hold an internally consistent part
/// (a live unit's last damage tick in -1..tick and accumulator in 0..399, a free slot's -1 / 0 / no count, each slot's count
/// equal to its regenerating entries, the total equal to the slots counting one), re-save byte for byte and tick 30 ticks.
/// And the honest side: 50 mid-match saves of an AI match where both factions regenerate (a seeded stream applying
/// regenerating statuses and hits between ticks) each load, re-save byte-identical and play 40 ticks hash-identical.
/// </summary>
[Collection(SerialCollection.Name)]
public class M7H1RegnFuzzQaStressTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Stress")]
    public void TenThousandMutatedRegnParts_NeverCrash_AndNeverLoadInconsistent()
    {
        (Simulation sim, byte[] bytes, _, _) = M7H1QaTests.Scene();
        GameData d = sim.World.Data;
        int cap = sim.World.Units.Capacity, tick = sim.TickNumber;
        M7H1QaTests.Regn o = M7H1QaTests.Regn.Find(bytes, cap);
        var rng = new Random(1165);
        int[] ints = { -1, 0, 1, tick - 1, tick, tick + 1, 399, 400, -2, int.MaxValue, int.MinValue, cap, cap + 1 };
        int loaded = 0, refused = 0, unchanged = 0;
        var refusals = new Dictionary<SaveError, int>();
        for (int n = 0; n < 10_000; n++)
        {
            byte[] b = (byte[])bytes.Clone();
            int edits = 1 + rng.Next(3);
            for (int e = 0; e < edits; e++)
            {
                int slot = rng.Next(4) == 0 ? rng.Next(cap) : rng.Next(8); // the scene's units sit in the low slots
                int v = rng.Next(3) == 0 ? rng.Next(-1000, 1000) : ints[rng.Next(ints.Length)];
                switch (rng.Next(4))
                {
                    case 0: BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(o.Ldt + 4 * slot), v); break;
                    case 1: BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(o.Acc + 4 * slot), v); break;
                    case 2: b[o.Counts + slot] = (byte)(rng.Next(3) == 0 ? rng.Next(256) : rng.Next(3)); break;
                    default: BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(o.Total), rng.Next(2) == 0 ? v : rng.Next(0, 6)); break;
                }
            }
            if (b.AsSpan().SequenceEqual(bytes)) { unchanged++; continue; }
            byte[] forged = SaveBytes.Rechecksum(b);
            Simulation? s;
            SaveError error;
            try
            {
                using var stream = new MemoryStream(forged, writable: false);
                Simulation.TryLoad(d, stream, out s, out error);
            }
            catch (Exception ex)
            {
                Assert.Fail($"mutation {n}: the load threw {ex.GetType().Name}: {ex.Message}");
                return;
            }
            if (s == null)
            {
                refused++;
                refusals[error] = refusals.GetValueOrDefault(error) + 1;
                continue;
            }
            loaded++;
            CheckConsistent(s, $"mutation {n}");
            Assert.True(SaveTestRun.Save(s).AsSpan().SequenceEqual(forged), $"mutation {n}: an accepted part does not re-save as read");
            for (int k = 0; k < 30; k++) s.Tick();
            CheckConsistent(s, $"mutation {n}, 30 ticks on");
        }
        output.WriteLine($"loaded {loaded}, refused {refused} ({string.Join(", ", refusals.Select(kv => $"{kv.Key} {kv.Value}"))}), unchanged {unchanged}");
        Assert.True(refused > 9_000, $"only {refused} refusals");
    }

    /// <summary>The REGN invariants a load must hold (and a later tick must keep).</summary>
    private static void CheckConsistent(Simulation s, string at)
    {
        World w = s.World;
        UnitStore u = w.Units;
        StatusStore st = u.Statuses;
        int with = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            int entries = 0;
            for (int k = 0; k < st.Count[i]; k++)
                if (w.Data.Statuses[st.StatusId[i * StatusStore.PerUnit + k]].Kind == StatusKind.Regenerating) entries++;
            Assert.True(st.HasRegen(i) == (entries > 0), $"{at}: slot {i}'s regen flag {st.HasRegen(i)} with {entries} entries");
            if (entries > 0) with++;
            if (!u.Alive[i])
            {
                Assert.True(u.LastDamagedTick[i] == -1 && u.RegenAccum[i] == 0 && entries == 0, $"{at}: free slot {i} carries ({u.LastDamagedTick[i]}, {u.RegenAccum[i]}, {entries})");
                continue;
            }
            Assert.True(u.LastDamagedTick[i] >= -1 && u.LastDamagedTick[i] <= s.TickNumber, $"{at}: slot {i}'s last damage {u.LastDamagedTick[i]} at tick {s.TickNumber}");
            Assert.True(u.RegenAccum[i] >= 0 && u.RegenAccum[i] < CombatConstants.RegenFixedOne, $"{at}: slot {i}'s accumulator {u.RegenAccum[i]}");
            Assert.True(u.Hp[i] >= 1 && u.Hp[i] <= w.MaxHp(i), $"{at}: slot {i}'s hp {u.Hp[i]}");
        }
        Assert.True(with == st.RegeneratingUnits, $"{at}: total {st.RegeneratingUnits}, slots {with}");
    }

    [Fact]
    [Trait("Category", "Stress")]
    public void FiftyHonestMidMatchSaves_WithRegeneratingUnits_LoadReSaveAndPlayOnIdentically()
    {
        GameData d = RegenQaTests.Variant(2); // Malazan 100 HP/s after 0.5 s, Whirlwind 0.3 HP/s after 2 s
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai);
        Simulation sim = AiScenes.Generated(seats, 7, false, out _, data: d, raiders: 6);
        World w = sim.World;
        UnitStore u = w.Units;
        var rng = new Random(1165_50);
        int regen = d.FindStatus("regenerating");
        int saves = 0, withRegenStatus = 0, midDelay = 0;
        while (saves < 50)
        {
            int pick = rng.Next(u.Capacity);
            if (sim.TickNumber > 10 && u.Alive[pick])
            {
                var h = new EntityHandle(pick, u.Generation[pick]);
                if (rng.Next(2) == 0) CombatSystem.HitUnit(w, new PendingHit(default, 1 - u.Owner[pick], h, false, 1 + rng.Next(20)));
                else StatusSystem.Apply(w, pick, regen, 0.05f * (1 + rng.Next(40)), 1 + rng.Next(300), u.Owner[pick]);
            }
            sim.Tick();
            if (sim.TickNumber % 37 != 0) continue;
            byte[] bytes = SaveTestRun.Save(sim);
            Simulation loaded = SaveTestRun.Load(d, bytes);
            Assert.True(bytes.AsSpan().SequenceEqual(SaveTestRun.Save(loaded)), $"save at {sim.TickNumber}: save -> load -> save differs");
            if (u.Statuses.RegeneratingUnits > 0) withRegenStatus++;
            for (int i = 0; i < u.Capacity; i++)
                if (u.Alive[i] && u.LastDamagedTick[i] >= 0 && sim.TickNumber - u.LastDamagedTick[i] < w.RegenDelayOf[u.Owner[i]]) { midDelay++; break; }
            // The load plays on beside the reference for 40 ticks (no stream events meanwhile), hash for hash.
            int at = sim.TickNumber;
            for (int k = 0; k < 40; k++)
            {
                sim.Tick();
                loaded.Tick();
                Assert.True(sim.StateHash() == loaded.StateHash(), $"save at {at}: the load diverges at +{k + 1}");
            }
            saves++;
        }
        output.WriteLine($"50 saves to tick {sim.TickNumber}: {withRegenStatus} with a regenerating status, {midDelay} with a unit mid-delay");
        Assert.True(withRegenStatus >= 25 && midDelay >= 10, $"weak run: {withRegenStatus} / {midDelay}");
    }
}
