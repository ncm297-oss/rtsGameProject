using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Replays;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-H1 (BUG-1190 item 1): <c>Replays/seed21_playable.replay</c> holds two Research commands by tech id. Before M6-S10 they
/// were <c>c 4348 0 30 15 0</c> (age_ii) and <c>c 5556 0 31 15 4</c> (melee_weapons_1 when recorded; melee_weapons_2 since M7-3b
/// renumbered the techs), and this row asserted the click at 5556 was refused, so the stale id never applied
/// (<c>Assert.Equal(new[] { (4348, 0), (5556, 4) }, ...)</c>, a ResearchRefused for tech 4, and tech 4 never had).
/// M6-S10 re-recorded the file on today's ids (<c>GatherWedgeQaTests.CheckpointPrefixTicks</c>' comment): its two Research
/// commands now name age_ii and melee_weapons_1 by their current ids, and both apply. Re-stated: the recording's techs are those
/// two keys, neither click is refused, and Age II is in by tick 5,480; if a later renumbering makes an id stale,
/// this fails for the reason that matters (re-record it), not as a gathering checkpoint.
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
        Assert.Equal(new[] { (4267, data.FindTech("age_ii")), (5475, data.FindTech("melee_weapons_1")) }, research.Select(c => (c.Tick, c.TypeId)));
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
        while (sim.TickNumber < 5480)
        {
            while (next < replay.Commands.Length && replay.Commands[next].Tick == sim.TickNumber + 1) sim.Enqueue(replay.Commands[next++]);
            sim.Tick();
        }
        var researchRefusals = refused.Where(r => r.Kind == CommandKind.Research).ToList();
        foreach (var r in researchRefusals)
            output.WriteLine($"tick {r.Tick}: Research {r.Type} ({data.Techs[r.Type].Key}) refused {r.Why} ({(ResearchError)r.Detail})");
        Assert.Empty(researchRefusals);
        Assert.True(sim.World.HasTech(0, data.FindTech("age_ii")), "the recording's Age II did not apply");
    }
}
