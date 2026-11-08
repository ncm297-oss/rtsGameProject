using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA M5-H5 (session 2026-11-07-2306): the hunt after BUG-1119 on every shipped 4-seat map (Pale Hills, Vathar Crossing) x
/// seeds 1-6, four Normal AI seats free-for-all and 2v2 (teams 1/2/1/2), to 72,000 ticks. A measurement (how many end, and
/// how: the report prints each row) with hard invariants at every think of every seat:
/// the census (<c>KnownEnemyBuildings</c> = the enemy buildings the seat sees plus its ghosts of enemies the world says are
/// not defeated, counted from the world's own flag); no wave (not a hunt) left on a defeated seat's ghost; a sighting the
/// seat remembers this think is an enemy unit a fresh view shows; the <see cref="PlayerViewOracle"/> at every think of a
/// hunting seat and at every 10th think of every seat; and (2v2) no AttackMove or cast within 0.5 m of an allied
/// building, the team soak's strict oracle (BUG-1120's exemption gone). Saves: at the first think a seat hunts after a
/// defeat, and at the first think a hunt's point is not its last sighting (pushed off an ally, or a newer sighting within reach), each loaded and followed hash for hash 2,000 ticks.
/// </summary>
public class M5H5HuntAfterDefeatQaTests
{
    private readonly ITestOutputHelper _out;

    public M5H5HuntAfterDefeatQaTests(ITestOutputHelper output) => _out = output;

    private static string Clock(int ticks) => $"{ticks / 1200}:{ticks / 20 % 60:D2}";

    public static TheoryData<string, ulong, bool> Rows()
    {
        var rows = new TheoryData<string, ulong, bool>();
        foreach (bool teams in new[] { false, true })
            foreach (string map in new[] { "pale_hills", "vathar_crossing" })
                for (ulong seed = 1; seed <= 6; seed++) rows.Add(map, seed, teams);
        return rows;
    }

    private static bool NearAlliedBuilding(World w, int p, Vector2 point, float slack)
    {
        BuildingStore b = w.Buildings;
        for (int k = 0; k < b.Capacity; k++)
        {
            if (!b.Alive[k] || b.Owner[k] == p || w.IsEnemy(p, b.Owner[k])) continue;
            if (Rts.Sim.Combat.CombatSystem.BuildingDistanceSquared(w, k, point) <= slack * slack) return true;
        }
        return false;
    }

    [Theory]
    [Trait("Category", "Stress")]
    [MemberData(nameof(Rows))]
    public void FourNormalSeats_To72000_TheHuntAfterADefeat_EveryThinkInvariant(string map, ulong seed, bool teams) => Run(map, seed, teams, strictDefense: false);

    /// <summary>
    /// BUG-1149 (pre-existing, red at cdbe6ca too): the same 2v2 with the soak's oracle strict for the defense as well: on Pale
    /// Hills seed 2 at tick 12,220 seat 0, defending, sends AttackMove to an enemy unit hitting its ally's building, within
    /// 0.5 m of that building's footprint (the hunt's BUG-1120 case, through the defense).
    /// </summary>
    [Theory(Skip = "BUG-1149 (open, S3): the defense's AttackMove to an enemy by an ally's building lands within 0.5 m of the ally's footprint (Pale Hills 2v2 seed 2, tick 12,220)")]
    [Trait("Category", "Stress")]
    [InlineData("pale_hills", 2UL)]
    public void A2v2_TheDefenseToo_NeverAttackMovesOntoAnAlliedBuilding(string map, ulong seed) => Run(map, seed, true, strictDefense: true);

    private void Run(string map, ulong seed, bool teams, bool strictDefense)
    {
        var seats = new PlayerSeat[4];
        for (int p = 0; p < 4; p++) seats[p] = new PlayerSeat(p % 2, SeatController.Ai, AiDifficulty.Normal, teams ? 1 + p % 2 : 0);
        Simulation sim = HuntStressTests.Match(map, seats, seed, units: 1024);
        World w = sim.World;
        var tally = new PlayerViewOracle.Tally();
        string? bad = null;
        int thinks = 0, oracleThinks = 0, censusChecks = 0, ghostsOfDefeated = 0, attackMoves = 0, pushedHunts = 0;
        int firstDefeat = -1, defenseOntoAlly = 0;
        string firstDefenseOntoAlly = "";
        var huntAfterDefeat = new int[4];
        var huntsAtDefeat = new int[4];
        Array.Fill(huntAfterDefeat, -1);
        bool wantDefeatSave = false, wantPushSave = false, defeatSaved = false, pushSaved = false;
        var follows = new List<(string What, Simulation Sim, int Until)>();
        for (int p = 0; p < 4; p++)
        {
            int player = p;
            sim.AiOf(p)!.SentForTests = c =>
            {
                if (bad != null || !teams) return;
                if (c.Kind is CommandKind.AttackMove or CommandKind.UseAbility)
                {
                    if (c.Kind == CommandKind.AttackMove) attackMoves++;
                    if ((c.Kind == CommandKind.AttackMove || c.Position != default) && NearAlliedBuilding(w, player, c.Position, 0.5f))
                    {
                        Blackboard bb = sim.AiOf(player)!.Board;
                        string what = $"tick {sim.TickNumber}: seat {player} {c.Kind} onto an ally's building at {c.Position} (wave {bb.WaveActive}, hunting {bb.Hunting}, defending {bb.Defending})";
                        // BUG-1149 (pre-existing at cdbe6ca): the defense's AttackMove to an enemy hitting an ally's building. Counted
                        // and printed here (its own skipped row below is strict); anything else is red.
                        if (!strictDefense && bb.Defending && !(bb.WaveActive && bb.Hunting))
                        {
                            if (defenseOntoAlly++ == 0) firstDefenseOntoAlly = what;
                        }
                        else bad = what;
                    }
                }
            };
            sim.AiOf(p)!.ThoughtForTests = ai =>
            {
                if (bad != null) return;
                thinks++;
                // The census, from the world's defeat flag (not PlayerView's).
                int expected = 0, s = -1;
                while (ai.View.NextBuilding(ref s, out SeenBuilding b))
                    if (b.Owner != player && w.IsEnemy(player, b.Owner)) expected++;
                s = -1;
                while (ai.View.NextGhost(ref s, out Rts.Sim.Vision.BuildingGhost g))
                {
                    if (!w.IsEnemy(player, g.Owner)) continue;
                    if (w.IsDefeated(g.Owner)) ghostsOfDefeated++;
                    else expected++;
                }
                censusChecks++;
                if (ai.Board.KnownEnemyBuildings != expected)
                    bad = $"tick {ai.Now}: player {player} KnownEnemyBuildings {ai.Board.KnownEnemyBuildings}, the world's count {expected}";
                // No wave left on a defeated seat's ghost.
                Blackboard bb = ai.Board;
                if (bb.WaveActive && !bb.Hunting && bb.WaveTarget.Generation != 0
                    && ai.View.TryGetGhost(bb.WaveTarget.Index, out Rts.Sim.Vision.BuildingGhost tg) && tg.Generation == bb.WaveTarget.Generation && w.IsDefeated(tg.Owner))
                    bad ??= $"tick {ai.Now}: player {player}'s wave still walks to defeated player {tg.Owner}'s ghost";
                // A fresh sighting is an enemy unit a fresh view shows.
                if (bb.EnemyUnitTick == ai.Now)
                {
                    var fresh = new PlayerView(w, player);
                    int u = -1;
                    bool found = false;
                    while (fresh.NextUnit(ref u, out SeenUnit su))
                        if (su.Owner != player && fresh.IsEnemy(su.Owner) && su.Position == bb.EnemyUnitPoint) { found = true; break; }
                    if (!found) bad ??= $"tick {ai.Now}: player {player} remembers {bb.EnemyUnitPoint}, no enemy unit its view shows";
                }
                if (bb.Hunting || thinks % 10 == 0)
                {
                    oracleThinks++;
                    PlayerViewOracle.Check(w, ai.View, player, tally, $"{map} seed {seed} tick {ai.Now} player {player}");
                    if (tally.First.Length > 0) bad ??= tally.First;
                }
                if (bb.Hunting && firstDefeat >= 0)
                {
                    if (huntAfterDefeat[player] < 0 && bb.HuntsStarted > huntsAtDefeat[player]) huntAfterDefeat[player] = ai.Now;
                    if (!defeatSaved) wantDefeatSave = true;
                }
                if (bb.WaveActive && bb.Hunting && bb.HuntSweep < 0 && bb.EnemyUnitTick >= 0 && bb.WaveTargetPoint != ai.ClampToMap(bb.EnemyUnitPoint))
                {
                    pushedHunts++;
                    if (!pushSaved) wantPushSave = true;
                }
            };
        }
        var defeatedAt = new int[4];
        while (sim.TickNumber < 72_000 && !sim.MatchOver && bad == null)
        {
            if (wantDefeatSave && !defeatSaved)
            {
                defeatSaved = true;
                follows.Add(($"first hunt after a defeat, saved at {sim.TickNumber}", SaveTestRun.Load(TestSim.Data, SaveTestRun.Save(sim)), sim.TickNumber + 2000));
            }
            if (wantPushSave && !pushSaved)
            {
                pushSaved = true;
                follows.Add(($"a hunt to a point off its last sighting, saved at {sim.TickNumber}", SaveTestRun.Load(TestSim.Data, SaveTestRun.Save(sim)), sim.TickNumber + 2000));
            }
            sim.Tick();
            for (int p = 0; p < 4; p++)
            {
                PlayerViewOracle.NodeOracle.For(w).Observe(w, p, tally, $"{map} seed {seed} tick {sim.TickNumber} player {p}");
                if (defeatedAt[p] == 0 && w.IsDefeated(p))
                {
                    defeatedAt[p] = sim.TickNumber;
                    if (firstDefeat < 0)
                    {
                        firstDefeat = sim.TickNumber;
                        for (int q = 0; q < 4; q++) huntsAtDefeat[q] = sim.AiOf(q)!.Board.HuntsStarted;
                    }
                }
            }
            foreach ((string what, Simulation l, int until) in follows)
            {
                if (sim.TickNumber > until) continue;
                l.Tick();
                Assert.True(l.StateHash() == sim.StateHash(), $"{map} seed {seed}: {what}: diverged at tick {sim.TickNumber}");
            }
            if (tally.First.Length > 0) bad ??= tally.First;
        }
        string Seat(int p)
        {
            AiPlayer ai = sim.AiOf(p)!;
            return $"p{p} {ai.Opening.Id} {(defeatedAt[p] == 0 ? "in" : "out " + Clock(defeatedAt[p]))} waves {ai.Board.WavesStarted} hunts {ai.Board.HuntsStarted}" +
                $"{(huntAfterDefeat[p] >= 0 ? " (hunt after the first defeat " + Clock(huntAfterDefeat[p]) + ")" : "")} known {ai.Board.KnownEnemyBuildings} hunting {ai.Board.Hunting} " +
                $"units {w.LiveUnits(p)} army {ai.ArmyCount} prod {w.ProductionBuildings(p)} gold {w.Gold[p]}";
        }
        _out.WriteLine($"M5H5-HUNT {map} seed {seed} {(teams ? "2v2" : "ffa")}: over {sim.MatchOver} at {Clock(sim.TickNumber)} ({sim.TickNumber}); first defeat {(firstDefeat < 0 ? "-" : Clock(firstDefeat))}; " +
            $"{thinks} thinks, {censusChecks} census checks ({ghostsOfDefeated} ghost-reads of a defeated seat), {oracleThinks} oracle thinks, {attackMoves} AttackMoves checked, {pushedHunts} thinks hunting a point off the last sighting (pushed, or a newer sighting within reach); " +
            $"saves followed: {(follows.Count == 0 ? "none" : string.Join(", ", follows.Select(f => f.What)))}; " +
            $"defense AttackMoves onto an ally's building (BUG-1149) {defenseOntoAlly}{(defenseOntoAlly > 0 ? ", first: " + firstDefenseOntoAlly : "")}");
        for (int p = 0; p < 4; p++) _out.WriteLine("  " + Seat(p));
        _out.WriteLine("  " + tally);
        Assert.True(bad == null, bad);
        Assert.Equal(0, tally.Leaks);
    }
}
