using Rts.Sim.Commands;
using Rts.Sim.Tests.Draw;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// M6-SH3 B, "first, before changing the rule" (BUG-1854 / BUG-1860): the four-seat Pale Hills free-for-all (every seat a
/// Normal AI, <see cref="DrawStalledRowsReportTests.Build"/>) on seeds 2 and 4 to 72,000 ticks, QA's two stalled states. At
/// the end, per seat, the terms of "can still train" scanned from the stores (<see cref="DrawReachScan"/>): bank in gold and
/// wood, workers, finished drop-offs, carried and map resources, queued items, which clause of the exact rule holds and what
/// the old rule said; with each seat's Train commands from 48,000 (count and last tick), so a seat that can pay for a unit
/// and never sends the Train shows as an AI fault, not the rule's. A measurement with no bound; it runs on the base too (it
/// reads nothing M6-SH3 added).
/// </summary>
[Trait("Category", "Stress")]
public class DrawReachPreTraceTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(2UL)]
    [InlineData(4UL)]
    public void PaleHillsFourSeats_At72000_TheTermsOfCanStillTrain(ulong seed)
    {
        Simulation sim = DrawStalledRowsReportTests.Build("pale_hills", seed, 4, null);
        World w = sim.World;
        int players = w.Config.PlayerCount;
        var trains = new int[players];
        var lastTrain = new int[players];
        var lastAny = new int[players];
        for (int p = 0; p < players; p++)
        {
            int player = p;
            lastTrain[p] = lastAny[p] = -1;
            sim.AiOf(p)!.SentForTests = c =>
            {
                if (c.Tick < 48_000) return;
                lastAny[player] = c.Tick;
                if (c.Kind != CommandKind.Train) return;
                trains[player]++;
                lastTrain[player] = c.Tick;
            };
        }
        DrawStalledRowsReportTests.Outcome o = DrawStalledRowsReportTests.Play(sim, 72_000);
        output.WriteLine($"PRETRACE pale_hills ffa seed {seed}: tick {o.End}, over {o.Over}, drawn {o.Drawn}, first spent {o.FirstSpent}, spent now {w.MatchSpent}");
        for (int p = 0; p < players; p++)
            output.WriteLine($"PRETRACE   {DrawReachScan.Line(w, p)}; faction {w.Data.Factions[w.FactionOf(p)].Key}; Trains sent from 48,000: {trains[p]} (last {lastTrain[p]}), last command {lastAny[p]}");
        Assert.True(o.End > 0);
    }
}
