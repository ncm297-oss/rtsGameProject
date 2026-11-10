using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Save;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-2 criteria 5 and 7: both hooks in one match on a test copy (every unit bites, <see cref="OnHitPassiveTests.BiteAll"/>;
/// Malazan Heavy Infantry and Whirlwind Raiders train as packs of 3): a scripted 3,000-tick match runs twice to the same
/// hash every tick, a save made with a pack item started and a slow on a unit loads and plays on to the same hashes, and
/// the ticks allocate nothing while packs complete and bites land.
/// </summary>
[Collection(SerialCollection.Name)]
public class PackMatchTests
{
    private static readonly Lazy<GameData> s_match = new(() => AiScenes.DataWith(dir =>
    {
        OnHitPassiveTests.BiteAll(dir);
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "spawnCount", "3");
        dir.SetUnitField("whirlwind", "whirlwind_raider", "spawnCount", "3");
    }));

    /// <summary>The allocation copy: as <see cref="s_match"/>, the Heavy Infantry pack trained in 1.5 s (30 ticks), so many complete inside a short window.</summary>
    private static readonly Lazy<GameData> s_quick = new(() => AiScenes.DataWith(dir =>
    {
        OnHitPassiveTests.BiteAll(dir);
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "spawnCount", "3");
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "trainTime", "1.5");
    }));

    private const int Size = 64;

    /// <summary>Each side: five town halls for the cap, a pack building with a rally on the middle; a mixed brawl in the middle.</summary>
    private static (Simulation Sim, EntityHandle Barracks, EntityHandle Camp) Match(GameData d)
    {
        var sim = new Simulation(new SimConfig(11, 2, 512, 4128) { Data = d }, LocalMovementTests.Flat(Size));
        World w = sim.World;
        int keep = d.FindBuilding("malazan_garrison_keep"), hall = d.FindBuilding("whirlwind_holy_camp");
        for (int k = 0; k < 5; k++)
        {
            Assert.True(w.Buildings.Spawn(0, keep, (k < 4 ? 2 + 6 * k : 44) * Size + 3, out _));
            Assert.True(w.Buildings.Spawn(1, hall, (k < 4 ? 2 + 6 * k : 44) * Size + 57, out _));
        }
        Assert.True(w.Buildings.Spawn(0, d.FindBuilding("malazan_barracks"), 30 * Size + 8, out EntityHandle barracks));
        Assert.True(w.Buildings.Spawn(1, d.FindBuilding("whirlwind_raider_camp"), 30 * Size + 53, out EntityHandle camp));
        BuildMaps.SetTotals(sim, 0, 100_000, 100_000);
        BuildMaps.SetTotals(sim, 1, 100_000, 100_000);
        Vector2 middle = CombatScenes.At(sim, 32, 32);
        sim.Enqueue(Command.SetRally(0, w.Buildings.Cell[barracks.Index], middle));
        sim.Enqueue(Command.SetRally(1, w.Buildings.Cell[camp.Index], middle));
        CombatScenes.MixedBrawl(sim, 20, new Vector2(64f, 80f), gap: 8f);
        return (sim, barracks, camp);
    }

    /// <summary>The script: a pack queued on each side every 200 ticks; every 400 ticks each side attack-moves its army across.</summary>
    private static void Script(Simulation sim, EntityHandle barracks, EntityHandle camp)
    {
        World w = sim.World;
        int t = sim.TickNumber;
        if (t % 200 == 5)
        {
            sim.Enqueue(Command.Train(0, ProductionMaps.In(sim, barracks.Index), w.Data.FindUnit("malazan_heavy_infantry")));
            sim.Enqueue(Command.Train(1, ProductionMaps.In(sim, camp.Index), w.Data.FindUnit("whirlwind_raider")));
        }
        if (t % 400 == 390)
            foreach (int i in ProductionMaps.LiveUnits(w))
                sim.Enqueue(Command.AttackMove(w.Units.Owner[i], new EntityHandle(i, w.Units.Generation[i]),
                    CombatScenes.At(sim, w.Units.Owner[i] == 0 ? 50 : 14, 32)));
    }

    /// <summary>Live units of either side with a slowed entry applied this tick (40 left): bites that landed.</summary>
    private static int FreshBites(World w, int slowed)
    {
        int n = 0;
        var s = w.Units.Statuses;
        for (int i = 0; i < w.Units.Capacity; i++)
        {
            if (!w.Units.Alive[i]) continue;
            int at = s.IndexOf(i, slowed);
            if (at >= 0 && s.TicksRemaining[at] == 40) n++;
        }
        return n;
    }

    private static ulong[] Run(out int bites, out int packs)
    {
        GameData d = s_match.Value;
        (Simulation sim, EntityHandle barracks, EntityHandle camp) = Match(d);
        World w = sim.World;
        int slowed = d.FindStatus("slowed");
        var hashes = new ulong[3000];
        bites = 0;
        packs = 0;
        for (int k = 0; k < hashes.Length; k++)
        {
            Script(sim, barracks, camp);
            int trained = w.Ledger.Stats.UnitsTrained[0] + w.Ledger.Stats.UnitsTrained[1];
            sim.Tick();
            int now = w.Ledger.Stats.UnitsTrained[0] + w.Ledger.Stats.UnitsTrained[1];
            Assert.Equal(0, (now - trained) % 3); // packs only: three at a time
            packs += (now - trained) / 3;
            bites += FreshBites(w, slowed);
            for (int p = 0; p < 2; p++)
                Assert.True(ProductionMaps.RecountHalfPop(w, p) == w.HalfPop[p], $"tick {sim.TickNumber}, player {p}");
            hashes[k] = sim.StateHash();
        }
        return hashes;
    }

    /// <summary>Criterion 5: two runs of the scripted match hash the same every tick; packs completed on both sides and bites landed.</summary>
    [Fact]
    public void TwoRunsOfAScriptedMatch_WithPacksAndBites_HashTheSameEveryTick()
    {
        ulong[] first = Run(out int bites1, out int packs1), second = Run(out int bites2, out int packs2);
        Assert.Equal(first, second);
        Assert.Equal((bites1, packs1), (bites2, packs2));
        Assert.True(packs1 >= 10, $"only {packs1} packs completed");
        Assert.True(bites1 >= 100, $"only {bites1} bites");
    }

    /// <summary>Criterion 5: a save with a pack item started and a slow active loads, saves back to the same bytes, and plays on to the same hashes.</summary>
    [Fact]
    public void AMidMatchSave_WithAPackStartedAndASlowActive_LoadsAndContinuesToTheSameHashes()
    {
        GameData d = s_match.Value;
        (Simulation sim, EntityHandle barracks, EntityHandle camp) = Match(d);
        World w = sim.World;
        int slowed = d.FindStatus("slowed");
        bool Ready()
        {
            if (sim.TickNumber < 400 || w.Buildings.Progress[barracks.Index] == 0) return false;
            for (int i = 0; i < w.Units.Capacity; i++)
                if (w.Units.Alive[i] && w.Units.Statuses.IndexOf(i, slowed) >= 0) return true;
            return false;
        }
        while (!Ready())
        {
            Assert.True(sim.TickNumber < 3000, "no tick with a pack started and a slow on");
            Script(sim, barracks, camp);
            sim.Tick();
        }
        Assert.Equal(6, w.Buildings.ReservedHalfPop(barracks.Index));
        byte[] bytes = SaveTestRun.Save(sim);
        Simulation loaded = SaveTestRun.Load(d, bytes);
        Assert.Equal(sim.StateHash(), loaded.StateHash());
        Assert.Equal(bytes, SaveTestRun.Save(loaded));
        for (int k = 0; k < 800; k++)
        {
            Script(sim, barracks, camp);
            Script(loaded, barracks, camp);
            sim.Tick();
            loaded.Tick();
            Assert.True(sim.StateHash() == loaded.StateHash(), $"diverged at tick {sim.TickNumber}");
            Assert.Equal(sim.World.HalfPop[0], loaded.World.HalfPop[0]);
        }
    }

    /// <summary>
    /// Criterion 7: 200 ticks with packs completing (four Barracks, five 1.5 s packs queued at each: 20 packs) and bites landing
    /// (a 20 v 20 brawl of biting Heavy Infantry and Raiders) allocate nothing.
    /// </summary>
    [Fact]
    public void TicksWithPacksCompletingAndBitesLanding_AllocateNothing()
    {
        GameData d = s_quick.Value;
        var sim = new Simulation(new SimConfig(13, 2, 512, 4128) { Data = d }, LocalMovementTests.Flat(Size));
        World w = sim.World;
        int keep = d.FindBuilding("malazan_garrison_keep"), heavy = d.FindUnit("malazan_heavy_infantry"), slowed = d.FindStatus("slowed");
        for (int k = 0; k < 9; k++) Assert.True(w.Buildings.Spawn(0, keep, (2 + 6 * (k % 3)) * Size + 2 + 6 * (k / 3), out _)); // cap 100 pop
        var barracks = new int[4];
        for (int k = 0; k < 4; k++)
        {
            Assert.True(w.Buildings.Spawn(0, d.FindBuilding("malazan_barracks"), (40 + 5 * (k % 2)) * Size + 4 + 5 * (k / 2), out EntityHandle b));
            barracks[k] = b.Index;
        }
        Assert.True(w.Buildings.Spawn(1, d.FindBuilding("whirlwind_holy_camp"), 4 * Size + 56, out _));
        CombatScenes.FlatBrawl(sim, 20, new Vector2(80f, 100f), gap: 4f, ranks: 5);
        var trainAt = new Vector2[4];
        for (int k = 0; k < 4; k++) trainAt[k] = ProductionMaps.In(sim, barracks[k]);
        // Warm up: one pack a barracks (the spawn path, the bite path), and the fight joined.
        BuildMaps.SetTotals(sim, 0, 100_000, 100_000);
        for (int k = 0; k < 4; k++) sim.Enqueue(Command.Train(0, trainAt[k], heavy));
        for (int k = 0; k < 60; k++) sim.Tick();
        Assert.Equal(12, w.Ledger.Stats.UnitsTrained[0]); // four packs of three
        int packs = 0, bites = 0;
        Action setup = () =>
        {
            for (int k = 0; k < 4; k++)
                for (int n = 0; n < 5; n++) sim.Enqueue(Command.Train(0, trainAt[k], heavy));
        };
        Action ticks = () =>
        {
            for (int k = 0; k < 200; k++)
            {
                int trained = w.Ledger.Stats.UnitsTrained[0];
                sim.Tick();
                packs += (w.Ledger.Stats.UnitsTrained[0] - trained) / 3;
                bites += FreshBites(w, slowed);
                sim.StateHash();
            }
        };
        int runs = AllocationProbe.AssertZero(ticks, setup: setup);
        Assert.True(packs >= 20, $"only {packs} packs completed in {runs} measured run(s)");
        Assert.True(bites >= 100, $"only {bites} bites");
    }
}
