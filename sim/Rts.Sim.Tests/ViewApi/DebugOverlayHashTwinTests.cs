using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M2-5 read-only proof: the debug overlay's helpers, run several times a tick with the overlay toggling, the selection changing and fields churning, never change the state hash.</summary>
public class DebugOverlayHashTwinTests
{
    private static Simulation Spawned(ulong seed, int perPlayer)
    {
        var sim = new Simulation(TestSim.Config(seed, 2, 2000, 4096));
        float maxR = TestSim.Data.Units.Max(u => u.Radius);
        for (int p = 0; p < 2; p++)
        {
            Vector2[] spots = StartLayout.Block(sim.World.NavGrid, perPlayer, p == 0, maxR);
            for (int k = 0; k < spots.Length; k++) sim.Enqueue(Command.SpawnUnit(p, k % TestSim.UnitTypeCount, spots[k]));
        }
        return sim;
    }

    [Theory]
    [InlineData(1UL, 300)]
    [InlineData(5UL, 1000)]
    public void OverlayHelpers_EveryFrame_HashEqualsABareTwin_EveryTick(ulong seed, int perPlayer)
    {
        Simulation a = Spawned(seed, perPlayer), b = Spawned(seed, perPlayer);
        World w = a.World;
        UnitStore u = w.Units;
        NavGrid g = w.NavGrid;
        var nav = new NavOverlayBuilder(w.Heightmap);
        var arrows = new FlowArrowLayout();
        var ring = new TickTimeRing();
        var selection = new SelectionSet(u.Capacity);
        int[] passable = Enumerable.Range(0, g.Width * g.Height).Where(c => g.IsPassable(c % g.Width, c / g.Width)).ToArray();
        bool on = true;
        long sink = 0;

        for (int tick = 0; tick < 240; tick++)
        {
            // Orders: every 30 ticks, the live units go round-robin to 48 goals (more than the cache holds), so fields churn.
            if (tick % 30 == 2)
            {
                int n = 0, goals = 48;
                for (int i = 0; i < u.Capacity; i++)
                {
                    if (!u.Alive[i]) continue;
                    int cell = passable[(tick * 131 + (n++ % goals) * (passable.Length / goals)) % passable.Length];
                    Command cmd = Command.Move(u.Owner[i], new EntityHandle(i, u.Generation[i]), g.CellCenter(cell % g.Width, cell / g.Width));
                    a.Enqueue(cmd);
                    b.Enqueue(cmd);
                }
            }
            // Passability changes once (both twins), so cached fields go stale mid-march.
            if (tick == 100)
            {
                g.BumpVersionForTests();
                b.World.NavGrid.BumpVersionForTests();
            }

            // The selection changes every 7 ticks: a different slice of own units.
            if (tick % 7 == 0)
            {
                selection.Clear();
                for (int i = tick % 50; i < u.Capacity && selection.Count < 40; i += 3)
                    if (u.Alive[i] && u.Owner[i] == 0) selection.Add(new EntityHandle(i, u.Generation[i]));
            }
            // The overlay toggles every 11 ticks.
            if (tick % 11 == 0)
            {
                on = !on;
                if (on) arrows.Invalidate();
            }

            ulong before = a.StateHash();
            for (int frame = 0; frame < 3 && on; frame++)
            {
                selection.Prune(u.Alive, u.Generation);
                nav.Refresh(g);
                int goal = FlowArrowLayout.GoalOf(selection.Items, u.Alive, u.Generation, u.GoalCell);
                var focus = new Vector2(20f + tick * 1.7f + frame, 128f + frame * 3f);
                arrows.Refresh(w.FlowFields, g, goal, focus);
                // Peek a few more goals the way a panning camera would.
                arrows.Refresh(w.FlowFields, g, passable[(tick * 7 + frame) % passable.Length], focus);
                sink += arrows.Count + DebugCounts.Moving(u.Alive, u.State) + w.FlowFields.Count + u.Count;
                ring.Add(tick);
            }
            Assert.Equal(before, a.StateHash());

            a.Tick();
            b.Tick();
            Assert.True(b.StateHash() == a.StateHash(), $"tick {a.TickNumber}: overlay-read sim diverged from its twin");
        }
        Assert.True(sink > 0);
        Assert.True(nav.Builds == 2, $"nav overlay filled {nav.Builds} times, expected 2 (start and the version bump)");
        Assert.True(w.FlowFields.BuildCount > 0);
    }
}
