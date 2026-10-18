using Rts.Sim.Ai;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA for D21 (three named openings per faction, session 2026-10-18-0516). Content rules the loader does not check, read
/// from the shipped files: every opening can actually field its own army mix (a trainer for every unit stands in the order,
/// an Age I unit needs no Age II), every research step has a Forge built in the same order, the wave's cost is what the
/// Age I mix costs at that pop (a wave that can't be reached never leaves), the player-facing description quotes the
/// opening's own wave pop, names don't collide with a building's name, and Easy stays on opening 0.
/// The Stress row replays every Malazan x Whirlwind pairing on its second-smallest seed (the report uses the smallest).
/// </summary>
public class D21OpeningsQaTests
{
    private readonly ITestOutputHelper _out;

    public D21OpeningsQaTests(ITestOutputHelper output) => _out = output;

    private static readonly string[] Factions = { "malazan", "whirlwind" };

    public static TheoryData<string, int> AllOpenings()
    {
        var rows = new TheoryData<string, int>();
        foreach (string f in Factions)
            for (int i = 0; i < TestSim.Data.Ai[TestSim.Data.FindFaction(f)].Openings.Length; i++) rows.Add(f, i);
        return rows;
    }

    private static AiOpening Opening(string faction, int index) => TestSim.Data.Ai[TestSim.Data.FindFaction(faction)].Openings[index];

    [Theory]
    [InlineData("malazan")]
    [InlineData("whirlwind")]
    public void EachFaction_ShipsThreeOpenings_StandardFirst_UniqueIdsAndNames(string faction)
    {
        AiDef ai = TestSim.Data.Ai[TestSim.Data.FindFaction(faction)];
        Assert.Equal(3, ai.Openings.Length);
        Assert.Equal("standard", ai.Openings[0].Id);
        Assert.Equal(3, ai.Openings.Select(o => o.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(3, ai.Openings.Select(o => o.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>The holy_camp rename's reason, as a rule: no opening is named like one of the faction's buildings or units.</summary>
    [Theory]
    [MemberData(nameof(AllOpenings))]
    public void OpeningName_IsNotABuildingOrUnitName(string faction, int index)
    {
        GameData d = TestSim.Data;
        int f = d.FindFaction(faction);
        string name = Opening(faction, index).DisplayName;
        foreach (BuildingDef b in d.Buildings)
            if (b.Faction == f) Assert.False(string.Equals(b.DisplayName, name, StringComparison.OrdinalIgnoreCase), $"{faction} opening {index} is named like the building {b.Key}");
        foreach (UnitDef u in d.Units)
            if (u.Faction == f) Assert.False(string.Equals(u.DisplayName, name, StringComparison.OrdinalIgnoreCase), $"{faction} opening {index} is named like the unit {u.Key}");
    }

    /// <summary>Every unit in either age's mix is the faction's own, has a trainer the opening builds (or the hall), and an Age I unit needs nothing of Age II.</summary>
    [Theory]
    [MemberData(nameof(AllOpenings))]
    public void EveryCompositionUnit_HasATrainerTheOpeningBuilds_AndFitsItsAge(string faction, int index)
    {
        GameData d = TestSim.Data;
        int f = d.FindFaction(faction);
        AiOpening o = Opening(faction, index);
        var built = new HashSet<int>();
        foreach (AiStep s in o.BuildOrder)
            if (s.Action == AiStepAction.Build) built.Add(s.Target);
        int ageTwo = Array.FindIndex(d.Techs.ToArray(), t => t.Key == "age_ii");
        Assert.True(ageTwo >= 0);
        foreach (AiComposition c in o.Compositions)
        {
            Assert.NotEmpty(c.Units);
            foreach (int u in c.Units)
            {
                UnitDef def = d.Units[u];
                Assert.True(def.Faction == f, $"{faction} {o.Id} age {c.Age}: {def.Key} is not the faction's");
                Assert.NotEqual(UnitSlot.Worker, def.Slot);
                BuildingDef trainer = d.Buildings[def.TrainedAtTypeId];
                Assert.True(trainer.Slot == BuildingSlot.TownHall || built.Contains(trainer.Id),
                    $"{faction} {o.Id} age {c.Age}: {def.Key} trains at {trainer.Key}, which the opening never builds");
                if (c.Age == 1)
                {
                    Assert.DoesNotContain(ageTwo, def.RequiresTechs);
                    Assert.DoesNotContain(ageTwo, trainer.RequiresTechs);
                }
            }
        }
    }

    /// <summary>Every research step's tech is researched at a slot the order builds; every build / research requirement is met by an earlier step (or the start).</summary>
    [Theory]
    [MemberData(nameof(AllOpenings))]
    public void EveryStepRequirement_StandsEarlierInTheSameOrder(string faction, int index)
    {
        GameData d = TestSim.Data;
        AiOpening o = Opening(faction, index);
        var buildings = new HashSet<int>();
        var techs = new HashSet<int>();
        foreach (BuildingDef b in d.Buildings)
            if (b.Faction == d.FindFaction(faction) && b.Slot == BuildingSlot.TownHall) buildings.Add(b.Id);
        for (int i = 0; i < o.BuildOrder.Length; i++)
        {
            AiStep s = o.BuildOrder[i];
            string at = $"{faction} {o.Id} step {i + 1} ({s.TargetKey})";
            if (s.Action == AiStepAction.Build)
            {
                foreach (int t in d.Buildings[s.Target].RequiresTechs) Assert.True(techs.Contains(t), $"{at}: needs {d.Techs[t].Key}, not researched earlier");
                foreach (int b in d.Buildings[s.Target].RequiresBuildings) Assert.True(buildings.Contains(b), $"{at}: needs {d.Buildings[b].Key}, not built earlier");
                buildings.Add(s.Target);
            }
            else if (s.Action == AiStepAction.Research)
            {
                TechDef t = d.Techs[s.Target];
                foreach (int r in t.RequiresTechs) Assert.True(techs.Contains(r), $"{at}: needs {d.Techs[r].Key}, not researched earlier");
                Assert.True(buildings.Any(b => d.Buildings[b].Slot == t.ResearchedAtSlot), $"{at}: no {t.ResearchedAtSlot} built earlier");
                techs.Add(s.Target);
            }
        }
    }

    /// <summary>
    /// The shape the brief asks for: at most 40 steps, exactly one <c>expand</c>, house steps up to the cap and no house beyond
    /// 12 (the 12th house is the D15 C question; the measurement kept it).
    /// </summary>
    [Theory]
    [MemberData(nameof(AllOpenings))]
    public void Shape_AtMost40Steps_OneExpand_HousesThroughTheCap(string faction, int index)
    {
        GameData d = TestSim.Data;
        int f = d.FindFaction(faction);
        AiOpening o = Opening(faction, index);
        Assert.InRange(o.BuildOrder.Length, 1, 40);
        Assert.Equal(1, o.BuildOrder.Count(s => s.Action == AiStepAction.Expand));
        BuildingDef house = d.Buildings.Single(b => b.Faction == f && b.Slot == BuildingSlot.House);
        BuildingDef hall = d.Buildings.Single(b => b.Faction == f && b.Slot == BuildingSlot.TownHall);
        int houses = o.BuildOrder.Count(s => s.Action == AiStepAction.Build && s.Target == house.Id);
        int capHalf = hall.HalfPopProvided + houses * house.HalfPopProvided;
        _out.WriteLine($"{faction} {o.Id}: {houses} houses, cap from the hall and houses {capHalf / 2.0} of {d.Rules.HalfPopCap / 2.0}");
        Assert.True(capHalf >= d.Rules.HalfPopCap, $"{faction} {o.Id}: houses reach only {capHalf / 2.0} pop");
        Assert.True(houses <= 12, $"{faction} {o.Id}: {houses} house steps");
        // Every pop-triggered house is due before the cap it leads to: a house at pop p is due while the cap is above p.
        int cap = hall.HalfPopProvided;
        foreach (AiStep s in o.BuildOrder)
        {
            if (s.Action != AiStepAction.Build || s.Target != house.Id || s.Trigger != AiStepTrigger.Pop) continue;
            Assert.True(s.At <= cap,$"{faction} {o.Id}: a house at pop {s.At / 2.0} can't come due with the cap at {cap / 2.0}");
            cap += house.HalfPopProvided;
        }
    }

    /// <summary>
    /// The wave can be reached: <c>minArmyCost</c> is at most what <c>minArmyPop</c> of the Age I mix costs (compositions are
    /// by pop) and not far under it (a cost far below the pop's makes the cost meaningless).
    /// </summary>
    [Theory]
    [MemberData(nameof(AllOpenings))]
    public void WaveCost_IsTheAgeOneMixCostAtTheWavePop(string faction, int index)
    {
        GameData d = TestSim.Data;
        AiOpening o = Opening(faction, index);
        AiComposition one = o.Compositions[0];
        Assert.Equal(1, one.Age);
        double weight = 0, perPop = 0;
        for (int k = 0; k < one.Units.Length; k++)
        {
            UnitDef u = d.Units[one.Units[k]];
            weight += one.Weights[k];
            perPop += one.Weights[k] * (u.CostGold + u.CostWood) / (u.HalfPop / 2.0);
        }
        perPop /= weight;
        double atPop = perPop * o.AttackMinHalfPop / 2.0;
        _out.WriteLine($"{faction} {o.Id}: wave {o.AttackMinHalfPop / 2.0} pop / {o.AttackMinCost}; the Age I mix costs {perPop:0.00} a pop, {atPop:0.0} at the wave pop");
        Assert.True(o.AttackMinCost <= Math.Ceiling(atPop), $"{faction} {o.Id}: minArmyCost {o.AttackMinCost} above the mix's {atPop:0.0} at {o.AttackMinHalfPop / 2.0} pop");
        Assert.True(o.AttackMinCost >= 0.9 * atPop, $"{faction} {o.Id}: minArmyCost {o.AttackMinCost} far under the mix's {atPop:0.0}");
    }

    /// <summary>The player-facing description quotes the wave pop ("… 16 army pop"): it must be the opening's own number.</summary>
    [Theory]
    [MemberData(nameof(AllOpenings))]
    public void Description_QuotesTheOpeningsOwnWavePop(string faction, int index)
    {
        AiOpening o = Opening(faction, index);
        Assert.InRange(o.DisplayName.Length, 1, 40);
        Assert.InRange(o.Description.Length, 1, 200);
        Assert.Contains($"{o.AttackMinHalfPop / 2} army pop", o.Description);
    }

    /// <summary>Easy keeps opening 0 on both seats, seeds 1-50; Normal and Hard reach all three openings for both seats in 1-50.</summary>
    [Fact]
    public void Easy_AlwaysStandard_NormalAndHard_ReachEveryOpening()
    {
        for (int p = 0; p < 2; p++)
        {
            var normal = new HashSet<int>();
            var hard = new HashSet<int>();
            for (ulong seed = 1; seed <= 50; seed++)
            {
                Assert.Equal(0, Rts.Sim.Tests.Content.AiOpeningSeeds.Pick(seed, p, AiDifficulty.Easy));
                normal.Add(Rts.Sim.Tests.Content.AiOpeningSeeds.Pick(seed, p, AiDifficulty.Normal));
                hard.Add(Rts.Sim.Tests.Content.AiOpeningSeeds.Pick(seed, p, AiDifficulty.Hard));
            }
            Assert.Equal(new[] { 0, 1, 2 }, normal.OrderBy(x => x));
            Assert.Equal(new[] { 0, 1, 2 }, hard.OrderBy(x => x));
        }
    }

    /// <summary>
    /// The 9 pairings again on a second seed each (the second-smallest in 1-500 making the pair; the report used the
    /// smallest): two Normal AI seats on the menu's map for 20 minutes, both send a wave.
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [MemberData(nameof(Pairs))]
    public void EveryPairing_SecondSeed_BothSidesWaveIn20Minutes(int malazan, int whirlwind)
    {
        ulong seed = 0;
        int found = 0;
        for (ulong s = 1; s <= 500 && found < 2; s++)
            if (Rts.Sim.Tests.Content.AiOpeningSeeds.Pick(s, 0, AiDifficulty.Normal) == malazan && Rts.Sim.Tests.Content.AiOpeningSeeds.Pick(s, 1, AiDifficulty.Normal) == whirlwind) { seed = s; found++; }
        Assert.Equal(2, found);
        Simulation sim = Rts.Sim.Tests.Content.AiSpendingReportTests.MenuMap(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai), seed);
        Assert.Equal((malazan, whirlwind), (sim.AiOpening(0), sim.AiOpening(1)));
        var first = new[] { -1, -1 };
        var fell = new[] { -1, -1 };
        var peakArmy = new int[2];
        for (int t = 0; t < 24_000; t++)
        {
            sim.Tick();
            for (int p = 0; p < 2; p++)
            {
                if (first[p] < 0 && sim.AiOf(p)!.Board.WavesStarted > 0) first[p] = sim.TickNumber;
                if (fell[p] < 0 && sim.World.IsDefeated(p)) fell[p] = sim.TickNumber;
            }
            if (t % 20 == 0)
                for (int p = 0; p < 2; p++) peakArmy[p] = Math.Max(peakArmy[p], ArmyHalfPop(sim, p));
        }
        string Sec(int tick) => tick < 0 ? "never" : $"{tick / 20 / 60}:{tick / 20 % 60:00}";
        _out.WriteLine($"seed {seed}: malazan {sim.AiOf(0)!.Opening.Id} first wave {Sec(first[0])} ({sim.AiOf(0)!.Board.WavesStarted} waves), whirlwind {sim.AiOf(1)!.Opening.Id} first wave {Sec(first[1])} ({sim.AiOf(1)!.Board.WavesStarted} waves); defeated at {Sec(fell[0])} / {Sec(fell[1])}; peak army pop {peakArmy[0] / 2.0} / {peakArmy[1] / 2.0}");
        for (int p = 0; p < 2; p++)
            Assert.True(first[p] >= 0 || sim.World.IsDefeated(p), $"seed {seed}: player {p} ({sim.AiOf(p)!.Opening.Id}) sent no wave in 20 minutes and was not defeated");
    }

    /// <summary>Determinism per pairing with the shipped openings: two Normal v Normal runs on the pair's smallest seed, equal state hashes every 50 ticks for 6,000 ticks, and the same commands sent.</summary>
    [Theory]
    [Trait("Category", "Stress")]
    [MemberData(nameof(Pairs))]
    public void EveryPairing_TwoRuns_SameHashes(int malazan, int whirlwind)
    {
        ulong seed = Rts.Sim.Tests.Content.AiOpeningSeeds.SmallestPair(malazan, whirlwind);
        Simulation a = Rts.Sim.Tests.Content.AiSpendingReportTests.MenuMap(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai), seed);
        Simulation b = Rts.Sim.Tests.Content.AiSpendingReportTests.MenuMap(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai), seed);
        Assert.Equal((malazan, whirlwind), (a.AiOpening(0), a.AiOpening(1)));
        Assert.Equal((malazan, whirlwind), (b.AiOpening(0), b.AiOpening(1)));
        for (int t = 1; t <= 6000; t++)
        {
            a.Tick();
            b.Tick();
            if (t % 50 == 0) Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: runs differ at tick {t}");
        }
        for (int p = 0; p < 2; p++) Assert.Equal(a.AiOf(p)!.CommandsSent, b.AiOf(p)!.CommandsSent);
    }

    private static int ArmyHalfPop(Simulation sim, int player)
    {
        var u = sim.World.Units;
        int army = 0;
        for (int s = 0; s < u.Capacity; s++)
            if (u.Alive[s] && u.Owner[s] == player && sim.World.Data.Units[u.TypeId[s]].Slot != UnitSlot.Worker) army += sim.World.Data.Units[u.TypeId[s]].HalfPop;
        return army;
    }

    public static TheoryData<int, int> Pairs()
    {
        var rows = new TheoryData<int, int>();
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++) rows.Add(i, j);
        return rows;
    }
}
