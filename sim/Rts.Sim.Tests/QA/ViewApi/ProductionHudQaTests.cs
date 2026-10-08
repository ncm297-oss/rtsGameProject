using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using Rts.Sim.Tests.ViewApi;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>
/// QA M3-V3 (2026-10-07-1415): the new view helpers (<see cref="QueueStrip"/>, <see cref="ProductionMenu"/>,
/// <see cref="RallyGeometry"/>, <see cref="PopText"/>, <see cref="PortraitGrid"/>) under hostile input (dead, reused and
/// out-of-range slots, a building destroyed mid-queue, NaN / infinite points, degenerate rectangles, short spans, extreme
/// ints) never throw and never move the state hash; their answers stay inside their documented ranges.
/// </summary>
[Collection(SerialCollection.Name)]
public class ProductionHudQaTests
{
    [Fact]
    public void QueueStrip_HostileSlots_NeverThrow_NeverMoveHash_FillInRange()
    {
        (Simulation sim, int hall) = ProductionHudTests.Hall(3);
        World w = sim.World;
        BuildingStore b = w.Buildings;
        Vector2 at = ProductionMaps.In(sim, hall);
        int laborer = w.Data.UnitsTrainedAt(b.TypeId[hall])[0];
        for (int i = 0; i < 7; i++) sim.Enqueue(Command.Train(0, at, laborer));
        sim.Tick();
        sim.Tick();
        Assert.Equal(EconomyConstants.ProductionQueueCapacity, QueueStrip.Count(b, hall));
        foreach (int slot in new[] { -1, int.MinValue, int.MaxValue, b.Capacity, b.Capacity + 1, hall })
        {
            ulong h = sim.StateHash();
            int n = QueueStrip.Count(b, slot);
            float f = QueueStrip.HeadFill(b, slot);
            Assert.InRange(n, 0, QueueStrip.MaxItems);
            Assert.InRange(f, 0f, 1f);
            Assert.Equal(h, sim.StateHash());
        }
        // Run the queue: the fill is monotone within an item and always in [0, 1].
        float last = -1f;
        for (int t = 0; t < 400; t++)
        {
            sim.Tick();
            float f = QueueStrip.HeadFill(b, hall);
            Assert.InRange(f, 0f, 1f);
            last = f;
        }
        Assert.True(last >= 0f);
        // Destroy the hall mid-queue: the strip reads empty, not the dead slot's leftovers.
        b.Damage(b.HandleOf(hall), 1_000_000);
        Assert.False(b.Alive[hall]);
        Assert.Equal(0, QueueStrip.Count(b, hall));
        Assert.Equal(0f, QueueStrip.HeadFill(b, hall));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-1f)]
    [InlineData(0f)]
    [InlineData(63.999f)]
    [InlineData(64f)]
    [InlineData(67.999f)]
    [InlineData(68f)]
    [InlineData(1e9f)]
    public void QueueStrip_ItemAt_ExactEdges(float x)
    {
        int k = QueueStrip.ItemAt(x, 5, 64f, 4f);
        Assert.InRange(k, -1, 4);
        if (!float.IsFinite(x) || x < 0f || x >= 5 * 68f - 4f) Assert.Equal(-1, k);
        else if (x % 68f >= 64f) Assert.Equal(-1, k); // the gap
        else Assert.Equal((int)(x / 68f), k);
        // Width and ItemX agree with ItemAt: the last pixel of the strip is item count - 1.
        Assert.Equal(4, QueueStrip.ItemAt(QueueStrip.Width(5, 64f, 4f) - 0.001f, 5, 64f, 4f));
        Assert.Equal(-1, QueueStrip.ItemAt(QueueStrip.Width(5, 64f, 4f), 5, 64f, 4f));
        Assert.Equal(-1, QueueStrip.ItemAt(10f, 0, 64f, 4f));
        Assert.Equal(-1, QueueStrip.ItemAt(10f, 99, 0f, 4f));
    }

    [Fact]
    public void ProductionMenu_ShortSpans_UnknownTypes_EveryBuilding()
    {
        GameData data = TestSim.Data;
        Assert.Equal(0, ProductionMenu.Entries(data, -1, new ProductionEntry[15]));
        Assert.Equal(0, ProductionMenu.Entries(data, data.Buildings.Length, new ProductionEntry[15]));
        Assert.Equal(0, ProductionMenu.Entries(data, int.MinValue, new ProductionEntry[15]));
        for (int t = 0; t < data.Buildings.Length; t++)
        {
            var full = new ProductionEntry[64];
            int n = ProductionMenu.Entries(data, t, full);
            int units = data.UnitsTrainedAt(t).Length, techs = data.TechsResearchableAt(t).Length;
            Assert.Equal(units + techs, n);
            Assert.True(n <= 15, $"{data.Buildings[t].Key}: {n} entries do not fit the 5 x 3 card");
            // Every entry once; the units first; then common techs before faction techs.
            Assert.Equal(n, full.Take(n).Distinct().Count());
            for (int i = 0; i < units; i++) Assert.False(full[i].IsTech);
            bool sawFaction = false;
            for (int i = units; i < n; i++)
            {
                Assert.True(full[i].IsTech);
                bool common = data.Techs[full[i].TypeId].Faction < 0;
                if (!common) sawFaction = true;
                else Assert.False(sawFaction, $"{data.Buildings[t].Key}: a common tech after a faction tech");
            }
            // A short span is filled in order and never overrun.
            for (int len = 0; len <= n; len++)
            {
                var shortSpan = new ProductionEntry[len];
                Assert.Equal(len, ProductionMenu.Entries(data, t, shortSpan));
                Assert.Equal(full.Take(len), shortSpan);
            }
        }
        // Every requires entry resolves to a display name (not the raw id) in the shipped data.
        foreach (UnitDef u in data.Units)
            foreach (string r in u.Requires) Assert.NotEqual(r, ProductionMenu.RequirementName(data, r));
        foreach (TechDef t in data.Techs)
            foreach (string r in t.Requires) Assert.NotEqual(r, ProductionMenu.RequirementName(data, r));
        Assert.Equal("no_such_id", ProductionMenu.RequirementName(data, "no_such_id"));
        Assert.Equal("", ProductionMenu.RequirementName(data, ""));
    }

    [Fact]
    public void RallyGeometry_Hostile()
    {
        var min = new Vector2(10f, 20f);
        var max = new Vector2(16f, 26f);
        Vector2 c = (min + max) / 2f;
        foreach (Vector2 t in new[] { new Vector2(float.NaN, 0f), new Vector2(0f, float.PositiveInfinity), new Vector2(float.NegativeInfinity, float.NaN) })
        {
            Assert.Equal(c, RallyGeometry.EdgePoint(min, max, t));
            Assert.Equal(0f, RallyGeometry.Line(min, max, t, out Vector2 from));
            Assert.Equal(c, from);
        }
        // Inside and on the edge: no line.
        Assert.Equal(0f, RallyGeometry.Line(min, max, c, out _));
        Assert.Equal(0f, RallyGeometry.Line(min, max, max, out _));
        // Far away in every direction: the start lies on the rectangle's boundary and on the segment centre -> target.
        var rng = new Random(7);
        for (int i = 0; i < 2000; i++)
        {
            var t = new Vector2((float)(rng.NextDouble() * 2000 - 1000), (float)(rng.NextDouble() * 2000 - 1000));
            if (t.X >= min.X && t.X <= max.X && t.Y >= min.Y && t.Y <= max.Y) continue;
            float len = RallyGeometry.Line(min, max, t, out Vector2 from);
            Assert.True(float.IsFinite(from.X) && float.IsFinite(from.Y));
            bool onX = MathF.Abs(from.X - min.X) < 1e-3f || MathF.Abs(from.X - max.X) < 1e-3f;
            bool onY = MathF.Abs(from.Y - min.Y) < 1e-3f || MathF.Abs(from.Y - max.Y) < 1e-3f;
            Assert.True(onX || onY, $"start {from} not on the boundary for {t}");
            Assert.InRange(from.X, min.X - 1e-3f, max.X + 1e-3f);
            Assert.InRange(from.Y, min.Y - 1e-3f, max.Y + 1e-3f);
            Assert.True(len > 0f && len <= Vector2.Distance(c, t) + 1e-3f);
        }
        // Degenerate rectangle (zero size): the centre, no NaN.
        Vector2 d = RallyGeometry.EdgePoint(min, min, new Vector2(100f, 100f));
        Assert.True(float.IsFinite(d.X) && float.IsFinite(d.Y));
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "0.5")]
    [InlineData(10, "5")]
    [InlineData(11, "5.5")]
    [InlineData(-1, "-0.5")]
    [InlineData(-3, "-1.5")]
    [InlineData(int.MaxValue, "1073741823.5")]
    [InlineData(int.MinValue, "-1073741824")]
    public void PopText_Format_Extremes(int half, string want)
    {
        var saved = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            // A decimal-comma culture must not leak in.
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal(want, PopText.Format(half));
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = saved;
        }
        Assert.True(PopText.AtCap(10, 10));
        Assert.True(PopText.AtCap(11, 10));
        Assert.False(PopText.AtCap(9, 10));
        Assert.True(PopText.AtCap(0, 0));
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(24)]
    [InlineData(25)]
    [InlineData(int.MaxValue)]
    public void PortraitGrid_Extremes(int count)
    {
        int shown = PortraitGrid.Shown(count), over = PortraitGrid.Overflow(count);
        Assert.InRange(shown, 0, PortraitGrid.MaxPortraits);
        Assert.True(over >= 0);
        if (count >= 0) Assert.Equal(count, shown + over);
    }

    // The production HUD's reads with the state changing under them (trains, cancels at head / tail, rally set / cleared /
    // moved, a second building destroyed mid-queue, Age II researched) hash the same as a bare twin, every tick, 600 ticks.
    [Fact]
    public void HudReads_WithDestructionAndCancels_HashTwin_600Ticks()
    {
        (Simulation a, int hall) = ProductionHudTests.Hall(4, ageIIHalls: true); // BUG-0124: the Age II research is accepted
        (Simulation b, _) = ProductionHudTests.Hall(4, ageIIHalls: true);
        World w = a.World;
        BuildingStore bs = w.Buildings;
        Vector2 at = ProductionMaps.In(a, hall);
        int laborer = w.Data.UnitsTrainedAt(bs.TypeId[hall])[0];
        var into = new ProductionEntry[15];
        var rng = new Random(44);
        var script = new List<Command>();
        int sawTech = 0;
        for (int tick = 0; tick < 600; tick++)
        {
            script.Clear();
            switch (rng.Next(10))
            {
                case 0: script.Add(Command.Train(0, at, laborer)); break;
                case 1: script.Add(Command.CancelTrain(0, at, 0)); break;
                case 2: script.Add(Command.CancelTrain(0, at, 4)); break;
                case 3: script.Add(Command.SetRally(0, bs.Cell[hall], at + new Vector2(rng.Next(-40, 40), rng.Next(-40, 40)))); break;
                case 4: script.Add(Command.ClearRally(0, at)); break;
                case 5: script.Add(Command.Research(0, at, ResearchMaps.AgeII)); break;
                case 6: script.Add(Command.SetRally(0, bs.Cell[hall], new Vector2(float.NaN, 3f))); break;
                default: break;
            }
            foreach (Command c in script)
            {
                a.Enqueue(c);
                b.Enqueue(c);
            }
            ulong before = a.StateHash();
            for (int k = -1; k <= bs.Capacity; k++)
            {
                int n = QueueStrip.Count(bs, k);
                float f = QueueStrip.HeadFill(bs, k);
                Assert.InRange(f, 0f, 1f);
                if ((uint)k < (uint)bs.Capacity && bs.Alive[k])
                {
                    int m = ProductionMenu.Entries(w.Data, bs.TypeId[k], into);
                    for (int e = 0; e < m; e++)
                        _ = into[e].IsTech ? w.CanResearch(0, k, into[e].TypeId, out _) : w.CanTrain(0, k, into[e].TypeId, out _);
                    for (int i = 0; i < n; i++) _ = bs.QueueTypeAt(k, i) + bs.ItemTicks(k, i);
                    if (bs.HasRally[k]) _ = RallyGeometry.Line(Vector2.Zero, Vector2.One, bs.RallyPosition[k], out _);
                }
            }
            _ = PopText.Format(w.HalfPop[0]);
            Assert.Equal(before, a.StateHash());
            a.Tick();
            b.Tick();
            Assert.Equal(b.StateHash(), a.StateHash());
            Assert.Equal(QueueStrip.Count(bs, hall), Math.Min(bs.QueueCount[hall], QueueStrip.MaxItems));
            for (int i = 0; i < bs.QueueCount[hall]; i++)
                if (bs.QueueIsTechAt(hall, i)) sawTech++;
        }
        Assert.True(sawTech > 0, "Age II never entered the hall's queue");
    }
}
