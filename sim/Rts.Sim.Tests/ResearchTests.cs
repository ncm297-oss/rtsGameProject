using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Replays;
using static Rts.Sim.Tests.BuildMaps;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ProductionMaps;
using static Rts.Sim.Tests.ResearchMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests;

/// <summary>M3-5 criteria 3-6: research through the production queue, the tech state, the bonus query, and the dropped cases.</summary>
[Collection(SerialCollection.Name)]
public class ResearchTests
{
    private const int Start = 5000;

    /// <summary>
    /// A flat 40 x 30 map: player 0 (Malazan) with a Keep at (25, 10), an Armory at (10, 10) and a Barracks at (10, 18);
    /// player 1 (Whirlwind) with a Holy Camp at (25, 20) and a Smithy at (32, 4). Both have 5,000 gold and wood.
    /// </summary>
    private sealed class Scene
    {
        public Scene()
        {
            Sim = BuildMaps.NewSim(Flat(40, 30), units: 64, players: 2);
            Keep = Building(Sim, 25, 10).Index;
            Armory = Building(Sim, 10, 10, type: ResearchMaps.Armory).Index;
            Barracks = Building(Sim, 10, 18, type: ProductionMaps.Barracks).Index;
            HolyCamp = Building(Sim, 25, 20, player: 1, type: ProductionMaps.HolyCamp).Index;
            Smithy = Building(Sim, 32, 4, player: 1, type: ResearchMaps.Smithy).Index;
            SetTotals(Sim, 0, Start, Start);
            SetTotals(Sim, 1, Start, Start);
        }

        public Simulation Sim { get; }
        public World W => Sim.World;
        public int Keep { get; }
        public int Armory { get; }
        public int Barracks { get; }
        public int HolyCamp { get; }
        public int Smithy { get; }
        public Vector2 At(int k) => In(Sim, k);
        public (int, int) Totals(int p = 0) => (W.Gold[p], W.Wood[p]);
    }

    // ---------- criterion 3: Age II at the Town Hall ----------

    [Fact]
    public void AgeII_PaysAtQueueTime_CompletesAfterExactly1200Ticks_SpawnsNothing_TakesNoPop_AndCantBeQueuedTwice()
    {
        var s = new Scene();
        World w = s.W;
        int units = w.Units.Count, halfPop = w.HalfPop[0];
        Assert.Equal(1, w.Age(0));
        Assert.True(w.CanResearch(0, s.Keep, AgeII, out _));
        // Two in the same tick: the second finds the first queued.
        s.Sim.Enqueue(Command.Research(0, s.At(s.Keep), AgeII));
        s.Sim.Enqueue(Command.Research(0, s.At(s.Keep), AgeII));
        s.Sim.Tick();
        Assert.Equal((Start, Start), s.Totals());
        s.Sim.Tick(); // applies: paid, and research starts this tick
        Assert.Equal((Start - 400, Start - 200), s.Totals());
        BuildingStore b = w.Buildings;
        Assert.Equal((1, 1, true, AgeII), (b.QueueCount[s.Keep], b.Progress[s.Keep], b.QueueIsTechAt(s.Keep, 0), b.QueueTypeAt(s.Keep, 0)));
        Assert.Equal(1200, b.ItemTicks(s.Keep, 0));
        Assert.Equal(0, b.ReservedHalfPop(s.Keep));
        Assert.False(w.CanResearch(0, s.Keep, AgeII, out ResearchError why));
        Assert.Equal(ResearchError.AlreadyQueued, why);
        // And once more while it runs: dropped, totals unchanged.
        s.Sim.Enqueue(Command.Research(0, s.At(s.Keep), AgeII));
        for (int t = 2; t < 1200; t++)
        {
            s.Sim.Tick();
            Assert.Equal(halfPop, w.HalfPop[0]);
            Assert.False(w.HasTech(0, AgeII));
        }
        Assert.Equal(1199, b.Progress[s.Keep]);
        Assert.Equal((1, 1), (w.Age(0), b.QueueCount[s.Keep]));
        s.Sim.Tick(); // the 1,200th tick counting the one it applied in
        Assert.True(w.HasTech(0, AgeII));
        Assert.Equal(2, w.Age(0));
        Assert.Equal((0, 0), (b.QueueCount[s.Keep], b.Progress[s.Keep]));
        Assert.False(b.QueueIsTechEntry(s.Keep, 0)); // the freed entry is default again
        Assert.Equal(units, w.Units.Count);
        Assert.Equal(halfPop, w.HalfPop[0]);
        Assert.Equal((Start - 400, Start - 200), s.Totals());
        Assert.False(w.HasTech(1, AgeII));
        Assert.Equal(1, w.Age(1));

        Assert.False(w.CanResearch(0, s.Keep, AgeII, out why));
        Assert.Equal(ResearchError.AlreadyResearched, why);
        Apply(s.Sim, Command.Research(0, s.At(s.Keep), AgeII));
        Assert.Equal(0, b.QueueCount[s.Keep]);
        Assert.Equal((Start - 400, Start - 200), s.Totals());
    }

    // ---------- criterion 4: Forge upgrades and the bonus query ----------

    [Fact]
    public void MeleeWeapons1_AtTheArmory_CostsAndTakesItsTicks_AndAddsOneAttackToMeleeUnitsOnly()
    {
        var s = new Scene();
        World w = s.W;
        int crossbow = w.Data.FindUnit("malazan_crossbowman");
        Assert.Equal(0f, w.TechBonus(0, Infantry, TechStat.Attack));
        Apply(s.Sim, Command.Research(0, s.At(s.Armory), Melee1));
        Assert.Equal((Start - 100, Start - 50), s.Totals());
        Assert.Equal(600, w.Buildings.ItemTicks(s.Armory, 0));
        Run(s.Sim, 598);
        Assert.Equal(0f, w.TechBonus(0, Infantry, TechStat.Attack));
        s.Sim.Tick();
        Assert.True(w.HasTech(0, Melee1));
        Assert.Equal(1f, w.TechBonus(0, Infantry, TechStat.Attack));
        Assert.Equal(0f, w.TechBonus(0, crossbow, TechStat.Attack));
        Assert.Equal(0f, w.TechBonus(0, Infantry, TechStat.Armor));
        Assert.Equal(0f, w.TechBonus(1, w.Data.FindUnit("whirlwind_raider"), TechStat.Attack)); // the other player's
        // Every melee-attack unit type gets it (workers included: docs/02 says "melee units"), nothing else.
        int melee = w.Data.DamageTable.DamageTypeKeys.IndexOf("melee");
        foreach (UnitDef u in w.Data.Units)
            Assert.Equal(u.Attack.DamageType == melee ? 1f : 0f, w.TechBonus(0, u.Id, TechStat.Attack));
    }

    [Fact]
    public void BothMeleeLevels_SumToTwo()
    {
        var s = new Scene();
        s.Sim.Enqueue(Command.Research(0, s.At(s.Armory), Melee1));
        s.Sim.Enqueue(Command.Research(0, s.At(s.Armory), Melee2)); // requires isn't gated until M3-6
        Run(s.Sim, 2 + 600 + 800);
        Assert.True(s.W.HasTech(0, Melee1) && s.W.HasTech(0, Melee2));
        Assert.Equal(2f, s.W.TechBonus(0, Infantry, TechStat.Attack));
    }

    [Fact]
    public void Armor1_GivesEveryNonSiegeUnitOneArmor_AndTheCatapultNothing()
    {
        var s = new Scene();
        Apply(s.Sim, Command.Research(0, s.At(s.Armory), Armor1));
        Run(s.Sim, 700);
        World w = s.W;
        Assert.True(w.HasTech(0, Armor1));
        Assert.Equal(0f, w.TechBonus(0, w.Data.FindUnit("malazan_catapult"), TechStat.Armor));
        Assert.Equal(1f, w.TechBonus(0, Sapper, TechStat.Armor)); // a siege attack, but not a siege engine
        foreach (UnitDef u in w.Data.Units)
            Assert.Equal(u.Slot == UnitSlot.Siege ? 0f : 1f, w.TechBonus(0, u.Id, TechStat.Armor));
    }

    [Fact]
    public void FactionUpgrades_ChangeTheNamedUnitsOnly()
    {
        var s = new Scene();
        World w = s.W;
        s.Sim.Enqueue(Command.Research(0, s.At(s.Armory), Moranth));
        s.Sim.Enqueue(Command.Research(1, s.At(s.Smithy), Dryjhna));
        Run(s.Sim, 2 + 900);
        Assert.True(w.HasTech(0, Moranth) && w.HasTech(1, Dryjhna));
        int catapult = w.Data.FindUnit("malazan_catapult"), priest = w.Data.FindUnit("whirlwind_priest");
        foreach (UnitDef u in w.Data.Units)
        {
            foreach (TechStat stat in Enum.GetValues<TechStat>())
            {
                float p0 = (u.Id, stat) == (Sapper, TechStat.AbilityCooldown) ? -300f : (u.Id, stat) == (catapult, TechStat.Range) ? 4f : 0f;
                float p1 = (u.Id, stat) == (Zealot, TechStat.Hp) ? 20f : (u.Id, stat) == (priest, TechStat.AbilityCooldown) ? -300f : 0f;
                Assert.True(p0 == w.TechBonus(0, u.Id, stat), $"player 0 {u.Key} {stat}");
                Assert.True(p1 == w.TechBonus(1, u.Id, stat), $"player 1 {u.Key} {stat}");
            }
        }
    }

    [Fact]
    public void TechBonus_IsZeroForOutOfRangeIds()
    {
        var s = new Scene();
        World w = s.W;
        Assert.Equal(0f, w.TechBonus(-1, Infantry, TechStat.Attack));
        Assert.Equal(0f, w.TechBonus(2, Infantry, TechStat.Attack));
        Assert.Equal(0f, w.TechBonus(0, -1, TechStat.Attack));
        Assert.Equal(0f, w.TechBonus(0, w.Data.Units.Length, TechStat.Attack));
        Assert.Equal(0f, w.TechBonus(0, Infantry, (TechStat)99));
        Assert.False(w.HasTech(0, -1));
        Assert.False(w.HasTech(0, w.Data.Techs.Length));
        Assert.False(w.HasTech(5, AgeII));
        Assert.Equal(0, w.Age(5));
    }

    // ---------- criterion 5: a mixed queue ----------

    [Fact]
    public void ABarracksCantResearch_AndAForgeCantTrain()
    {
        var s = new Scene();
        World w = s.W;
        Assert.False(w.CanResearch(0, s.Barracks, Melee1, out ResearchError r));
        Assert.Equal(ResearchError.NotResearchedHere, r);
        Assert.False(w.CanResearch(0, s.Keep, Melee1, out r));
        Assert.Equal(ResearchError.NotResearchedHere, r);
        Assert.False(w.CanResearch(0, s.Armory, AgeII, out r));
        Assert.Equal(ResearchError.NotResearchedHere, r);
        Assert.False(w.CanTrain(0, s.Armory, Laborer, out TrainError t));
        Assert.Equal(TrainError.NotTrainedHere, t);
        s.Sim.Enqueue(Command.Research(0, s.At(s.Barracks), Melee1));
        s.Sim.Enqueue(Command.Train(0, s.At(s.Armory), Infantry));
        Run(s.Sim, 2);
        Assert.Equal((0, 0), (w.Buildings.QueueCount[s.Barracks], w.Buildings.QueueCount[s.Armory]));
        Assert.Equal((Start, Start), s.Totals());
    }

    [Fact]
    public void ATownHallQueue_LaborerAgeIILaborer_RunsInOrder()
    {
        var s = new Scene();
        World w = s.W;
        BuildingStore b = w.Buildings;
        int units = w.Units.Count, laborer = w.Data.Units[Laborer].TrainTicks;
        s.Sim.Enqueue(Command.Train(0, s.At(s.Keep), Laborer));
        s.Sim.Enqueue(Command.Research(0, s.At(s.Keep), AgeII));
        s.Sim.Enqueue(Command.Train(0, s.At(s.Keep), Laborer));
        Run(s.Sim, 2);
        Assert.Equal((Start - 400 - 100, Start - 200), s.Totals());
        Assert.Equal(new[] { false, true, false }, new[] { b.QueueIsTechAt(s.Keep, 0), b.QueueIsTechAt(s.Keep, 1), b.QueueIsTechAt(s.Keep, 2) });
        Assert.Equal(new[] { laborer, 1200, laborer }, new[] { b.ItemTicks(s.Keep, 0), b.ItemTicks(s.Keep, 1), b.ItemTicks(s.Keep, 2) });
        Run(s.Sim, laborer - 1);
        Assert.Equal(units + 1, w.Units.Count); // the first Laborer
        Assert.Equal((2, true, 1), (b.QueueCount[s.Keep], b.QueueIsTechAt(s.Keep, 0), b.Progress[s.Keep])); // Age II started the same tick
        Run(s.Sim, 1199);
        Assert.True(w.HasTech(0, AgeII));
        Assert.Equal(units + 1, w.Units.Count);
        Assert.Equal((1, false, 1), (b.QueueCount[s.Keep], b.QueueIsTechAt(s.Keep, 0), b.Progress[s.Keep])); // the second Laborer started
        Assert.Equal(RecountHalfPop(w, 0), w.HalfPop[0]);
        Run(s.Sim, laborer - 1);
        Assert.Equal(units + 2, w.Units.Count);
        Assert.Equal(0, b.QueueCount[s.Keep]);
    }

    [Fact]
    public void CancellingTheQueuedTech_RefundsInFull_AndShiftsTheRestDown()
    {
        var s = new Scene();
        World w = s.W;
        BuildingStore b = w.Buildings;
        s.Sim.Enqueue(Command.Train(0, s.At(s.Keep), Laborer));
        s.Sim.Enqueue(Command.Research(0, s.At(s.Keep), AgeII));
        s.Sim.Enqueue(Command.Train(0, s.At(s.Keep), Laborer));
        Run(s.Sim, 12);
        int progress = b.Progress[s.Keep];
        (int gold, int wood) = s.Totals();
        Apply(s.Sim, Command.CancelTrain(0, s.At(s.Keep), 1));
        Assert.Equal((gold + 400, wood + 200), s.Totals());
        Assert.Equal(2, b.QueueCount[s.Keep]);
        Assert.Equal((Laborer, Laborer), (b.QueueTypeAt(s.Keep, 0), b.QueueTypeAt(s.Keep, 1)));
        Assert.False(b.QueueIsTechAt(s.Keep, 1));
        Assert.False(b.QueueIsTechEntry(s.Keep, 2));
        Assert.Equal(progress + 2, b.Progress[s.Keep]); // the head kept training
        Assert.True(w.CanResearch(0, s.Keep, AgeII, out _)); // no longer queued anywhere
    }

    [Fact]
    public void CancellingAgeIIInProgress_RefundsInFull_AndTheNextItemStarts()
    {
        var s = new Scene();
        World w = s.W;
        BuildingStore b = w.Buildings;
        s.Sim.Enqueue(Command.Research(0, s.At(s.Keep), AgeII));
        s.Sim.Enqueue(Command.Train(0, s.At(s.Keep), Laborer));
        Run(s.Sim, 300);
        int halfPop = w.HalfPop[0];
        Apply(s.Sim, Command.CancelTrain(0, s.At(s.Keep), 0));
        Assert.Equal((Start - 50, Start), s.Totals());
        Assert.False(w.HasTech(0, AgeII));
        Assert.Equal((1, false), (b.QueueCount[s.Keep], b.QueueIsTechAt(s.Keep, 0)));
        Assert.Equal(halfPop + w.Data.Units[Laborer].HalfPop, w.HalfPop[0]); // the Laborer started and reserved its pop
        Assert.Equal(RecountHalfPop(w, 0), w.HalfPop[0]);
    }

    [Fact]
    public void ATownHallDestroyedWithAgeIIHalfDone_RefundsInFull_AndTheTechIsNeverResearched()
    {
        var s = new Scene();
        World w = s.W;
        Apply(s.Sim, Command.Research(0, s.At(s.Keep), AgeII));
        Run(s.Sim, 599);
        Assert.Equal(600, w.Buildings.Progress[s.Keep]);
        Assert.Equal((Start - 400, Start - 200), s.Totals());
        w.Buildings.Damage(w.Buildings.HandleOf(s.Keep), 1_000_000);
        Assert.Equal((Start, Start), s.Totals());
        Run(s.Sim, 1300);
        Assert.False(w.HasTech(0, AgeII));
        Assert.Equal(1, w.Age(0));
    }

    // ---------- criterion 6: dropped ----------

    private static void AssertDropped(Scene s, Command c, int building, ResearchError expected, int player = 0)
    {
        World w = s.W;
        if (building >= 0)
        {
            Assert.False(w.CanResearch(player, building, c.TypeId, out ResearchError why));
            Assert.Equal(expected, why);
        }
        var queues = new int[w.Buildings.Capacity];
        for (int k = 0; k < queues.Length; k++) queues[k] = w.Buildings.QueueCount[k];
        (int, int) t0 = s.Totals(0), t1 = s.Totals(1);
        Apply(s.Sim, c);
        for (int k = 0; k < queues.Length; k++) Assert.Equal(queues[k], w.Buildings.QueueCount[k]);
        Assert.Equal(t0, s.Totals(0));
        Assert.Equal(t1, s.Totals(1));
    }

    [Fact]
    public void ResearchByTheWrongPlayer_AtAnEnemyBuilding_IsDropped()
    {
        var s = new Scene();
        AssertDropped(s, Command.Research(1, s.At(s.Keep), AgeII), s.Keep, ResearchError.NoBuilding, player: 1);
        AssertDropped(s, Command.Research(0, s.At(s.HolyCamp), AgeII), s.HolyCamp, ResearchError.NoBuilding);
        AssertDropped(s, Command.Research(0, s.At(s.Smithy), Melee1), s.Smithy, ResearchError.NoBuilding);
    }

    [Fact]
    public void ResearchAtASite_IsDropped()
    {
        var s = new Scene();
        EntityHandle worker = Unit(s.Sim, At(s.Sim, 3, 26));
        Apply(s.Sim, Command.Build(0, worker, Keep, At(s.Sim, 2, 2)));
        int site = SiteAt(s.Sim, 2, 2);
        Assert.True(site >= 0 && s.W.Buildings.UnderConstruction[site]);
        AssertDropped(s, Command.Research(0, At(s.Sim, 3, 3), AgeII), site, ResearchError.NoBuilding);
    }

    [Fact]
    public void ResearchOfAnUnknownTech_OrAnotherFactionsUpgrade_IsDropped()
    {
        var s = new Scene();
        AssertDropped(s, Command.Research(0, s.At(s.Armory), -1), s.Armory, ResearchError.UnknownTech);
        AssertDropped(s, Command.Research(0, s.At(s.Armory), s.W.Data.Techs.Length), s.Armory, ResearchError.UnknownTech);
        AssertDropped(s, Command.Research(0, s.At(s.Armory), Dryjhna), s.Armory, ResearchError.WrongFaction);
        AssertDropped(s, Command.Research(1, s.At(s.Smithy), Moranth), s.Smithy, ResearchError.WrongFaction, player: 1);
        AssertDropped(s, Command.Research(0, At(s.Sim, 1, 1), AgeII), -1, ResearchError.NoBuilding); // no building there
    }

    [Fact]
    public void ResearchWithAFullQueue_IsDropped()
    {
        var s = new Scene();
        for (int n = 0; n < 5; n++) s.Sim.Enqueue(Command.Train(0, s.At(s.Keep), Laborer));
        Run(s.Sim, 2);
        Assert.Equal(5, s.W.Buildings.QueueCount[s.Keep]);
        AssertDropped(s, Command.Research(0, s.At(s.Keep), AgeII), s.Keep, ResearchError.QueueFull);
    }

    [Theory]
    [InlineData(399, 200)]
    [InlineData(400, 199)]
    public void ResearchThePlayerCantAfford_IsDropped(int gold, int wood)
    {
        var s = new Scene();
        SetTotals(s.Sim, 0, gold, wood);
        AssertDropped(s, Command.Research(0, s.At(s.Keep), AgeII), s.Keep, ResearchError.CannotAfford);
    }

    [Fact]
    public void AnAlreadyQueuedTech_IsDroppedAtAnotherBuildingToo()
    {
        var s = new Scene();
        int second = Building(s.Sim, 16, 24, type: ResearchMaps.Armory).Index;
        Apply(s.Sim, Command.Research(0, s.At(s.Armory), Melee1));
        AssertDropped(s, Command.Research(0, s.At(second), Melee1), second, ResearchError.AlreadyQueued);
        Assert.True(s.W.CanResearch(0, second, Armor1, out _));
    }

    [Fact]
    public void Research_IsNotAUnitOrder_TheQueuedFlagIsRefused()
    {
        var s = new Scene();
        Command c = Command.Research(0, s.At(s.Keep), AgeII);
        Assert.True(c.IsWellFormed());
        Assert.False(c.IsUnitOrder);
        c.Flags = Command.QueuedFlag;
        Assert.False(c.IsWellFormed());
        Assert.Throws<ArgumentException>(() => s.Sim.Enqueue(c));
        Command nan = Command.Research(0, new Vector2(float.NaN, 1f), AgeII);
        Assert.False(nan.IsValid());
        AssertDropped(s, nan, -1, ResearchError.None);
    }

    [Fact]
    public void AReplayWithResearchAndCancel_ReadsBack_AndReplaysEveryCheckpoint()
    {
        var sim = new Simulation(TestSim.Config(Seed: 21, PlayerCount: 2, UnitCapacity: 64, CommandCapacity: 64) with { Map = new Map.MapGenParams { GoldMines = 4 } });
        var rec = new ReplayRecorder(sim, checkpointInterval: 50);
        World w = sim.World;
        Map.NavGrid g = w.NavGrid;
        // Starting totals (200 / 200) only: the replay can't carry a test seam's gift. The dev spawn doesn't charge.
        int c = 0;
        while (!w.CanPlace(0, ResearchMaps.Armory, c, out PlacementError why) && why != PlacementError.CannotAfford) c += 37;
        sim.Enqueue(Command.SpawnBuilding(0, ResearchMaps.Armory, g.CellCenter(c % g.Width, c / g.Width)));
        Run(sim, 2);
        Vector2 at = g.CellCenter(c % g.Width + 1, c / g.Width + 1);
        sim.Enqueue(Command.Research(0, at, Armor1));
        sim.Enqueue(Command.Research(0, at, Melee1)); // 225 gold: can't afford the second, dropped
        Run(sim, 100);
        sim.Enqueue(Command.CancelTrain(0, at, 0));
        sim.Enqueue(Command.Research(0, at, Melee1));
        Run(sim, 700);
        Assert.False(w.HasTech(0, Armor1));
        Assert.True(w.HasTech(0, Melee1));
        Replay replay = rec.ToReplay();
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(replay), out Replay? back));
        Assert.Contains(back!.Commands, x => x.Kind == CommandKind.Research);
        ReplayResult result = ReplayPlayer.Run(back, TestSim.Data);
        Assert.True(result.Ok, $"{result.Error} at tick {result.Tick}");
    }
}
