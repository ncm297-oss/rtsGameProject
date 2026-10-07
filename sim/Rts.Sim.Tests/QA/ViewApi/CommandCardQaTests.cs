using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Tests.ViewApi;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>
/// QA M3-V2 (2026-10-07-1131): the placement ghost's reads under load. Twin A plays a ghost swept over the whole map
/// (every building type, border and corner anchors, anchors over units, own and enemy buildings) asking
/// <c>World.CanPlace</c> dozens of times per tick, including between an Enqueue and the tick that applies it (a worker
/// paying mid-frame); twin B gets the same commands and asks nothing. Hashes must match every tick, and CanPlace must
/// give the same answer when asked twice in a row (its flow-field scratch must not leak into the next answer).
/// </summary>
[Collection(SerialCollection.Name)]
public class CommandCardQaTests
{
    private readonly ITestOutputHelper _out;

    public CommandCardQaTests(ITestOutputHelper output) => _out = output;

    [Theory]
    [InlineData(1UL)]
    [InlineData(6UL)]
    [InlineData(31UL)]
    [InlineData(77UL)]
    public void GhostSweepEveryTick_WithPaidBuildsMidFrame_HashEqualsBareTwin_AndAnswersAreStable(ulong seed)
    {
        (Simulation a, _, _, _) = BuildingPickerTests.Base(seed);
        (Simulation b, _, _, _) = BuildingPickerTests.Base(seed);
        World w = a.World;
        NavGrid g = w.NavGrid;
        Assert.Equal(b.StateHash(), a.StateHash());
        int cells = g.Width * g.Height;
        var rng = new Random((int)seed);

        var workers = new List<EntityHandle>();
        var soldiers = new List<EntityHandle>();
        for (int i = 0; i < w.Units.Capacity; i++)
        {
            if (!w.Units.Alive[i] || w.Units.Owner[i] != 0) continue;
            var h = new EntityHandle(i, w.Units.Generation[i]);
            if (w.Data.Units[w.Units.TypeId[i]].Slot == UnitSlot.Worker) workers.Add(h);
            else soldiers.Add(h);
        }
        Assert.True(workers.Count >= 2 && soldiers.Count >= 5, $"seed {seed}: {workers.Count} workers, {soldiers.Count} soldiers");
        int faction0 = w.FactionOf(0);
        int[] ownTypes = w.Data.Buildings.Where(d => d.Faction == faction0).Select(d => d.Id).ToArray();
        int houseType = StartBase.BuildingOfSlot(w.Data, faction0, BuildingSlot.House);
        int[] corners = { 0, g.Width - 1, (g.Height - 1) * g.Width, cells - 1 };

        int green = 0, red = 0, builds = 0, unstable = 0;
        long canPlaceTicks = 0, worstTicks = 0;
        var reasons = new SortedSet<PlacementError>();
        for (int tick = 0; tick < 300; tick++)
        {
            // Soldiers wander so UnitInTheWay comes and goes.
            if (tick % 20 == 0)
                foreach (EntityHandle s in soldiers.Take(5))
                {
                    var p = new Vector2((float)rng.NextDouble() * g.Width * MapConstants.CellSize, (float)rng.NextDouble() * g.Height * MapConstants.CellSize);
                    a.Enqueue(Command.Move(0, s, p));
                    b.Enqueue(Command.Move(0, s, p));
                }

            ulong before = a.StateHash();
            int paidThisTick = 0;
            for (int k = 0; k < 48; k++)
            {
                int type = k % 3 == 0 ? rng.Next(w.Data.Buildings.Length) : k % 3 == 1 ? houseType : ownTypes[rng.Next(ownTypes.Length)];
                BuildingDef def = w.Data.Buildings[type];
                int anchor = k < 4
                    ? PlacementGhost.Anchor(g, def, g.CellCenter(corners[k] % g.Width, corners[k] / g.Width))
                    : PlacementGhost.Anchor(g, def, g.CellCenter(rng.Next(g.Width), rng.Next(g.Height)));
                Assert.InRange(anchor, 0, cells - 1);
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                bool ok = w.CanPlace(0, type, anchor, out PlacementError r1);
                long dt = System.Diagnostics.Stopwatch.GetTimestamp() - t0;
                canPlaceTicks += dt;
                worstTicks = Math.Max(worstTicks, dt);
                bool ok2 = w.CanPlace(0, type, anchor, out PlacementError r2);
                if (ok != ok2 || r1 != r2) unstable++;
                if (ok) green++;
                else
                {
                    red++;
                    reasons.Add(r1);
                }

                // Every 25 ticks, a green own placement becomes a paid Build in both twins, enqueued mid-sweep:
                // the remaining reads this tick see the queue but not the payment (it lands next tick).
                if (ok && tick % 25 == 0 && k >= 4 && def.Slot == BuildingSlot.House && paidThisTick++ == 0)
                {
                    Vector2 pt = PlacementGhost.AnchorPoint(g, anchor);
                    EntityHandle wk = workers[builds % workers.Count];
                    a.Enqueue(Command.Build(0, wk, type, pt));
                    b.Enqueue(Command.Build(0, wk, type, pt));
                    builds++;
                    before = a.StateHash(); // the pending queue is hashed; only the reads must leave it alone
                }
            }
            Assert.Equal(before, a.StateHash());
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed} tick {a.TickNumber}: the ghost's reads changed the sim");
        }
        _out.WriteLine($"seed {seed}: {green} green, {red} red ({string.Join(", ", reasons)}), {builds} paid Builds, {unstable} unstable answers; CanPlace avg {canPlaceTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / (green + red):0.0000} ms, worst {worstTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0.000} ms (Debug, info only)");
        Assert.Equal(0, unstable);
        Assert.True(builds >= 1, $"no paid Build"); // houses cost wood the start ledger runs out of after one
        Assert.True(green > 0 && red > 0);
        Assert.Contains(PlacementError.Blocked, reasons);
        Assert.Contains(PlacementError.WrongFaction, reasons);
    }

    [Fact]
    public void GhostAnchor_AtEveryBorderCell_StaysOnTheMap_AndTheBorderIsRed()
    {
        (Simulation sim, _, _, _) = BuildingPickerTests.Base(1);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        int greenOnEdge = 0;
        foreach (BuildingDef def in w.Data.Buildings)
        {
            if (def.Faction != w.FactionOf(0)) continue;
            for (int x = 0; x < g.Width; x++)
                foreach (int y in new[] { 0, g.Height - 1 })
                    greenOnEdge += Check(w, g, def, x, y);
            for (int y = 0; y < g.Height; y++)
                foreach (int x in new[] { 0, g.Width - 1 })
                    greenOnEdge += Check(w, g, def, x, y);
        }
        // The border ring is blocked (docs/03), so a ghost pinned at the edge touches it and is never green.
        Assert.Equal(0, greenOnEdge);

        static int Check(World w, NavGrid g, BuildingDef def, int x, int y)
        {
            int anchor = PlacementGhost.Anchor(g, def, g.CellCenter(x, y));
            Assert.True(anchor >= 0, $"{def.Key} at ({x},{y}): no anchor");
            int ax = anchor % g.Width, ay = anchor / g.Width;
            Assert.True(ax >= 0 && ay >= 0 && ax + def.FootprintWidth <= g.Width && ay + def.FootprintHeight <= g.Height, $"{def.Key} at ({x},{y}): footprint off the map");
            Assert.True(x >= ax && x < ax + def.FootprintWidth && y >= ay && y < ay + def.FootprintHeight, $"{def.Key} at ({x},{y}): cursor cell outside the footprint");
            return w.CanPlace(0, def.Id, anchor, out _) ? 1 : 0;
        }
    }
}
