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

    /// <summary>
    /// BUG-1121: a fighter's own focus reach (m): its attack range + its radius + a cell, at most <c>ai.focusReach</c>
    /// (docs/03 "Implementation (M5-4c slice 2)").
    /// </summary>
    internal static float OwnReach(Simulation sim, int slot)
    {
        UnitDef def = sim.World.Data.Units[sim.World.Units.TypeId[slot]];
        return Math.Min(Reach, def.Attack.Range + def.Radius + Map.MapConstants.CellSize);
    }

    /// <summary>What a think's focus pass did: the focus picked, every member's position and reach, and the live raiders' hp and positions then.</summary>
    private sealed record Pick(int Tick, EntityHandle Focus, Vector2 FocusAt, Dictionary<int, Vector2> Members, Dictionary<int, float> MemberReach,
        Dictionary<int, int> RaiderHp, Dictionary<int, Vector2> RaiderAt)
    {
        public bool InReach(int member, Vector2 p) => Vector2.Distance(Members[member], p) <= MemberReach[member];

        public bool InAnyReach(Vector2 p) => Members.Keys.Any(m => InReach(m, p));
    }

    /// <summary>
    /// Criterion 3 (BUG-1121's rule): a Hard wave meeting three of the enemy's Raiders at 100, 40 and 70 hp (combat on). At
    /// every think with a focus, it is the lowest-hp Raider within some member's own reach (attack range + radius + a cell,
    /// at most <c>ai.focusReach</c>), slot order on ties; at the first, exactly the members with it in their own reach are
    /// sent <c>Attack</c> on it; every unit <c>Attack</c> the seat sends is on the focus of its think; the 40-hp Raider is
    /// focused, and once it dies a later focus is another Raider, with <c>Attack</c>s on it.
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
            var reach = new Dictionary<int, float>();
            foreach (EntityHandle m in members)
                if (sim.World.Units.IsAlive(m)) // a wave member or, once the Raiders are at the base, a defender
                {
                    at[m.Index] = sim.World.Units.Position[m.Index];
                    reach[m.Index] = OwnReach(sim, m.Index);
                }
            var hp = new Dictionary<int, int>();
            var rat = new Dictionary<int, Vector2>();
            foreach (EntityHandle r in raiders)
                if (sim.World.Units.IsAlive(r))
                {
                    hp[r.Index] = sim.World.Units.Hp[r.Index];
                    rat[r.Index] = sim.World.Units.Position[r.Index];
                }
            picks.Add(new Pick(a.Now, a.FocusTarget, sim.World.Units.Position[a.FocusTarget.Index], at, reach, hp, rat));
        };
        for (int t = 0; t < 2400 && raiders.Any(r => sim.World.Units.IsAlive(r)); t++) sim.Tick();
        Assert.NotEmpty(picks);

        // The first pick: every member with it in its own reach ordered onto it in that think's commands.
        Pick first = picks[0];
        List<Command> firstAttacks = log.Where(c => c.Tick == first.Tick + 1 && c.Kind == CommandKind.Attack).ToList();
        Assert.All(firstAttacks, c => Assert.Equal(first.Focus, c.Target));
        Assert.NotEmpty(firstAttacks);
        // Exactly the members with it in their own reach (none held an Attack before: this is the first pick).
        int[] inReach = first.Members.Keys.Where(k => first.InReach(k, first.FocusAt)).OrderBy(k => k).ToArray();
        Assert.Equal(inReach, firstAttacks.Select(c => c.Unit.Index).OrderBy(k => k).ToArray());
        _out.WriteLine($"first focus at tick {first.Tick}: {firstAttacks.Count} Attacks, members {first.Members.Count}");

        // Every unit Attack is on its think's focus.
        var focusOf = picks.ToDictionary(p => p.Tick + 1, p => p.Focus);
        foreach (Command c in log.Where(c => c.Kind == CommandKind.Attack && !c.TargetIsBuilding))
        {
            Assert.True(focusOf.TryGetValue(c.Tick, out EntityHandle f), $"an Attack at {c.Tick} with no focus that think");
            Assert.Equal(f, c.Target);
        }

        // Every pick is the lowest-hp live Raider in some member's own reach (slot order on ties).
        foreach (Pick p in picks)
        {
            int lowest = p.RaiderHp.Where(kv => p.InAnyReach(p.RaiderAt[kv.Key])).OrderBy(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
            Assert.True(p.Focus.Index == lowest, $"tick {p.Tick}: focus {p.Focus.Index}, the lowest-hp raider in reach {lowest} ({string.Join(", ", p.RaiderHp)})");
        }
        Assert.Contains(picks, p => p.Focus == raiders[1]);
        int died = picks.FindIndex(p => !p.RaiderHp.ContainsKey(raiders[1].Index));
        Assert.True(died > 0, "no focus after the 40-hp Raider died");
        Assert.NotEqual(raiders[1], picks[died].Focus);
        Assert.Contains(log, c => c.Kind == CommandKind.Attack && c.Target == picks[died].Focus);
        _out.WriteLine($"re-targeted at tick {picks[died].Tick} to {picks[died].Focus.Index}; picks {picks.Select(p => p.Focus.Index).Distinct().Count()} distinct");
    }

    /// <summary>
    /// Criterion 3, the reach (BUG-1121's rule): in each pick, a member with the focus out of its own reach (attack range +
    /// radius + a cell, at most <c>ai.focusReach</c>) was not sent <c>Attack</c> on it, nor one that already held that order.
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
                if (!sim.World.Units.IsAlive(m)) continue; // a wave member or, once the Raiders are at the base, a defender
                bool inReach = Vector2.Distance(sim.World.Units.Position[m.Index], focus) <= OwnReach(sim, m.Index);
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

    /// <summary>
    /// Criterion 4, BUG-1123 regression: the pulled-back member goes out again with the next wave (counted, a member, sent its
    /// AttackMove), flagged <see cref="MilitaryManager.FightsOn"/>, and fights it out: cut further, it is not sent home again.
    /// The fresh members are not flagged: one of them falling below the fraction is pulled back as before. Before the fix a
    /// wounded unit was in no wave again (nothing heals), so a Hard army shrank over a long match.
    /// </summary>
    [Fact]
    public void APulledBackMember_GoesOutWithTheNextWave_AndFightsItOut()
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
        EntityHandle wounded = members[1];
        Assert.Equal(wounded.Generation, ai.Board.WaveGeneration[wounded.Index]);
        Assert.NotEqual(0, ai.Board.WaveFlag[wounded.Index] & MilitaryManager.FightsOn);
        Assert.Equal(5 * TestSim.Data.Units[Heavy].HalfPop, ai.Board.WaveStartHalfPop); // it counts toward the start
        foreach (EntityHandle h in fresh)
        {
            Assert.Equal(h.Generation, ai.Board.WaveGeneration[h.Index]);
            Assert.Equal(0, ai.Board.WaveFlag[h.Index] & MilitaryManager.FightsOn);
        }
        Assert.Contains(log, c => c.Unit == wounded && c.Kind == CommandKind.AttackMove);
        Assert.Contains(log, c => c.Unit == fresh[0] && c.Kind == CommandKind.AttackMove);
        log.Clear();
        sim.World.Units.Hp[wounded.Index] = 10;
        sim.World.Units.Hp[fresh[0].Index] = 30;
        for (int k = 0; k < 6; k++) NextThink(sim);
        Assert.True(ai.Board.WaveActive);
        Assert.DoesNotContain(log, c => c.Unit == wounded && c.Kind == CommandKind.Move);
        Assert.Equal(wounded.Generation, ai.Board.WaveGeneration[wounded.Index]);
        Assert.Equal(CommandKind.Move, Assert.Single(log, c => c.Unit == fresh[0]).Kind);
        Assert.NotEqual(fresh[0].Generation, ai.Board.WaveGeneration[fresh[0].Index]);
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
    /// Criterion 4 (BUG-1123's rule): an army of only wounded units (each below the fraction, together over the threshold)
    /// waves on Hard as on Normal, and production goes on (a barracks trains). On Hard every member is flagged
    /// <see cref="MilitaryManager.FightsOn"/> (wounded when the wave started) and none is sent home while the wave goes on.
    /// </summary>
    [Theory]
    [InlineData(AiDifficulty.Hard)]
    [InlineData(AiDifficulty.Normal)]
    public void AnArmyOfOnlyWounded_Waves_AndFightsItOut_AndProductionGoesOn(AiDifficulty difficulty)
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
        Assert.Equal(1, ai.Board.WavesStarted);
        Assert.True(ai.Board.WaveActive, $"{difficulty}: the wave of the wounded ended");
        foreach (EntityHandle h in army)
        {
            Assert.Equal(h.Generation, ai.Board.WaveGeneration[h.Index]);
            Assert.Contains(log, c => c.Unit == h && c.Kind == CommandKind.AttackMove);
            Assert.DoesNotContain(log, c => c.Unit == h && c.Kind == CommandKind.Move);
            Assert.Equal(difficulty == AiDifficulty.Hard ? MilitaryManager.FightsOn : 0, ai.Board.WaveFlag[h.Index] & MilitaryManager.FightsOn);
        }
        Assert.Contains(log, c => c.Kind == CommandKind.Train);
        _out.WriteLine($"{difficulty}: waves {ai.Board.WavesStarted}, trains {log.Count(c => c.Kind == CommandKind.Train)}");
    }

    /// <summary>
    /// BUG-1121 regression (the try rule): three Hard wave members stopped Idle mid-wave with 2 of their <c>MaxTries</c>
    /// spent and no progress since. One flagged <see cref="MilitaryManager.Focused"/> (its focus died) is re-sent its
    /// AttackMove at the next think with its tries still 2; one flagged so that gave its focus up (the give-up memory set)
    /// spends one (3), as does the unflagged one (the wave's old rule). Before the fix every re-send after a focus spent a
    /// try, so a member that killed three focuses where it stood left the wave.
    /// </summary>
    [Fact]
    public void AMemberWhoseFocusDied_IsSentOn_WithoutSpendingATry_UnlessItGaveTheFocusUp()
    {
        Simulation sim = Scene(AiDifficulty.Hard, combat: false);
        AiPlayer ai = sim.AiOf(0)!;
        List<EntityHandle> members = Army(sim, 4, 18, 28);
        EntityHandle far = Army(sim, 1, 60, 60, owner: 1, type: Raider)[0];
        NextThink(sim);
        Assert.True(ai.Board.WaveActive);
        for (int k = 0; k < 3; k++) sim.Enqueue(Command.Stop(0, members[k]));
        sim.Tick();
        sim.Tick();
        Blackboard board = ai.Board;
        for (int k = 0; k < 3; k++)
        {
            int i = members[k].Index;
            Assert.Equal(UnitState.Idle, sim.World.Units.State[i]);
            board.WaveFlag[i] = k < 2 ? MilitaryManager.Focused : (byte)0;
            board.WaveTries[i] = 2;
            board.WaveBest[i] = Vector2.Distance(sim.World.Units.Position[i], board.WaveTargetPoint);
        }
        sim.World.Units.Ignored[members[1].Index] = far; // it gave its focus up: out of reach
        List<Command> log = AiScenes.Log(sim, 0);
        NextThink(sim);
        for (int k = 0; k < 3; k++)
            Assert.Equal(CommandKind.AttackMove, Assert.Single(log, c => c.Unit == members[k]).Kind);
        Assert.Equal(2, board.WaveTries[members[0].Index]); // its focus died: no try spent
        Assert.Equal(3, board.WaveTries[members[1].Index]); // it gave the focus up: a try spent, the churn stays bounded
        Assert.Equal(3, board.WaveTries[members[2].Index]); // not focused: the wave's rule as before
        for (int k = 0; k < 3; k++) Assert.Equal(0, board.WaveFlag[members[k].Index]);
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
