using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-H3, BUG-0401 (Producer decision 2026-10-10, docs/03 "Giving up a chase"): a player's Attack (<see cref="CombatMode.Ordered"/>)
/// whose target its walk reaches (a finite flow-field cost at the unit's cell) never counts stalled scans, so a route that
/// first leads away from the target (round a cliff by a ramp) is walked to the end; an Attack on a target it can't reach
/// still gives up by the old rule once its walk ends. The scene is <c>QaGhostViewTest</c> seed 6's ramp detour: a level-1
/// unit across a canyon from a level-1 building, the only way over leading 30 m away first.
/// </summary>
public class OrderedChaseReachTests
{
    private static int Tent => TestSim.Data.FindBuilding("whirlwind_tent");

    /// <summary>
    /// 40 x 30: two level-1 plateaus (west x 2-16, east x 21-37, rows 1-20) across a level-0 canyon (x 17-20) and open low
    /// ground south of them; each plateau has a 3-wide ramp down on its south edge (x 8-10 west, x 27-29 east, row 21) when
    /// <paramref name="eastRamp"/>, else only the west one (the east plateau is then out of reach on foot).
    /// </summary>
    private static Heightmap Canyon(bool eastRamp)
    {
        const int w = 40, h = 30;
        float height = MapConstants.LevelHeight;
        var levels = new byte[w * h];
        var elevations = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                bool plateau = y >= 1 && y <= 20 && ((x >= 2 && x <= 16) || (x >= 21 && x <= 37));
                bool ramp = y == 21 && ((x >= 8 && x <= 10) || (eastRamp && x >= 27 && x <= 29));
                levels[i] = (byte)(plateau ? 1 : 0);
                elevations[i] = plateau ? height : ramp ? height / 2 : 0f;
            }
        return new Heightmap(w, h, levels, elevations);
    }

    private static Simulation NewSim(bool eastRamp) =>
        TestSim.Explored(new Simulation(TestSim.Config(Seed: 6, PlayerCount: 2, UnitCapacity: 16, CommandCapacity: 128), Canyon(eastRamp)));

    /// <summary>Runs the two ticks after which a command enqueued now has applied.</summary>
    private static void Apply(Simulation sim)
    {
        sim.Tick();
        sim.Tick();
    }

    /// <summary>
    /// The route leads 34 m south, away from the tent (its gap grows for about 8 s, far past 10 scans), then across and up the
    /// east ramp: the unit keeps its order the whole way and lands its blows. Before M4-H3 it stood Idle after 2 s.
    /// </summary>
    [Fact]
    public void AnOrderedAttackOnAReachableBuilding_WalksARouteThatFirstLeadsAway_AndAttacksIt()
    {
        Simulation sim = NewSim(eastRamp: true);
        Assert.True(sim.World.Buildings.Spawn(1, Tent, 4 * sim.World.NavGrid.Width + 25, out EntityHandle tent));
        Spotter(sim, 0, At(sim, 30, 8)); // the tent stays in player 0's sight
        EntityHandle unit = Place(sim, 0, HeavyInfantry, At(sim, 14, 4));
        Apply(sim);
        sim.Enqueue(Command.Attack(0, unit, tent, isBuilding: true));
        Apply(sim);
        UnitStore u = sim.World.Units;
        BuildingStore b = sim.World.Buildings;
        int full = b.Hp[tent.Index];
        float farthest = 0f;
        int t = 0;
        for (; t < 1200 && b.Hp[tent.Index] == full; t++)
        {
            sim.Tick();
            Assert.True(u.Target[unit.Index] == tent, $"tick {sim.TickNumber - 1}: dropped the order ({u.State[unit.Index]}, mode {u.Mode[unit.Index]}, stall {u.ChaseStall[unit.Index]}) at {u.Position[unit.Index]}");
            farthest = MathF.Max(farthest, u.Position[unit.Index].Y);
        }
        Assert.True(b.Hp[tent.Index] < full, $"no blow in {t} ticks");
        Assert.True(farthest > At(sim, 0, 20).Y, $"setup: the route didn't lead away (farthest y {farthest})");
        Assert.Equal(CombatMode.Ordered, u.Mode[unit.Index]);
    }

    /// <summary>
    /// With no ramp up the east plateau there is no ground there at all (the pocket rule seals it), so the target is an enemy
    /// unit standing on it (straight into the store, as on a cliff-top) and the attacker stands in the canyon below: the nearest
    /// ground it can walk to is where it stands, out of reach. Its walk ends at once, so the old give-up holds: within 3 s it
    /// stands Idle with no target and no combat mode, the target remembered as given up, and it doesn't walk off after it.
    /// </summary>
    [Fact]
    public void AnOrderedAttackOnAnUnreachableTarget_StillGivesUp()
    {
        Simulation sim = NewSim(eastRamp: false);
        UnitStore u = sim.World.Units;
        EntityHandle enemy = Place(sim, 1, HeavyInfantry, At(sim, 24, 6));
        u.Hold[enemy.Index] = true;
        Spot(sim, 0, enemy);
        EntityHandle unit = Place(sim, 0, HeavyInfantry, At(sim, 19, 6));
        Apply(sim);
        Vector2 start = u.Position[unit.Index];
        sim.Enqueue(Command.Attack(0, unit, enemy, isBuilding: false));
        Apply(sim);
        Assert.Equal(enemy, u.Target[unit.Index]);
        Assert.Equal(CombatMode.Ordered, u.Mode[unit.Index]);
        int ran = RunUntil(sim, () => u.Target[unit.Index] == default && u.Mode[unit.Index] == CombatMode.None && u.State[unit.Index] == UnitState.Idle, 60);
        Assert.True(ran < 60, $"still holds the order after {ran} ticks: {u.State[unit.Index]}, mode {u.Mode[unit.Index]}, stall {u.ChaseStall[unit.Index]}");
        Assert.Equal(enemy, u.Ignored[unit.Index]);
        Assert.False(u.IgnoredIsBuilding[unit.Index]);
        for (int k = 0; k < 200; k++) sim.Tick();
        Assert.True(Vector2.Distance(start, u.Position[unit.Index]) < 2f, $"walked off: {u.Position[unit.Index]}");
        Assert.Equal(default, u.Target[unit.Index]);
    }
}
