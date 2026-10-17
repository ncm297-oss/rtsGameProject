using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Movement;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// Pathing slice 2 (docs/03 "Implementation (pathing slice 2)"): a melee unit whose way is blocked by its own side's
/// planted fighters walks round the end of their line. The owner's scene: flat open ground, 10 ranged units planted in a
/// row 1 m apart attacking from their max range, 10 melee units 3-4 m behind the row's middle. Every unit's hit points
/// (and the building's) are topped up between ticks, so the counts are about standing room, not damage.
/// <c>World.FlankRuleOn</c> off gives the before counts. Serial: one row probes allocation.
/// </summary>
[Collection(SerialCollection.Name)]
public class FlankTests
{
    private const int MapSize = 64;
    private const int HallX = 40, HallY = 30; // a 4x4 Town Hall: footprint x 80-88 m, y 60-68 m
    private const float RowX = 80f - 0.4f - 14.9f; // the row's centres: 14.9 m from the footprint, inside a 15 m range
    private const float MidY = 64f;
    private const int Window = 200; // 10 s

    // The enemy unit target stands this far beyond the row (m): its far side within a retaliator's leash (its sight, 14 m,
    // from where it stood 3-4 m behind the row), so a leash pull-back isn't counted as a give-up of the chase.
    private const float TargetBeyond = 6f;

    private readonly ITestOutputHelper _out;

    public FlankTests(ITestOutputHelper output) => _out = output;

    public enum Order { Attack, AttackMove, Retaliate, Move }

    /// <summary>What the line shoots: the enemy Town Hall, or an enemy unit <see cref="TargetBeyond"/> beyond the row (a holding siege unit).</summary>
    public enum Aim { Hall, Unit }

    private sealed class Staged
    {
        public required Simulation Sim;
        public required EntityHandle[] Row;
        public required EntityHandle[] Melee;
        public EntityHandle Hall;
        public EntityHandle Target;
        public bool TargetIsBuilding;
        public Vector2 MovePoint;
    }

    /// <summary>The row's k-th centre (k = 0..9, 1 m apart, centred on the hall's middle).</summary>
    private static Vector2 RowAt(int k) => new(RowX, MidY + (k - 4.5f));

    /// <summary>The k-th melee unit's spawn: two ranks of five, 3 and 4 m behind the row, 1 m apart.</summary>
    private static Vector2 MeleeAt(int k) => new(RowX - 3f - k / 5, MidY + (k % 5 - 2f));

    /// <summary>
    /// The scene, staged: player 1's Town Hall (and for <see cref="Aim.Unit"/> a holding siege unit 8 m beyond the row),
    /// the row of <paramref name="shooter"/> attacking it until all are planted, then the <paramref name="melee"/> placed
    /// and given <paramref name="order"/> (not yet applied). <paramref name="reversed"/> spawns both groups in reverse.
    /// </summary>
    private static Staged Scene(string shooter, string melee, Aim aim, Order order, bool flank, bool reversed = false, ulong seed = 7, bool reverseMelee = true)
    {
        var sim = new Simulation(TestSim.Config(seed, 2, 40, 256), LocalMovementTests.Flat(MapSize));
        World w = sim.World;
        UnitStore u = w.Units;
        w.FlankRuleOn = flank;
        TestSim.KeepIn(sim, 1);
        int shooterType = TestSim.Data.FindUnit(shooter), meleeType = TestSim.Data.FindUnit(melee);
        int enemyFaction = 1 - TestSim.Data.Units[meleeType].Faction;
        Assert.True(w.Buildings.Spawn(1, BuildingOf(enemyFaction, BuildingSlot.TownHall), HallY * MapSize + HallX, out EntityHandle hall));
        var staged = new Staged { Sim = sim, Row = new EntityHandle[10], Melee = new EntityHandle[10], Hall = hall };
        if (aim == Aim.Unit)
        {
            staged.Target = Place(sim, 1, SiegeOf(enemyFaction), new Vector2(RowX + TargetBeyond, MidY));
            u.Hold[staged.Target.Index] = true;
        }
        else
        {
            staged.Target = hall;
            staged.TargetIsBuilding = true;
        }
        for (int n = 0; n < 10; n++)
        {
            int k = reversed ? 9 - n : n;
            staged.Row[k] = Place(sim, 0, shooterType, RowAt(k));
            sim.Enqueue(Command.Attack(0, staged.Row[k], staged.Target, staged.TargetIsBuilding));
        }
        for (int t = 0; t < 40 && !staged.Row.All(h => u.State[h.Index] == UnitState.Attacking); t++) Tick(staged);
        Assert.True(staged.Row.All(h => u.State[h.Index] == UnitState.Attacking), "the row didn't plant");
        for (int n = 0; n < 10; n++)
        {
            int k = reversed && reverseMelee ? 9 - n : n;
            staged.Melee[k] = Place(sim, 0, meleeType, MeleeAt(k));
        }
        staged.MovePoint = new Vector2(RowX + 8f, MidY);
        Vector2 past = aim == Aim.Hall ? new Vector2(96f, MidY) : new Vector2(RowX + TargetBeyond + 6f, MidY);
        for (int n = 0; n < 10; n++)
        {
            int k = reversed && reverseMelee ? 9 - n : n;
            EntityHandle h = staged.Melee[k];
            switch (order)
            {
                case Order.Attack: sim.Enqueue(Command.Attack(0, h, staged.Target, staged.TargetIsBuilding)); break;
                case Order.AttackMove: sim.Enqueue(Command.AttackMove(0, h, past)); break;
                case Order.Move: sim.Enqueue(Command.Move(0, h, staged.MovePoint)); break;
                case Order.Retaliate: break; // Idle: its own scan takes the target in sight
            }
        }
        return staged;
    }

    private static int BuildingOf(int faction, BuildingSlot slot)
    {
        GameData data = TestSim.Data;
        for (int b = 0; b < data.Buildings.Length; b++)
            if (data.Buildings[b].Faction == faction && data.Buildings[b].Slot == slot) return b;
        throw new InvalidOperationException($"no {slot} for faction {faction}");
    }

    /// <summary>The faction's biggest unit (a siege engine): a target with room round it for ten melee units.</summary>
    private static int SiegeOf(int faction)
    {
        GameData data = TestSim.Data;
        int best = -1;
        for (int t = 0; t < data.Units.Length; t++)
            if (data.Units[t].Faction == faction && (best < 0 || data.Units[t].Radius > data.Units[best].Radius)) best = t;
        return best;
    }

    /// <summary>One tick, every unit's and the hall's hit points topped up first.</summary>
    private static void Tick(Staged s)
    {
        World w = s.Sim.World;
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i]) u.Hp[i] = w.Data.Units[u.TypeId[i]].Hp;
        if (w.Buildings.IsAlive(s.Hall)) w.Buildings.SetHp(s.Hall.Index, w.Data.Buildings[w.Buildings.TypeId[s.Hall.Index]].Hp);
        s.Sim.Tick();
    }

    private readonly record struct Outcome(int Struck, int GaveUp, int Arrived);

    /// <summary>
    /// Runs <see cref="Window"/> ticks: how many melee units struck (a swing started, or stood Attacking in reach), how many
    /// gave a target up, and (for a Move) how many stood arrived at its end.
    /// </summary>
    private static Outcome Run(Staged s)
    {
        UnitStore u = s.Sim.World.Units;
        int n = s.Melee.Length;
        var struck = new bool[n];
        var gaveUp = new bool[n];
        var before = new int[n];
        var arrived = new bool[n];
        int goalCell = s.Sim.World.NavGrid.WorldToCell(s.MovePoint, out int gx, out int gy) ? gy * s.Sim.World.NavGrid.Width + gx : -1;
        for (int t = 0; t < Window; t++)
        {
            for (int k = 0; k < n; k++) before[k] = u.CooldownTicks[s.Melee[k].Index];
            Tick(s);
            for (int k = 0; k < n; k++)
            {
                int i = s.Melee[k].Index;
                if (u.CooldownTicks[i] > before[k] || u.State[i] == UnitState.Attacking) struck[k] = true;
                if (u.GiveUps[i] > 0) gaveUp[k] = true;
                // Arrived: stopped holding the Move's goal cell (at the point, or packed against those there). An arrived
                // unit's own scan may then take the hall in sight, so it counts once it has stood there.
                if (u.State[i] == UnitState.Idle && u.GoalCell[i] == goalCell && u.Goal[i] == s.MovePoint) arrived[k] = true;
            }
        }
        return new Outcome(struck.Count(b => b), gaveUp.Count(b => b), arrived.Count(b => b));
    }

    private (Outcome Before, Outcome After) BeforeAfter(string shooter, string melee, Aim aim, Order order)
    {
        Outcome before = Run(Scene(shooter, melee, aim, order, flank: false));
        Outcome after = Run(Scene(shooter, melee, aim, order, flank: true));
        _out.WriteLine($"{melee} behind {shooter}, {order} on the {aim}: struck {before.Struck} -> {after.Struck} / 10, gave up "
            + $"{before.GaveUp} -> {after.GaveUp}, arrived {before.Arrived} -> {after.Arrived} (within {Window} ticks; before -> after)");
        return (before, after);
    }

    // Criterion 1: the owner's scene, an Attack on the hall: at least 8 of 10 strike within 10 s, none gives up.
    [Theory]
    [InlineData("malazan_crossbowman", "malazan_heavy_infantry")]
    [InlineData("whirlwind_desert_archer", "whirlwind_raider")]
    public void MeleeBehindItsOwnRangedLine_Attack_WalksRoundAndStrikes(string shooter, string melee)
    {
        (_, Outcome after) = BeforeAfter(shooter, melee, Aim.Hall, Order.Attack);
        Assert.True(after.Struck >= 8, $"{after.Struck} / 10 struck");
        Assert.Equal(0, after.GaveUp);
    }

    // Criterion 2: attack-move past the hall, and an enemy unit behind the line as the target (attack-move past it, and
    // retaliation: Idle units whose own scan takes it): at least 8 of 10 in striking range within 10 s, none gives up.
    [Theory]
    [InlineData("malazan_crossbowman", "malazan_heavy_infantry", Aim.Hall, Order.AttackMove)]
    [InlineData("malazan_crossbowman", "malazan_heavy_infantry", Aim.Unit, Order.AttackMove)]
    [InlineData("malazan_crossbowman", "malazan_heavy_infantry", Aim.Unit, Order.Retaliate)]
    [InlineData("whirlwind_desert_archer", "whirlwind_raider", Aim.Hall, Order.AttackMove)]
    [InlineData("whirlwind_desert_archer", "whirlwind_raider", Aim.Unit, Order.AttackMove)]
    [InlineData("whirlwind_desert_archer", "whirlwind_raider", Aim.Unit, Order.Retaliate)]
    public void MeleeBehindItsOwnRangedLine_AttackMoveAndRetaliation_ReachStrikingRange(string shooter, string melee, Aim aim, Order order)
    {
        (_, Outcome after) = BeforeAfter(shooter, melee, aim, order);
        Assert.True(after.Struck >= 8, $"{after.Struck} / 10 in striking range");
        Assert.Equal(0, after.GaveUp);
    }

    // Criterion 3: a plain Move to 8 m beyond the row: at least 9 of 10 arrive within 10 s.
    [Theory]
    [InlineData("malazan_crossbowman", "malazan_heavy_infantry")]
    [InlineData("whirlwind_desert_archer", "whirlwind_raider")]
    public void MoveThroughItsOwnRangedLine_Arrives(string shooter, string melee)
    {
        (_, Outcome after) = BeforeAfter(shooter, melee, Aim.Hall, Order.Move);
        Assert.True(after.Arrived >= 9, $"{after.Arrived} / 10 arrived");
    }

    /// <summary>
    /// Criterion 4's scene: the same row of 10, but player 1's standing units (combat off: Idle walls; on: holders, which
    /// strike what comes in reach), and 10 player-0 Heavy Infantry behind it ordered to Move 8 m beyond. Returns the sim.
    /// </summary>
    private static Simulation EnemyLine(bool combat, bool flank, out EntityHandle[] walkers)
    {
        SimConfig config = combat ? TestSim.Config(7, 2, 40, 256) : TestSim.ConfigNoCombat(7, 2, 40, 256);
        var sim = new Simulation(config, LocalMovementTests.Flat(MapSize));
        sim.World.FlankRuleOn = flank;
        for (int k = 0; k < 10; k++)
        {
            EntityHandle e = Place(sim, 1, Raider, RowAt(k));
            if (combat) sim.World.Units.Hold[e.Index] = true;
        }
        walkers = new EntityHandle[10];
        for (int k = 0; k < 10; k++)
        {
            walkers[k] = Place(sim, 0, HeavyInfantry, MeleeAt(k));
            sim.Enqueue(Command.Move(0, walkers[k], new Vector2(RowX + 8f, MidY)));
        }
        return sim;
    }

    /// <summary>Walkers whose centre is past the row (x beyond it) after the scene's 200 ticks.</summary>
    private static int Passed(Simulation sim, EntityHandle[] walkers)
    {
        UnitStore u = sim.World.Units;
        int passed = 0;
        foreach (EntityHandle h in walkers) if (u.IsAlive(h) && u.Position[h.Index].X > RowX) passed++;
        return passed;
    }

    // Criterion 4: nothing flanks an enemy. With combat off the enemy row is Idle walls: the rule changes nothing, every
    // tick's hash is the same with it on and off. With combat on (holders that strike) the count that gets past the row is
    // the same with the rule on and off: only the old rules (the detour round an enemy line's end) move anyone past it.
    [Fact]
    public void AnEnemyLine_IsNeverFlanked()
    {
        Simulation off = EnemyLine(combat: false, flank: false, out EntityHandle[] wOff);
        Simulation on = EnemyLine(combat: false, flank: true, out EntityHandle[] wOn);
        for (int t = 0; t < Window; t++)
        {
            off.Tick();
            on.Tick();
            Assert.True(off.StateHash() == on.StateHash(), $"combat off, tick {t}: the flank rule changed the state");
        }
        int idleBefore = Passed(off, wOff), idleAfter = Passed(on, wOn);
        Simulation offC = EnemyLine(combat: true, flank: false, out EntityHandle[] cOff);
        Simulation onC = EnemyLine(combat: true, flank: true, out EntityHandle[] cOn);
        bool sameHash = true;
        for (int t = 0; t < Window; t++)
        {
            offC.Tick();
            onC.Tick();
            sameHash &= offC.StateHash() == onC.StateHash();
        }
        int holdBefore = Passed(offC, cOff), holdAfter = Passed(onC, cOn);
        _out.WriteLine($"enemy row, Idle (combat off): past it {idleBefore} -> {idleAfter} / 10; holders (combat on): past it {holdBefore} -> {holdAfter} / 10, hashes equal every tick: {sameHash}");
        Assert.Equal(idleBefore, idleAfter);
        Assert.Equal(holdBefore, holdAfter);
    }

    // Criterion 6: the owner's scene twice hashes equal every tick (both groups).
    [Theory]
    [InlineData("malazan_crossbowman", "malazan_heavy_infantry")]
    [InlineData("whirlwind_desert_archer", "whirlwind_raider")]
    public void FlankScene_HashTwins(string shooter, string melee)
    {
        Staged x = Scene(shooter, melee, Aim.Hall, Order.Attack, flank: true);
        Staged y = Scene(shooter, melee, Aim.Hall, Order.Attack, flank: true);
        for (int t = 0; t < Window; t++)
        {
            Tick(x);
            Tick(y);
            Assert.Equal(x.Sim.StateHash(), y.Sim.StateHash());
        }
    }

    // Criterion 6, spawn order: one melee unit flanking the row walks the bit-equal path whatever order the row was
    // spawned in. The block is a set (a connected component) and its edges a min and a max, so the slot order the query
    // returns changes nothing. (The ten-unit scene itself is not spawn-order free with the rule off either: the surround
    // claims are taken in slot order, and walls clip in slot order where a walker touches several, BUG-0046; measured, it
    // parts at tick 1 with the melee reversed and at tick 12-24 with only the row reversed, rule on or off. A lone Raider,
    // 0.17 m a tick, already parts at tick 12 with the rule off, as it first touches two of the row: BUG-0046's clip order.)
    [Theory]
    [InlineData("malazan_crossbowman", "malazan_heavy_infantry", Order.Attack, 0.2f)]
    [InlineData("malazan_crossbowman", "malazan_heavy_infantry", Order.Move, 0.2f)]
    [InlineData("malazan_crossbowman", "malazan_heavy_infantry", Order.Move, -0.7f)]
    public void OneFlanker_ReversedRowSpawnOrder_BitEqualPath(string shooter, string melee, Order order, float dy)
    {
        Vector2[] a = OneFlanker(shooter, melee, order, dy, reversed: false, out bool passedA);
        Vector2[] b = OneFlanker(shooter, melee, order, dy, reversed: true, out _);
        _out.WriteLine($"{melee}, {order}, {dy} m off the middle: got past the row {passedA}; end {a[^1]}");
        Assert.True(passedA, "the flanker never got past the row");
        for (int t = 0; t < a.Length; t++) Assert.True(a[t] == b[t], $"tick {t}: {a[t]} vs {b[t]}");
    }

    /// <summary>The row (spawned in order or reversed), then one melee unit 3 m behind its middle (offset <paramref name="dy"/>), ordered; its position every tick.</summary>
    private static Vector2[] OneFlanker(string shooter, string melee, Order order, float dy, bool reversed, out bool passed, bool flank = true)
    {
        var sim = new Simulation(TestSim.Config(7, 2, 40, 256), LocalMovementTests.Flat(MapSize));
        World w = sim.World;
        w.FlankRuleOn = flank;
        UnitStore u = w.Units;
        TestSim.KeepIn(sim, 1);
        int shooterType = TestSim.Data.FindUnit(shooter), meleeType = TestSim.Data.FindUnit(melee);
        Assert.True(w.Buildings.Spawn(1, BuildingOf(1 - TestSim.Data.Units[meleeType].Faction, BuildingSlot.TownHall), HallY * MapSize + HallX, out EntityHandle hall));
        var staged = new Staged { Sim = sim, Row = new EntityHandle[10], Melee = new EntityHandle[1], Hall = hall, Target = hall, TargetIsBuilding = true };
        for (int n = 0; n < 10; n++)
        {
            int k = reversed ? 9 - n : n;
            staged.Row[k] = Place(sim, 0, shooterType, RowAt(k));
            sim.Enqueue(Command.Attack(0, staged.Row[k], hall, isBuilding: true));
        }
        for (int t = 0; t < 40 && !staged.Row.All(h => u.State[h.Index] == UnitState.Attacking); t++) Tick(staged);
        EntityHandle m = Place(sim, 0, meleeType, new Vector2(RowX - 3f, MidY + dy));
        staged.Melee[0] = m;
        sim.Enqueue(order == Order.Attack ? Command.Attack(0, m, hall, isBuilding: true) : Command.Move(0, m, new Vector2(RowX + 8f, MidY)));
        var path = new Vector2[Window];
        passed = false;
        for (int t = 0; t < Window; t++)
        {
            Tick(staged);
            path[t] = u.Position[m.Index];
            if (path[t].X > RowX + 1f) passed = true;
        }
        return path;
    }

    // Criterion 7: the owner's scene, mid-flank, allocates nothing per tick.
    [Fact]
    public void FlankScene_AllocatesNothing()
    {
        Staged s = Scene("malazan_crossbowman", "malazan_heavy_infantry", Aim.Hall, Order.Attack, flank: true);
        for (int t = 0; t < 20; t++) Tick(s);
        Action ticks = () =>
        {
            for (int t = 0; t < 40; t++) Tick(s);
        };
        AllocationProbe.AssertZero(ticks, _out);
    }
}
