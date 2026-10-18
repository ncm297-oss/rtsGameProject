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
/// shipped data still plays the recorded game tick for tick. M4-V5 (BUG-0273) re-recorded it the same way on the M4-3b tree (placement needs explored ground): 12,131 ticks. The file is headered with the data hash it was recorded at;
/// <see cref="SameGameDataHashes"/> lists the shipped hashes since then whose changes do not touch this match (the header
/// is substituted in code: the file is checksummed and lives in studio/).
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

    /// <summary>The replay's recorded data hash (M5-4f's shipped data; before it M4-3b's C22FBFEA0197CF3E, re-recorded in M4-V5, BUG-0273).</summary>
    private const ulong RecordedDataHash = 0xC059AD620DE581F1;

    /// <summary>
    /// Shipped data hashes that play the recorded match unchanged: the recording's own. Add one only for a data change that
    /// cannot touch this game, and keep <see cref="CheckpointPrefixTicks"/> passing: it proves the game is the same. A
    /// change of the state hash's composition (any newly hashed state) fails every checkpoint: re-record then (docs/03
    /// "Save/load and replays", hash format).
    /// </summary>
    // M5-4f re-record: the list starts again from the new recording. The old one (C22FBFEA0197CF3E) was played by these
    // shipped hashes, the last AB811AD47F68D0AE (D19); gatherRate 0.85 / 0.75 (plus ai.defendRatio / defendMinValue /
    // recallHallHp, no AI seat) changes the gathering, so no old hash plays the new recording. Old list, for the record:
    // 7E04011FC88881F3, 41842085985611BF, 5896D3E7C9FD36AD, EA5CB5A0AFCEEDBE, D22C849A175F9ED5, 4383EBB9345FABF7,
    // 889D6BEA3A49F08E, C4C16BD39408471A, 5760E83CB301B62E, 801A8BC7AA612A21, 8409DB7517579FEF, 72EF431E70C49C39,
    // C6FC120A55AE4A0B, AB811AD47F68D0AE, with the reasons below.
    private static readonly ulong[] SameGameDataHashes = { RecordedDataHash }; // was { RecordedDataHash, 0x7E04011FC88881F3, 0x41842085985611BF, 0x5896D3E7C9FD36AD, 0xEA5CB5A0AFCEEDBE, 0xD22C849A175F9ED5, 0x4383EBB9345FABF7, 0x889D6BEA3A49F08E, 0xC4C16BD39408471A, 0x5760E83CB301B62E, 0x801A8BC7AA612A21, 0x8409DB7517579FEF, 0x72EF431E70C49C39, 0xC6FC120A55AE4A0B, 0xAB811AD47F68D0AE }; // M4-4a: statuses/abilities files, BUG-0303; M4-4b-1: the Cusser (no Sapper in this match); M4-4b-2: Blinded, Sandstorm (no Priest casts in it); D10c: description text only; M4-4b-3a: Frenzied, the Zealot's passives (no Zealot in it); D11: Frenzied / Blinded description text only; M4-5: the stealthed / revealed statuses, rules.json stealth, the units' detector word (no stealth in this match); D12: casters' detector (no stealth in this match); D13: the line units' bonusVs.mounted 1.5 -> 1.1 (no line unit hits a mounted one in this match); M5-3: ai.json and rules.json ai joined the hash (no AI seat in this match); M5-3 fix round 1: rules.json ai.scoutGrid (BUG-0593, no AI seat); M5-4b: rules.json ai.defendRadius / defendFor / workerFleeRadius (no AI seat); M5-4c: rules.json ai.castMinTargets (no AI seat); D19: ×1.5, Easy casts, building damage: no line unit hits a mounted one, no AI seat, no building is attacked in the seed-21 match

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
        // A data-hash mismatch fails loudly rather than returning: a silent return would make the un-skipped row pass
        // without testing anything (D4 moves the shipped data hash; see BUG-0146's notes).
        Assert.Equal(RecordedDataHash, replay!.DataHash);
        Assert.True(Array.IndexOf(SameGameDataHashes, data.ContentHash()) >= 0,
            $"shipped data hash {data.ContentHash():X16} is not one known to play the recorded match ({replay.DataHash:X16}): re-record it from M3PlayableTest -- --seed 21, or add the hash for a change that cannot touch this game");
        Assert.True(replay.Checkpoints.Length >= CheckpointPrefixTicks, $"the replay holds {replay.Checkpoints.Length} checkpoints, fewer than the {CheckpointPrefixTicks} the row checks");
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
