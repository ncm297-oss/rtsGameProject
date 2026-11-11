using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Replays;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M3-V4 (2026-10-07-2014), BUG-0146: on the M3 Playable scene's seed 21 run, four laborers gathering the tree at
/// cell (57, 45) stood in <see cref="UnitState.Gathering"/> 1.7-3.8 m from its footprint (out of
/// <see cref="EconomyConstants.Reach"/>), so player 0's wood income stopped. The replay (the scene's own command stream,
/// a checkpoint every tick) is attached to the bug. The row replays it and requires that no worker on a wood loop stays
/// out of reach and within 1 m of one spot for more than 600 ticks in a row (its 20-tick retry walks end where they
/// started). Un-skipped with the fix (M4-2a).
/// <para>
/// M4-H1 (BUG-0211): re-recorded from <c>M3PlayableTest -- --seed 21 --break 19</c> on the post-fix, fog-era build (the
/// state hash covers the fog's explored and visible bits, which no older recording can match): 11,541 ticks, the
/// scene's run up to its "Heavy Infantry panel" step. The row now checks every recorded checkpoint, so it proves the
/// shipped data still plays the recorded game tick for tick. M4-V5 (BUG-0273) re-recorded it the same way on the M4-3b tree (placement needs explored ground): 12,131 ticks. The file is headered with the data hash it was recorded at
/// (M6-SH1: not compared; the recorded checkpoints are what proves the shipped data still plays this game).
/// </para>
/// <para>
/// M5-4f: <c>rules.json</c> <c>gatherRate</c> gold 0.7 -> 0.85, wood 0.6 -> 0.75 changes every gathering game, this one too,
/// so it was re-recorded the same way (<c>M3PlayableTest -- --seed 21 --break 19</c>) on the M5-4f tree: 7,682 ticks (the
/// quicker economy reaches the scene's "Heavy Infantry panel" step sooner). The new file is
/// <c>sim/Rts.Sim.Tests/Replays/seed21_playable.replay</c> (the sim dev doesn't write <c>studio/**</c>; the bug's
/// attachment stays as BUG-0146's history). The scene runs on frame time, so two recordings differ by a few ticks; any
/// one of them is a valid recording for this row.
/// </para>
/// <para>
/// M5-H3 (BUG-0790): the stand-point change (corner cells, the own cell's free point) moves this game from tick 19, so the
/// file's checkpoints were re-derived from its own recorded commands on the M5-H3 tree (header and commands unchanged; the
/// <c>k</c> lines and checksum only). Longest out-of-reach stand on a wood loop 478 ticks (332 before), bound 600.
/// </para>
/// </summary>
[Collection(SerialCollection.Name)]
public class GatherWedgeQaTests
{
    private readonly ITestOutputHelper _out;

    public GatherWedgeQaTests(ITestOutputHelper output) => _out = output;

    // M6-SH1: the data-hash pin (a recorded hash plus a list of shipped hashes known to play this match, the last
    // 30C51BB619A7E650) is gone: CheckpointPrefixTicks below is the proof the shipped data plays the recorded game. Its
    // history is in git (this file before M6-SH1). M7-1 (BUG-0750's rule): the golden cross_map_seed1.replay's header was
    // regenerated once, to 64DF327980743173 (UnitDef.Scale, FactionDef.RegenPerTick / RegenDelayTicks, StatusDef.StateHashId
    // and the new regenerating status entered GameData.ContentHash); its k lines are byte for byte as before.
    // D27 (BUG-1163, BUG-0750's rule): regenerated once more, 64DF327980743173 -> 3E33BCE5D418EC9B, because Hard's ai.json
    // gatherBonus moved 0.25 -> 0.4 in both factions (difficulty values are in GameData.ContentHash); the golden's one seat is
    // a human Normal seat, so its k lines are byte for byte as before (header data-hash and checksum only).
    // M7-3b (BUG-1171, the one regen of SameGameDataHashes' rule): "M7-3b: tech / ability ids by faction order; outcomes
    // unchanged". GameData.Techs is the common techs first, then each faction's in common/factions.json order, and
    // GameData.Abilities faction by faction, so ContentHash (it hashes the keys in id order) moved: 3E33BCE5D418EC9B ->
    // 3A04D4E370C643DA. The golden's game researches nothing and casts nothing, so its k lines are byte for byte as
    // before (header data-hash and checksum only). seed21_playable.replay holds two Research commands by id (ticks 4348
    // and 5556); its checkpoints still hold to CheckpointPrefixTicks on the M7-3b tree (this row passes unchanged).

    /// <summary>
    /// Ticks the replay must still match its recorded checkpoints: all of them (M4-H1, BUG-0211; before the re-recording
    /// only the first 19, the setup before the M4-2a fix changed the match, and from M4-3a none; M4-V5's re-recording,
    /// BUG-0273, held 12,131; M5-4f's, 7,682).
    /// </summary>
    private const int CheckpointPrefixTicks = 7682;

    // M4-V5 (BUG-0273): re-recorded from M3PlayableTest -- --seed 21 --break 19 on the M4-3b tree, whose scene builds on
    // explored ground (the old recording built a House on unexplored ground at tick 616, which placement refuses since M4-3b).
    [Fact]
    public void Seed21PlayableReplay_NoGathererStandsOutOfReachForever()
    {
        // M5-4f: the re-recording (the studio/bugs attachment is the M4-V5 one, kept as BUG-0146's history).
        string path = System.IO.Path.Combine(TestDataDir.RepoRoot(), "sim", "Rts.Sim.Tests", "Replays", "seed21_playable.replay");
        Assert.Equal(ReplayError.None, ReplayFormat.TryReadFile(path, out Replay? replay));
        GameData data = DataLoader.LoadAll(TestDataDir.Shipped).Data!;
        // M6-SH1: no data-hash check; the checkpoints are the pin (a data change that moves this game fails one of them:
        // re-record it then from M3PlayableTest -- --seed 21 --break 19).
        Assert.True(replay!.Checkpoints.Length >= CheckpointPrefixTicks, $"the replay holds {replay.Checkpoints.Length} checkpoints, fewer than the {CheckpointPrefixTicks} the row checks");
        var sim = new Simulation(new SimConfig(replay.Seed, replay.PlayerCount, replay.UnitCapacity, replay.CommandCapacity)
        {
            Data = data,
            Map = replay.Map,
            ResourceCapacity = replay.ResourceCapacity,
            Combat = replay.Combat,
        });
        World w = sim.World;
        UnitStore u = w.Units;
        // Per worker on a wood loop: ticks since it was last in reach of its node or moved 1 m from where
        // the count started (the 20-tick retry walks of a wedged worker end on the same spot, so state and velocity flicker).
        var still = new int[u.Capacity];
        var anchor = new Vector2[u.Capacity];
        int next = 0, worst = 0, worstUnit = -1, worstTick = 0;
        while (sim.TickNumber < replay.TickCount)
        {
            while (next < replay.Commands.Length && replay.Commands[next].Tick == sim.TickNumber + 1) sim.Enqueue(replay.Commands[next++]);
            sim.Tick();
            if (sim.TickNumber <= CheckpointPrefixTicks)
                Assert.True(replay.Checkpoints[sim.TickNumber - 1].Hash == sim.StateHash(), $"the replay no longer plays the recorded game: checkpoint {sim.TickNumber} differs");
            for (int i = 0; i < u.Capacity; i++)
            {
                bool onWood = u.Alive[i] && w.Resources.IsAlive(u.GatherNode[i])
                    && w.Data.Resources[w.Resources.TypeId[u.GatherNode[i].Index]].Resource == ResourceKind.Wood;
                if (!onWood || InReach(w, i) || Vector2.Distance(u.Position[i], anchor[i]) > 1f)
                {
                    still[i] = 0;
                    anchor[i] = u.Position[i];
                    continue;
                }
                if (++still[i] > worst) (worst, worstUnit, worstTick) = (still[i], i, sim.TickNumber);
            }
        }
        _out.WriteLine($"longest out-of-reach stand on a wood loop: unit {worstUnit}, {worst} ticks, up to tick {worstTick}; wood {w.Wood[0]}");
        Assert.True(worst <= 600, $"unit {worstUnit} stood out of reach of its tree for {worst} ticks (to tick {worstTick})");
    }

    private static bool InReach(World w, int i)
    {
        int n = w.Units.GatherNode[i].Index;
        ResourceDef def = w.Data.Resources[w.Resources.TypeId[n]];
        const float cs = Map.MapConstants.CellSize;
        int a = w.Resources.Cell[n], gw = w.NavGrid.Width;
        float x0 = a % gw * cs, z0 = a / gw * cs, x1 = x0 + def.FootprintWidth * cs, z1 = z0 + def.FootprintHeight * cs;
        Vector2 p = w.Units.Position[i];
        float dx = MathF.Max(MathF.Max(x0 - p.X, 0f), p.X - x1), dz = MathF.Max(MathF.Max(z0 - p.Y, 0f), p.Y - z1);
        return dx * dx + dz * dz <= EconomyConstants.Reach * EconomyConstants.Reach;
    }
}
