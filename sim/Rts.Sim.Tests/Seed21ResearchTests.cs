using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Replays;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-H1 (BUG-1190 item 1): <c>Replays/seed21_playable.replay</c> holds two Research commands by tech id, <c>c 4348 0 30 15 0</c>
/// (age_ii on every tree) and <c>c 5556 0 31 15 4</c> (melee_weapons_1 when recorded; melee_weapons_2 since M7-3b renumbered the
/// techs). <c>GatherWedgeQaTests</c> replays it without a data-hash check (M6-SH1) and passes because the click at 5556 was
/// refused on both trees. This row makes that dependency explicit: if a data edit lets the click succeed, this fails for
/// the reason that matters (the recording names a different tech now: re-record it), not as a gathering checkpoint.
/// </summary>
[Collection(SerialCollection.Name)]
public class Seed21ResearchTests(ITestOutputHelper output)
{
    [Fact]
    public void TheRecordedResearchAtTick5556_IsRefused_SoTheRenumberedTechIdNeverApplies()
    {
        string path = System.IO.Path.Combine(TestDataDir.RepoRoot(), "sim", "Rts.Sim.Tests", "Replays", "seed21_playable.replay");
        Assert.Equal(ReplayError.None, ReplayFormat.TryReadFile(path, out Replay? replay));
        GameData data = TestSim.Data;
        var research = new List<Command>();
        foreach (Command c in replay!.Commands)
            if (c.Kind == CommandKind.Research) research.Add(c);
        Assert.Equal(new[] { (4348, 0), (5556, 4) }, research.Select(c => (c.Tick, c.TypeId)));
        var sim = new Simulation(new SimConfig(replay.Seed, replay.PlayerCount, replay.UnitCapacity, replay.CommandCapacity)
        {
            Data = data,
            Map = replay.Map,
            ResourceCapacity = replay.ResourceCapacity,
            Combat = replay.Combat,
        });
        var refused = new List<(int Tick, CommandKind Kind, int Type, CommandRefusal Why, int Detail)>();
        sim.RefusedForTests = (c, why, detail) => refused.Add((c.Tick, c.Kind, c.TypeId, why, detail));
        int next = 0;
        while (sim.TickNumber < 5560)
        {
            while (next < replay.Commands.Length && replay.Commands[next].Tick == sim.TickNumber + 1) sim.Enqueue(replay.Commands[next++]);
            sim.Tick();
        }
        var researchRefusals = refused.Where(r => r.Kind == CommandKind.Research).ToList();
        foreach (var r in researchRefusals)
            output.WriteLine($"tick {r.Tick}: Research {r.Type} ({data.Techs[r.Type].Key}) refused {r.Why} ({(ResearchError)r.Detail})");
        Assert.Contains(researchRefusals, r => r.Type == 4 && r.Why == CommandRefusal.ResearchRefused);
        Assert.False(sim.World.HasTech(0, 4), $"player 0 has tech 4 ({data.Techs[4].Key}): the recording's click at 5556 applied");
    }
}
