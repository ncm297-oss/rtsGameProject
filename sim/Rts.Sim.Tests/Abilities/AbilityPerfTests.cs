using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-4a criterion 6: 500 units, 50 Burning and 50 Slowed live, a Telas Fire cast every tick (20 a second): the status and
/// ability phases cost at most 0.1 ms a tick, and ticks with all of it allocate nothing.
/// </summary>
[Collection(SerialCollection.Name)]
public class AbilityPerfTests
{
    private const int Mages = 50;
    private readonly ITestOutputHelper _out;

    public AbilityPerfTests(ITestOutputHelper output) => _out = output;

    private sealed class Scene
    {
        public required Simulation Sim { get; init; }
        public required EntityHandle[] Mages { get; init; }
        public required EntityHandle[] Targets { get; init; }
    }

    private static Scene Build()
    {
        var sim = TestSim.Explored(new Simulation(TestSim.ConfigNoCombat(Seed: 1, PlayerCount: 2, UnitCapacity: 512, CommandCapacity: 1024), LocalMovementTests.Flat(128)));
        UnitStore u = sim.World.Units;
        var mages = new EntityHandle[Mages];
        var targets = new EntityHandle[500 - Mages];
        for (int k = 0; k < Mages; k++)
            mages[k] = CombatScenes.Place(sim, 0, AbilityScenes.Mage, new Vector2(20f + 4f * (k % 25), 40f + 40f * (k / 25)));
        for (int k = 0; k < targets.Length; k++)
        {
            targets[k] = CombatScenes.Place(sim, 1, CombatScenes.Crossbowman, new Vector2(20f + 2f * (k % 50), 50f + 2.5f * (k / 50)));
            u.Hp[targets[k].Index] = 1_000_000; // nobody dies: the load stays the same
        }
        return new Scene { Sim = sim, Mages = mages, Targets = targets };
    }

    /// <summary>Keeps 50 targets Burning and 50 other targets Slowed.</summary>
    private static void Refresh(Scene s)
    {
        for (int k = 0; k < 50; k++)
        {
            StatusSystem.Apply(s.Sim.World, s.Targets[k].Index, AbilityScenes.Burning, 10f, 80, 0);
            StatusSystem.Apply(s.Sim.World, s.Targets[100 + k].Index, AbilityScenes.Slowed, 0.3f, 80, 0);
        }
    }

    /// <summary>One cast a tick: mage <paramref name="t"/> mod 50, its cooldown cleared, at a point in range among the targets.</summary>
    private static void Cast(Scene s, int t)
    {
        EntityHandle m = s.Mages[t % Mages];
        UnitStore u = s.Sim.World.Units;
        u.AbilityReadyTick[m.Index * DataLimits.MaxUnitAbilities] = 0;
        s.Sim.Enqueue(Command.UseAbility(0, m, 0, u.Position[m.Index] + new Vector2(0f, 10f)));
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void FiveHundredUnits_50Burning_50Slowed_20CastsASecond_PhasesUnderPoint1Ms_AndAllocateNothing()
    {
        Scene s = Build();
        Simulation sim = s.Sim;
        World w = sim.World;
        for (int t = 0; t < 60; t++) // warm-up: casts under way, statuses live
        {
            if (t % 20 == 0) Refresh(s);
            Cast(s, t);
            sim.Tick();
        }
        long freq = Stopwatch.Frequency;
        const int Measured = 200;
        var ms = new double[Measured];
        int resolves = 0, burning = 0, slowed = 0;
        for (int t = 0; t < Measured; t++)
        {
            if (t % 20 == 0) Refresh(s);
            Cast(s, t);
            sim.Tick();
            foreach (AbilityEvent e in w.AbilityEvents) if (e.Resolved) resolves++;
            // The two phases once more on this state, timed alone (the tick around them would drown them in movement).
            int seen = w.AbilityEvents.Length;
            long start = Stopwatch.GetTimestamp();
            StatusSystem.Run(w);
            AbilitySystem.Run(w);
            ms[t] = (Stopwatch.GetTimestamp() - start) * 1000.0 / freq;
            foreach (AbilityEvent e in w.AbilityEvents[seen..]) if (e.Resolved) resolves++; // the extra run resolves some too (it counts down as well)
        }
        StatusStore st = w.Units.Statuses;
        for (int i = 0; i < w.Units.Capacity; i++)
        {
            if (st.IndexOf(i, AbilityScenes.Burning) >= 0) burning++;
            if (st.IndexOf(i, AbilityScenes.Slowed) >= 0) slowed++;
        }
        Assert.True(burning >= 50 && slowed >= 50, $"burning {burning}, slowed {slowed}");
        Assert.True(resolves >= Measured / 2, $"only {resolves} resolves");
        AllocationProbe.AssertZero(() =>
        {
            for (int t = 0; t < 20; t++) sim.Tick();
        }, _out, setup: () =>
        {
            Refresh(s);
            for (int k = 0; k < 20; k++) Cast(s, k);
        });
        double avg = ms.Average();
        _out.WriteLine($"status + ability phases: avg {avg:F4} ms, worst {ms.Max():F4} ms a tick; {resolves} resolves, {burning} burning, {slowed} slowed");
        Assert.True(avg <= 0.1, $"avg {avg:F4} ms over the 0.1 ms budget");
    }

    /// <summary>
    /// M4-4b-2 criterion 6: 500 units (two players), 8 live zones over player 0's units (two of them blocking vision): ticks
    /// allocate nothing; the zone phase costs at most 0.1 ms a tick and a fog update with the two blockers at most 0.1 ms
    /// more than one without zones.
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void FiveHundredUnits_8Zones_TwoBlockers_TicksAllocateNothing_ZonesAndBlockersCheap()
    {
        GameData data = ZoneData();
        var sim = TestSim.Explored(new Simulation(
            TestSim.ConfigNoCombat(Seed: 1, PlayerCount: 2, UnitCapacity: 512, CommandCapacity: 1024) with { Data = data }, LocalMovementTests.Flat(128)));
        World w = sim.World;
        for (int k = 0; k < 400; k++)
            CombatScenes.Place(sim, 0, CombatScenes.Crossbowman, new Vector2(30f + 2f * (k % 40), 40f + 2.5f * (k / 40)));
        for (int k = 0; k < 100; k++)
            CombatScenes.Place(sim, 1, CombatScenes.Raider, new Vector2(30f + 2f * (k % 40), 90f + 2.5f * (k / 40)));
        sim.Tick();
        // Fog update cost without zones, for the comparison below.
        long freq = Stopwatch.Frequency;
        double FogMs()
        {
            const int Runs = 50;
            long start = Stopwatch.GetTimestamp();
            for (int r = 0; r < Runs; r++) w.Fog.Update();
            return (Stopwatch.GetTimestamp() - start) * 1000.0 / freq / Runs;
        }
        FogMs();
        double fogPlain = FogMs();
        AbilityDef storm = data.Abilities[data.FindAbility("test_storm")], dust = data.Abilities[data.FindAbility("test_dust")];
        for (int k = 0; k < 8; k++)
            ZoneSystem.Create(w, 1, k < 2 ? storm : dust, new Vector2(40f + 18f * (k % 4), 45f + 12f * (k / 4)));
        Assert.Equal((8, 2), (w.Zones.Count, w.Zones.BlockerCount));
        for (int t = 0; t < 40; t++) sim.Tick();
        const int Measured = 200;
        var ms = new double[Measured];
        for (int t = 0; t < Measured; t++)
        {
            sim.Tick();
            long start = Stopwatch.GetTimestamp();
            ZoneSystem.Run(w);
            ms[t] = (Stopwatch.GetTimestamp() - start) * 1000.0 / freq;
        }
        FogMs();
        double fogZones = FogMs();
        int blinded = 0;
        int blind = data.FindStatus("blinded");
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Statuses.BlindOf(i) == blind) blinded++;
        Assert.Equal((8, 2), (w.Zones.Count, w.Zones.BlockerCount));
        Assert.True(blinded >= 100, $"only {blinded} units Blinded");
        AllocationProbe.AssertZero(() =>
        {
            for (int t = 0; t < 20; t++) sim.Tick();
        }, _out);
        double avg = ms.Average();
        _out.WriteLine($"zone phase: avg {avg:F4} ms, worst {ms.Max():F4} ms a tick; fog update {fogPlain:F4} ms without zones, {fogZones:F4} ms with 2 blockers; {blinded} Blinded");
        Assert.True(avg <= 0.1, $"zone phase avg {avg:F4} ms over the 0.1 ms budget");
        Assert.True(fogZones - fogPlain <= 0.1, $"the blockers add {fogZones - fogPlain:F4} ms to a fog update (budget 0.1 ms)");
    }

    /// <summary>The shipped data plus two test zone abilities for the Priest (60 s each, Blinded + Slowed): one blocks vision, one doesn't.</summary>
    private static GameData ZoneData()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        File.WriteAllText(dir.FullPath("factions/whirlwind/abilities.json"), """
            { "abilities": [
              { "id": "sandstorm", "displayName": "S", "description": "S.", "kind": "targetGround", "range": 18, "radius": 6, "castTime": 1.2,
                "cooldown": 45, "duration": 12, "affects": "enemy_units",
                "effects": [ { "kind": "createZone", "blocksVision": true, "statuses": [ { "status": "blinded", "duration": 1 } ] } ] },
              { "id": "test_storm", "displayName": "T", "description": "T.", "kind": "targetGround", "range": 18, "radius": 6, "castTime": 1,
                "cooldown": 45, "duration": 60, "affects": "enemy_units",
                "effects": [ { "kind": "createZone", "blocksVision": true,
                  "statuses": [ { "status": "blinded", "duration": 1 }, { "status": "slowed", "magnitude": 0.3, "duration": 1 } ] } ] },
              { "id": "test_dust", "displayName": "D", "description": "D.", "kind": "targetGround", "range": 18, "radius": 6, "castTime": 1,
                "cooldown": 45, "duration": 60, "affects": "enemy_units",
                "effects": [ { "kind": "createZone",
                  "statuses": [ { "status": "blinded", "duration": 1 }, { "status": "slowed", "magnitude": 0.3, "duration": 1 } ] } ] } ] }
            """);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join(Environment.NewLine, r.Errors));
        return r.Data!;
    }
}
