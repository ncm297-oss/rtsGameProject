using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Vision;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA (M4-4b-2, 2026-10-10-0215): the fog with vision-blocking zones and Blinded units against an independent brute-force
/// oracle on the two-level map (cliff, ramp, the 4 m lip), three players, zones of every owner thrown on cliff edges and
/// ramps and overlapping each other. The oracle restates docs/01 2026-10-10 (c) and docs/03 "Implementation (M4-4b-2)":
/// a viewer stamps its sight now (its type's, or its smallest blind's when smaller) by the high-ground rule; then every
/// cell whose centre is within a live blocking zone of another player is visible only if one of the player's own units
/// whose center is inside that same zone sees it by the same rule. Also checks explored never regresses, and (re-derived
/// for BUG-0360's fix) that <see cref="FogStore.CanSeeUnit"/> and combat's own-sight rule (<see cref="VisionSystem.ZoneHides"/>)
/// hide a unit by its own centre, not its cell's.
/// </summary>
[Collection(SerialCollection.Name)]
public class ZoneFogOracleFuzzStressTests
{
    private static AbilityDef Sandstorm => TestSim.Data.Abilities[TestSim.Data.FindAbility("sandstorm")];

    private static readonly string[] Types =
    {
        "malazan_crossbowman", "malazan_heavy_infantry", "malazan_laborer",
        "whirlwind_raider", "whirlwind_desert_archer", "whirlwind_priest",
    };

    /// <summary>The oracle's sight for unit <paramref name="i"/>: its type's, or the smallest blind status's sight among its entries when smaller.</summary>
    private static float OracleSight(World w, int i)
    {
        UnitStore u = w.Units;
        float sight = w.Data.Units[u.TypeId[i]].Sight;
        StatusStore s = u.Statuses;
        for (int k = 0; k < s.Count[i]; k++)
        {
            StatusDef def = w.Data.Statuses[s.StatusId[i * StatusStore.PerUnit + k]];
            if (def.Kind == StatusKind.Blind && def.Sight < sight) sight = def.Sight;
        }
        return sight;
    }

    private static bool Sees(Heightmap hm, int vx, int vy, float sight, int x, int y)
    {
        float d = Vector2.Distance(FogMaps.Cell(vx, vy), FogMaps.Cell(x, y));
        return d <= sight && (hm.LevelAt(x, y) <= hm.LevelAt(vx, vy) || d <= VisionConstants.LipRadius);
    }

    private static (int X, int Y) CellXY(Heightmap hm, Vector2 p) =>
        (Math.Clamp((int)(p.X / MapConstants.CellSize), 0, hm.Width - 1), Math.Clamp((int)(p.Y / MapConstants.CellSize), 0, hm.Height - 1));

    private static bool InZone(ZoneStore z, int k, Vector2 p) => Vector2.DistanceSquared(p, z.Center[k]) <= z.Radius(k) * z.Radius(k);

    /// <summary>The oracle's visible set for <paramref name="player"/> (units only; the scenes have no buildings); <paramref name="zones"/> false leaves the zones out (the circles' cover alone, Blinded sight included).</summary>
    private static bool[] Expected(World w, int player, bool zones = true)
    {
        Heightmap hm = w.Heightmap;
        UnitStore u = w.Units;
        ZoneStore z = w.Zones;
        var seen = new bool[hm.Width * hm.Height];
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.Owner[i] != player) continue;
            (int vx, int vy) = CellXY(hm, u.Position[i]);
            float sight = OracleSight(w, i);
            for (int y = 0; y < hm.Height; y++)
                for (int x = 0; x < hm.Width; x++)
                    if (Sees(hm, vx, vy, sight, x, y)) seen[y * hm.Width + x] = true;
        }
        if (!zones) return seen;
        for (int y = 0; y < hm.Height; y++)
            for (int x = 0; x < hm.Width; x++)
            {
                int c = y * hm.Width + x;
                if (!seen[c]) continue;
                Vector2 cc = FogMaps.Cell(x, y);
                for (int k = 0; k < z.Capacity && seen[c]; k++)
                {
                    if (!z.Alive[k] || !z.BlocksVision(k) || z.Owner[k] == player || !InZone(z, k, cc)) continue;
                    bool fromInside = false;
                    for (int i = 0; i < u.Capacity && !fromInside; i++)
                    {
                        if (!u.Alive[i] || u.Owner[i] != player || !InZone(z, k, u.Position[i])) continue;
                        (int vx, int vy) = CellXY(hm, u.Position[i]);
                        fromInside = Sees(hm, vx, vy, OracleSight(w, i), x, y);
                    }
                    if (!fromInside) seen[c] = false;
                }
            }
        return seen;
    }

    private static void AssertFog(World w, int players, string context, bool[][] everVisible)
    {
        for (int p = 0; p < players; p++)
        {
            bool[] expected = Expected(w, p);
            ReadOnlySpan<byte> vis = w.Fog.Visibility(p);
            var wrong = new List<string>();
            for (int c = 0; c < expected.Length; c++)
            {
                bool actual = vis[c] == VisionConstants.Visible;
                if (actual) everVisible[p][c] = true;
                if (actual != expected[c] && wrong.Count < 6)
                    wrong.Add($"({c % w.Heightmap.Width}, {c / w.Heightmap.Width}) L{w.Heightmap.Levels[c]}: fog {vis[c]}, oracle {(expected[c] ? "visible" : "hidden")}");
                if (everVisible[p][c] && vis[c] == VisionConstants.Unexplored) wrong.Add($"cell {c} lost its explored state");
            }
            Assert.True(wrong.Count == 0, $"{context} player {p}: {string.Join("; ", wrong)}");
            AssertUnits(w, p, context);
        }
    }

    /// <summary>
    /// BUG-0360's unit rule (docs/02 "Zones", docs/03 "Implementation (M4-4b-2)"), re-derived for the oracle: an enemy unit
    /// whose own centre lies in one or more blocking zones of other owners than <paramref name="player"/> is seen only when,
    /// for each of those zones, a unit of <paramref name="player"/> whose centre is in that zone sees the unit's cell by the
    /// stamp's rule; a unit outside every such zone is seen when <paramref name="player"/>'s circles cover its cell (the
    /// zones aside). Checked right after an update, so the positions are the update's. Own units are always seen.
    /// </summary>
    private static void AssertUnits(World w, int player, string context)
    {
        Heightmap hm = w.Heightmap;
        UnitStore u = w.Units;
        ZoneStore z = w.Zones;
        bool[] plain = Expected(w, player, zones: false);
        for (int j = 0; j < u.Capacity; j++)
        {
            if (!u.Alive[j] || u.Owner[j] == player) continue;
            (int x, int y) = CellXY(hm, u.Position[j]);
            bool inAny = false, expected = true;
            for (int k = 0; k < z.Capacity; k++)
            {
                if (!z.Alive[k] || !z.BlocksVision(k) || z.Owner[k] == player || !InZone(z, k, u.Position[j])) continue;
                inAny = true;
                bool fromInside = false;
                for (int i = 0; i < u.Capacity && !fromInside; i++)
                {
                    if (!u.Alive[i] || u.Owner[i] != player || !InZone(z, k, u.Position[i])) continue;
                    (int vx, int vy) = CellXY(hm, u.Position[i]);
                    fromInside = Sees(hm, vx, vy, OracleSight(w, i), x, y);
                }
                if (!fromInside) expected = false;
            }
            if (!inAny) expected = plain[y * hm.Width + x];
            Assert.True(expected == w.Fog.CanSeeUnit(player, j),
                $"{context} player {player}: unit {j} at {u.Position[j]} (cell {x}, {y}, in a zone {inAny}): fog {!expected}, oracle {expected}");
        }
    }

    /// <summary>
    /// The fog stays exactly the oracle's with up to 6 blocking zones of all three owners (cliff edges, the ramp, overlaps)
    /// and Blinded units on both levels, over 40 random scenes a seed, then 200 ticks of random walking (combat off) with
    /// zones made and expiring, checked after every fog update.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(7UL)]
    [InlineData(42UL)]
    [InlineData(99UL)] // QA M4-H2 (2026-10-10-0624): three more seeds on the re-derived unit rule
    [InlineData(2026UL)]
    [InlineData(31337UL)]
    public void TheFogWithZonesAndBlinds_MatchesTheBruteForceOracle_OnTheTwoLevelMap(ulong seed)
    {
        var rng = new SimRng(seed, 4242);
        int blinded = TestSim.Data.FindStatus("blinded");
        int checks = 0, hiddenByZone = 0;
        for (int scene = 0; scene < 40; scene++)
        {
            Simulation sim = FogMaps.Sim(FogMaps.TwoLevel(), combat: false, units: 64, players: 3);
            World w = sim.World;
            UnitStore u = w.Units;
            NavGrid g = w.NavGrid;
            int n = rng.NextInt(4, 30);
            for (int k = 0; k < n; k++)
            {
                int x, y;
                do
                {
                    // Bias toward the cliff (x 18-22) and the ramp (x 14-19, y 18-20).
                    x = rng.NextInt(0, 3) == 0 ? rng.NextInt(16, 24) : rng.NextInt(1, FogMaps.Size - 1);
                    y = rng.NextInt(0, 3) == 0 ? rng.NextInt(16, 23) : rng.NextInt(1, FogMaps.Size - 1);
                }
                while (!g.IsPassable(x, y));
                int owner = rng.NextInt(0, 3);
                int type = TestSim.Data.FindUnit(Types[rng.NextInt(0, Types.Length)]);
                EntityHandle h = Place(sim, owner, type, FogMaps.Cell(x, y, rng.NextFloat() * 1.9f - 0.95f) + new Vector2(0f, rng.NextFloat() * 1.9f - 0.95f));
                if (rng.NextInt(0, 4) == 0) StatusSystem.Apply(w, h.Index, blinded, 0f, 10_000, (owner + 1) % 3);
            }
            int zones = rng.NextInt(1, 7);
            for (int k = 0; k < zones; k++)
            {
                Vector2 centre = rng.NextInt(0, 2) == 0
                    ? new Vector2(FogMaps.PlateauX * MapConstants.CellSize + (rng.NextFloat() * 8f - 4f), rng.NextFloat() * 80f) // the cliff line
                    : new Vector2(rng.NextFloat() * 80f, rng.NextFloat() * 80f);
                ZoneSystem.Create(w, rng.NextInt(0, 3), Sandstorm, centre);
            }
            var ever = new[] { new bool[1600], new bool[1600], new bool[1600] };
            w.Fog.Update();
            AssertFog(w, 3, $"seed {seed} scene {scene} (static)", ever);
            checks++;
            for (int p = 0; p < 3; p++)
            {
                bool[] plain = FogMaps.Expected(w, p);
                ReadOnlySpan<byte> vis = w.Fog.Visibility(p);
                for (int c = 0; c < plain.Length; c++) if (plain[c] && vis[c] != VisionConstants.Visible) hiddenByZone++;
            }
            // Combat's own-sight rule agrees with the unit rule (BUG-0360): for every pair of a viewer and an enemy unit,
            // ZoneHides says "hidden" exactly when a blocking zone of another owner than the viewer's holds the target's own
            // centre and the viewer's center is outside that zone.
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                for (int j = 0; j < u.Capacity; j++)
                {
                    if (!u.Alive[j] || u.Owner[j] == u.Owner[i]) continue;
                    bool expect = false;
                    for (int k = 0; k < w.Zones.Capacity; k++)
                        if (w.Zones.Alive[k] && w.Zones.BlocksVision(k) && w.Zones.Owner[k] != u.Owner[i] && InZone(w.Zones, k, u.Position[j]) && !InZone(w.Zones, k, u.Position[i]))
                            expect = true;
                    Assert.True(expect == VisionSystem.ZoneHides(w, u.Owner[i], u.Position[i], true, u.Position[j]),
                        $"seed {seed} scene {scene}: ZoneHides({i} -> {j}) disagrees with the unit rule");
                }
            }
            // Then walk: random Moves every 10 ticks, a fresh zone every 50; compare after each update tick.
            for (int t = 0; t < 200; t++)
            {
                if (t % 10 == 0)
                    for (int i = 0; i < u.Capacity; i++)
                        if (u.Alive[i] && rng.NextInt(0, 3) == 0)
                            sim.Enqueue(Command.Move(u.Owner[i], new EntityHandle(i, u.Generation[i]), new Vector2(rng.NextFloat() * 76f + 2f, rng.NextFloat() * 76f + 2f), false));
                if (t % 50 == 25) ZoneSystem.Create(w, rng.NextInt(0, 3), Sandstorm, u.Alive[0] ? u.Position[0] : new Vector2(40f, 40f));
                sim.Tick();
                if (!VisionSystem.IsUpdateTick(sim.TickNumber - 1)) continue;
                AssertFog(w, 3, $"seed {seed} scene {scene} tick {sim.TickNumber - 1}", ever);
                checks++;
            }
        }
        Assert.True(hiddenByZone > 100, $"the zones hid only {hiddenByZone} cells: the fuzz isn't exercising the blocker");
        Assert.True(checks > 1000);
    }
}
