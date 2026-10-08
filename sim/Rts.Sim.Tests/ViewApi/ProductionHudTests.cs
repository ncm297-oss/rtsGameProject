using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M3-V3: the pure helpers behind the selection panel, production card, queue strip, rally marker and population text.</summary>
[Collection(SerialCollection.Name)]
public class ProductionHudTests
{
    private readonly ITestOutputHelper _out;

    public ProductionHudTests(ITestOutputHelper output) => _out = output;

    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "0.5")]
    [InlineData(10, "5")]
    [InlineData(11, "5.5")]
    [InlineData(20, "10")]
    [InlineData(200, "100")]
    [InlineData(-3, "-1.5")]
    [InlineData(-4, "-2")]
    [InlineData(int.MaxValue, "1073741823.5")]
    [InlineData(int.MinValue, "-1073741824")]
    public void PopText_IsHalfPopOverTwo_WithPointFiveForOdd(int half, string want) => Assert.Equal(want, PopText.Format(half));

    [Fact]
    public void PopText_AtCap_WhenPopReachesOrPassesTheCap()
    {
        Assert.False(PopText.AtCap(10, 20));
        Assert.False(PopText.AtCap(19, 20));
        Assert.True(PopText.AtCap(20, 20));
        Assert.True(PopText.AtCap(21, 20));
        Assert.True(PopText.AtCap(0, 0)); // no hall: nothing fits
    }

    [Fact]
    public void QueueStrip_Layout_ItemAtInvertsItemX_GapsAndOutsideAreMinusOne()
    {
        const float size = 64f, gap = 4f;
        Assert.Equal(0f, QueueStrip.Width(0, size, gap));
        Assert.Equal(64f, QueueStrip.Width(1, size, gap));
        Assert.Equal(5 * 64f + 4 * 4f, QueueStrip.Width(5, size, gap));
        for (int count = 0; count <= QueueStrip.MaxItems; count++)
            for (int k = 0; k < QueueStrip.MaxItems; k++)
            {
                float x = QueueStrip.ItemX(k, size, gap);
                Assert.Equal(k * 68f, x);
                int want = k < count ? k : -1;
                Assert.Equal(want, QueueStrip.ItemAt(x, count, size, gap));
                Assert.Equal(want, QueueStrip.ItemAt(x + size - 0.01f, count, size, gap));
                Assert.Equal(-1, QueueStrip.ItemAt(x + size + 1f, count, size, gap)); // the gap
            }
        Assert.Equal(-1, QueueStrip.ItemAt(-1f, 5, size, gap));
        Assert.Equal(-1, QueueStrip.ItemAt(float.NaN, 5, size, gap));
        Assert.Equal(-1, QueueStrip.ItemAt(5 * 68f, 9, size, gap)); // never past the queue's capacity
        Assert.Equal(-1, QueueStrip.ItemAt(10f, 5, 0f, gap));
    }

    [Fact]
    public void QueueStrip_CountAndHeadFill_FollowTheQueueThroughLaborerAgeIILaborer()
    {
        (Simulation sim, int hall) = Hall(1, ageIIHalls: true); // BUG-0124: Age II needs two finished halls of distinct slots
        World w = sim.World;
        BuildingStore b = w.Buildings;
        int laborer = w.Data.UnitsTrainedAt(b.TypeId[hall])[0];
        int age = ResearchMaps.AgeII;
        Vector2 at = ProductionMaps.In(sim, hall);
        Assert.True(w.CanTrain(0, hall, laborer, out Economy.TrainError why), $"laborer: {why}");
        sim.Enqueue(Command.Train(0, at, laborer));
        sim.Enqueue(Command.Research(0, at, age));
        sim.Enqueue(Command.Train(0, at, laborer));
        sim.Tick(); // commands enqueued between ticks apply at the start of the tick after next
        sim.Tick();
        Assert.Equal(3, b.QueueCount[hall]);
        int saw3 = 0, sawTechHead = 0, ticks = 0;
        float lastFill = -1f;
        while (ticks++ < 3000)
        {
            sim.Tick();
            int n = QueueStrip.Count(b, hall);
            Assert.Equal(b.QueueCount[hall], n);
            float fill = QueueStrip.HeadFill(b, hall);
            float want = n == 0 || b.ItemTicks(hall, 0) == 0 ? 0f : Math.Clamp(b.Progress[hall] / (float)b.ItemTicks(hall, 0), 0f, 1f);
            Assert.Equal(want, fill);
            Assert.InRange(fill, 0f, 1f);
            if (n == 3) saw3++;
            if (n > 0 && b.QueueIsTechAt(hall, 0)) sawTechHead++;
            lastFill = fill;
            if (n == 0) break;
        }
        Assert.True(saw3 > 0 && sawTechHead > 0, $"saw 3 items {saw3} ticks, tech head {sawTechHead} ticks");
        Assert.Equal(2, w.Age(0));
        Assert.Equal(0, QueueStrip.Count(b, -1));
        Assert.Equal(0, QueueStrip.Count(b, b.Capacity));
        Assert.Equal(0f, QueueStrip.HeadFill(b, b.Capacity + 3));
        _out.WriteLine($"queue emptied after {ticks} ticks; last fill {lastFill}");
    }

    [Fact]
    public void RallyGeometry_EdgePoint_LeavesTheFootprintTowardTheTarget()
    {
        var min = new Vector2(10f, 20f);
        var max = new Vector2(18f, 24f); // 8 x 4 m, centre (14, 22)
        Assert.Equal(new Vector2(18f, 22f), RallyGeometry.EdgePoint(min, max, new Vector2(30f, 22f))); // east
        Assert.Equal(new Vector2(10f, 22f), RallyGeometry.EdgePoint(min, max, new Vector2(0f, 22f))); // west
        Assert.Equal(new Vector2(14f, 24f), RallyGeometry.EdgePoint(min, max, new Vector2(14f, 40f))); // south (+y)
        Assert.Equal(new Vector2(14f, 20f), RallyGeometry.EdgePoint(min, max, new Vector2(14f, 0f))); // north
        Assert.Equal(new Vector2(18f, 24f), RallyGeometry.EdgePoint(min, max, new Vector2(22f, 26f))); // through the corner
        Vector2 inside = new(12f, 21f);
        Assert.Equal(inside, RallyGeometry.EdgePoint(min, max, inside));
        Assert.Equal(new Vector2(14f, 22f), RallyGeometry.EdgePoint(min, max, new Vector2(float.NaN, 1f)));
        // Every far target on a circle: the edge point is on the rectangle's boundary and on the centre-target segment.
        for (int a = 0; a < 360; a += 7)
        {
            float r = a * MathF.PI / 180f;
            var t = new Vector2(14f + 50f * MathF.Cos(r), 22f + 50f * MathF.Sin(r));
            Vector2 e = RallyGeometry.EdgePoint(min, max, t);
            bool onX = MathF.Abs(e.X - min.X) < 1e-3f || MathF.Abs(e.X - max.X) < 1e-3f;
            bool onY = MathF.Abs(e.Y - min.Y) < 1e-3f || MathF.Abs(e.Y - max.Y) < 1e-3f;
            Assert.True(onX || onY, $"angle {a}: {e} not on the edge");
            Assert.InRange(e.X, min.X - 1e-3f, max.X + 1e-3f);
            Assert.InRange(e.Y, min.Y - 1e-3f, max.Y + 1e-3f);
            Vector2 d = Vector2.Normalize(t - new Vector2(14f, 22f)), de = Vector2.Normalize(e - new Vector2(14f, 22f));
            Assert.True(Vector2.Dot(d, de) > 0.9999f, $"angle {a}: edge point off the line");
            float len = RallyGeometry.Line(min, max, t, out Vector2 from);
            Assert.Equal(e, from);
            Assert.Equal(Vector2.Distance(e, t), len, 4);
        }
        Assert.Equal(0f, RallyGeometry.Line(min, max, inside, out _));
        Assert.Equal(0f, RallyGeometry.Line(min, max, new Vector2(float.PositiveInfinity, 0f), out _));
    }

    [Fact]
    public void ProductionMenu_TownHall_Barracks_Armory_ListUnitsThenCommonThenFactionTechs()
    {
        GameData data = TestSim.Data;
        var into = new ProductionEntry[15];
        int hall = data.FindBuilding("malazan_garrison_keep");
        int n = ProductionMenu.Entries(data, hall, into);
        Assert.Equal(new[] { new ProductionEntry(data.FindUnit("malazan_laborer"), false), new ProductionEntry(ResearchMaps.AgeII, true) }, into.Take(n));

        n = ProductionMenu.Entries(data, ProductionMaps.Barracks, into);
        Assert.Equal(data.UnitsTrainedAt(ProductionMaps.Barracks).Select(u => new ProductionEntry(u, false)), into.Take(n));

        n = ProductionMenu.Entries(data, ResearchMaps.Armory, into);
        string[] keys = into.Take(n).Select(e => { Assert.True(e.IsTech); return data.Techs[e.TypeId].Key; }).ToArray();
        _out.WriteLine($"armory: {string.Join(", ", keys)}");
        Assert.Equal(7, n);
        Assert.Equal("moranth_supply", keys[^1]);
        Assert.All(keys.Take(6), k => Assert.Equal(-1, data.Techs[data.FindTech(k)].Faction));
        Assert.Equal(data.TechsResearchableAt(ResearchMaps.Armory).Where(t => data.Techs[t].Faction < 0), into.Take(6).Select(e => e.TypeId));
        n = ProductionMenu.Entries(data, ResearchMaps.Smithy, into);
        Assert.Equal("dryjhnas_prophecy", data.Techs[into[n - 1].TypeId].Key);

        // Every building of both factions: units first, then techs; nothing dropped; a short span truncates in order.
        for (int t = 0; t < data.Buildings.Length; t++)
        {
            n = ProductionMenu.Entries(data, t, into);
            Assert.Equal(data.UnitsTrainedAt(t).Length + data.TechsResearchableAt(t).Length, n);
            for (int k = 1; k < n; k++) Assert.False(into[k - 1].IsTech && !into[k].IsTech, $"{data.Buildings[t].Key}: a unit after a tech");
        }
        var two = new ProductionEntry[1];
        Assert.Equal(1, ProductionMenu.Entries(data, hall, two));
        Assert.False(two[0].IsTech);
        Assert.Equal(0, ProductionMenu.Entries(data, -1, into));
        Assert.Equal(0, ProductionMenu.Entries(data, data.Buildings.Length, into));
    }

    [Fact]
    public void ProductionMenu_RequirementName_IsTheTechsOrBuildingsDisplayName()
    {
        GameData data = TestSim.Data;
        Assert.Equal(data.Techs[ResearchMaps.AgeII].DisplayName, ProductionMenu.RequirementName(data, "age_ii"));
        Assert.Equal(data.Buildings[ProductionMaps.Barracks].DisplayName, ProductionMenu.RequirementName(data, "malazan_barracks"));
        Assert.Equal("no_such_id", ProductionMenu.RequirementName(data, "no_such_id"));
        // Every shipped requires entry resolves to a display name, never its id.
        foreach (UnitDef u in data.Units)
            foreach (string r in u.Requires) Assert.NotEqual(r, ProductionMenu.RequirementName(data, r));
        foreach (TechDef t in data.Techs)
            foreach (string r in t.Requires) Assert.NotEqual(r, ProductionMenu.RequirementName(data, r));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 1, 0)]
    [InlineData(24, 24, 0)]
    [InlineData(25, 24, 1)]
    [InlineData(200, 24, 176)]
    [InlineData(-5, 0, 0)]
    public void PortraitGrid_ShowsUpTo24_RestIsOverflow(int count, int shown, int overflow)
    {
        Assert.Equal(shown, PortraitGrid.Shown(count));
        Assert.Equal(overflow, PortraitGrid.Overflow(count));
    }

    [Fact]
    public void QueueStrip_RallyGeometry_ProductionMenu_PortraitGrid_AllocateZeroBytes()
    {
        (Simulation sim, int hall) = Hall(6);
        World w = sim.World;
        sim.Enqueue(Command.Train(0, ProductionMaps.In(sim, hall), w.Data.UnitsTrainedAt(w.Buildings.TypeId[hall])[0]));
        sim.Tick();
        sim.Tick();
        var into = new ProductionEntry[15];
        float sum = 0f;
        Action block = () =>
        {
            for (int k = 0; k < w.Buildings.Capacity; k++)
            {
                sum += QueueStrip.Count(w.Buildings, k) + QueueStrip.HeadFill(w.Buildings, k);
                sum += ProductionMenu.Entries(w.Data, w.Buildings.TypeId[k], into);
            }
            sum += QueueStrip.ItemAt(130f, 5, 64f, 4f) + QueueStrip.ItemX(3, 64f, 4f) + QueueStrip.Width(4, 64f, 4f);
            sum += RallyGeometry.Line(new Vector2(0f, 0f), new Vector2(8f, 8f), new Vector2(30f, 12f), out Vector2 from) + from.X;
            sum += PortraitGrid.Shown(40) + PortraitGrid.Overflow(40) + (PopText.AtCap(11, 20) ? 1 : 0);
        };
        block();
        int runs = AllocationProbe.AssertZero(block, _out);
        _out.WriteLine($"M3-V3 helpers: 0 bytes (runs {runs}), checksum {sum}");
    }

    // BUG-0124: a bare Town Hall (no Barracks, Range, Corral or Armory) can't research Age II: CanResearch answers Requires
    // (the card's "Locked"), and a Research command for it enqueues nothing.
    [Fact]
    public void AgeII_AtABareTownHall_IsRequires_AndAResearchQueuesNothing()
    {
        (Simulation sim, int hall) = Hall(2);
        World w = sim.World;
        BuildingStore b = w.Buildings;
        Assert.False(w.CanResearch(0, hall, ResearchMaps.AgeII, out Economy.ResearchError why));
        Assert.Equal(Economy.ResearchError.Requires, why);
        sim.Enqueue(Command.Research(0, ProductionMaps.In(sim, hall), ResearchMaps.AgeII));
        sim.Tick();
        sim.Tick();
        Assert.Equal(0, b.QueueCount[hall]);
        // The same fixture with the two halls spawned opens it.
        (Simulation open, int hall2) = Hall(2, ageIIHalls: true);
        Assert.True(open.World.CanResearch(0, hall2, ResearchMaps.AgeII, out why), $"with two halls: {why}");
    }

    // M3-V4 (BUG-0126 item 2): the research button's words. Researched beats queued beats the sim's own first reason,
    // for every reason the sim can give.
    [Fact]
    public void ShownResearchReason_ResearchedThenQueuedThenTheSimsReason()
    {
        foreach (Economy.ResearchError e in Enum.GetValues<Economy.ResearchError>())
        {
            Assert.Equal(e, ProductionMenu.ShownResearchReason(e, researched: false, queued: false));
            Assert.Equal(Economy.ResearchError.AlreadyQueued, ProductionMenu.ShownResearchReason(e, researched: false, queued: true));
            Assert.Equal(Economy.ResearchError.AlreadyResearched, ProductionMenu.ShownResearchReason(e, researched: true, queued: false));
            Assert.Equal(Economy.ResearchError.AlreadyResearched, ProductionMenu.ShownResearchReason(e, researched: true, queued: true));
        }
    }

    // M3-V4 (BUG-0126 item 2), shipped data: Age II queued at a hall with two distinct halls standing, then the Armory
    // destroyed: the sim answers Requires ("Locked") for the queued item, the button reads AlreadyQueued ("In a queue");
    // once researched with one hall slot left it reads AlreadyResearched ("Researched"). IsTechQueued equals the store's
    // own rule every tick for every tech and both players.
    [Fact]
    public void AgeII_QueuedThenAHallLost_ReadsInAQueue_ThenResearched()
    {
        (Simulation sim, int hall) = Hall(1);
        World w = sim.World;
        BuildingStore b = w.Buildings;
        int age = ResearchMaps.AgeII;
        SpawnNear(sim, hall, BuildingSlot.InfantryHall);
        int forge = SpawnNear(sim, hall, BuildingSlot.Forge);
        Assert.True(w.CanResearch(0, hall, age, out Economy.ResearchError why), $"two halls: {why}");
        Assert.False(ProductionMenu.IsTechQueued(b, 0, age));
        sim.Enqueue(Command.Research(0, ProductionMaps.In(sim, hall), age));
        sim.Tick();
        sim.Tick();
        Assert.True(ProductionMenu.IsTechQueued(b, 0, age));
        Assert.False(ProductionMenu.IsTechQueued(b, 1, age));
        w.CanResearch(0, hall, age, out why);
        Assert.Equal(Economy.ResearchError.AlreadyQueued, why);
        b.Damage(b.HandleOf(forge), 1_000_000);
        sim.Tick();
        Assert.False(b.Alive[forge]);
        int queuedLocked = 0, researchedLocked = 0, ticks = 0;
        while (ticks++ < 2000 && w.Age(0) < 2)
        {
            w.CanResearch(0, hall, age, out why);
            bool queued = ProductionMenu.IsTechQueued(b, 0, age);
            Assert.Equal(Economy.ResearchError.Requires, why); // M3-6's order: requires before queued / researched
            Assert.True(queued, $"tick {sim.TickNumber}: Age II left the queue before it completed");
            Assert.Equal(Economy.ResearchError.AlreadyQueued, ProductionMenu.ShownResearchReason(why, w.HasTech(0, age), queued));
            queuedLocked++;
            for (int t = 0; t < w.Data.Techs.Length; t++)
                for (int p = 0; p < 2; p++)
                    Assert.Equal(b.IsTechQueued(p, t), ProductionMenu.IsTechQueued(b, p, t));
            sim.Tick();
        }
        Assert.Equal(2, w.Age(0));
        for (int k = 0; k < 20; k++)
        {
            w.CanResearch(0, hall, age, out why);
            Assert.Equal(Economy.ResearchError.Requires, why);
            Assert.Equal(Economy.ResearchError.AlreadyResearched, ProductionMenu.ShownResearchReason(why, w.HasTech(0, age), ProductionMenu.IsTechQueued(b, 0, age)));
            researchedLocked++;
            sim.Tick();
        }
        SpawnNear(sim, hall, BuildingSlot.Forge);
        w.CanResearch(0, hall, age, out why);
        Assert.Equal(Economy.ResearchError.AlreadyResearched, why); // with two slots again the sim says it itself
        _out.WriteLine($"queued with a hall lost: {queuedLocked} ticks sim Requires / shown AlreadyQueued; researched: {researchedLocked} ticks shown AlreadyResearched");
    }

    // M3-V4 (BUG-0126 item 1), shipped data: a build-menu button asks CanPlace at BuildMenu.NoAnchor. For every building
    // type and both players the answer is Requires exactly when an oracle (every required tech researched, an own finished
    // building of every required type) says locked, OffMap when it is open (WrongFaction for the other faction's), and the
    // shown reason maps OffMap to live. The Cadre Tower and the Engineers' Yard are locked until Age II, the Wickan Corral
    // until a Barracks is finished; the call changes nothing (hash) and allocates nothing.
    [Fact]
    public void BuildMenu_NoAnchor_IsRequiresExactlyWhileLocked_CadreTowerUntilAgeII()
    {
        (Simulation sim, int hall) = Hall(1);
        World w = sim.World;
        GameData data = w.Data;
        int tower = data.FindBuilding("malazan_cadre_tower"), yard = data.FindBuilding("malazan_engineers_yard");
        int corral = data.FindBuilding("malazan_wickan_corral"), billet = data.FindBuilding("malazan_billet");
        int checks = 0, locked = 0;
        void CheckAll(string when)
        {
            ulong hash = sim.StateHash();
            for (int p = 0; p < 2; p++)
                for (int t = 0; t < data.Buildings.Length; t++)
                {
                    BuildingDef def = data.Buildings[t];
                    w.CanPlace(p, t, BuildMenu.NoAnchor, out Economy.PlacementError e);
                    bool open = def.RequiresTechs.All(x => w.HasTech(p, x))
                        && def.RequiresBuildings.All(x => Enumerable.Range(0, w.Buildings.Capacity).Any(k =>
                            w.Buildings.Alive[k] && w.Buildings.Owner[k] == p && w.Buildings.TypeId[k] == x && !w.Buildings.UnderConstruction[k]));
                    Economy.PlacementError want = def.Faction != w.FactionOf(p) ? Economy.PlacementError.WrongFaction
                        : open ? Economy.PlacementError.OffMap : Economy.PlacementError.Requires;
                    Assert.True(want == e, $"{when}: player {p} {def.Key}: CanPlace(NoAnchor) {e}, oracle {want}");
                    Economy.PlacementError shown = BuildMenu.ShownPlaceReason(e);
                    Assert.Equal(e == Economy.PlacementError.OffMap ? Economy.PlacementError.None : e, shown);
                    checks++;
                    if (e == Economy.PlacementError.Requires) locked++;
                }
            Assert.Equal(hash, sim.StateHash());
        }
        Economy.PlacementError Shown(int type)
        {
            w.CanPlace(0, type, BuildMenu.NoAnchor, out Economy.PlacementError e);
            return BuildMenu.ShownPlaceReason(e);
        }
        CheckAll("start");
        Assert.Equal(Economy.PlacementError.Requires, Shown(tower));
        Assert.Equal(Economy.PlacementError.Requires, Shown(yard));
        Assert.Equal(Economy.PlacementError.Requires, Shown(corral));
        Assert.Equal(Economy.PlacementError.None, Shown(billet));
        SpawnNear(sim, hall, BuildingSlot.InfantryHall);
        CheckAll("barracks");
        Assert.Equal(Economy.PlacementError.None, Shown(corral));
        Assert.Equal(Economy.PlacementError.Requires, Shown(tower));
        SpawnNear(sim, hall, BuildingSlot.Forge);
        sim.Enqueue(Command.Research(0, ProductionMaps.In(sim, hall), ResearchMaps.AgeII));
        for (int t = 0; t < 2000 && w.Age(0) < 2; t++) sim.Tick();
        Assert.Equal(2, w.Age(0));
        CheckAll("age II");
        Assert.Equal(Economy.PlacementError.None, Shown(tower));
        Assert.Equal(Economy.PlacementError.None, Shown(yard));
        Assert.Equal(Economy.PlacementError.Requires, ShownFor(w, 1, data.Buildings.Select((d, i) => (d, i)).First(x => x.d.Faction == w.FactionOf(1) && x.d.Slot == BuildingSlot.CasterHall).i));
        _out.WriteLine($"{checks} NoAnchor answers equal the oracle, {locked} of them Requires");
        Assert.True(locked > 6);
    }

    private static Economy.PlacementError ShownFor(World w, int player, int type)
    {
        w.CanPlace(player, type, BuildMenu.NoAnchor, out Economy.PlacementError e);
        return BuildMenu.ShownPlaceReason(e);
    }

    [Fact]
    public void ShownReasons_IsTechQueued_AndTheNoAnchorAsk_AllocateZeroBytes()
    {
        (Simulation sim, int hall) = Hall(6, ageIIHalls: true);
        World w = sim.World;
        sim.Enqueue(Command.Research(0, ProductionMaps.In(sim, hall), ResearchMaps.AgeII));
        sim.Tick();
        sim.Tick();
        long sum = 0;
        Action block = () =>
        {
            for (int t = 0; t < w.Data.Techs.Length; t++)
            {
                w.CanResearch(0, hall, t, out Economy.ResearchError e);
                bool q = ProductionMenu.IsTechQueued(w.Buildings, 0, t);
                sum += (int)ProductionMenu.ShownResearchReason(e, w.HasTech(0, t), q) + (q ? 1 : 0);
            }
            for (int t = 0; t < w.Data.Buildings.Length; t++) sum += (int)ShownFor(w, 0, t);
        };
        block();
        int runs = AllocationProbe.AssertZero(block, _out);
        _out.WriteLine($"M3-V4 card reads: 0 bytes (runs {runs}), checksum {sum}");
    }

    // A match with one army unit and 5 workers a side (population room to train) and player 0's Town Hall slot; player 0
    // gets money for Age II (the ledger directly, as the research tests do). With <paramref name="ageIIHalls"/> player 0
    // also gets a finished Barracks and Armory (dev spawns, which ignore requirements): Age II needs any two finished
    // halls of distinct slots (BUG-0124).
    internal static (Simulation Sim, int Hall) Hall(ulong seed, bool ageIIHalls = false)
    {
        (Simulation sim, Vector2[][] blocks, StartBasePlan plan) = StartBaseTests.MatchSetup(seed, 1, 5);
        StartBaseTests.Apply(sim, blocks, plan);
        World w = sim.World;
        BuildingStore b = w.Buildings;
        int hall = -1;
        for (int k = 0; k < b.Capacity && hall < 0; k++)
            if (b.Alive[k] && b.Owner[k] == 0 && w.Data.Buildings[b.TypeId[k]].Slot == BuildingSlot.TownHall) hall = k;
        Assert.True(hall >= 0);
        w.Ledger.Gold[0] = 2000;
        w.Ledger.Wood[0] = 2000;
        if (ageIIHalls)
        {
            SpawnNear(sim, hall, BuildingSlot.InfantryHall);
            SpawnNear(sim, hall, BuildingSlot.Forge);
        }
        return (sim, hall);
    }

    /// <summary>Spawns player 0's building of <paramref name="slot"/> finished, at the nearest spot 10-45 m from the hall the sim accepts; returns its slot.</summary>
    internal static int SpawnNear(Simulation sim, int hall, BuildingSlot slot)
    {
        World w = sim.World;
        NavGrid g = w.NavGrid;
        BuildingStore b = w.Buildings;
        int type = StartBase.BuildingOfSlot(w.Data, w.FactionOf(0), slot);
        BuildingDef def = w.Data.Buildings[type];
        Vector2 near = StartBase.FootprintCenter(g, w.Data.Buildings[b.TypeId[hall]], b.Cell[hall]);
        var tried = new HashSet<int>();
        for (int attempt = 0; attempt < 40; attempt++)
        {
            int best = -1;
            float bestD = float.PositiveInfinity;
            for (int cell = 0; cell < g.Width * g.Height; cell++)
            {
                Vector2 c = StartBase.FootprintCenter(g, def, cell);
                float d = Vector2.Distance(c, near);
                if (d >= bestD || d < 10f || d > 45f || tried.Contains(cell)) continue;
                // Requires is checked before the map rules, so a locked type can't be probed with CanPlace; Fits is the map rule.
                if (!w.CanPlace(0, type, cell, out Economy.PlacementError r) && r is not (Economy.PlacementError.CannotAfford or Economy.PlacementError.UnitInTheWay)
                    && !(r == Economy.PlacementError.Requires && b.Fits(type, cell))) continue;
                (best, bestD) = (cell, d);
            }
            Assert.True(best >= 0, $"no spot for {def.Key}");
            tried.Add(best);
            int before = b.Count;
            sim.Enqueue(Command.SpawnBuilding(0, type, g.CellCenter(best % g.Width, best / g.Width)));
            sim.Tick(); // a command applies in the tick after the one running when it was queued
            sim.Tick();
            if (b.Count == before) continue; // a unit of player 0 stood in the footprint: the dev spawn refuses
            for (int k = 0; k < b.Capacity; k++)
                if (b.Alive[k] && b.Cell[k] == best && b.TypeId[k] == type) return k;
        }
        throw new InvalidOperationException($"could not spawn {def.Key}");
    }
}
