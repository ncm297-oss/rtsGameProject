using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Stress;
using Xunit.Abstractions;
using static Rts.Sim.Tests.HardKnobsScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M5-4c-2 round 1 (2026-11-07-0926): the BUG-1121 fix's try rule (a <see cref="MilitaryManager.Focused"/> member whose
/// focus ended without a give-up is re-sent its AttackMove without spending one of its <c>MaxTries</c>) under a focus that
/// flickers in and out of the seat's sight. (The BUG-1120 oracle exemption's narrowness row is gone with the exemption:
/// M5-H5 pushes a hunt's sighting off allied buildings, <c>EndGameHardeningTests</c>.)
/// </summary>
public class HardKnobsRecheckQaTests
{
    private readonly ITestOutputHelper _out;

    public HardKnobsRecheckQaTests(ITestOutputHelper output) => _out = output;

    private static int Crossbow => TestSim.Data.FindUnit("malazan_crossbowman");

    /// <summary>
    /// A focus that flickers: four Crossbowmen of the AI seat (a wave at the first think) meet one Raider of player 1 holding
    /// position at cell (<paramref name="raiderX"/>, 29) (kept at full hp, so it never dies); a Sandstorm of player 1 (a vision
    /// blocker, radius 6 m) on the Raider is made and removed every <paramref name="period"/> ticks, so the Raider leaves and
    /// re-enters the seat's sight while Crossbowmen outside the storm have it in reach. At x 31 the Raider stands in player
    /// 0's base (the Crossbowmen fight it as defenders); at x 46 outside it (as wave members, so BUG-1121's try rule runs).
    /// Over 6,000 ticks the seat's orders stay bounded by the flicker: per member at most one Attack per re-sighting and at
    /// most one AttackMove per re-sighting + 3; a free re-send (an AttackMove to a member flagged
    /// <see cref="MilitaryManager.Focused"/>) at most one per re-sighting. Prints the counts per member.
    /// </summary>
    [Theory]
    [InlineData(AiDifficulty.Normal, 20, 46)]
    [InlineData(AiDifficulty.Hard, 20, 46)]
    [InlineData(AiDifficulty.Hard, 60, 46)]
    [InlineData(AiDifficulty.Hard, 20, 31)]
    [InlineData(AiDifficulty.Hard, 60, 31)]
    [InlineData(AiDifficulty.Hard, 200, 31)]
    public void AFocusFlickeringInAndOutOfSight_CostsAtMostAnOrderPerSighting(AiDifficulty difficulty, int period, int raiderX)
    {
        Simulation sim = Scene(difficulty, combat: true);
        World w = sim.World;
        List<EntityHandle> members = Army(sim, 4, 18, 28, type: Crossbow);
        EntityHandle raider = Army(sim, 1, raiderX, 29, owner: 1, type: Raider)[0];
        sim.Enqueue(Command.HoldPosition(1, raider));
        AbilityDef storm = w.Data.Abilities[w.Data.FindAbility("sandstorm")];
        Assert.True(period < storm.DurationTicks, "a storm must not run out by itself between flips");
        AiPlayer ai = sim.AiOf(0)!;
        var log = new List<Command>();
        int free = 0;
        ai.SentForTests = c =>
        {
            log.Add(c);
            int i = c.Unit.Index;
            if (c.Kind == CommandKind.AttackMove && ai.Board.WaveGeneration[i] == c.Unit.Generation && MilitaryManager.Owed(ai.Board, i) == MilitaryManager.Focused) free++;
        };
        int zone = -1, flips = 0, sightings = 0, focusedThinks = 0;
        bool seen = false;
        ai.ThoughtForTests = a =>
        {
            bool now = w.Units.IsAlive(raider) && w.Fog.CanSeeUnit(0, raider.Index);
            if (now && !seen) sightings++;
            seen = now;
            if (a.FocusTarget == raider) focusedThinks++;
        };
        for (int t = 0; t < 6000; t++)
        {
            if (w.Units.IsAlive(raider)) w.Units.Hp[raider.Index] = w.MaxHp(raider.Index);
            if (t > 0 && t % period == 0)
            {
                if (zone >= 0)
                {
                    w.Zones.Free(zone);
                    zone = -1;
                }
                else zone = w.Zones.Add(1, storm, w.Units.Position[raider.Index]);
                flips++;
            }
            sim.Tick();
        }
        var per = members.Select(m => (
            Attack: log.Count(c => c.Unit == m && c.Kind == CommandKind.Attack),
            AttackMove: log.Count(c => c.Unit == m && c.Kind == CommandKind.AttackMove),
            All: log.Count(c => c.Unit == m))).ToArray();
        _out.WriteLine($"{difficulty}, Raider at x {raiderX}, flip every {period} ticks: {flips} flips, {sightings} sightings at a think, {focusedThinks} thinks focused, " +
            $"{free} free re-sends, waves {ai.Board.WavesStarted}; per member Attack/AttackMove/all: {string.Join("  ", per.Select(p => $"{p.Attack}/{p.AttackMove}/{p.All}"))}");
        foreach (var p in per)
        {
            Assert.True(p.Attack <= sightings, $"a member was sent {p.Attack} Attacks over {sightings} sightings");
            Assert.True(p.AttackMove <= sightings + 3, $"a member was sent {p.AttackMove} AttackMoves over {sightings} sightings");
        }
        Assert.True(free <= members.Count * sightings, $"{free} free re-sends over {sightings} sightings");
        if (difficulty != AiDifficulty.Hard) Assert.Equal(0, free);
    }
}
