using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;
using static Rts.Sim.Tests.AbilityScenes;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M4-V6b: which status markers the view draws (<see cref="StatusMarkers"/>): one per active status of each shown unit, side by side.</summary>
[Collection(SerialCollection.Name)]
public class StatusMarkersTests
{
    private readonly ITestOutputHelper _out;

    public StatusMarkersTests(ITestOutputHelper output) => _out = output;

    private static int TelasFire => TestSim.Data.FindAbility("telas_fire");

    private static int StatusCount => TestSim.Data.Statuses.Length;

    private static int Collect(Simulation sim, StatusMark[] marks) =>
        StatusMarkers.Collect(sim.World.Units.Statuses, sim.World.Units.Alive, StatusCount, marks);

    private static int MarksOf(StatusMark[] marks, int n, EntityHandle unit, int status)
    {
        int c = 0;
        for (int k = 0; k < n; k++) if (marks[k].Unit == unit.Index && marks[k].Status == status) c++;
        return c;
    }

    [Fact]
    public void TelasFire_BurningMarker_ExactlyWhileTicksRemain_GoneOnExpiry()
    {
        Simulation sim = NoFights();
        EntityHandle mage = Place(sim, 0, Mage, At(sim, 10, 20));
        EntityHandle a = Place(sim, 1, HeavyInfantry, At(sim, 16, 20)), b = Place(sim, 1, HeavyInfantry, At(sim, 16, 21));
        EntityHandle away = Place(sim, 1, HeavyInfantry, At(sim, 30, 20));
        var marks = new StatusMark[sim.World.Units.Capacity * StatusStore.PerUnit];
        Assert.Equal(0, Collect(sim, marks));
        sim.Enqueue(Command.UseAbility(0, mage, 0, sim.World.Units.Position[a.Index]));
        TickOf(sim, mage, resolved: true);
        int burningTicks = 0;
        for (int t = 0; t < 120; t++)
        {
            int n = Collect(sim, marks);
            foreach (EntityHandle u in new[] { a, b, away })
            {
                bool burning = StatusOf(sim, u, Burning).Ticks > 0;
                Assert.True(MarksOf(marks, n, u, Burning) == (burning ? 1 : 0), $"tick {sim.TickNumber}: unit {u.Index} burning {burning}, marks {MarksOf(marks, n, u, Burning)}");
            }
            Assert.Equal(0, MarksOf(marks, n, away, Burning));
            if (StatusOf(sim, a, Burning).Ticks > 0) burningTicks++;
            for (int k = 0; k < n; k++) Assert.Equal(new StatusMark(marks[k].Unit, Burning, 0, 1), marks[k]);
            sim.Tick();
        }
        Assert.Equal(0, Collect(sim, marks));
        _out.WriteLine($"Burning marked for {burningTicks} ticks (4 s = 80)");
        Assert.InRange(burningTicks, 79, 80);
    }

    [Fact]
    public void BothStatuses_SideBySide_InTheOrderApplied_RowOffsetsCentred()
    {
        Simulation sim = NoFights();
        EntityHandle a = Place(sim, 1, HeavyInfantry, At(sim, 16, 20)), b = Place(sim, 1, HeavyInfantry, At(sim, 20, 20));
        StatusStore s = sim.World.Units.Statuses;
        s.Apply(a.Index, Slowed, 0.3f, 100, 0);
        s.Apply(a.Index, Burning, 10f, 80, 0);
        s.Apply(b.Index, Burning, 10f, 80, 0);
        var marks = new StatusMark[64];
        int n = Collect(sim, marks);
        int lo = Math.Min(a.Index, b.Index);
        Assert.Equal(3, n);
        var want = a.Index < b.Index
            ? new[] { new StatusMark(a.Index, Slowed, 0, 2), new StatusMark(a.Index, Burning, 1, 2), new StatusMark(b.Index, Burning, 0, 1) }
            : new[] { new StatusMark(b.Index, Burning, 0, 1), new StatusMark(a.Index, Slowed, 0, 2), new StatusMark(a.Index, Burning, 1, 2) };
        Assert.Equal(want, marks[..3]);
        Assert.Equal(lo, marks[0].Unit);
        Assert.Equal(-0.15f, StatusMarkers.RowOffset(0, 2, 0.3f), 5);
        Assert.Equal(0.15f, StatusMarkers.RowOffset(1, 2, 0.3f), 5);
        Assert.Equal(0f, StatusMarkers.RowOffset(0, 1, 0.3f), 5);
        Assert.Equal(-0.3f, StatusMarkers.RowOffset(0, 3, 0.3f), 5);
    }

    [Fact]
    public void Hidden_Dead_Expired_AndUnknownStatuses_DrawNothing_AFullOutputDropsAWholeRow()
    {
        Simulation sim = NoFights();
        EntityHandle a = Place(sim, 1, HeavyInfantry, At(sim, 16, 20)), b = Place(sim, 1, HeavyInfantry, At(sim, 20, 20));
        StatusStore s = sim.World.Units.Statuses;
        s.Apply(a.Index, Burning, 10f, 80, 0);
        s.Apply(a.Index, Slowed, 0.3f, 80, 0);
        s.Apply(b.Index, Burning, 10f, 80, 0);
        var marks = new StatusMark[64];
        // The fog hides b: only a's row.
        var shown = new bool[sim.World.Units.Capacity];
        shown[a.Index] = true;
        int n = StatusMarkers.Collect(s, shown, StatusCount, marks);
        Assert.Equal(2, n);
        Assert.True(marks[0].Unit == a.Index && marks[1].Unit == a.Index);
        // An entry at 0 ticks or of an unknown status id is not drawn, and the row closes up.
        s.TicksRemaining[a.Index * StatusStore.PerUnit] = 0;
        n = StatusMarkers.Collect(s, shown, StatusCount, marks);
        Assert.Equal(new StatusMark(a.Index, Slowed, 0, 1), marks[0]);
        Assert.Equal(1, n);
        Assert.Equal(0, StatusMarkers.Collect(s, shown, Slowed, marks)); // only ids below Slowed known: Burning, which is at 0 ticks
        // A dead unit's statuses are cleared with its slot.
        sim.World.Units.Free(a);
        Assert.Equal(0, StatusMarkers.Collect(s, sim.World.Units.Alive, StatusCount, marks[..0]));
        Assert.Equal(1, Collect(sim, marks));
        // An output too small for a row leaves the row out whole.
        EntityHandle c = Place(sim, 1, HeavyInfantry, At(sim, 24, 20));
        s.Apply(c.Index, Burning, 10f, 80, 0);
        s.Apply(c.Index, Slowed, 0.3f, 80, 0);
        var two = new StatusMark[2];
        n = Collect(sim, two);
        Assert.True(n <= 2 && (n == 1 || (two[0].Unit == c.Index && two[1].Unit == c.Index)), $"{n} marks");
        for (int k = 0; k < n; k++) Assert.Equal(two[k].Unit == c.Index ? 2 : 1, two[k].Row);
    }

    [Fact]
    public void FiveHundredUnits_EightStatusesEach_AllMarked_ZeroBytes()
    {
        Simulation sim = NoFights(units: 512);
        StatusStore s = sim.World.Units.Statuses;
        for (int i = 0; i < 500; i++)
        {
            EntityHandle u = Place(sim, i % 2, HeavyInfantry, new Vector2(4f + i % 40 * 2f, 4f + i / 40 * 2f));
            for (int k = 0; k < StatusStore.PerUnit; k++) s.Apply(u.Index, k, 1f, 200, 0);
        }
        var marks = new StatusMark[sim.World.Units.Capacity * StatusStore.PerUnit];
        int n = StatusMarkers.Collect(s, sim.World.Units.Alive, StatusStore.PerUnit, marks);
        Assert.Equal(500 * StatusStore.PerUnit, n);
        for (int k = 0; k < n; k++) Assert.True(marks[k].Row == 8 && marks[k].Place == k % 8 && marks[k].Status == k % 8);
        long sum = 0;
        int runs = AllocationProbe.AssertZero(() => sum += StatusMarkers.Collect(s, sim.World.Units.Alive, StatusStore.PerUnit, marks) + (long)StatusMarkers.RowOffset(3, 8, 0.3f), _out);
        _out.WriteLine($"4,000 markers: 0 bytes (runs {runs}, sum {sum})");
    }
}
