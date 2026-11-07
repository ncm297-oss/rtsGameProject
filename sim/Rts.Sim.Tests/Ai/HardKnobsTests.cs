using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;
using static Rts.Sim.Tests.HardKnobsScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-4c slice 2 (roadmap M5 criterion 4; docs/02 "AI opponent": Hard focuses fire and pulls back wounded units): the
/// <c>focusFire</c> and <c>pullBackWounded</c> knobs on the flat scene (<see cref="HardKnobsScenes"/>), judged by player 0's
/// command log. Normal's logs in the same scenes are pinned to the pre-slice code in <see cref="HardKnobsPinTests"/>.
/// </summary>
public class HardKnobsTests
{
    private readonly ITestOutputHelper _out;

    public HardKnobsTests(ITestOutputHelper output) => _out = output;

    private static float Reach => HardKnobsScenes.Data.Value.AiRules.FocusReach;

    /// <summary>What a think's focus pass did: the focus picked, every member's position, and the live raiders' hp then.</summary>
    private sealed record Pick(int Tick, EntityHandle Focus, Vector2 FocusAt, Dictionary<int, Vector2> Members, Dictionary<int, int> RaiderHp);

    /// <summary>
    /// Criterion 3: a Hard wave meeting three of the enemy's Raiders at 100, 40 and 70 hp (combat on). At the first think that
    /// sees them every member within <c>ai.focusReach</c> of the 40-hp one is sent <c>Attack</c> on it, and only on it; every
    /// unit <c>Attack</c> the seat sends is on the focus of its think; and once that Raider dies the next focus is the
    /// lowest-hp Raider left, with <c>Attack</c>s on it.
    /// </summary>
    [Fact]
    public void AHardWave_MeetingThreeUnits_AttacksTheLowestHpFromEveryMemberInReach_AndRetargetsWhenItDies()
    {
        (Simulation sim, List<EntityHandle> members, EntityHandle[] raiders) = Focus(AiDifficulty.Hard);
        AiPlayer ai = sim.AiOf(0)!;
        List<Command> log = AiScenes.Log(sim, 0);
        var picks = new List<Pick>();
        ai.ThoughtForTests = a =>
        {
            if (a.FocusTarget.Generation == 0) return;
            var at = new Dictionary<int, Vector2>();
            foreach (EntityHandle m in members)
                if (sim.World.Units.IsAlive(m) && a.Board.WaveGeneration[m.Index] == m.Generation) at[m.Index] = sim.World.Units.Position[m.Index];
            var hp = new Dictionary<int, int>();
            foreach (EntityHandle r in raiders)
                if (sim.World.Units.IsAlive(r)) hp[r.Index] = sim.World.Units.Hp[r.Index];
            picks.Add(new Pick(a.Now, a.FocusTarget, sim.World.Units.Position[a.FocusTarget.Index], at, hp));
        };
        for (int t = 0; t < 2400 && raiders.Any(r => sim.World.Units.IsAlive(r)); t++) sim.Tick();
        Assert.NotEmpty(picks);

        // The first pick: the 40-hp Raider, every member in reach ordered onto it in that think's commands.
        Pick first = picks[0];
        Assert.Equal(raiders[1], first.Focus);
        List<Command> firstAttacks = log.Where(c => c.Tick == first.Tick + 1 && c.Kind == CommandKind.Attack).ToList();
        Assert.All(firstAttacks, c => Assert.Equal(raiders[1], c.Target));
        Assert.NotEmpty(firstAttacks);
        // Exactly the members within ai.focusReach of it (none held an Attack before: this is the first pick).
        int[] inReach = first.Members.Where(kv => Vector2.Distance(kv.Value, first.FocusAt) <= Reach).Select(kv => kv.Key).OrderBy(k => k).ToArray();
        Assert.Equal(inReach, firstAttacks.Select(c => c.Unit.Index).OrderBy(k => k).ToArray());
        _out.WriteLine($"first focus at tick {first.Tick}: {firstAttacks.Count} Attacks, members {first.Members.Count}");

        // Every unit Attack is on its think's focus.
        var focusOf = picks.ToDictionary(p => p.Tick + 1, p => p.Focus);
        foreach (Command c in log.Where(c => c.Kind == CommandKind.Attack && !c.TargetIsBuilding))
        {
            Assert.True(focusOf.TryGetValue(c.Tick, out EntityHandle f), $"an Attack at {c.Tick} with no focus that think");
            Assert.Equal(f, c.Target);
        }

        // Every pick is the lowest-hp live Raider (slot order on ties): after the 40-hp one dies, the next lowest.
        foreach (Pick p in picks)
        {
            int lowest = p.RaiderHp.OrderBy(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
            Assert.True(p.Focus.Index == lowest, $"tick {p.Tick}: focus {p.Focus.Index}, the lowest-hp raider {lowest} ({string.Join(", ", p.RaiderHp)})");
        }
        int died = picks.FindIndex(p => p.Focus != raiders[1]);
        Assert.True(died > 0, "the focus never moved off the first Raider");
        Assert.False(picks[died].RaiderHp.ContainsKey(raiders[1].Index), "the focus moved while the first was alive");
        Assert.Contains(log, c => c.Kind == CommandKind.Attack && c.Target == picks[died].Focus);
        _out.WriteLine($"re-targeted at tick {picks[died].Tick} to {picks[died].Focus.Index}; picks {picks.Select(p => p.Focus.Index).Distinct().Count()} distinct");
    }

    /// <summary>
    /// Criterion 3, the reach: in each of the first picks, a member within <c>ai.focusReach</c> of the focus was sent
    /// <c>Attack</c> on it unless it already held that order; one farther was not.
    /// </summary>
    [Fact]
    public void TheFocusOrders_GoToTheMembersInReach_OnlyOnce()
    {
        (Simulation sim, List<EntityHandle> members, EntityHandle[] raiders) = Focus(AiDifficulty.Hard);
        AiPlayer ai = sim.AiOf(0)!;
        List<Command> log = AiScenes.Log(sim, 0);
        var checkedPicks = 0;
        var held = new Dictionary<int, EntityHandle>();
        ai.ThoughtForTests = a =>
        {
            if (a.FocusTarget.Generation == 0) return;
            Vector2 focus = sim.World.Units.Position[a.FocusTarget.Index];
            foreach (EntityHandle m in members)
            {
                if (!sim.World.Units.IsAlive(m) || a.Board.WaveGeneration[m.Index] != m.Generation) continue;
                bool inReach = Vector2.Distance(sim.World.Units.Position[m.Index], focus) <= Reach;
                bool sent = log.Any(c => c.Tick == a.Now + 1 && c.Kind == CommandKind.Attack && c.Unit == m);
                bool already = held.TryGetValue(m.Index, out EntityHandle h) && h == a.FocusTarget
                    && sim.World.Units.Mode[m.Index] == Combat.CombatMode.Ordered && sim.World.Units.Target[m.Index] == h;
                if (!inReach) Assert.False(sent, $"tick {a.Now}: member {m.Index} out of reach was ordered");
                else if (already) Assert.False(sent, $"tick {a.Now}: member {m.Index} re-ordered onto the focus it holds");
                else if (a.OrderedNotApplied(m.Index, m.Generation) && !sent) { } // ordered by an earlier manager this think
                if (sent) held[m.Index] = a.FocusTarget;
            }
            checkedPicks++;
        };
        for (int t = 0; t < 2400 && raiders.Any(r => sim.World.Units.IsAlive(r)); t++) sim.Tick();
        Assert.True(checkedPicks > 0);
        // Bounded: no member is sent Attack on one Raider more than a few times (the order churn guard).
        foreach (var g in log.Where(c => c.Kind == CommandKind.Attack && !c.TargetIsBuilding).GroupBy(c => (c.Unit, c.Target)))
            Assert.True(g.Count() <= 4, $"member {g.Key.Unit.Index} sent Attack on {g.Key.Target.Index} {g.Count()} times");
    }

    /// <summary>Criterion 4: a Hard member cut below <c>retreat.hpFraction</c> of its max hp is sent Move to the rally point and leaves the wave; the wave goes on.</summary>
    [Fact]
    public void AHardMember_BelowTheFraction_MovesToTheRally_AndLeavesTheWave()
    {
        Simulation sim = Scene(AiDifficulty.Hard, combat: false);
        AiPlayer ai = sim.AiOf(0)!;
        List<EntityHandle> members = Army(sim, 4, 18, 28);
        NextThink(sim);
        Assert.True(ai.Board.WaveActive);
        int start = ai.Board.WaveStartHalfPop;
        List<Command> log = AiScenes.Log(sim, 0);
        sim.World.Units.Hp[members[1].Index] = 30; // 0.3 x 130 = 39
        NextThink(sim);
        Command home = Assert.Single(log, c => c.Unit == members[1]);
        Assert.Equal(CommandKind.Move, home.Kind);
        Assert.Equal(ai.Board.Rally, home.Position);
        Assert.NotEqual(members[1].Generation, ai.Board.WaveGeneration[members[1].Index]);
        Assert.True(ai.Board.WaveActive, "the wave ended");
        Assert.Equal(start - TestSim.Data.Units[Heavy].HalfPop, ai.Board.WaveStartHalfPop); // it left alive: no loss
        foreach (int k in new[] { 0, 2, 3 }) Assert.Equal(members[k].Generation, ai.Board.WaveGeneration[members[k].Index]);
        // A full-hp member at 39 is not wounded ("below"): the boundary.
        sim.World.Units.Hp[members[2].Index] = 39;
        log.Clear();
        NextThink(sim);
        Assert.DoesNotContain(log, c => c.Unit == members[2] && c.Kind == CommandKind.Move);
    }

    /// <summary>Criterion 4: the pulled-back member is not a member of the next wave while it is still below the fraction; the fresh units are.</summary>
    [Fact]
    public void APulledBackMember_IsNotInTheNextWave_WhileStillBelow()
    {
        Simulation sim = Scene(AiDifficulty.Hard, combat: false);
        AiPlayer ai = sim.AiOf(0)!;
        List<EntityHandle> members = Army(sim, 4, 18, 28);
        NextThink(sim);
        sim.World.Units.Hp[members[1].Index] = 30;
        NextThink(sim);
        foreach (int k in new[] { 0, 2, 3 }) sim.World.Units.Free(members[k]);
        NextThink(sim);
        Assert.False(ai.Board.WaveActive, "the wave of three dead members did not end");
        List<EntityHandle> fresh = Army(sim, 4, 18, 34);
        List<Command> log = AiScenes.Log(sim, 0);
        for (int k = 0; k < 30 && ai.Board.WavesStarted < 2; k++) NextThink(sim);
        Assert.Equal(2, ai.Board.WavesStarted);
        Assert.NotEqual(members[1].Generation, ai.Board.WaveGeneration[members[1].Index]);
        foreach (EntityHandle h in fresh) Assert.Equal(h.Generation, ai.Board.WaveGeneration[h.Index]);
        Assert.DoesNotContain(log, c => c.Unit == members[1] && c.Kind == CommandKind.AttackMove);
        Assert.Contains(log, c => c.Unit == fresh[0] && c.Kind == CommandKind.AttackMove);
    }

    /// <summary>Criterion 4: on Normal a member at the same hp stays in the wave, sent nothing home.</summary>
    [Fact]
    public void ANormalMember_AtTheSameHp_IsNotPulledBack()
    {
        Simulation sim = Scene(AiDifficulty.Normal, combat: false);
        AiPlayer ai = sim.AiOf(0)!;
        List<EntityHandle> members = Army(sim, 4, 18, 28);
        NextThink(sim);
        List<Command> log = AiScenes.Log(sim, 0);
        sim.World.Units.Hp[members[1].Index] = 30;
        for (int k = 0; k < 5; k++) NextThink(sim);
        Assert.DoesNotContain(log, c => c.Unit == members[1] && c.Kind == CommandKind.Move);
        Assert.Equal(members[1].Generation, ai.Board.WaveGeneration[members[1].Index]);
    }

    /// <summary>
    /// Criterion 4: an army of only wounded units (each below the fraction, together over the threshold) never starts a wave
    /// on Hard, and production goes on (a barracks trains); on Normal the same army waves.
    /// </summary>
    [Theory]
    [InlineData(AiDifficulty.Hard, 0)]
    [InlineData(AiDifficulty.Normal, 1)]
    public void AWaveOfOnlyWounded_DoesNotStart_AndProductionGoesOn(AiDifficulty difficulty, int waves)
    {
        Simulation sim = Scene(difficulty, combat: false);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        Assert.True(w.Buildings.Spawn(0, w.Data.FindBuilding("malazan_barracks"), 38 * AiScenes.FlatSize + 4, out _));
        BuildMaps.SetTotals(sim, 0, 5000, 5000);
        List<EntityHandle> army = Army(sim, 6, 18, 28);
        foreach (EntityHandle h in army) w.Units.Hp[h.Index] = 30;
        List<Command> log = AiScenes.Log(sim, 0);
        for (int k = 0; k < 20; k++) NextThink(sim);
        if (waves == 0)
        {
            // Fresh units trained meanwhile may start a wave of their own; none of the wounded is ever in one.
            Assert.DoesNotContain(log, c => c.Kind == CommandKind.AttackMove && army.Contains(c.Unit));
            foreach (EntityHandle h in army) Assert.NotEqual(h.Generation, ai.Board.WaveGeneration[h.Index]);
        }
        else Assert.True(ai.Board.WavesStarted >= 1, "Normal's wounded army did not wave");
        Assert.Contains(log, c => c.Kind == CommandKind.Train);
        _out.WriteLine($"{difficulty}: waves {ai.Board.WavesStarted}, trains {log.Count(c => c.Kind == CommandKind.Train)}");
    }

    /// <summary>
    /// BUG-0670 kept with focus fire: a Hard defender standing by enemy units in the enemy's base gets the defense's Move home,
    /// and no focus Attack while it is outside its own base (an Attack there would keep it in that fight); once inside, it may.
    /// </summary>
    [Fact]
    public void ADefenderOutsideTheBase_IsNotFocused_UntilItIsHome()
    {
        Simulation sim = Scene(AiDifficulty.Hard, combat: false);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        EntityHandle away = Army(sim, 1, 46, 40)[0]; // in the east, far outside player 0's defend radius
        HardKnobsScenes.Army(sim, 2, 48, 40, owner: 1, type: Raider); // enemies beside it
        EntityHandle intruder = CombatScenes.Place(sim, 1, Raider, w.NavGrid.CellCenter(16, 27));
        CombatScenes.Spot(sim, 0, intruder);
        List<Command> log = AiScenes.Log(sim, 0);
        var outsideAt = new List<int>();
        ai.ThoughtForTests = a =>
        {
            if (Vector2.Distance(w.Units.Position[away.Index], a.HallPoint) > a.Rules.DefendRadius) outsideAt.Add(a.Now + 1);
        };
        for (int k = 0; k < 40; k++) NextThink(sim);
        Assert.True(ai.Board.Defending, "no defense");
        Assert.Contains(log, c => c.Unit == away && c.Kind == CommandKind.Move); // the defense's Move home
        Assert.True(outsideAt.Count >= 2, "the defender was never outside at a think");
        Assert.DoesNotContain(log, c => c.Unit == away && c.Kind == CommandKind.Attack && outsideAt.Contains(c.Tick));
    }

    /// <summary>Criterion 4: a wounded unit at home still answers the defense (only waves leave it out).</summary>
    [Fact]
    public void AWoundedUnitAtHome_StillAnswersTheDefense()
    {
        Simulation sim = Scene(AiDifficulty.Hard, combat: false);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        EntityHandle wounded = Army(sim, 1, 12, 38)[0];
        w.Units.Hp[wounded.Index] = 20;
        EntityHandle raider = CombatScenes.Place(sim, 1, Raider, w.NavGrid.CellCenter(16, 27));
        CombatScenes.Spot(sim, 0, raider);
        List<Command> log = AiScenes.Log(sim, 0);
        for (int k = 0; k < 3; k++) NextThink(sim);
        Assert.True(ai.Board.Defending, "no defense");
        Assert.Contains(log, c => c.Unit == wounded && c.Kind is CommandKind.AttackMove or CommandKind.Move or CommandKind.Attack);
    }
}
