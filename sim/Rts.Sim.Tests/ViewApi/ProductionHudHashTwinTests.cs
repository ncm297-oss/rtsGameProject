using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M3-V3 read-only proof: every new view read (<see cref="QueueStrip"/>, <see cref="ProductionMenu"/>, <see cref="RallyGeometry"/>, <see cref="PopText"/>, <see cref="PortraitGrid"/>) and the HUD's sim reads (<c>CanTrain</c>, <c>CanResearch</c>, <c>TechBonus</c>, <c>Age</c>, the queue and rally spans, the any-owner box pick), every tick, never change the state hash.</summary>
[Collection(SerialCollection.Name)]
public class ProductionHudHashTwinTests
{
    private const float BoxHeight = 3f, SiteMin = 0.15f;

    [Fact]
    public void ProductionHudReadsEveryTick_HashEqualsABareTwin_400Ticks()
    {
        (Simulation a, int hall) = ProductionHudTests.Hall(1, ageIIHalls: true); // BUG-0124: the Age II research is accepted
        (Simulation b, _) = ProductionHudTests.Hall(1, ageIIHalls: true);
        World w = a.World;
        NavGrid g = w.NavGrid;
        BuildingStore bs = w.Buildings;
        Assert.Equal(b.StateHash(), a.StateHash());
        int laborer = w.Data.UnitsTrainedAt(bs.TypeId[hall])[0];
        Vector2 at = ProductionMaps.In(a, hall);
        BuildingDef hallDef = w.Data.Buildings[bs.TypeId[hall]];
        Vector2 hallMin = new Vector2(bs.Cell[hall] % g.Width, bs.Cell[hall] / g.Width) * MapConstants.CellSize;
        Vector2 hallMax = hallMin + new Vector2(hallDef.FootprintWidth, hallDef.FootprintHeight) * MapConstants.CellSize;
        Vector2 rally = (hallMin + hallMax) / 2f + new Vector2(14f, 6f);

        var script = new Dictionary<int, Command[]>
        {
            [0] = new[] { Command.Train(0, at, laborer), Command.Research(0, at, ResearchMaps.AgeII), Command.Train(0, at, laborer) },
            [5] = new[] { Command.SetRally(0, bs.Cell[hall], rally) },
            [60] = new[] { Command.CancelTrain(0, at, 2), Command.Train(0, at, laborer) },
            [200] = new[] { Command.SetRally(0, bs.Cell[hall], rally + new Vector2(-30f, 2f)) },
            [320] = new[] { Command.ClearRally(0, at), Command.CancelTrain(0, at, 0) },
        };
        var into = new ProductionEntry[15];
        long sink = 0;
        int sawQueue = 0, sawRally = 0, greyed = 0, enabled = 0, sawTech = 0;
        for (int tick = 0; tick < 400; tick++)
        {
            if (script.TryGetValue(tick, out Command[]? cmds))
                foreach (Command c in cmds)
                {
                    a.Enqueue(c);
                    b.Enqueue(c);
                }
            ulong before = a.StateHash();
            // The production card: every entry of every own building, greyed by the sim's own rules.
            for (int k = 0; k < bs.Capacity; k++)
            {
                if (!bs.Alive[k]) continue;
                int n = ProductionMenu.Entries(w.Data, bs.TypeId[k], into);
                for (int e = 0; e < n; e++)
                {
                    bool ok = into[e].IsTech ? w.CanResearch(0, k, into[e].TypeId, out ResearchError re) : w.CanTrain(0, k, into[e].TypeId, out TrainError te);
                    if (ok) enabled++;
                    else greyed++;
                }
                // The queue strip and the rally marker.
                int q = QueueStrip.Count(bs, k);
                if (q > 0 && k == hall) sawQueue++;
                if (k == hall && q > 0 && bs.QueueIsTechAt(k, 0)) sawTech++;
                for (int i = 0; i < q; i++) sink += bs.QueueTypeAt(k, i) + (bs.QueueIsTechAt(k, i) ? 1 : 0) + bs.ItemTicks(k, i);
                sink += (long)(QueueStrip.HeadFill(bs, k) * 1000);
                if (bs.HasRally[k])
                {
                    if (k == hall) sawRally++;
                    sink += (long)RallyGeometry.Line(hallMin, hallMax, bs.RallyPosition[k], out Vector2 from) + (long)from.X;
                }
            }
            // The panel: stats with bonuses for every unit type, the age, the pop text, the grid; the any-owner box pick.
            for (int t = 0; t < w.Data.Units.Length; t++)
                foreach (TechStat s in Enum.GetValues<TechStat>()) sink += (long)w.TechBonus(0, t, s);
            sink += w.Age(0) + PortraitGrid.Shown(w.Units.Count) + PortraitGrid.Overflow(w.Units.Count);
            sink += PopText.Format(w.HalfPop[0]).Length + (PopText.AtCap(w.HalfPop[0], w.HalfPopCap[0]) ? 1 : 0);
            for (int r = 0; r < 10; r++)
            {
                var o = new Vector3(at.X + r - 5f, 50f, at.Y + 30f);
                sink += BuildingPicker.PickRay(bs, w.Data.Buildings, g, w.Heightmap, -1, o, new Vector3(0f, -1f, -0.8f), BoxHeight, SiteMin);
                // M3-V3b: the right click's resource ray pick and the building pick's entry overload.
                sink += ResourcePicker.PickRay(g, w.Data.Resources, w.Resources.Alive, w.Resources.TypeId, w.Resources.Cell, w.Heightmap, o,
                    new Vector3(0.3f, -1f, -0.8f), 3.5f, 2.1f, out float nodeT) + (float.IsFinite(nodeT) ? 1 : 0);
                sink += BuildingPicker.PickRay(bs, w.Data.Buildings, g, w.Heightmap, 0, o, new Vector3(-0.2f, -1f, -0.6f), BoxHeight, SiteMin, out float boxT) + (float.IsFinite(boxT) ? 1 : 0);
            }
            Assert.Equal(before, a.StateHash());

            a.Tick();
            b.Tick();
            Assert.True(b.StateHash() == a.StateHash(), $"tick {a.TickNumber}: view-read sim diverged from its twin");
        }
        Assert.True(sink != 0);
        Assert.True(sawQueue > 300 && sawRally > 250 && greyed > 0 && enabled > 0 && sawTech > 0, $"queue {sawQueue}, rally {sawRally}, greyed {greyed}, enabled {enabled}, Age II at the head {sawTech}");
        Assert.False(bs.HasRally[hall]);
    }
}
