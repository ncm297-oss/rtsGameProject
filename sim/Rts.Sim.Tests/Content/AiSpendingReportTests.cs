using System.Globalization;
using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D17 report (D15's note: the AI ends its first minutes with gold in hand): on the flat AI scene (<see cref="AiScenes.Flat"/>:
/// a Town Hall, five workers, two mines and 12 trees a base; the other seat a human that does nothing; combat off), Normal,
/// 24,000 ticks (20 min), this prints the AI's gold and wood in hand, its army and worker pop at 5 / 10 / 15 / 20 min, the
/// share of finished ticks each hall / production building had an empty queue, and the waves it started. Only the
/// preconditions are asserted (the seat is an AI of the faction; the match ran 24,000 ticks): the numbers are a report for the
/// owner, and the proposal is in the D17 session report.
/// <para>
/// D18 (QA's D17 note: the flat scene's 12 trees run dry, so re-measure on a real map): the same columns on
/// <see cref="AiScenes.Generated"/> seeds 1 and 7, both factions, with the menu's 12 forests (<c>LaunchOptions.DefaultForests</c>;
/// the helper's six mines stay), combat on (the helper's default: the idle seat's base can fall, and the report says when),
/// and a dated conclusion line on whether wood is still the limit.
/// </para>
/// <para>
/// Reads the AI through its own members (BUG-0641: read from the AI, not re-derived beside it; all <c>internal</c>, reached
/// through <c>InternalsVisibleTo</c>): <c>Simulation.AiOf</c>, <c>AiPlayer.Faction</c>, <c>AiPlayer.Def.BuildOrder</c>,
/// <c>AiPlayer.Board.WavesStarted</c> and <c>AiPlayer.Board.StepDone</c>.
/// Everything else is world state (<c>World.Gold</c> / <c>Wood</c>, the unit and building stores, the queue counts) or
/// <see cref="GameData"/>. A production building is any building type a unit of the faction is <c>trainedAt</c> (the hall too).
/// </para>
/// </summary>
public class AiSpendingReportTests
{
    private const int Tps = SimConstants.TicksPerSecond;
    private const int Ticks = 24_000;
    private const int MarkEvery = 5 * 60 * Tps;

    /// <summary>The game's default forest count (<c>game/scripts/LaunchOptions.DefaultForests</c>, read 2026-10-14).</summary>
    private const int MenuForests = 12;

    /// <summary>A mark counts as wood-bound when wood in hand is under this while gold in hand is at least <see cref="GoldPiled"/>.</summary>
    private const int WoodShort = 100;

    private const int GoldPiled = 300;

    private readonly ITestOutputHelper _out;

    public AiSpendingReportTests(ITestOutputHelper output) => _out = output;

    [Theory]
    [InlineData("malazan")]
    [InlineData("whirlwind")]
    public void FlatNormal_Spending_Report(string faction)
    {
        // Player p plays faction p mod 2 (AiScenes.Seats): the AI seat is the faction's, the other a human that does nothing.
        int player = TestSim.Data.FindFaction(faction);
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, Controllers(player)));
        _out.WriteLine($"{faction}, flat scene, Normal, {Ticks} ticks ({Ticks / Tps / 60} min), combat off, the other seat idle");
        Report(sim, player, -1);
    }

    /// <summary>D18 item 2: seeds 1 and 7 of the generated map, both factions, and the dated conclusion.</summary>
    [Fact]
    public void GeneratedNormal_Spending_Report()
    {
        int bound = 0, marks = 0;
        foreach (ulong seed in new ulong[] { 1, 7 })
            foreach (string faction in new[] { "malazan", "whirlwind" })
            {
                int player = TestSim.Data.FindFaction(faction);
                Simulation sim = AiScenes.Generated(AiScenes.Seats(AiDifficulty.Normal, Controllers(player)), seed, record: false, out _, forests: MenuForests);
                _out.WriteLine("");
                _out.WriteLine($"== {faction}, generated 128 map seed {seed} ({MenuForests} forests, 6 mines), Normal, {Ticks} ticks, combat on, the other seat idle");
                (int b, int m) = Report(sim, player, sim.World.StartLocations[player].Cell);
                bound += b;
                marks += m;
            }
        _out.WriteLine("");
        string verdict = 2 * bound >= marks ? "wood is still the limit" : bound > 0 ? "wood binds at some marks only, not the limit overall" : "wood is not the limit";
        _out.WriteLine($"Conclusion (2026-10-14, shipped data): {verdict} on the generated map: {bound} of {marks} marks had wood in hand < {WoodShort} with gold >= {GoldPiled}.");
    }

    private static SeatController[] Controllers(int aiPlayer)
    {
        SeatController[] controllers = { SeatController.Human, SeatController.Human };
        controllers[aiPlayer] = SeatController.Ai;
        return controllers;
    }

    /// <summary>
    /// Runs <see cref="Ticks"/> and prints the report for <paramref name="player"/>'s AI; with a start cell, the last column
    /// is the wood left in trees within 60 m of it (else on the whole map). Returns the wood-bound marks and the marks.
    /// </summary>
    private (int WoodBound, int Marks) Report(Simulation sim, int player, int startCell)
    {
        World w = sim.World;
        GameData d = w.Data;
        // Preconditions, not findings: the AI seat plays this faction, or every row reports the other faction's AI.
        AiPlayer? ai = sim.AiOf(player);
        Assert.NotNull(ai);
        Assert.Equal(player, w.FactionOf(player));
        Assert.Equal(player, ai!.Faction);

        var producesAt = new bool[d.Buildings.Length];
        foreach (UnitDef u in d.Units)
            if (u.Faction == player && u.TrainedAtTypeId >= 0) producesAt[u.TrainedAtTypeId] = true;

        var b = w.Buildings;
        var finishedTicks = new int[b.Capacity];
        var emptyTicks = new int[b.Capacity];
        var firstFinished = new int[b.Capacity];
        var typeAt = new int[b.Capacity];
        var generationAt = new int[b.Capacity];
        Array.Fill(firstFinished, -1);
        Array.Fill(typeAt, -1);
        var marks = new List<string>();
        int woodBound = 0, idleDefeatedAt = -1;

        for (int t = 0; t < Ticks; t++)
        {
            sim.Tick();
            if (idleDefeatedAt < 0 && w.IsDefeated(1 - player)) idleDefeatedAt = sim.TickNumber;
            for (int s = 0; s < b.Capacity; s++)
            {
                if (!b.Alive[s] || b.Owner[s] != player || !producesAt[b.TypeId[s]] || b.UnderConstruction[s]) continue;
                // A reused slot is another building: the report keeps the first one per slot (the "slot reused" line below
                // says if one was).
                if (typeAt[s] < 0) (typeAt[s], generationAt[s], firstFinished[s]) = (b.TypeId[s], b.Generation[s], sim.TickNumber);
                if (b.Generation[s] != generationAt[s]) continue;
                finishedTicks[s]++;
                if (b.QueueCount[s] == 0) emptyTicks[s]++;
            }
            if (sim.TickNumber % MarkEvery == 0)
            {
                (int army, int workers) = Pop(sim, player);
                if (w.Wood[player] < WoodShort && w.Gold[player] >= GoldPiled) woodBound++;
                marks.Add($"{sim.TickNumber / Tps / 60} min | {w.Gold[player]} | {w.Wood[player]} | {army / 2.0} | {workers / 2.0} | {w.HalfPop[player] / 2.0} / {w.HalfPopCap[player] / 2.0} | {ai.Board.WavesStarted} | {w.Age(player)} | {StepsDone(ai)} / {ai.Def.BuildOrder.Length} | {Left(sim, "gold_mine", -1)} | {Left(sim, "tree", startCell)}");
            }
        }
        Assert.Equal(Ticks, sim.TickNumber);

        _out.WriteLine($"waves started {ai.Board.WavesStarted}; the idle seat defeated: {(idleDefeatedAt < 0 ? "no" : $"at {idleDefeatedAt / Tps} s")}");
        _out.WriteLine("");
        _out.WriteLine($"time | gold | wood | army pop | worker pop | pop / cap | waves started | age | order steps done | gold in mines | wood in trees{(startCell >= 0 ? " within 60 m of the start" : "")}");
        foreach (string row in marks) _out.WriteLine(row);
        _out.WriteLine("");
        _out.WriteLine("building | slot | finished at (s) | finished ticks | empty-queue share");
        int reused = 0;
        for (int s = 0; s < b.Capacity; s++)
        {
            if (typeAt[s] < 0) continue;
            if (b.Alive[s] && b.Generation[s] != generationAt[s]) reused++;
            string share = (100.0 * emptyTicks[s] / Math.Max(1, finishedTicks[s])).ToString("0.0", CultureInfo.InvariantCulture);
            _out.WriteLine($"{d.Buildings[typeAt[s]].Key} | {s} | {firstFinished[s] / Tps} | {finishedTicks[s]} | {share} %");
        }
        _out.WriteLine($"slot reused by another building: {reused}");
        var open = new List<string>();
        for (int i = 0; i < ai.Def.BuildOrder.Length; i++)
            if (!ai.Board.StepDone[i]) open.Add($"{i + 1} {AiContentTests.StepText(ai.Def.BuildOrder[i]).Split(" | ")[0]}");
        _out.WriteLine("order steps not done at the end: " + (open.Count == 0 ? "none" : string.Join("; ", open)));
        return (woodBound, marks.Count);
    }

    private static int StepsDone(AiPlayer ai)
    {
        int n = 0;
        foreach (bool done in ai.Board.StepDone) if (done) n++;
        return n;
    }

    /// <summary>The player's live army and worker half-pop now (the units store, not the AI's last-think census).</summary>
    private static (int Army, int Workers) Pop(Simulation sim, int player)
    {
        var u = sim.World.Units;
        GameData d = sim.World.Data;
        int army = 0, workers = 0;
        for (int s = 0; s < u.Capacity; s++)
        {
            if (!u.Alive[s] || u.Owner[s] != player) continue;
            UnitDef def = d.Units[u.TypeId[s]];
            if (def.Slot == UnitSlot.Worker) workers += def.HalfPop;
            else army += def.HalfPop;
        }
        return (army, workers);
    }

    /// <summary>What is left in every node of the resource <paramref name="key"/> on the map, or within 60 m of <paramref name="aroundCell"/> when it is a cell.</summary>
    private static int Left(Simulation sim, string key, int aroundCell)
    {
        int type = sim.World.Data.FindResource(key);
        var r = sim.World.Resources;
        NavGrid g = sim.World.NavGrid;
        Vector2 at = aroundCell >= 0 ? g.CellCenter(aroundCell % g.Width, aroundCell / g.Width) : default;
        int left = 0;
        for (int k = 0; k < r.Capacity; k++)
        {
            if (!r.Alive[k] || r.TypeId[k] != type) continue;
            if (aroundCell >= 0 && Vector2.Distance(at, g.CellCenter(r.Cell[k] % g.Width, r.Cell[k] / g.Width)) > 60f) continue;
            left += r.Remaining[k];
        }
        return left;
    }
}
