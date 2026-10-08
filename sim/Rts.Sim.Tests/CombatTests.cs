using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>M4-1 criteria 2-5: melee fights, target priority, leash and resume, death.</summary>
public class CombatTests
{
    private static UnitDef Def(int type) => TestSim.Data.Units[type];

    // ---------- criterion 2: two Heavy Infantry ----------

    [Fact]
    public void TwoHeavyInfantry_AttackMovedIntoEachOther_HitOnTheWindupThenEveryCooldown_7Each_BothDie()
    {
        UnitDef hi = Def(HeavyInfantry);
        int windup = hi.Attack.WindupTicks, cooldown = hi.Attack.CooldownTicks;
        Assert.Equal(6, windup);     // 0.3 s
        Assert.Equal(30, cooldown);  // 1.5 s
        int hit = DamageCalc.Compute(TestSim.Data.DamageTable, hi.Attack, 0, hi.ArmorClass, hi.Armor);
        Assert.Equal(7, hit); // 10 x 1.0 x 1.0 - 3
        int hitsToKill = (hi.Hp + hit - 1) / hit;
        Assert.Equal(19, hitsToKill);

        Simulation sim = Flat();
        // 40 m apart: far beyond sight (14 m), so they only meet through the attack-move.
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 10, 24));
        EntityHandle b = Place(sim, 1, HeavyInfantry, At(sim, 30, 24));
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.AttackMove(0, a, u.Position[b.Index]));
        sim.Enqueue(Command.AttackMove(1, b, u.Position[a.Index]));

        var log = new List<(UnitState A, UnitState B, int HpA, int HpB, bool AliveA, bool AliveB)>();
        for (int t = 0; t < 2000 && (u.IsAlive(a) || u.IsAlive(b)); t++)
        {
            sim.Tick();
            log.Add((u.IsAlive(a) ? u.State[a.Index] : default, u.IsAlive(b) ? u.State[b.Index] : default,
                u.IsAlive(a) ? u.Hp[a.Index] : 0, u.IsAlive(b) ? u.Hp[b.Index] : 0, u.IsAlive(a), u.IsAlive(b)));
        }
        Assert.False(u.IsAlive(a));
        Assert.False(u.IsAlive(b));

        // Each side: the tick it first stood Attacking starts its first swing; the victim's hp drops at start + windup,
        // then every cooldown, 7 each, and it dies on hit 19.
        CheckSide(log.Select(e => (e.A, e.HpB, e.AliveB)).ToList());
        CheckSide(log.Select(e => (e.B, e.HpA, e.AliveA)).ToList());

        void CheckSide(List<(UnitState Attacker, int VictimHp, bool VictimAlive)> side)
        {
            int start = side.FindIndex(e => e.Attacker == UnitState.Attacking);
            Assert.True(start >= 0);
            var drops = new List<(int Tick, int Amount)>();
            int prev = hi.Hp;
            for (int t = 0; t < side.Count; t++)
            {
                int hp = side[t].VictimAlive ? side[t].VictimHp : 0;
                if (hp < prev) drops.Add((t, prev - hp));
                prev = hp;
            }
            Assert.Equal(hitsToKill, drops.Count);
            for (int k = 0; k < drops.Count; k++)
            {
                Assert.Equal(start + windup + k * cooldown, drops[k].Tick);
                // The killing hit takes what was left (4 of 7).
                Assert.Equal(k < drops.Count - 1 ? hit : hi.Hp - hit * (hitsToKill - 1), drops[k].Amount);
            }
        }
    }

    [Fact]
    public void EqualFighters_EngagingOnTheSameTick_DieOnTheSameTick_EachKilledByTheOther()
    {
        Simulation sim = Flat();
        // Slots 0 and 4 scan on the same ticks (slot mod 4); slots 1-3 stand far off in a corner.
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 30, 30));
        for (int k = 0; k < 3; k++) Place(sim, 0, Laborer, At(sim, 3 + k, 3));
        EntityHandle b = Place(sim, 1, HeavyInfantry, At(sim, 30, 30, dx: 1f));
        Assert.Equal(0, a.Index);
        Assert.Equal(4, b.Index);
        UnitStore u = sim.World.Units;
        int deathTick = -1;
        for (int t = 0; t < 2000 && deathTick < 0; t++)
        {
            sim.Tick();
            if (!u.IsAlive(a) || !u.IsAlive(b))
            {
                deathTick = t;
                Assert.False(u.IsAlive(a));
                Assert.False(u.IsAlive(b));
                ReadOnlySpan<DeathEvent> deaths = sim.World.Deaths;
                Assert.Equal(2, deaths.Length);
                // Hits apply in attacker slot order: a's (slot 0) kills b, then b's own queued hit still lands and kills a.
                Assert.Equal(b, deaths[0].Victim);
                Assert.Equal(0, deaths[0].KillerOwner);
                Assert.Equal(a, deaths[1].Victim);
                Assert.Equal(1, deaths[1].KillerOwner);
                Assert.Equal(new[] { 1, 1 }, sim.World.Kills.ToArray());
                Assert.Equal(new[] { 1, 1 }, sim.World.Losses.ToArray());
            }
        }
        Assert.True(deathTick > 0);
    }

    // ---------- criterion 3: target priority ----------

    /// <summary>Runs ticks until unit <paramref name="h"/> has a target (at most <paramref name="max"/>); returns its target slot or -1.</summary>
    private static int FirstTarget(Simulation sim, EntityHandle h, int max = 12)
    {
        UnitStore u = sim.World.Units;
        RunUntil(sim, () => u.Target[h.Index] != default, max);
        return u.Target[h.Index] == default ? -1 : u.Target[h.Index].Index;
    }

    [Fact]
    public void Priority_FartherSoldierBeatsNearerWorker()
    {
        Simulation sim = Flat();
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 20, 20));
        EntityHandle worker = Place(sim, 1, Laborer, At(sim, 20, 20, dx: 5f));
        EntityHandle soldier = Place(sim, 1, Crossbowman, At(sim, 20, 20, dx: -9f));
        sim.Enqueue(Command.HoldPosition(1, worker)); // out of its reach: it never takes a on, so it is no "attacker"
        Assert.Equal(soldier.Index, FirstTarget(sim, a));
        Assert.False(sim.World.Units.TargetIsBuilding[a.Index]);
        Assert.Equal(default, sim.World.Units.Target[worker.Index]);
    }

    [Fact]
    public void Priority_TheSoldierAttackingMeBeatsANearerOne()
    {
        Simulation sim = Flat();
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 20, 20));
        EntityHandle near = Place(sim, 1, HeavyInfantry, At(sim, 20, 20, dx: 4f));
        EntityHandle attacker = Place(sim, 1, HeavyInfantry, At(sim, 20, 20, dx: -8f));
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.HoldPosition(1, near));
        sim.Enqueue(Command.AttackMove(1, attacker, u.Position[a.Index]));
        for (int t = 0; t < 10; t++) sim.Tick();
        Assert.Equal(a, u.Target[attacker.Index]);
        Assert.Equal(attacker, u.Target[a.Index]);
        Assert.True(Vector2.Distance(u.Position[a.Index], u.Position[near.Index]) < Vector2.Distance(u.Position[a.Index], u.Position[attacker.Index]));
    }

    [Fact]
    public void Priority_OnlyABuildingInSight_IsTakenAndHitAsStructure()
    {
        Simulation sim = Flat();
        int tent = TestSim.Data.FindBuilding("whirlwind_tent");
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 20, 20));
        sim.Enqueue(Command.SpawnBuilding(1, tent, At(sim, 24, 19)));
        UnitStore u = sim.World.Units;
        BuildingStore b = sim.World.Buildings;
        RunUntil(sim, () => u.Target[a.Index] != default, 12);
        Assert.True(u.TargetIsBuilding[a.Index]);
        int k = u.Target[a.Index].Index;
        Assert.Equal(tent, b.TypeId[k]);
        int full = b.Hp[k];
        RunUntil(sim, () => b.Hp[k] < full, 400);
        UnitDef hi = Def(HeavyInfantry);
        int expected = DamageCalc.Compute(TestSim.Data.DamageTable, hi.Attack, 0, sim.World.StructureClass, TestSim.Data.Buildings[tent].Armor);
        Assert.Equal(1, expected); // 10 x 0.4 - 3 = 1
        Assert.Equal(full - expected, b.Hp[k]);
        Assert.Equal(UnitState.Attacking, u.State[a.Index]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Priority_EqualDistance_GoesToTheLowestSlot(bool rightFirst)
    {
        Simulation sim = Flat();
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 20, 20));
        EntityHandle first = Place(sim, 1, Crossbowman, At(sim, 20, 20, dx: rightFirst ? 5f : -5f));
        EntityHandle second = Place(sim, 1, Crossbowman, At(sim, 20, 20, dx: rightFirst ? -5f : 5f));
        UnitStore u = sim.World.Units;
        Assert.Equal(Vector2.DistanceSquared(u.Position[a.Index], u.Position[first.Index]), Vector2.DistanceSquared(u.Position[a.Index], u.Position[second.Index]));
        Assert.True(first.Index < second.Index);
        Assert.Equal(first.Index, FirstTarget(sim, a));
    }

    [Theory]
    [InlineData(-0.25f, true)]
    [InlineData(0.25f, false)]
    public void Scan_SeesExactlyItsSightRadius(float pastSight, bool expectTarget)
    {
        Simulation sim = Flat();
        float sight = Def(HeavyInfantry).Sight;
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 20, 20));
        Place(sim, 1, Crossbowman, At(sim, 20, 20, dx: sight + pastSight));
        Assert.Equal(expectTarget, FirstTarget(sim, a, max: 3 * CombatConstants.ScanInterval) >= 0);
    }

    [Fact]
    public void PlainMove_NeverAcquires_ThoughItPassesAnEnemyInSight()
    {
        Simulation sim = Flat();
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 6, 20));
        EntityHandle c = Place(sim, 1, Crossbowman, At(sim, 16, 22));
        UnitStore u = sim.World.Units;
        Assert.True(Vector2.Distance(u.Position[a.Index], u.Position[c.Index]) > Def(HeavyInfantry).Sight); // not seen before the Move
        sim.Enqueue(Command.Move(0, a, At(sim, 40, 20)));
        sim.Tick();
        sim.Tick(); // the Move applies
        Assert.Equal(UnitState.Moving, u.State[a.Index]);
        float closest = float.MaxValue;
        for (int t = 0; t < 600 && u.State[a.Index] == UnitState.Moving; t++)
        {
            sim.Tick();
            closest = MathF.Min(closest, Vector2.Distance(u.Position[a.Index], u.Position[c.Index]));
            if (u.State[a.Index] == UnitState.Moving) Assert.Equal(default, u.Target[a.Index]);
        }
        Assert.True(closest < 6f, $"closest {closest}");
        Assert.Equal(UnitState.Idle, u.State[a.Index]);
        Assert.Equal(CombatMode.None, u.Mode[a.Index]);
    }

    [Fact]
    public void HoldingUnit_FightsInReach_WithABitIdenticalPositionThroughout()
    {
        Simulation sim = Flat();
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 20, 20));
        // Out of sight at first, so a only meets b holding (the orders apply on the second tick).
        EntityHandle b = Place(sim, 1, HeavyInfantry, At(sim, 20, 20, dx: 16f));
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.HoldPosition(0, a));
        sim.Enqueue(Command.AttackMove(1, b, u.Position[a.Index]));
        Vector2 at = u.Position[a.Index];
        bool attacked = false;
        for (int t = 0; t < 1500 && u.IsAlive(a) && u.IsAlive(b); t++)
        {
            sim.Tick();
            if (!u.IsAlive(a)) break;
            Assert.Equal(BitConverter.SingleToInt32Bits(at.X), BitConverter.SingleToInt32Bits(u.Position[a.Index].X));
            Assert.Equal(BitConverter.SingleToInt32Bits(at.Y), BitConverter.SingleToInt32Bits(u.Position[a.Index].Y));
            attacked |= u.State[a.Index] == UnitState.Attacking;
        }
        Assert.True(attacked);
        Assert.True(!u.IsAlive(b) || u.Hp[b.Index] < Def(HeavyInfantry).Hp);
        Assert.True(sim.World.Kills[0] + sim.World.Kills[1] >= 1);
    }

    [Fact]
    public void GatheringWorker_NeverRetaliates()
    {
        Simulation sim = new(TestSim.Config(Seed: 5, PlayerCount: 2, UnitCapacity: 16, CommandCapacity: 64), ResourceMaps.Flat(32, 20));
        ResourceMaps.Spawn(sim.World, ResourceMaps.Mine, 16, 8, 2500);
        GatherMaps.Building(sim, 8, 7);
        EntityHandle w = Place(sim, 0, Laborer, At(sim, 14, 9));
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.Gather(0, w, At(sim, 16, 8)));
        RunUntil(sim, () => u.State[w.Index] == UnitState.Gathering, 200);
        Assert.Equal(UnitState.Gathering, u.State[w.Index]);
        EntityHandle enemy = Place(sim, 1, HeavyInfantry, u.Position[w.Index] + new Vector2(0f, -1.2f));
        for (int t = 0; t < 600 && u.IsAlive(w); t++)
        {
            sim.Tick();
            if (!u.IsAlive(w)) break;
            Assert.Equal(default, u.Target[w.Index]);
            Assert.Equal(CombatMode.None, u.Mode[w.Index]);
            Assert.NotEqual(UnitState.Attacking, u.State[w.Index]);
        }
        Assert.False(u.IsAlive(w)); // it took the hits to the end
        Assert.True(u.IsAlive(enemy));
        Assert.Equal(Def(HeavyInfantry).Hp, u.Hp[enemy.Index]);
    }

    // ---------- criterion 4: leash and resume ----------

    [Fact]
    public void IdleUnit_ChasedPastItsSight_WalksBack_AndIsIdleOnItsAnchorCell()
    {
        Simulation sim = Flat();
        Vector2 home = At(sim, 10, 24);
        EntityHandle a = Place(sim, 0, HeavyInfantry, home);
        EntityHandle c = Place(sim, 1, Crossbowman, At(sim, 10, 24, dx: 4f));
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.Move(1, c, At(sim, 44, 24))); // faster than a: never caught
        float sight = Def(HeavyInfantry).Sight;
        float farthest = 0f;
        bool chased = false, returned = false;
        for (int t = 0; t < 1500; t++)
        {
            sim.Tick();
            farthest = MathF.Max(farthest, Vector2.Distance(u.Position[a.Index], home));
            chased |= u.Target[a.Index] == c;
            returned |= u.Mode[a.Index] == CombatMode.Returning;
            if (returned && u.State[a.Index] == UnitState.Idle && u.Mode[a.Index] == CombatMode.None) break;
        }
        Assert.True(chased);
        Assert.True(returned);
        Assert.True(farthest > sight, $"farthest {farthest}");
        Assert.True(farthest <= sight + Def(HeavyInfantry).SpeedPerTick + 1e-3f, $"farthest {farthest}");
        Assert.Equal(UnitState.Idle, u.State[a.Index]);
        Assert.Equal(CombatMode.None, u.Mode[a.Index]);
        Assert.Equal(default, u.Target[a.Index]);
        NavGrid g = sim.World.NavGrid;
        Assert.True(g.WorldToCell(home, out int hx, out int hy));
        Assert.True(g.WorldToCell(u.Position[a.Index], out int ax, out int ay));
        Assert.Equal((hx, hy), (ax, ay));
    }

    [Fact]
    public void AttackMove_KillsWhatIsOnTheWay_ThenResumesItsLeg_AndArrives()
    {
        Simulation sim = Flat();
        Vector2 dest = At(sim, 40, 24);
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 6, 24));
        EntityHandle v = Place(sim, 1, Laborer, At(sim, 18, 25));
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.HoldPosition(1, v));
        sim.Enqueue(Command.AttackMove(0, a, dest));
        bool fought = false;
        for (int t = 0; t < 2000; t++)
        {
            sim.Tick();
            fought |= u.Target[a.Index] == v;
            if (!u.IsAlive(v) && u.State[a.Index] == UnitState.Idle && u.Mode[a.Index] == CombatMode.None) break;
        }
        Assert.True(fought);
        Assert.False(u.IsAlive(v));
        // The finished leg ended the attack-move where the leg ends.
        Assert.Equal(UnitState.Idle, u.State[a.Index]);
        Assert.Equal(CombatMode.None, u.Mode[a.Index]);
        Assert.True(Vector2.Distance(u.Position[a.Index], dest) < 1f, $"stopped {Vector2.Distance(u.Position[a.Index], dest)} m from the leg's end");
        Assert.Equal(1, sim.World.Kills[0]);
    }

    // ---------- criterion 5: death ----------

    [Fact]
    public void Death_FreesTheSameTick_ReleasesPop_EmitsTheEvent_ClearsAttackers_WhoReacquireOnTheirNextScan()
    {
        Simulation sim = Flat();
        Vector2 o = At(sim, 20, 20);
        EntityHandle a1 = Place(sim, 0, HeavyInfantry, o);
        EntityHandle v1 = Place(sim, 1, Laborer, o + new Vector2(1f, 0f));
        EntityHandle a2 = Place(sim, 0, HeavyInfantry, o + new Vector2(2f, 0f));
        EntityHandle v2 = Place(sim, 1, Laborer, o + new Vector2(1f, 5f));
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.HoldPosition(1, v1));
        sim.Enqueue(Command.HoldPosition(1, v2));
        int popBefore = sim.World.HalfPop[1];
        Assert.Equal(ProductionMaps.RecountHalfPop(sim.World, 1), popBefore);
        RunUntil(sim, () => !u.IsAlive(v1), 400);
        Assert.False(u.IsAlive(v1));
        DeathEvent[] deaths = sim.World.Deaths.ToArray();
        DeathEvent e = Assert.Single(deaths);
        Assert.Equal(new DeathEvent(v1, false, Laborer, 1, 0, o + new Vector2(1f, 0f)), e);
        Assert.Equal(popBefore - Def(Laborer).HalfPop, sim.World.HalfPop[1]);
        Assert.Equal(ProductionMaps.RecountHalfPop(sim.World, 1), sim.World.HalfPop[1]);
        Assert.Equal(1, sim.World.Kills[0]);
        Assert.Equal(1, sim.World.Losses[1]);
        Assert.Equal(0, sim.World.Kills[1] + sim.World.Losses[0]);
        Assert.NotEqual(v1, u.Target[a1.Index]);
        Assert.NotEqual(v1, u.Target[a2.Index]);
        // Both attackers had v1: both pick v2 on their next scan.
        for (int t = 0; t < CombatConstants.ScanInterval; t++) sim.Tick();
        Assert.Equal(v2, u.Target[a1.Index]);
        Assert.Equal(v2, u.Target[a2.Index]);
        Assert.Equal(0, sim.World.Deaths.Length); // the buffer holds one tick's deaths
    }

    [Fact]
    public void BuildingKilledByMelee_IsFreed_CountsAKill_AndItsCellsReopen()
    {
        Simulation sim = Flat();
        int tent = TestSim.Data.FindBuilding("whirlwind_tent");
        EntityHandle site = GatherMaps.Building(sim, 24, 19, player: 1, type: tent);
        BuildingStore b = sim.World.Buildings;
        b.SetHp(site.Index, 2);
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 22, 20));
        RunUntil(sim, () => !b.IsAlive(site), 400);
        Assert.False(b.IsAlive(site));
        Assert.Equal(0, b.Count);
        DeathEvent e = Assert.Single(sim.World.Deaths.ToArray());
        // Cells 24-25 x 19-20: x 48-52 m, y 38-42 m.
        Assert.Equal(new DeathEvent(site, true, tent, 1, 0, new Vector2(50f, 40f)), e);
        Assert.Equal(1, sim.World.Kills[0]);
        Assert.Equal(1, sim.World.Losses[1]);
        // The footprint touches open ground, so the pocket rule reopens it.
        NavGrid g = sim.World.NavGrid;
        for (int y = 19; y <= 20; y++)
            for (int x = 24; x <= 25; x++)
                Assert.True(g.IsPassable(x, y));
        Assert.Equal(default, sim.World.Units.Target[a.Index]);
    }

    // ---------- wind-up grace and facing ----------

    /// <summary>
    /// Producer default (M4-1): a target that steps out of reach during the wind-up is still hit within reach + 0.5 m,
    /// else the swing is lost and the attacker goes after it. The attacker faces its target while it swings.
    /// </summary>
    [Theory]
    [InlineData(0.4f, true)]
    [InlineData(0.6f, false)]
    public void TargetLeavingReachDuringTheWindup_IsHitWithinTheGrace_ElseTheSwingIsLost(float pastReach, bool hits)
    {
        Simulation sim = Flat();
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 20, 20));
        EntityHandle b = Place(sim, 1, Crossbowman, At(sim, 20, 20, dx: 1f, dy: 0.5f)); // in reach, and it never swings back
        UnitStore u = sim.World.Units;
        UnitDef hi = Def(HeavyInfantry);
        RunUntil(sim, () => u.WindupTicks[a.Index] > 0, 20);
        Assert.Equal(UnitState.Attacking, u.State[a.Index]);
        Assert.Equal(hi.Attack.WindupTicks, u.WindupTicks[a.Index]);
        Vector2 d = u.Position[b.Index] - u.Position[a.Index];
        Assert.Equal(Determinism.SimMath.Atan2(d.Y, d.X), u.Facing[a.Index]);
        // Step b straight out to reach + pastReach, edge to edge.
        float reachCenter = hi.Attack.Range + u.Radius[a.Index] + u.Radius[b.Index];
        u.Position[b.Index] = u.Position[a.Index] + Vector2.Normalize(d) * (reachCenter + pastReach);
        int hp = u.Hp[b.Index];
        for (int t = 0; t < hi.Attack.WindupTicks; t++) sim.Tick();
        Assert.Equal(hits ? hp - DamageCalc.Compute(TestSim.Data.DamageTable, hi.Attack, 0, Def(Crossbowman).ArmorClass, Def(Crossbowman).Armor) : hp, u.Hp[b.Index]);
        Assert.Equal(0, u.WindupTicks[a.Index]);
        Assert.Equal(b, u.Target[a.Index]);
        // Out of reach once the swing is over: a stood down at once (phase 10) and chases from the next phase 7.
        Assert.Equal(UnitState.Idle, u.State[a.Index]);
        sim.Tick();
        Assert.Equal(UnitState.Moving, u.State[a.Index]);
    }
}
