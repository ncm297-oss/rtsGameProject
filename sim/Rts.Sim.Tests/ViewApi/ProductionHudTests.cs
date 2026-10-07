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
        (Simulation sim, int hall) = Hall(1);
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

    // A match with one army unit and 5 workers a side (population room to train) and player 0's Town Hall slot; player 0
    // gets money for Age II (the ledger directly, as the research tests do).
    internal static (Simulation Sim, int Hall) Hall(ulong seed)
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
        return (sim, hall);
    }
}
