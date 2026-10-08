using System.Numerics;
using System.Text;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M4-2a (session 2026-10-08-0313): replay format 4 attacks beyond the developer's rows: a format 3 file recorded with
/// combat off (which can't say so) played with the default switch fails cleanly at a checkpoint and plays with the
/// override; a header that misplaces or drops the <c>combat</c> line; out-of-range integers in the target fields; an
/// Attack with a stale or bogus target handle recorded and played back.
/// </summary>
public class ReplayFormat4QaTests
{
    private static byte[] Reseal(string text) => ReplayFormat.Seal(text[..text.LastIndexOf("checksum ", StringComparison.Ordinal)]);

    private static string Text(Replay r) => Encoding.ASCII.GetString(ReplayFormat.Write(r));

    /// <summary>Two players' fighters standing in reach of each other, recorded for 200 ticks with combat as given.</summary>
    private static Replay FightRecorded(bool combat, Action<Simulation, int>? perTick = null)
    {
        var config = combat
            ? TestSim.Config(Seed: 4, PlayerCount: 2, UnitCapacity: 16, CommandCapacity: 64)
            : TestSim.ConfigNoCombat(Seed: 4, PlayerCount: 2, UnitCapacity: 16, CommandCapacity: 64);
        var sim = new Simulation(config);
        var rec = new ReplayRecorder(sim, checkpointInterval: 10);
        NavGrid g = sim.World.NavGrid;
        int center = MoveScenario.CentralCell(g);
        Vector2 c = g.CellCenter(center % g.Width, center / g.Width);
        sim.Enqueue(Command.SpawnUnit(0, HeavyInfantry, c));
        sim.Enqueue(Command.SpawnUnit(1, Raider, c + new Vector2(1.0f, 0f)));
        for (int t = 0; t < 200; t++)
        {
            perTick?.Invoke(sim, t);
            sim.Tick();
        }
        return rec.ToReplay();
    }

    [Fact]
    public void Format3File_RecordedCombatOff_DefaultPlaysCombatOn_FailsCleanlyAtACheckpoint_OverridePlays()
    {
        Replay off = FightRecorded(combat: false);
        Assert.False(off.Combat);
        // What a pre-M4-2a combat-off recording looks like on disk: format 3, no combat line (so "on").
        Replay asFormat3 = ReplayTestRun.With(off, formatVersion: 3, combat: true);
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(asFormat3), out Replay? back));
        Assert.True(back!.Combat);
        ReplayResult played = ReplayPlayer.Run(back, TestSim.Data);
        Assert.Equal(ReplayError.CheckpointMismatch, played.Error);
        Assert.True(played.Tick > 0);
        Assert.True(ReplayPlayer.Run(back, TestSim.Data, combat: false).Ok);
    }

    [Fact]
    public void Format4_CombatLineMissing_Misplaced_OrDoubled_IsMalformed()
    {
        string text = Text(FightRecorded(combat: true));
        Assert.Contains("\ncombat 1\n", text);
        string missing = text.Replace("\ncombat 1\n", "\n");
        Assert.Equal(ReplayError.Malformed, ReplayFormat.TryRead(Reseal(missing), out _));
        string doubled = text.Replace("\ncombat 1\n", "\ncombat 1\ncombat 1\n");
        Assert.Equal(ReplayError.Malformed, ReplayFormat.TryRead(Reseal(doubled), out _));
        int at = missing.IndexOf("\nmap.width ", StringComparison.Ordinal);
        int end = missing.IndexOf('\n', at + 1);
        string misplaced = missing[..(end + 1)] + "combat 1\n" + missing[(end + 1)..];
        Assert.Equal(ReplayError.Malformed, ReplayFormat.TryRead(Reseal(misplaced), out _));
        // A combat line in a format 3 file is an unknown line there.
        string format3 = text.Replace("rts-replay 4\n", "rts-replay 3\n");
        Assert.NotEqual(ReplayError.None, ReplayFormat.TryRead(Reseal(format3), out _));
    }

    [Theory]
    [InlineData(9, "2147483648")]   // index past int
    [InlineData(9, "-2147483649")]  // index below int
    [InlineData(10, "99999999999")] // generation past int
    [InlineData(9, "+1")]           // a sign
    [InlineData(9, "-0")]           // non-canonical zero
    [InlineData(11, "-1")]          // building flag
    public void Format4_OutOfRangeTargetFields_AreMalformed(int field, string value)
    {
        string text = Text(FightRecorded(combat: true));
        string first = text.Split('\n').First(l => l.StartsWith("c ", StringComparison.Ordinal));
        string[] f = first.Split(' ');
        f[field + 1] = value; // f[0] is "c"
        string edited = text.Replace(first + "\n", string.Join(' ', f) + "\n");
        Assert.Equal(ReplayError.Malformed, ReplayFormat.TryRead(Reseal(edited), out _));
    }

    /// <summary>Attacks with a stale generation, a bogus index and int.MinValue fields are recorded, round-trip and replay to every checkpoint.</summary>
    [Fact]
    public void Format4_StaleAndBogusAttackTargets_RoundTripAndPlay()
    {
        Replay r = FightRecorded(combat: true, (sim, t) =>
        {
            UnitStore u = sim.World.Units;
            if (t < 3 || u.Count < 2) return;
            var me = new EntityHandle(0, u.Generation[0]);
            if (!u.Alive[0]) return;
            EntityHandle target = (t % 4) switch
            {
                0 => new EntityHandle(1, u.Generation[1] + 1),
                1 => new EntityHandle(-1, 1),
                2 => new EntityHandle(int.MinValue, int.MinValue),
                _ => new EntityHandle(int.MaxValue, 0),
            };
            sim.Enqueue(Command.Attack(0, me, target, t % 8 < 4, queued: t % 3 == 0));
        });
        Assert.Contains(r.Commands, c => c.Kind == CommandKind.Attack && c.Target.Index == int.MinValue);
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(r), out Replay? back));
        ReplayTestRun.AssertEqual(r, back!);
        Assert.True(ReplayPlayer.Run(back!, TestSim.Data).Ok);
    }
}
