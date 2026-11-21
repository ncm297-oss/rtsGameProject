using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using static Rts.Sim.Tests.HitBonusScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M8-3 criteria 3, 4 and 6: a hit by a unit whose attack carries <c>fromStealthBonus</c> 0.5 deals <c>x 1.5</c> when the
/// unit is Stealthed (and not Revealed) at the moment its damage is worked out (a melee blow at the strike, a shot at its
/// landing); the landing reveals it, so the next hit is plain. On the <see cref="HitBonusScenes"/> copy, against workers
/// (light, armor 0, they never answer). The numbers, by <see cref="DamageCalc"/>'s rule: the Crossbowman's 9 pierce x 1.25 =
/// 11.25 -> 11, x 1.5 = 16.875 -> 17; the Desert Archer's 7 x 1.25 = 8.75 -> 9, x 1.5 = 13.125 -> 13; the Javelin
/// Thrower's 12 x 1.25 = 15, x 1.5 = 22.5 -> 23 (half up); the Heavy Infantry's 10 melee, x 1.5 = 15.
/// </summary>
public class FirstShotTests
{
    /// <summary>Player <paramref name="owner"/>'s <paramref name="type"/> at cell (10, 20), a dummy of the next player 10 m east, the attack ordered.</summary>
    private static (Simulation Sim, EntityHandle Shooter, EntityHandle Target) Duel(string type, int owner = 0, int gapCells = 5, bool order = true)
    {
        Simulation sim = Scene();
        EntityHandle a = Place(sim, owner, type, At(sim, 10, 20));
        EntityHandle t = Dummy(sim, (owner + 1) % 3, At(sim, 10 + gapCells, 20));
        if (order) sim.Enqueue(Command.Attack(owner, a, t, false));
        return (sim, a, t);
    }

    private static int Revealed(World w) => w.Data.FindStatus("revealed");

    /// <summary>Criterion 3's plain number: the same shot from a unit that is not Stealthed (its first, before any stealth could start).</summary>
    [Theory]
    [InlineData("malazan_crossbowman", 0, 11)]
    [InlineData("whirlwind_desert_archer", 1, 9)]
    [InlineData("teblor_javelin_thrower", 2, 15)]
    [InlineData("malazan_heavy_infantry", 0, 10)]
    public void NotStealthed_TheFirstHitIsPlain(string type, int owner, int plain)
    {
        (Simulation sim, EntityHandle a, EntityHandle t) = Duel(type, owner, type == "malazan_heavy_infantry" ? 1 : 5);
        Assert.False(sim.World.IsStealthed(a.Index));
        var hits = FirstHits(sim, t, 2);
        Assert.Equal(new[] { plain, plain }, hits.Select(h => h.Damage).ToArray());
    }

    /// <summary>
    /// Criterion 3, a timed Stealthed status (an ability's): the first shot lands x 1.5, the landing reveals the shooter, and
    /// the next shot (2.2 s later, inside the 3 s reveal) is plain. The same on a unit of each faction (criterion 9).
    /// </summary>
    [Theory]
    [InlineData("malazan_crossbowman", 0, 11, 17)]
    [InlineData("whirlwind_desert_archer", 1, 9, 13)]
    [InlineData("teblor_javelin_thrower", 2, 15, 23)]
    public void TimedStealth_TheFirstShotCarriesTheBonus_TheLandingRevealsIt_TheNextIsPlain(string type, int owner, int plain, int bonus)
    {
        (Simulation sim, EntityHandle a, EntityHandle t) = Duel(type, owner);
        Hide(sim, a);
        World w = sim.World;
        Assert.True(w.IsStealthed(a.Index));
        var first = FirstHits(sim, t, 1);
        Assert.Equal(bonus, first[0].Damage);
        Assert.False(w.IsStealthed(a.Index)); // revealed by that landing
        Assert.True(w.Units.Statuses.IndexOf(a.Index, Revealed(w)) >= 0);
        var second = FirstHits(sim, t, 1);
        Assert.Equal(plain, second[0].Damage);
    }

    /// <summary>Criterion 3, the stand-still stealth (M8-2): hidden after 3 s still, the first shot x 1.5, then plain.</summary>
    [Theory]
    [InlineData("whirlwind_desert_archer", 1, 9, 13)]
    [InlineData("teblor_javelin_thrower", 2, 15, 23)]
    public void StandStillStealth_TheFirstShotCarriesTheBonus_TheNextIsPlain(string type, int owner, int plain, int bonus)
    {
        (Simulation sim, EntityHandle a, EntityHandle t) = Duel(type, owner, order: false);
        World w = sim.World;
        w.Units.Position[t.Index] = At(sim, 40, 40); // out of sight while it waits, so it does not shoot first
        StillScenes.Ticks(sim, 61);
        Assert.True(StillScenes.Held(sim, a) && w.IsStealthed(a.Index));
        w.Units.Position[t.Index] = At(sim, 15, 20);
        sim.Enqueue(Command.Attack(owner, a, t, false));
        var hits = FirstHits(sim, t, 2);
        Assert.Equal(new[] { bonus, plain }, hits.Select(h => h.Damage).ToArray());
    }

    /// <summary>Criterion 3, the forest stealth (M8-1, the Malazan in this copy): beside a tree for 3 s, the first bolt x 1.5, then plain.</summary>
    [Fact]
    public void ForestStealth_TheFirstShotCarriesTheBonus_TheNextIsPlain()
    {
        (Simulation sim, EntityHandle a, EntityHandle t) = Duel("malazan_crossbowman", 0, order: false);
        World w = sim.World;
        ForestScenes.Tree(sim, 10, 21);
        w.Units.Position[t.Index] = At(sim, 40, 40); // out of sight while it waits, so it does not shoot first
        StillScenes.Ticks(sim, 61);
        Assert.True(ForestScenes.Held(sim, a) && w.IsStealthed(a.Index));
        w.Units.Position[t.Index] = At(sim, 15, 20);
        Assert.False(w.Data.Units[w.Units.TypeId[a.Index]].HasStillStealth); // the forest alone hides it
        sim.Enqueue(Command.Attack(0, a, t, false));
        var hits = FirstHits(sim, t, 2);
        Assert.Equal(new[] { 17, 11 }, hits.Select(h => h.Damage).ToArray());
    }

    /// <summary>Criterion 4: a melee unit with the field, hidden: the first blow carries the bonus (15), the second does not (10).</summary>
    [Fact]
    public void Melee_TheFirstBlowCarriesTheBonus_TheSecondDoesNot()
    {
        (Simulation sim, EntityHandle a, EntityHandle t) = Duel("malazan_heavy_infantry", 0, gapCells: 0, order: false);
        sim.World.Units.Position[t.Index] = At(sim, 10, 20, 1f); // 1 m east: in reach (0.2 m edge to edge)
        Hide(sim, a);
        sim.Enqueue(Command.Attack(0, a, t, false));
        var hits = FirstHits(sim, t, 2);
        Assert.Equal(new[] { 15, 10 }, hits.Select(h => h.Damage).ToArray());
        Assert.False(sim.World.IsStealthed(a.Index));
    }

    /// <summary>
    /// Criterion 6, a building: a hidden Crossbowman's bolt on a Whirlwind Tent (structure x 0.35, armor 1): 9 x 0.35 x 1.5 =
    /// 4.725 -> 5 - 1 = 4, then plain 3.15 -> 3 - 1 = 2.
    /// </summary>
    [Fact]
    public void TheBonus_ReachesAHitOnABuilding()
    {
        Simulation sim = Scene();
        World w = sim.World;
        EntityHandle a = Place(sim, 0, "malazan_crossbowman", At(sim, 12, 20));
        Assert.True(w.Buildings.Spawn(1, w.Data.FindBuilding("whirlwind_tent"), 20 * 48 + 18, out EntityHandle tent));
        Dummy(sim, 1, At(sim, 40, 40)); // a live unit, so player 1 is not defeated
        Hide(sim, a);
        w.Fog.Update();
        sim.Enqueue(Command.Attack(0, a, tent, true));
        var hits = new List<int>();
        for (int t = 0; t < 600 && hits.Count < 2; t++)
        {
            int before = w.Buildings.Hp[tent.Index];
            sim.Tick();
            if (w.Buildings.Hp[tent.Index] < before) hits.Add(before - w.Buildings.Hp[tent.Index]);
        }
        Assert.Equal(new[] { 4, 2 }, hits.ToArray());
    }

    /// <summary>
    /// Criterion 6, the splash: a hidden Cadre Mage's bolt (9 magic, splash 1.5 m, full to 0.6 m, half at the edge) on a dummy
    /// with two more 1.2 m and 1.4 m from it. Every victim of that one shot carries the bonus: the direct 9 -> 13.5 -> 14; at
    /// 1.2 m (falloff 0.667) 14 -> 9.33 -> 9 (plain 6); at 1.4 m (falloff 0.556) 14 -> 7.78 -> 8 (plain 5). The next shot is
    /// plain for all three.
    /// </summary>
    [Fact]
    public void TheBonus_ReachesEveryVictimOfTheShotsSplash()
    {
        Simulation sim = Scene();
        World w = sim.World;
        EntityHandle a = Place(sim, 0, "malazan_cadre_mage", At(sim, 10, 20));
        EntityHandle t = Dummy(sim, 1, At(sim, 15, 20));
        EntityHandle near = Dummy(sim, 1, At(sim, 15, 20, 0f, 1.2f));
        EntityHandle far = Dummy(sim, 1, At(sim, 15, 20, 0f, -1.4f));
        Hide(sim, a);
        sim.Enqueue(Command.Attack(0, a, t, false));
        int[] Hp() => new[] { w.Units.Hp[t.Index], w.Units.Hp[near.Index], w.Units.Hp[far.Index] };
        var shots = new List<int[]>();
        for (int k = 0; k < 600 && shots.Count < 2; k++)
        {
            int[] before = Hp();
            sim.Tick();
            int[] after = Hp();
            if (after[0] < before[0]) shots.Add(new[] { before[0] - after[0], before[1] - after[1], before[2] - after[2] });
        }
        Assert.Equal(2, shots.Count);
        Assert.Equal(new[] { 14, 9, 8 }, shots[0]);
        Assert.Equal(new[] { 9, 6, 5 }, shots[1]);
    }

    /// <summary>Criterion 6's last row: a Stealthed unit whose attack has no field (the Wickan Lancer, 12 x 1.5 v light = 18) gets nothing.</summary>
    [Fact]
    public void AStealthedUnitWithoutTheField_GetsNothing()
    {
        (Simulation sim, EntityHandle a, EntityHandle t) = Duel("malazan_wickan_lancer", 0, gapCells: 0, order: false);
        sim.World.Units.Position[t.Index] = At(sim, 10, 20, 1.3f);
        Hide(sim, a);
        Assert.True(sim.World.IsStealthed(a.Index));
        Assert.Equal(1f, HitFactor.Of(sim.World, a.Index));
        sim.Enqueue(Command.Attack(0, a, t, false));
        var hits = FirstHits(sim, t, 2);
        Assert.Equal(new[] { 18, 18 }, hits.Select(h => h.Damage).ToArray());
    }
}
