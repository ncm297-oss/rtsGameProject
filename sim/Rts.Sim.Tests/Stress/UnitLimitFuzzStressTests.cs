using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;
using static Rts.Sim.Tests.ProductionMaps;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// M8-4 criterion 15 (Request 56 G4): a fuzz of the limit's count. 20 seeds (odd seeds on the pack copy: <c>spawnCount</c> 3,
/// limit 7; even seeds on the single copy: limit 7), two players, 3,000 ticks; each tick a few random Trains of the capped type
/// (and of the Laborer, uncapped) at random own buildings (two Billets a player for room), random CancelTrains of random items, and random kills of the
/// capped type, all in one tick sometimes. After every tick, for each player: the units trained and alive plus the units
/// queued never pass the limit, the kept count equals a walk of the stores, and every refused Train of the type was refused
/// for the limit only when it would have passed it. Midway the sim is saved and the run goes on from the loaded copy (the
/// count rebuilt at load) and must match the unsaved twin's hash.
/// </summary>
[Trait("Category", "Stress")]
public class UnitLimitFuzzStressTests(ITestOutputHelper output)
{
    private const int Ticks = 3_000;

    [Fact]
    public void TwentySeeds_RandomTrainsCancelsAndKills_NeverPassTheLimit()
    {
        var lines = new List<string>();
        for (int seed = 1; seed <= 20; seed++) lines.Add(Run(seed));
        foreach (string l in lines) output.WriteLine(l);
    }

    private static string Run(int seed)
    {
        GameData d = seed % 2 == 1 ? UnitLimitTests.s_pack.Value : UnitLimitTests.s_capped.Value;
        (Simulation sim, int[,] bar) = UnitLimitTests.Base(d, players: 2, units: 256);
        (Simulation twin, int[,] _) = UnitLimitTests.Base(d, players: 2, units: 256);
        int heavy = UnitLimitTests.HeavyOf(d), laborer = d.FindUnit("malazan_laborer");
        // Two Billets a player (16 more pop), so trained units come out and can be killed.
        foreach (Simulation s in new[] { sim, twin })
            for (int p = 0; p < 2; p++)
                for (int k = 0; k < 2; k++)
                    Assert.True(s.World.Buildings.Spawn(p, d.FindBuilding("malazan_billet"), (4 + 14 * p) * 48 + 38 + 3 * k, out _));
        int keep0 = -1, keep1 = -1;
        for (int k = 0; k < sim.World.Buildings.Capacity; k++)
            if (sim.World.Buildings.Alive[k] && sim.World.Buildings.TypeId[k] == d.FindBuilding("malazan_garrison_keep"))
                if (sim.World.Buildings.Owner[k] == 0) keep0 = k; else keep1 = k;
        var rng = new Random(seed * 7919);
        int trains = 0, limitRefusals = 0, cancels = 0, kills = 0, maxCount = 0;
        sim.RefusedForTests = (c, why, detail) => { if (why == CommandRefusal.TrainRefused && (TrainError)detail == TrainError.LimitReached) limitRefusals++; };
        bool loaded = false;
        while (sim.TickNumber < Ticks)
        {
            // The commands of this tick, the same to both runs.
            var commands = new List<Command>();
            int n = rng.Next(0, 3);
            for (int c = 0; c < n; c++)
            {
                int p = rng.Next(2);
                int roll = rng.Next(10);
                if (roll < 6) commands.Add(Command.Train(p, In(sim, bar[p, rng.Next(2)]), heavy));
                else if (roll < 7) commands.Add(Command.Train(p, In(sim, p == 0 ? keep0 : keep1), laborer));
                else commands.Add(Command.CancelTrain(p, In(sim, bar[p, rng.Next(2)]), rng.Next(EconomyConstants.ProductionQueueCapacity)));
                if (roll < 6) trains++;
                else if (roll >= 7) cancels++;
            }
            // Between ticks, sometimes kill one of a player's capped units (in both runs: the same slot).
            if (rng.Next(3) == 0)
            {
                int p = rng.Next(2);
                int slot = PickUnit(sim.World, p, heavy, rng.Next(64));
                if (slot >= 0)
                {
                    Kill(sim.World, slot);
                    Kill(twin.World, slot);
                    kills++;
                }
            }
            foreach (Command c in commands)
            {
                sim.Enqueue(c);
                twin.Enqueue(c);
            }
            sim.Tick();
            twin.Tick();
            for (int p = 0; p < 2; p++)
            {
                int count = UnitLimitTests.Count(sim.World, p, heavy);
                Assert.True(count <= 7, $"seed {seed}: player {p}'s count {count} at tick {sim.TickNumber}");
                maxCount = Math.Max(maxCount, count);
                BuildMaps.SetTotals(sim, p, 5000, 5000);
                BuildMaps.SetTotals(twin, p, 5000, 5000);
            }
            Assert.True(sim.StateHash() == twin.StateHash(), $"seed {seed}: the twin differs at tick {sim.TickNumber}");
            if (!loaded && sim.TickNumber == 1_500)
            {
                // The run goes on from a loaded copy: the count is rebuilt, the hash unchanged.
                Action<Command, CommandRefusal, int>? hook = sim.RefusedForTests;
                sim = SaveTestRun.Load(d, SaveTestRun.Save(sim));
                sim.RefusedForTests = hook;
                for (int p = 0; p < 2; p++) UnitLimitTests.Count(sim.World, p, heavy);
                Assert.True(sim.StateHash() == twin.StateHash());
                loaded = true;
            }
        }
        Assert.True(loaded);
        Assert.True(maxCount > 7 - d.Units[heavy].SpawnCount, $"seed {seed}: the count never came within one item of the limit (max {maxCount})");
        Assert.True(limitRefusals > 0, $"seed {seed}: no Train was refused for the limit");
        return $"seed {seed} ({(seed % 2 == 1 ? "pack of 3" : "single")}): {trains} capped Trains, {limitRefusals} refused for the limit, {cancels} cancels, {kills} kills; max count {maxCount}; end {sim.StateHash():X16}";
    }

    /// <summary>The <paramref name="skip"/>-th (wrapping) live unit of <paramref name="type"/> of <paramref name="player"/>, or -1.</summary>
    private static int PickUnit(World w, int player, int type, int skip)
    {
        int found = 0;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == player && w.Units.TypeId[i] == type) found++;
        if (found == 0) return -1;
        skip %= found;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == player && w.Units.TypeId[i] == type && skip-- == 0) return i;
        return -1;
    }

    private static void Kill(World w, int slot) =>
        CombatSystem.HitUnit(w, new PendingHit(default, 1, new EntityHandle(slot, w.Units.Generation[slot]), false, w.Units.Hp[slot] + 1));
}
