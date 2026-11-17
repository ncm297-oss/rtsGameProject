using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Xunit.Abstractions;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-SH2 item C (BUG-1461 / BUG-1463, criterion 5): a gatherer whose way between its Keep and its mine is crossed by six of its
/// own soldiers parked on a point (a Move's blob, Idle). "Held" is the Producer's definition (BUG-1464 item 1): on any tick at
/// or after 1,200 ticks without a delivery (node alive, on its loop) it stands within 2 m of where it was 600 ticks earlier and
/// in those 600 ticks it was never in reach of its node or of a drop-off. In the open, and in a one-cell passage the soldiers
/// parked in before the worker came, it is not held (asserted). The shape that holds it (Raraku seed 1, BUG-1461): the blob
/// parks round the worker standing in the passage's mouth; that row is a report, for the Producer's ruling (docs/03
/// "Implementation (M6-SH2)": the cause traced, no movement change made).
/// </summary>
public class ParkedArmyGatherTests(ITestOutputHelper output)
{
    // 48 x 24 map; the Keep on cells 4-7 x 8-11, the mine at (30, 9); the wall map adds a level-1 strip on columns 20-22,
    // open only at row 10 (a one-cell passage on the way).
    private const int Ticks = 6000;

    internal sealed class Outcome
    {
        public int Deliveries;
        public int HeldFrom = -1;
        public int LongestGap;
    }

    /// <summary>
    /// One run: <paramref name="soldiers"/> own soldiers moved to <paramref name="park"/> and left Idle there, then the worker sent
    /// to the mine. With <paramref name="surround"/> the worker stands on the park point first, so the soldiers park round it
    /// (Raraku seed 1's shape: the worker stood on the rally point when the army arrived).
    /// </summary>
    internal static Outcome Measure(int soldiers, Vector2 park, bool surround = false, bool wall = false, int ticks = Ticks)
    {
        Simulation sim = NewSim(wall ? WallMap() : Flat(48, 24), units: 32, players: 2);
        World w = sim.World;
        EntityHandle mine = Spawn(w, Mine, 30, 9, 2500);
        EntityHandle keep = Building(sim, 4, 8);
        CombatScenes.Place(sim, 1, Infantry, At(sim, 45, 21));
        EntityHandle worker = Unit(sim, surround ? park : At(sim, 9, 10));
        var army = new EntityHandle[soldiers];
        for (int k = 0; k < soldiers; k++) army[k] = Unit(sim, At(sim, 9 + k % 3, 14 + k / 3), type: Infantry);
        foreach (EntityHandle h in army) sim.Enqueue(Command.Move(0, h, park));
        Run(sim, 400); // parked
        int cell = w.Resources.Cell[mine.Index];
        sim.Enqueue(Command.Gather(0, worker, w.NavGrid.CellCenter(cell % w.NavGrid.Width, cell / w.NavGrid.Width)));
        UnitStore u = w.Units;
        int i = worker.Index;
        const int ring = HeldWorkerProbe.LookBack + 1;
        var trail = new Vector2[ring];
        var inReach = new bool[ring];
        var r = new Outcome();
        int lastDelivery = sim.TickNumber, gathered = w.GatheredGold[0];
        var mineDef = w.Data.Resources[w.Resources.TypeId[mine.Index]];
        var keepDef = w.Data.Buildings[w.Buildings.TypeId[keep.Index]];
        int keepCell = w.Buildings.Cell[keep.Index];
        for (int t = 0; t < ticks; t++)
        {
            sim.Tick();
            int now = sim.TickNumber;
            if (w.GatheredGold[0] != gathered)
            {
                gathered = w.GatheredGold[0];
                r.Deliveries++;
                r.LongestGap = Math.Max(r.LongestGap, now - lastDelivery);
                lastDelivery = now;
            }
            trail[now % ring] = u.Position[i];
            inReach[now % ring] = EconomySystem.InReach(w.NavGrid, u.Position[i], cell, mineDef.FootprintWidth, mineDef.FootprintHeight)
                || EconomySystem.InReach(w.NavGrid, u.Position[i], keepCell, keepDef.FootprintWidth, keepDef.FootprintHeight);
            if (r.HeldFrom >= 0 || now - lastDelivery < HeldWorkerProbe.HeldTicks || !EconomySystem.OnLoop(u, i)) continue;
            bool reached = false;
            for (int k = 0; k < ring; k++) reached |= inReach[k];
            if (!reached && Vector2.Distance(trail[(now - HeldWorkerProbe.LookBack) % ring], u.Position[i]) <= HeldWorkerProbe.HeldRadius) r.HeldFrom = now;
        }
        r.LongestGap = Math.Max(r.LongestGap, sim.TickNumber - lastDelivery);
        return r;
    }

    /// <summary>The flat 48 x 24 map with a wall (an unreachable level-1 strip) on columns 20-22, open only at row 10.</summary>
    private static Map.Heightmap WallMap()
    {
        var rows = new string[24];
        for (int y = 0; y < 24; y++)
        {
            var row = new char[48];
            for (int x = 0; x < 48; x++) row[x] = x >= 20 && x <= 22 && y != 10 ? '1' : '0';
            rows[y] = new string(row);
        }
        return FromRows(rows);
    }

    private static Vector2 Cell(int x, int y) => new(x * 2f + 1f, y * 2f + 1f);

    /// <summary>Criterion 5's scene, the shapes that pass: six soldiers parked on the way in the open (before the worker or round it) and in the passage before the worker came; none held.</summary>
    [Theory]
    [InlineData(0, 18, 10, false, false)]
    [InlineData(6, 18, 10, false, false)]
    [InlineData(6, 12, 10, false, false)]
    [InlineData(6, 26, 10, false, false)]
    [InlineData(6, 18, 10, true, false)]
    [InlineData(0, 22, 10, false, true)]
    [InlineData(6, 22, 10, false, true)]
    public void AWorkersWayCrossedBySixParkedSoldiers_IsNotHeld(int soldiers, int px, int py, bool surround, bool wall)
    {
        Outcome r = Measure(soldiers, Cell(px, py), surround, wall);
        output.WriteLine($"{soldiers} soldiers at ({px}, {py}) surround {surround} wall {wall}: deliveries {r.Deliveries}, longest gap {r.LongestGap}, held from {r.HeldFrom}");
        Assert.True(r.HeldFrom < 0, $"held from tick {r.HeldFrom}; deliveries {r.Deliveries}");
        Assert.True(r.Deliveries > 0);
    }

    /// <summary>
    /// BUG-1461's shape, a report (for ruling): the blob parked round the worker in the passage's mouth holds it (6 at (22, 10):
    /// 1 delivery in 6,000 ticks, held from 2,148; 6 at (23, 10): none, held from 1,616; 8 at (22, 10): held from 2,157). The
    /// worker presses diagonally into the passage's mouth; the soldier in its way can yield only along the passage, but a shove
    /// moves a unit along the contact normal, into the wall (and a blob's point does not yield at all, BUG-0033's rule).
    /// M6-S12: still a report, on purpose. The cure decided on 2026-11-16 is an AI rule (the rally point keeps off one-cell
    /// passages, <c>Ai/RallyPassageTests</c> asserts it on a staged AI seat), not a movement rule, so this movement shape (an
    /// army parked by hand round a worker in a mouth) still holds the worker; no AI seat parks there any more.
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData(6, 22, 10)]
    [InlineData(6, 23, 10)]
    [InlineData(8, 22, 10)]
    public void Bug1461_ABlobParkedRoundAWorkerInAPassageMouth_Report(int soldiers, int px, int py)
    {
        Outcome r = Measure(soldiers, Cell(px, py), surround: true, wall: true);
        output.WriteLine($"BUG-1461 shape, {soldiers} soldiers at ({px}, {py}): deliveries {r.Deliveries}, longest gap {r.LongestGap}, held from {r.HeldFrom}");
    }
}
