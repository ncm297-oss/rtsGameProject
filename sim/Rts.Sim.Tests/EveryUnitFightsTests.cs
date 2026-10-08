using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-2b criterion 5: every shipped unit fights. Table-driven over <c>units.json</c>: each type, ordered to Attack, deals
/// damage 1 v 1 to a holding enemy Laborer (or, for a buildings-only attack, an enemy Tent), standing in its reach and
/// outside its minimum range; <see cref="CombatSystem.CanFight"/> is true for every one.
/// </summary>
public class EveryUnitFightsTests
{
    private readonly ITestOutputHelper _out;

    public EveryUnitFightsTests(ITestOutputHelper output) => _out = output;

    public static IEnumerable<object[]> EveryUnit() => TestSim.Data.Units.Select(u => new object[] { u.Key });

    [Fact]
    public void CanFight_IsTrueForEveryShippedUnit()
    {
        Assert.Equal(14, TestSim.Data.Units.Length);
        foreach (UnitDef d in TestSim.Data.Units)
            Assert.True(CombatSystem.CanFight(d), d.Key);
    }

    [Theory]
    [MemberData(nameof(EveryUnit))]
    public void EveryUnit_DealsDamage1v1_ToAHeldLaborerOrTent(string key)
    {
        GameData data = TestSim.Data;
        int type = data.FindUnit(key);
        AttackDef attack = data.Units[type].Attack;
        Simulation sim = Flat();
        World w = sim.World;
        UnitStore u = w.Units;
        // In reach, outside the minimum range, never more than 4 m short of the range.
        float gap = attack.Range <= 1f ? attack.Range * 0.5f : Math.Max(attack.MinRange + 2f, Math.Min(attack.Range - 1f, 4f));
        int damage, ticks;
        if (attack.Targets == AttackTargets.Buildings)
        {
            Assert.True(w.Buildings.Spawn(1, data.FindBuilding("whirlwind_tent"), 24 * w.NavGrid.Width + 24, out EntityHandle tent));
            EntityHandle a = Place(sim, 0, type, new Vector2(48f - gap - data.Units[type].Radius, 50f));
            sim.Enqueue(Command.Attack(0, a, tent, isBuilding: true));
            int full = w.Buildings.Hp[tent.Index];
            ticks = RunUntil(sim, () => w.Buildings.Hp[tent.Index] < full, 400);
            damage = full - w.Buildings.Hp[tent.Index];
        }
        else
        {
            EntityHandle target = Place(sim, 1, Laborer, At(sim, 24, 24));
            sim.Enqueue(Command.HoldPosition(1, target));
            EntityHandle a = Place(sim, 0, type, At(sim, 24, 24, dx: -(gap + data.Units[type].Radius + data.Units[Laborer].Radius)));
            sim.Enqueue(Command.Attack(0, a, target, isBuilding: false));
            int full = u.Hp[target.Index];
            ticks = RunUntil(sim, () => !u.IsAlive(target) || u.Hp[target.Index] < full, 400);
            damage = u.IsAlive(target) ? full - u.Hp[target.Index] : full;
            Assert.Equal(CombatMode.Ordered, u.Mode[a.Index]); // obeyed (M4-2a dropped it for ranged, casters and siege)
        }
        _out.WriteLine($"{key}: {damage} damage after {ticks} ticks at a {gap} m gap ({(attack.Projectile ?? "melee")})");
        Assert.True(damage > 0, $"{key} dealt no damage in 400 ticks");
    }
}
