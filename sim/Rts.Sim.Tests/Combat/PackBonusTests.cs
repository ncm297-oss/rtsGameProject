using System.Collections.Immutable;
using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using static Rts.Sim.Tests.HitBonusScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M8-3 criteria 7, 8 and 9: the <c>nearbyOwn</c> passive (8 m, +10 % a unit, at most 3) on the <see cref="HitBonusScenes"/>
/// copy. A Heavy Infantry of player 0 strikes a dummy 1 m west (10 melee, light, armor 0), with its pack mates (holding, so
/// they never reach the dummy) east of it; the blow is <c>10 x (1 + min(n, 3) x 0.1)</c>: 10 / 11 / 12 / 13 / 13 / 13.
/// </summary>
public class PackBonusTests
{
    private const string Infantry = "malazan_heavy_infantry";

    /// <summary>The striker at cell (20, 20) and its dummy 1 m west, the attack not yet ordered.</summary>
    private static (Simulation Sim, EntityHandle Striker, EntityHandle Target) Strike(ImmutableArray<PlayerSeat> seats = default, int owner = 0, string type = Infantry)
    {
        Simulation sim = Scene(seats: seats);
        EntityHandle a = Place(sim, owner, type, At(sim, 20, 20));
        EntityHandle t = Dummy(sim, (owner + 1) % 3, At(sim, 20, 20, -1f));
        return (sim, a, t);
    }

    /// <summary>A holding unit of <paramref name="type"/> for <paramref name="owner"/>, <paramref name="offset"/> from the striker's centre.</summary>
    private static EntityHandle Mate(Simulation sim, EntityHandle striker, Vector2 offset, int owner = 0, string type = Infantry)
    {
        EntityHandle h = Place(sim, owner, type, sim.World.Units.Position[striker.Index] + offset);
        sim.World.Units.Hold[h.Index] = true;
        return h;
    }

    /// <summary>The striker's first blow.</summary>
    private static int FirstBlow(Simulation sim, EntityHandle a, EntityHandle t)
    {
        sim.Enqueue(Command.Attack(sim.World.Units.Owner[a.Index], a, t, false));
        return FirstHits(sim, t, 1)[0].Damage;
    }

    [Theory]
    [InlineData(0, 10, 1f)]
    [InlineData(1, 11, 1.1f)]
    [InlineData(2, 12, 1.2f)]
    [InlineData(3, 13, 1.3f)]
    [InlineData(4, 13, 1.3f)]
    [InlineData(5, 13, 1.3f)]
    public void TheFactor_Is1Plus0point1PerMate_CappedAt3(int mates, int damage, float factor)
    {
        (Simulation sim, EntityHandle a, EntityHandle t) = Strike();
        for (int k = 0; k < mates; k++) Mate(sim, a, new Vector2(2f + k, 0f));
        Assert.Equal(Math.Min(mates, PackStacks), HitFactor.PackCount(SimAfterRebuild(sim), a.Index, Passive(sim)));
        Assert.Equal(1f + Math.Min(mates, PackStacks) * 0.1f, HitFactor.Of(sim.World, a.Index));
        Assert.Equal(factor, HitFactor.Of(sim.World, a.Index), 5);
        Assert.Equal(damage, FirstBlow(sim, a, t));
    }

    /// <summary>The striker's passive.</summary>
    private static UnitPassive Passive(Simulation sim)
    {
        UnitDef d = sim.World.Data.Units[sim.World.Data.FindUnit(Infantry)];
        return d.Passives[d.NearbyPassive];
    }

    /// <summary>The grid is rebuilt in phase 1; a scene placed between ticks rebuilds it here so a direct helper call sees the placed units.</summary>
    private static World SimAfterRebuild(Simulation sim)
    {
        sim.World.Spatial.Rebuild(sim.World.Units);
        return sim.World;
    }

    /// <summary>A mate whose centre is exactly 8 m off counts (the edge counts); one 8.01 m off does not. East and north (exact in float on the cell grid).</summary>
    [Theory]
    [InlineData(8f, 0f, 11)]
    [InlineData(8.01f, 0f, 10)]
    [InlineData(0f, 8f, 11)]
    [InlineData(0f, -8.01f, 10)]
    public void TheEdge_Counts_JustBeyondItDoesNot(float dx, float dy, int damage)
    {
        (Simulation sim, EntityHandle a, EntityHandle t) = Strike();
        Mate(sim, a, new Vector2(dx, dy));
        Assert.Equal(damage, FirstBlow(sim, a, t));
    }

    /// <summary>
    /// Never counted: the striker itself (alone: 10), a unit of another type (a Crossbowman), an enemy's unit of the listed
    /// type, an ally's (players 0 and 2 on one team) and a dead one. The same unit as the striker's own would count (11).
    /// </summary>
    [Fact]
    public void TheStrikerItself_AnotherType_AnEnemys_AnAllys_AndADeadUnit_DoNotCount()
    {
        (Simulation sim, EntityHandle a, EntityHandle t) = Strike();
        Assert.Equal(10, FirstBlow(sim, a, t));

        (sim, a, t) = Strike();
        Mate(sim, a, new Vector2(2f, 0f), type: "malazan_crossbowman");
        Assert.Equal(10, FirstBlow(sim, a, t));

        (sim, a, t) = Strike();
        Mate(sim, a, new Vector2(4f, 0f), owner: 2);
        Assert.Equal(10, FirstBlow(sim, a, t));

        var teams = ImmutableArray.Create(new PlayerSeat(0, SeatController.Human, AiDifficulty.Normal, 1),
            new PlayerSeat(1, SeatController.Human, AiDifficulty.Normal, 2), new PlayerSeat(2, SeatController.Human, AiDifficulty.Normal, 1));
        (sim, a, t) = Strike(teams);
        EntityHandle ally = Mate(sim, a, new Vector2(4f, 0f), owner: 2);
        Assert.True(sim.World.AreAllied(0, 2));
        Assert.Equal(10, FirstBlow(sim, a, t));
        (sim, a, t) = Strike(teams);
        Mate(sim, a, new Vector2(4f, 0f), owner: 0);
        Assert.Equal(11, FirstBlow(sim, a, t)); // the same place, its own: counted

        (sim, a, t) = Strike();
        EntityHandle dead = Mate(sim, a, new Vector2(2f, 0f));
        sim.World.Units.Free(dead);
        Assert.Equal(10, FirstBlow(sim, a, t));
    }

    /// <summary>
    /// A mate killed in the same tick before the blow is worked out does not count: the count reads the slots live at the
    /// strike (phase 10). Shown by freeing a mate between two ticks of a running fight: the next blow is one step lower.
    /// </summary>
    [Fact]
    public void AMateThatDies_StopsCountingAtOnce()
    {
        (Simulation sim, EntityHandle a, EntityHandle t) = Strike();
        EntityHandle m1 = Mate(sim, a, new Vector2(2f, 0f));
        Mate(sim, a, new Vector2(3f, 0f));
        sim.Enqueue(Command.Attack(0, a, t, false));
        Assert.Equal(12, FirstHits(sim, t, 1)[0].Damage);
        sim.World.Units.Free(m1);
        Assert.Equal(11, FirstHits(sim, t, 1)[0].Damage);
    }

    /// <summary>
    /// Criterion 8: the three factors multiply in their fixed order: the Blood-oil Frenzy's +20 % (a status), the first blow
    /// from stealth (x 1.5) and a pack of two (x 1.2): 10 x 1.2 x 1.5 x 1.2 = 21.6 -> 22. Without the stealth 10 x 1.44 = 14.4
    /// -> 14 (the second blow: revealed by the first).
    /// </summary>
    [Fact]
    public void AllThreeFactors_Multiply()
    {
        (Simulation sim, EntityHandle a, EntityHandle t) = Strike();
        World w = sim.World;
        Mate(sim, a, new Vector2(2f, 0f));
        Mate(sim, a, new Vector2(3f, 0f));
        StatusSystem.Apply(w, a.Index, Frenzy(w.Data), 0.4f, 100_000, 0);
        Hide(sim, a);
        w.Spatial.Rebuild(w.Units);
        Assert.Equal(1.2f * 1.5f * 1.2f, HitFactor.Of(w, a.Index));
        sim.Enqueue(Command.Attack(0, a, t, false));
        var hits = FirstHits(sim, t, 2);
        Assert.Equal(new[] { 22, 14 }, hits.Select(h => h.Damage).ToArray());
    }

    /// <summary>
    /// Criterion 9, the pack on a unit of each faction (a pack of two, each counting its own kind): the Heavy Infantry 10 ->
    /// 12, the Whirlwind Raider 11 -> 13.2 -> 13, the Teblor War-dog 7 -> 8.4 -> 8; alone, the plain number.
    /// </summary>
    [Theory]
    [InlineData(0, Infantry, 10, 12)]
    [InlineData(1, "whirlwind_raider", 11, 13)]
    [InlineData(2, "teblor_war_dog", 7, 8)]
    public void ThePack_WorksOnAUnitOfEveryFaction(int owner, string type, int alone, int withTwo)
    {
        (Simulation sim, EntityHandle a, EntityHandle t) = Strike(owner: owner, type: type);
        Assert.Equal(alone, FirstBlow(sim, a, t));
        (sim, a, t) = Strike(owner: owner, type: type);
        Mate(sim, a, new Vector2(2f, 0f), owner, type);
        Mate(sim, a, new Vector2(3f, 0f), owner, type);
        Assert.Equal(withTwo, FirstBlow(sim, a, t));
    }

    /// <summary>
    /// Criterion 9's last row: on data with neither field (the shipped two factions) the helper is the status factor alone,
    /// for a hidden unit with the Frenzy in a crowd of its own kind; on the copy, a type with neither field likewise.
    /// </summary>
    [Fact]
    public void OnDataWithNeitherField_TheHelperIsTheStatusFactorAlone()
    {
        GameData plain = TestSim.TwoFactionData;
        Assert.False(plain.AnyHitBonus());
        Simulation sim = Scene(players: 2, data: plain);
        World w = sim.World;
        Assert.False(w.HitBonusInData);
        EntityHandle a = Place(sim, 0, "malazan_crossbowman", At(sim, 20, 20));
        for (int k = 0; k < 3; k++) Place(sim, 0, "malazan_crossbowman", At(sim, 20, 20, 2f + k));
        Hide(sim, a);
        Assert.True(w.IsStealthed(a.Index));
        Assert.Equal(1f, HitFactor.Of(w, a.Index));
        int frenzy = plain.FindStatus(BloodOilScenes.Status);
        if (frenzy >= 0)
        {
            StatusSystem.Apply(w, a.Index, frenzy, 0.4f, 100, 0);
            Assert.Equal(StatusSystem.DamageFactor(w, a.Index), HitFactor.Of(w, a.Index));
            Assert.Equal(1.2f, HitFactor.Of(w, a.Index));
        }

        Simulation copy = Scene();
        EntityHandle lancer = Place(copy, 0, "malazan_wickan_lancer", At(copy, 20, 20));
        for (int k = 0; k < 3; k++) Place(copy, 0, "malazan_wickan_lancer", At(copy, 20, 20, 2f + k));
        Hide(copy, lancer);
        StatusSystem.Apply(copy.World, lancer.Index, Frenzy(copy.World.Data), 0.4f, 100, 0);
        copy.World.Spatial.Rebuild(copy.World.Units);
        Assert.Equal(StatusSystem.DamageFactor(copy.World, lancer.Index), HitFactor.Of(copy.World, lancer.Index));
    }
}
