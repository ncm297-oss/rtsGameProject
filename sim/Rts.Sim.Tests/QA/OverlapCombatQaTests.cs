using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// QA (M5-3 re-check round 1, session 2026-10-12-1930): BUG-0590's fix puts an overlapping body (a negative edge-to-edge gap) in
/// reach of an attack with no minimum range. Before it, two enemy units standing on one point never struck each other, so the
/// strike, projectile, splash, facing and push code never ran at a distance of 0. Every ordered pair of shipped unit types, one
/// of each player, spawned on the exact same point with combat on, runs 400 ticks: positions stay finite and on the map, hp
/// stays in 0..max, and the pairs where one side is a fighter (not a worker) with a unit attack and no minimum range see damage done
/// (measured: 196 pairs; the 12 quiet ones are a worker against a worker, a Catapult or a Battering Ram).
/// </summary>
public class OverlapCombatQaTests
{
    private readonly ITestOutputHelper _out;

    public OverlapCombatQaTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void EveryPairOfUnitTypes_OnOnePoint_FightsWithoutNaN_AndHpStaysInRange()
    {
        GameData d = TestSim.Data;
        int n = d.Units.Length, pairs = 0, quiet = 0;
        var quietPairs = new List<string>();
        for (int a = 0; a < n; a++)
            for (int b = 0; b < n; b++)
            {
                Simulation sim = CombatScenes.Flat(24, 16);
                World w = sim.World;
                Vector2 at = w.NavGrid.CellCenter(12, 12) + new Vector2(0.137f, -0.211f);
                EntityHandle ha = CombatScenes.Place(sim, 0, a, at);
                EntityHandle hb = CombatScenes.Place(sim, 1, b, at);
                int hpA = w.Units.Hp[ha.Index], hpB = w.Units.Hp[hb.Index];
                bool damaged = false;
                for (int t = 0; t < 400; t++)
                {
                    sim.Tick();
                    for (int i = 0; i < w.Units.Capacity; i++)
                    {
                        if (!w.Units.Alive[i]) continue;
                        Vector2 p = w.Units.Position[i];
                        Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y), $"{d.Units[a].Key} v {d.Units[b].Key}: unit {i} at {p} (tick {t})");
                        Assert.True(p.X >= 0 && p.Y >= 0 && p.X <= 24 * 2f && p.Y <= 24 * 2f, $"{d.Units[a].Key} v {d.Units[b].Key}: unit {i} off the map at {p}");
                        Assert.InRange(w.Units.Hp[i], 0, w.MaxHp(i));
                    }
                    if (!w.Units.IsAlive(ha) || !w.Units.IsAlive(hb) || w.Units.Hp[ha.Index] < hpA || w.Units.Hp[hb.Index] < hpB) damaged = true;
                }
                pairs++;
                bool armed = Hits(d.Units[a]) || Hits(d.Units[b]);
                if (armed && !damaged)
                {
                    quiet++;
                    quietPairs.Add($"{d.Units[a].Key} v {d.Units[b].Key}");
                }
            }
        _out.WriteLine($"{pairs} pairs; {quiet} armed pairs with no damage: {string.Join(", ", quietPairs)}");
        Assert.Empty(quietPairs);
    }

    /// <summary>Whether a unit of this type strikes units from a standing overlap: not a worker (it never fights unless attack-moved), an attack with damage, no minimum range, aimed at units.</summary>
    private static bool Hits(UnitDef u) =>
        u.Slot != UnitSlot.Worker && u.Attack.Value > 0 && u.Attack.MinRange <= 0f && u.Attack.Targets != AttackTargets.Buildings;
}
