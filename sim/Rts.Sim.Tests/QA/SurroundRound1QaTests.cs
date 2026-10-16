using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA re-check of M5-P1 fix round 1 (session 2026-10-15-2325, BUG-0731): building chases in every mode now keep a stalled
/// walk to the surround spot they hold while <c>ReachableOnFoot</c> says the walk reaches it. Attacks: attack-movers from
/// every side and on two footprints keep the building; a claimed spot no walk reaches (a sealed pocket) still ends the
/// chase in bounded time (no pacing); ranged attackers and unit chases are untouched (rule on and off hash equal).
/// </summary>
[Collection(SerialCollection.Name)]
public class SurroundRound1QaTests
{
    private const int MapSize = 64;
    private readonly ITestOutputHelper _out;

    public SurroundRound1QaTests(ITestOutputHelper output) => _out = output;

    private static int BuildingOf(GameData data, int faction, BuildingSlot slot)
    {
        for (int b = 0; b < data.Buildings.Length; b++)
            if (data.Buildings[b].Faction == faction && data.Buildings[b].Slot == slot) return b;
        throw new InvalidOperationException($"no {slot} for faction {faction}");
    }

    private static Simulation NewSim(ulong seed, int units, Heightmap? map = null) =>
        new(TestSim.Config(seed, 2, units, 16 * units + 64), map ?? LocalMovementTests.Flat(MapSize));

    private static int HeldSpot(World w, int i)
    {
        int c = w.SurroundClaim[i];
        return c >= 0 && w.SurroundHolder[c] == i + 1 ? c : -1;
    }

    private static void TopUp(World w, EntityHandle b)
    {
        if (w.Buildings.IsAlive(b)) w.Buildings.SetHp(b.Index, w.Data.Buildings[w.Buildings.TypeId[b.Index]].Hp);
    }

    // Attack-movers on open ground, from each side, on a House (20 spots) and a 3x3 hall, more attackers than spots: every
    // one keeps the building (the waiters by the queue rule, the jammed walkers by BUG-0731's fix) and the ring fills.
    [Theory]
    [InlineData("malazan_heavy_infantry", BuildingSlot.House, 0, 30)]
    [InlineData("malazan_heavy_infantry", BuildingSlot.House, 1, 30)]
    [InlineData("malazan_heavy_infantry", BuildingSlot.House, 2, 30)]
    [InlineData("malazan_heavy_infantry", BuildingSlot.House, 3, 30)]
    [InlineData("whirlwind_raider", BuildingSlot.House, 1, 20)]
    [InlineData("whirlwind_raider", BuildingSlot.House, 3, 20)]
    [InlineData("malazan_heavy_infantry", BuildingSlot.InfantryHall, 0, 40)]
    [InlineData("whirlwind_raider", BuildingSlot.InfantryHall, 2, 40)]
    public void AttackMove_FromEverySide_NoGiveUpWhileTheBuildingStands(string attacker, BuildingSlot slot, int side, int n)
    {
        Simulation sim = NewSim(41 + (ulong)side, n + 8);
        World w = sim.World;
        UnitStore u = w.Units;
        int type = w.Data.FindUnit(attacker);
        TestSim.KeepIn(sim, 1);
        Assert.True(w.Buildings.Spawn(1, BuildingOf(w.Data, 1 - w.Data.Units[type].Faction, slot), 30 * MapSize + 30, out EntityHandle b));
        Spotter(sim, 0, At(sim, 31, 28));
        // side 0 west, 1 north (+y), 2 east, 3 south: a block 12 m out, attack-moving to a point 12 m past the building.
        Vector2 c = At(sim, 31, 31);
        Vector2 dir = side switch { 0 => new(-1, 0), 1 => new(0, 1), 2 => new(1, 0), _ => new(0, -1) };
        Vector2 across = new(-dir.Y, dir.X);
        var a = new EntityHandle[n];
        for (int k = 0; k < n; k++)
        {
            int rank = k / 6, file = k % 6;
            a[k] = Place(sim, 0, type, c + dir * (12f + rank * 1.8f) + across * ((file - 2.5f) * 1.8f));
            sim.Enqueue(Command.AttackMove(0, a[k], c - dir * 14f));
        }
        var before = new int[n];
        var hit = new bool[n];
        int maxStriking = 0;
        for (int t = 0; t < 600; t++)
        {
            for (int k = 0; k < n; k++) before[k] = u.CooldownTicks[a[k].Index];
            TopUp(w, b);
            sim.Tick();
            int striking = 0;
            for (int k = 0; k < n; k++)
            {
                int i = a[k].Index;
                if (u.CooldownTicks[i] > before[k]) hit[k] = true;
                if (u.State[i] == UnitState.Attacking) striking++;
                if (u.GiveUps[i] > 0)
                    Assert.Fail($"{attacker} {slot} side {side} tick {sim.TickNumber}: unit {i} gave the building up (state {u.State[i]}, mode {u.Mode[i]}, held spot {HeldSpot(w, i)}, at {u.Position[i]})");
            }
            maxStriking = Math.Max(maxStriking, striking);
        }
        _out.WriteLine($"{attacker} on a {slot} from side {side}: {hit.Count(x => x)} / {n} struck in 600 ticks, at most {maxStriking} at once, no give-up");
        Assert.True(maxStriking >= 14, $"only {maxStriking} striking at once");
    }

    // Attack-movers past a House whose near face is a sealed pocket (open cells walled in by the attackers' own Houses,
    // store seam; BUG-0732's scene): a unit whose claimed spot is in the pocket can't walk there. It must still give the
    // House up in bounded time (the old rule) and not pace the wall: the longest run of ticks any unit spends holding a
    // pocket spot while standing outside the pocket stays within a few give-up windows.
    [Fact]
    public void AttackMove_ClaimedSpotInASealedPocket_StillGivesUp_NoPacing()
    {
        Simulation sim = NewSim(31, 64);
        World w = sim.World;
        UnitStore u = w.Units;
        TestSim.KeepIn(sim, 0, nearCorner: true);
        int hi = HeavyInfantry;
        int wall = BuildingOf(w.Data, w.Data.Units[hi].Faction, BuildingSlot.House);
        foreach ((int x, int y) in new[] { (31, 30), (32, 28), (32, 32) })
            Assert.True(w.Buildings.Spawn(0, wall, y * MapSize + x, out _), $"wall at ({x}, {y})");
        TestSim.KeepIn(sim, 1);
        Assert.True(w.Buildings.Spawn(1, BuildingOf(w.Data, 1 - w.Data.Units[hi].Faction, BuildingSlot.House), 30 * MapSize + 34, out EntityHandle b));
        Assert.True(w.NavGrid.IsPassable(33, 30) && w.NavGrid.IsPassable(33, 31), "the pocket should be open ground");
        Spotter(sim, 0, At(sim, 39, 30));
        var a = new EntityHandle[20];
        for (int k = 0; k < 20; k++)
        {
            a[k] = Place(sim, 0, hi, At(sim, 24 - k / 5, 29 + k % 5));
            sim.Enqueue(Command.AttackMove(0, a[k], At(sim, 50, 31)));
        }
        var run = new int[20];
        var gu = new int[20];
        int longest = 0, pocketClaimTicks = 0, giveUps = 0;
        for (int t = 0; t < 1200; t++)
        {
            TopUp(w, b);
            sim.Tick();
            for (int k = 0; k < 20; k++)
            {
                int i = a[k].Index;
                if (u.GiveUps[i] > gu[k])
                {
                    gu[k] = u.GiveUps[i];
                    _out.WriteLine($"  tick {sim.TickNumber}: unit {i} gave up (state {u.State[i]}, mode {u.Mode[i]}, claim {w.SurroundClaim[i] - b.Index * w.SurroundSpots}, held {HeldSpot(w, i)}, goal {u.Goal[i]}, at {u.Position[i]})");
                }
                int key = HeldSpot(w, i);
                bool pocketSpot = false;
                if (key >= 0 && u.TargetIsBuilding[i] && u.Target[i] == b)
                {
                    // A 2x2's ring (SurroundRule.SpotPoint): 4 south, 4 north, then 4 west (8..11), 4 east, 4 corners. The
                    // west face is the pocket, cells (33, 30) and (33, 31).
                    int spot = key - b.Index * w.SurroundSpots;
                    pocketSpot = spot >= 8 && spot < 12; // walking to it, or standing Idle where its walk ended
                }
                bool inPocket = w.NavGrid.WorldToCell(u.Position[i], out int px, out int py) && px == 33 && (py == 30 || py == 31);
                run[k] = pocketSpot && !inPocket ? run[k] + 1 : 0;
                if (pocketSpot && !inPocket) pocketClaimTicks++;
                longest = Math.Max(longest, run[k]);
            }
        }
        foreach (EntityHandle h in a) giveUps += u.GiveUps[h.Index];
        _out.WriteLine($"sealed pocket, attack-move: longest hold of a pocket spot from outside it {longest} ticks, {pocketClaimTicks} unit-ticks in all, give-ups {giveUps}");
        Assert.True(pocketClaimTicks > 0, "nobody claimed a pocket spot: the scene didn't exercise the unreachable case");
        // A stall ends after GiveUpScans scans of ScanInterval ticks once progress stops; allow the approach walk on top.
        Assert.True(longest <= 3 * CombatConstants.GiveUpScans * CombatConstants.ScanInterval,
            $"a unit held an unreachable pocket spot from outside for {longest} ticks without giving up");
    }

    // Ranged attackers (no spot) attack-moving past a House, and melee attack-movers fighting enemy units (no building):
    // the rule doesn't apply, so rule on and off must give the same hash on every tick (BUG-0731's fix is gated by it).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RangedAndUnitChases_RuleOnAndOff_HashEqualEveryTick(bool unitFight)
    {
        var sims = new Simulation[2];
        for (int r = 0; r < 2; r++)
        {
            Simulation sim = NewSim(53, 80);
            World w = sim.World;
            w.SurroundRuleOn = r == 1;
            TestSim.KeepIn(sim, 1);
            if (unitFight)
            {
                for (int k = 0; k < 20; k++)
                {
                    EntityHandle m = Place(sim, 0, HeavyInfantry, At(sim, 20 + k % 5, 26 + k / 5));
                    sim.Enqueue(Command.AttackMove(0, m, At(sim, 44, 28)));
                    EntityHandle e = Place(sim, 1, Raider, At(sim, 34 + k % 5, 26 + k / 5));
                    sim.Enqueue(Command.AttackMove(1, e, At(sim, 12, 28)));
                }
            }
            else
            {
                Assert.True(w.Buildings.Spawn(1, BuildingOf(w.Data, 1, BuildingSlot.House), 30 * MapSize + 34, out _));
                Spotter(sim, 0, At(sim, 39, 30));
                for (int k = 0; k < 24; k++)
                {
                    EntityHandle x = Place(sim, 0, Crossbowman, At(sim, 33 + k % 6, 20 - k / 6));
                    sim.Enqueue(Command.AttackMove(0, x, At(sim, 35, 44)));
                }
            }
            sims[r] = sim;
        }
        int attacking = 0;
        for (int t = 0; t < 800; t++)
        {
            sims[0].Tick();
            sims[1].Tick();
            Assert.True(sims[0].StateHash() == sims[1].StateHash(), $"{(unitFight ? "unit fight" : "ranged on a House")}: rule on and off part at tick {sims[0].TickNumber}");
            UnitStore u = sims[1].World.Units;
            for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.State[i] == UnitState.Attacking) attacking++;
        }
        _out.WriteLine($"{(unitFight ? "unit fight" : "ranged on a House")}: equal for 800 ticks, {attacking} attacking unit-ticks");
        Assert.True(attacking > 200, $"the scene barely fought: {attacking} attacking unit-ticks");
    }
}
