using System.Numerics;
using System.Reflection;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using static Rts.Sim.Tests.BuildMaps;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ProductionMaps;
using static Rts.Sim.Tests.ResearchMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M3-5 (2026-10-07-1131): the queue-item generalisation (a unit or a tech in one queue) at its edges: cancel at the
/// last tick, cancels behind a tech, a tech behind a population-blocked unit, one tech at two buildings in one tick,
/// research at a building finishing that tick, every tech researched against an oracle read from the raw JSON, and a
/// reflection audit of <see cref="TechState"/>.
/// </summary>
[Collection(SerialCollection.Name)]
public class ResearchQaTests
{
    private const int Start = 5000;

    private static Simulation Scene(out int keep, out int armory, out int armory2)
    {
        Simulation sim = BuildMaps.NewSim(Flat(48, 32), units: 64, players: 2);
        keep = Building(sim, 25, 10).Index;
        armory = Building(sim, 10, 10, type: Armory).Index;
        armory2 = Building(sim, 10, 20, type: Armory).Index;
        // M3-6: Age II needs two distinct hall slots finished; the two Armories are one slot, so a Barracks makes two.
        Building(sim, 34, 10, type: ProductionMaps.Barracks);
        Building(sim, 40, 24, player: 1, type: HolyCamp);
        SetTotals(sim, 0, Start, Start);
        SetTotals(sim, 1, Start, Start);
        return sim;
    }

    private static int AgeTicks => TestSim.Data.Techs[AgeII].ResearchTicks;

    // ---------- cancel at the edges ----------

    [Fact]
    public void CancellingTheTechHead_InTheTickItWouldComplete_RefundsInFull_AndItIsNeverResearched()
    {
        Simulation sim = Scene(out int keep, out _, out _);
        World w = sim.World;
        BuildingStore b = w.Buildings;
        Apply(sim, Command.Research(0, In(sim, keep), AgeII));
        Assert.Equal(1, b.Progress[keep]);
        while (b.Progress[keep] < AgeTicks - 2) sim.Tick();
        // Queued now, it applies two ticks later: in phase 1 of the tick that would take progress 1,199 -> 1,200.
        sim.Enqueue(Command.CancelTrain(0, In(sim, keep), 0));
        sim.Tick();
        Assert.Equal(AgeTicks - 1, b.Progress[keep]);
        sim.Tick();
        Assert.False(w.HasTech(0, AgeII));
        Assert.Equal(1, w.Age(0));
        Assert.Equal((0, 0), (b.QueueCount[keep], b.Progress[keep]));
        Assert.Equal((Start, Start), (w.Gold[0], w.Wood[0]));
        // And it can be queued again, starting from scratch: a full 1,200 ticks more.
        Apply(sim, Command.Research(0, In(sim, keep), AgeII));
        Assert.Equal(1, b.Progress[keep]);
        Run(sim, AgeTicks - 2);
        Assert.False(w.HasTech(0, AgeII));
        sim.Tick();
        Assert.True(w.HasTech(0, AgeII));
    }

    [Fact]
    public void ACancelOneTickTooLate_FindsTheQueueEmpty_AndRefundsNothing()
    {
        Simulation sim = Scene(out int keep, out _, out _);
        World w = sim.World;
        BuildingStore b = w.Buildings;
        Apply(sim, Command.Research(0, In(sim, keep), AgeII));
        while (b.Progress[keep] < AgeTicks - 1) sim.Tick();
        sim.Enqueue(Command.CancelTrain(0, In(sim, keep), 0));
        sim.Tick(); // completes
        Assert.True(w.HasTech(0, AgeII));
        sim.Tick(); // the cancel finds nothing
        Assert.True(w.HasTech(0, AgeII));
        Assert.Equal((Start - 400, Start - 200), (w.Gold[0], w.Wood[0]));
    }

    [Fact]
    public void CancellingAUnitBehindATech_RefundsTheUnit_AndTheTechKeepsItsTimer()
    {
        Simulation sim = Scene(out int keep, out _, out _);
        World w = sim.World;
        BuildingStore b = w.Buildings;
        int laborerGold = w.Data.Units[Laborer].CostGold, laborerWood = w.Data.Units[Laborer].CostWood;
        sim.Enqueue(Command.Research(0, In(sim, keep), AgeII));
        sim.Enqueue(Command.Train(0, In(sim, keep), Laborer));
        sim.Enqueue(Command.Train(0, In(sim, keep), Laborer));
        Run(sim, 2);
        Assert.Equal((3, true, false, false), (b.QueueCount[keep], b.QueueIsTechAt(keep, 0), b.QueueIsTechAt(keep, 1), b.QueueIsTechAt(keep, 2)));
        Run(sim, 300);
        int progress = b.Progress[keep];
        Apply(sim, Command.CancelTrain(0, In(sim, keep), 1));
        Assert.Equal(progress + 2, b.Progress[keep]); // never paused, never reset
        Assert.Equal((2, true, Laborer), (b.QueueCount[keep], b.QueueIsTechAt(keep, 0), b.QueueTypeAt(keep, 1)));
        Assert.False(b.QueueIsTechAt(keep, 1));
        Assert.Equal((Start - 400 - laborerGold, Start - 200 - laborerWood), (w.Gold[0], w.Wood[0]));
        // Age II completes on schedule and the remaining Laborer starts in the same tick.
        Run(sim, AgeTicks - b.Progress[keep]);
        Assert.True(w.HasTech(0, AgeII));
        Assert.Equal((1, 1, false), (b.QueueCount[keep], b.Progress[keep], b.QueueIsTechAt(keep, 0)));
    }

    [Fact]
    public void CancellingATechBetweenTwoUnits_ShiftsTheFlagWithTheEntries()
    {
        Simulation sim = Scene(out int keep, out _, out _);
        World w = sim.World;
        BuildingStore b = w.Buildings;
        sim.Enqueue(Command.Train(0, In(sim, keep), Laborer));
        sim.Enqueue(Command.Research(0, In(sim, keep), AgeII));
        sim.Enqueue(Command.Train(0, In(sim, keep), Laborer));
        Run(sim, 2);
        Apply(sim, Command.CancelTrain(0, In(sim, keep), 1));
        Assert.Equal(2, b.QueueCount[keep]);
        for (int q = 0; q < EconomyConstants.ProductionQueueCapacity; q++)
            Assert.False(b.QueueIsTechEntry(keep, q), $"entry {q} still flagged a tech after the tech left");
        Assert.True(w.CanResearch(0, keep, AgeII, out _));
    }

    // ---------- a tech behind a population-blocked unit ----------

    [Fact]
    public void ATechBehindAPopBlockedUnit_Waits_ThenRunsItsFullTime_AfterTheUnit_NeverSkippedOrLost()
    {
        Simulation sim = Scene(out int keep, out _, out _);
        World w = sim.World;
        BuildingStore b = w.Buildings;
        // Fill the cap (one Keep: 10) with dev-spawned workers.
        var workers = new List<EntityHandle>();
        for (int n = 0; w.HalfPop[0] < w.HalfPopCap[0]; n++) workers.Add(Unit(sim, At(sim, 30 + n % 8, 20 + n / 8)));
        Assert.Equal(w.HalfPopCap[0], w.HalfPop[0]);
        sim.Enqueue(Command.Train(0, In(sim, keep), Laborer));
        sim.Enqueue(Command.Research(0, In(sim, keep), AgeII));
        Run(sim, 2);
        Run(sim, AgeTicks + 100);
        Assert.Equal((2, 0), (b.QueueCount[keep], b.Progress[keep]));
        Assert.False(w.HasTech(0, AgeII)); // in order: the tech does not jump the blocked unit (M3-4 queue rule)
        Assert.True(b.QueueIsTechAt(keep, 1));
        // Room again: the Laborer trains its full time, Age II starts in the tick it spawns and takes exactly its ticks.
        w.Units.Free(workers[0]);
        int unitsBefore = w.Units.Count, ticks = 0;
        while (w.Units.Count == unitsBefore && ticks < 1000) { sim.Tick(); ticks++; }
        Assert.Equal(w.Data.Units[Laborer].TrainTicks, ticks);
        Assert.Equal((1, 1, true), (b.QueueCount[keep], b.Progress[keep], b.QueueIsTechAt(keep, 0)));
        Run(sim, AgeTicks - 2);
        Assert.False(w.HasTech(0, AgeII));
        sim.Tick();
        Assert.True(w.HasTech(0, AgeII));
        Assert.Equal(RecountHalfPop(w, 0), w.HalfPop[0]);
    }

    [Fact]
    public void ATechHead_AtFullPop_IsNotStarved_AndTheUnitBehindItWaits()
    {
        Simulation sim = Scene(out int keep, out _, out _);
        World w = sim.World;
        BuildingStore b = w.Buildings;
        for (int n = 0; w.HalfPop[0] < w.HalfPopCap[0]; n++) Unit(sim, At(sim, 30 + n % 8, 20 + n / 8));
        int halfPop = w.HalfPop[0];
        sim.Enqueue(Command.Research(0, In(sim, keep), AgeII));
        sim.Enqueue(Command.Train(0, In(sim, keep), Laborer));
        Run(sim, 2);
        Assert.Equal(1, b.Progress[keep]);
        Run(sim, AgeTicks - 1);
        Assert.True(w.HasTech(0, AgeII));
        Assert.Equal((1, 0, false), (b.QueueCount[keep], b.Progress[keep], b.QueueIsTechAt(keep, 0)));
        Run(sim, 50);
        Assert.Equal(0, b.Progress[keep]); // the Laborer waits for room
        Assert.Equal(halfPop, w.HalfPop[0]);
    }

    [Fact]
    public void CancellingTheBlockedUnitHead_StartsTheTechBehindItInTheSameTick()
    {
        Simulation sim = Scene(out int keep, out _, out _);
        World w = sim.World;
        BuildingStore b = w.Buildings;
        for (int n = 0; w.HalfPop[0] < w.HalfPopCap[0]; n++) Unit(sim, At(sim, 30 + n % 8, 20 + n / 8));
        sim.Enqueue(Command.Train(0, In(sim, keep), Laborer));
        sim.Enqueue(Command.Research(0, In(sim, keep), AgeII));
        Run(sim, 40);
        Apply(sim, Command.CancelTrain(0, In(sim, keep), 0));
        Assert.Equal((1, 1, true), (b.QueueCount[keep], b.Progress[keep], b.QueueIsTechAt(keep, 0)));
        Run(sim, AgeTicks - 2);
        Assert.False(w.HasTech(0, AgeII));
        sim.Tick();
        Assert.True(w.HasTech(0, AgeII));
    }

    // ---------- one tech, two buildings, one tick ----------

    [Fact]
    public void TheSameTechAtTwoForgesInOneTick_IsAcceptedOnce_AndChargedOnce()
    {
        Simulation sim = Scene(out _, out int armory, out int armory2);
        World w = sim.World;
        BuildingStore b = w.Buildings;
        TechDef m = w.Data.Techs[Melee1];
        sim.Enqueue(Command.Research(0, In(sim, armory), Melee1));
        sim.Enqueue(Command.Research(0, In(sim, armory2), Melee1));
        sim.Enqueue(Command.Research(0, In(sim, armory2), Melee1));
        Run(sim, 2);
        Assert.Equal((1, 0), (b.QueueCount[armory], b.QueueCount[armory2]));
        Assert.Equal((Start - m.CostGold, Start - m.CostWood), (w.Gold[0], w.Wood[0]));
        Assert.False(w.CanResearch(0, armory2, Melee1, out ResearchError why));
        Assert.Equal(ResearchError.AlreadyQueued, why);
    }

    [Fact]
    public void ResearchCancelResearch_InOneTick_LeavesItQueuedAtTheSecondForge_ChargedOnce()
    {
        Simulation sim = Scene(out _, out int armory, out int armory2);
        World w = sim.World;
        BuildingStore b = w.Buildings;
        TechDef m = w.Data.Techs[Melee1];
        sim.Enqueue(Command.Research(0, In(sim, armory), Melee1));
        sim.Enqueue(Command.CancelTrain(0, In(sim, armory), 0));
        sim.Enqueue(Command.Research(0, In(sim, armory2), Melee1));
        Run(sim, 2);
        Assert.Equal((0, 1), (b.QueueCount[armory], b.QueueCount[armory2]));
        Assert.Equal((Start - m.CostGold, Start - m.CostWood), (w.Gold[0], w.Wood[0]));
    }

    [Fact]
    public void ADestroyedForge_ReleasesItsQueuedTechs_ForAnotherForge()
    {
        Simulation sim = Scene(out _, out int armory, out int armory2);
        World w = sim.World;
        BuildingStore b = w.Buildings;
        sim.Enqueue(Command.Research(0, In(sim, armory), Melee1));
        sim.Enqueue(Command.Research(0, In(sim, armory), Armor1));
        Run(sim, 302);
        b.Damage(b.HandleOf(armory), 1_000_000);
        Assert.Equal((Start, Start), (w.Gold[0], w.Wood[0]));
        Assert.True(w.CanResearch(0, armory2, Melee1, out _));
        Assert.True(w.CanResearch(0, armory2, Armor1, out _));
        Run(sim, 1000);
        Assert.False(w.HasTech(0, Melee1) || w.HasTech(0, Armor1));
        Assert.Equal(0f, w.TechBonus(0, Infantry, TechStat.Attack));
    }

    [Fact]
    public void TwoPlayers_TheSameCommonTechInOneTick_BothAccepted()
    {
        Simulation sim = BuildMaps.NewSim(Flat(48, 32), units: 16, players: 2);
        int a = Building(sim, 10, 10, type: Armory).Index;
        int s = Building(sim, 30, 10, player: 1, type: Smithy).Index;
        SetTotals(sim, 0, Start, Start);
        SetTotals(sim, 1, Start, Start);
        sim.Enqueue(Command.Research(0, In(sim, a), Armor1));
        sim.Enqueue(Command.Research(1, In(sim, s), Armor1));
        sim.Enqueue(Command.Research(1, In(sim, a), Armor1)); // at the enemy's Forge: dropped
        Run(sim, 2);
        Assert.Equal((1, 1), (sim.World.Buildings.QueueCount[a], sim.World.Buildings.QueueCount[s]));
        Run(sim, sim.World.Data.Techs[Armor1].ResearchTicks);
        Assert.True(sim.World.HasTech(0, Armor1) && sim.World.HasTech(1, Armor1));
        Assert.Equal(1f, sim.World.TechBonus(1, Raider, TechStat.Armor));
    }

    // ---------- slots and money at the boundary ----------

    [Fact]
    public void ResearchAtTheWrongSlot_AndTrainAtAForge_AreDropped_WithNoMoneyMoved()
    {
        Simulation sim = Scene(out int keep, out int armory, out _);
        World w = sim.World;
        BuildingStore b = w.Buildings;
        int barracks = Building(sim, 30, 24, type: ProductionMaps.Barracks).Index;
        SetTotals(sim, 0, Start, Start);
        Assert.False(w.CanResearch(0, keep, Melee1, out ResearchError why));
        Assert.Equal(ResearchError.NotResearchedHere, why);
        Assert.False(w.CanResearch(0, armory, AgeII, out why));
        Assert.Equal(ResearchError.NotResearchedHere, why);
        Assert.False(w.CanResearch(0, barracks, Armor1, out why));
        Assert.Equal(ResearchError.NotResearchedHere, why);
        Assert.False(w.CanResearch(0, armory, Dryjhna, out why));
        Assert.Equal(ResearchError.WrongFaction, why);
        sim.Enqueue(Command.Research(0, In(sim, keep), Melee1));
        sim.Enqueue(Command.Research(0, In(sim, armory), AgeII));
        sim.Enqueue(Command.Research(0, In(sim, barracks), Armor1));
        sim.Enqueue(Command.Research(0, In(sim, armory), Dryjhna));
        sim.Enqueue(Command.Train(0, In(sim, armory), Laborer));
        sim.Enqueue(Command.Train(0, In(sim, armory), Infantry));
        Run(sim, 2);
        Assert.Equal((0, 0, 0), (b.QueueCount[keep], b.QueueCount[armory], b.QueueCount[barracks]));
        Assert.Equal((Start, Start), (w.Gold[0], w.Wood[0]));
    }

    [Theory]
    [InlineData(400, 200, true)]
    [InlineData(399, 200, false)]
    [InlineData(400, 199, false)]
    [InlineData(0, 0, false)]
    public void AgeII_AtExactlyItsCost_IsAccepted_OneShortIsDropped(int gold, int wood, bool accepted)
    {
        Simulation sim = Scene(out int keep, out _, out _);
        World w = sim.World;
        SetTotals(sim, 0, gold, wood);
        Assert.Equal(accepted, w.CanResearch(0, keep, AgeII, out ResearchError why));
        if (!accepted) Assert.Equal(ResearchError.CannotAfford, why);
        Apply(sim, Command.Research(0, In(sim, keep), AgeII));
        Assert.Equal(accepted ? 1 : 0, w.Buildings.QueueCount[keep]);
        Assert.Equal(accepted ? (0, 0) : (gold, wood), (w.Gold[0], w.Wood[0]));
    }

    // ---------- a building finishing construction in the same tick ----------

    [Fact]
    public void ResearchApplyingInTheTickAForgeFinishes_IsDropped_AndTheNextTickIsAccepted()
    {
        // Run once to find the tick the Armory site finishes; the twin then times the Research to apply in it.
        int finishTick = -1;
        for (int pass = 0; pass < 2; pass++)
        {
            Simulation sim = BuildMaps.NewSim(Flat(40, 30), units: 16);
            Give(sim, 0, 10_000, 10_000);
            EntityHandle worker = Unit(sim, At(sim, 8, 8));
            sim.Enqueue(Command.Build(0, worker, Armory, At(sim, 10, 10)));
            Run(sim, 2);
            int site = SiteAt(sim, 10, 10);
            Assert.True(site >= 0 && sim.World.Buildings.UnderConstruction[site]);
            BuildingStore b = sim.World.Buildings;
            if (pass == 0)
            {
                int t = 0;
                while (b.UnderConstruction[site] && t < 20_000) { sim.Tick(); t++; }
                Assert.False(b.UnderConstruction[site]);
                finishTick = t;
                continue;
            }
            Run(sim, finishTick - 2);
            int gold = sim.World.Gold[0], wood = sim.World.Wood[0];
            sim.Enqueue(Command.Research(0, In(sim, site), Melee1));
            sim.Tick();
            Assert.True(b.UnderConstruction[site]);
            sim.Tick(); // applies in phase 1 (still a site), the site finishes in phase 4
            Assert.False(b.UnderConstruction[site]);
            Assert.Equal(0, b.QueueCount[site]);
            Assert.Equal((gold, wood), (sim.World.Gold[0], sim.World.Wood[0]));
            Apply(sim, Command.Research(0, In(sim, site), Melee1));
            Assert.Equal((1, true), (b.QueueCount[site], b.QueueIsTechAt(site, 0)));
        }
    }

    // ---------- every tech, the bonus oracle ----------

    [Fact]
    public void ResearchingEveryTech_OneByOne_TechBonusMatchesTheRawDataOracle_AfterEachCompletion()
    {
        var oracle = new TechBonusOracle(TestDataDir.Shipped);
        Simulation sim = BuildMaps.NewSim(Flat(48, 32), units: 16, players: 2);
        int[] forge = { Building(sim, 10, 10, type: Armory).Index, Building(sim, 30, 10, player: 1, type: Smithy).Index };
        int[] hall = { Building(sim, 10, 20).Index, Building(sim, 30, 20, player: 1, type: HolyCamp).Index };
        // M3-6: Age II needs two hall slots (the Forge is one); research goes in id order, so Age II (id 0) comes first.
        Building(sim, 18, 10, type: ProductionMaps.Barracks);
        Building(sim, 38, 10, player: 1, type: RaiderCamp);
        SetTotals(sim, 0, 1_000_000, 1_000_000);
        SetTotals(sim, 1, 1_000_000, 1_000_000);
        World w = sim.World;
        oracle.AssertMatches(w, "start");
        int completions = 0;
        for (int p = 0; p < 2; p++)
        {
            for (int t = 0; t < w.Data.Techs.Length; t++)
            {
                int at = w.CanResearch(p, forge[p], t, out _) ? forge[p] : w.CanResearch(p, hall[p], t, out _) ? hall[p] : -1;
                if (at < 0) continue;
                Apply(sim, Command.Research(p, In(sim, at), t));
                Run(sim, w.Data.Techs[t].ResearchTicks - 1);
                Assert.True(w.HasTech(p, t), w.Data.Techs[t].Key);
                completions++;
                oracle.AssertMatches(w, $"after {w.Data.Techs[t].Key} for player {p}");
            }
        }
        Assert.Equal(16, completions); // 7 common x 2 + one faction upgrade each
        // Spot values against docs/02 and the faction pages, all techs in.
        Assert.Equal(2f, w.TechBonus(0, Infantry, TechStat.Attack));
        Assert.Equal(2f, w.TechBonus(0, Infantry, TechStat.Armor));
        Assert.Equal(0f, w.TechBonus(0, w.Data.FindUnit("malazan_catapult"), TechStat.Armor));
        Assert.Equal(4f, w.TechBonus(0, w.Data.FindUnit("malazan_catapult"), TechStat.Range));
        Assert.Equal(-300f, w.TechBonus(0, Sapper, TechStat.AbilityCooldown));
        Assert.Equal(20f, w.TechBonus(1, Zealot, TechStat.Hp));
        Assert.Equal(-300f, w.TechBonus(1, w.Data.FindUnit("whirlwind_priest"), TechStat.AbilityCooldown));
        Assert.Equal(0f, w.TechBonus(1, w.Data.FindUnit("whirlwind_battering_ram"), TechStat.Armor));
        Assert.Equal(2f, w.TechBonus(1, w.Data.FindUnit("whirlwind_desert_archer"), TechStat.Attack));
        Assert.Equal(0f, w.TechBonus(1, w.Data.FindUnit("whirlwind_priest"), TechStat.Attack)); // magic: neither melee nor pierce
    }

    // ---------- reflection audit of the tech state ----------

    /// <summary>
    /// Every array field of <see cref="TechState"/> is either hashed (the flags: each element changes the hash) or derived
    /// (the bonus table and sums: changing one leaves the hash alone, and <c>Set</c> rebuilds them). A new array fails the
    /// audit until it is classified here.
    /// </summary>
    [Fact]
    public void EveryTechStateArray_IsHashedOrDerived()
    {
        Simulation sim = Scene(out _, out _, out _);
        TechState ts = sim.World.Techs;
        var hashed = new HashSet<string> { "_flags" };
        var derived = new HashSet<string> { "_table", "_bonus" };
        int audited = 0;
        foreach (FieldInfo f in typeof(TechState).GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (!f.FieldType.IsArray) continue;
            Assert.True(hashed.Contains(f.Name) || derived.Contains(f.Name), $"TechState.{f.Name}: new array, classify it as hashed or derived");
            var arr = (Array)f.GetValue(ts)!;
            for (int e = 0; e < arr.Length; e++)
            {
                if (f.Name == "_flags")
                {
                    // Every bit of every player's words that names a tech.
                    int techs = sim.World.Data.Techs.Length, words = ts.WordsPerPlayer;
                    for (int bit = 0; bit < 64 && (e % words) * 64 + bit < techs; bit++)
                    {
                        ulong h0 = sim.StateHash();
                        ulong old = (ulong)arr.GetValue(e)!;
                        arr.SetValue(old ^ (1UL << bit), e);
                        Assert.True(sim.StateHash() != h0, $"_flags[{e}] bit {bit} is not hashed");
                        arr.SetValue(old, e);
                        Assert.Equal(h0, sim.StateHash());
                    }
                }
                else
                {
                    ulong h0 = sim.StateHash();
                    object old = arr.GetValue(e)!;
                    arr.SetValue((float)old + 1f, e);
                    Assert.Equal(h0, sim.StateHash());
                    arr.SetValue(old, e);
                }
            }
            audited++;
        }
        Assert.Equal(3, audited);
    }

    [Fact]
    public void AQueuedTechWithIdZero_IsToldApartFromAUnitWithIdZero_InTheHash()
    {
        // age_ii is tech id 0, the first ordinal id; a queue entry of type 0 writes no type word (bit 4 + k clear), so only
        // the tech flag bit tells a queued Age II from a queued unit type 0.
        Assert.Equal(0, AgeII);
        Simulation a = Scene(out int keep, out _, out _), b = Scene(out _, out _, out _);
        Apply(a, Command.Research(0, In(a, keep), AgeII));
        Apply(b, Command.Research(0, In(b, keep), AgeII));
        Assert.Equal(a.StateHash(), b.StateHash());
        b.World.Buildings.QueueIsTechEntry(keep, 0) = false;
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }
}
