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
    // D28 (data track, rule 51, BUG-0750's rule): regenerated once more, 3A04D4E370C643DA -> 67269D955D18D885, because the
    // Teblor roster shipped: common/factions.json appends "teblor" and game/data/factions/teblor/ adds 7 units, 10 buildings,
    // the tech teblor_blood_oil_anointing (id 9) and the ability blood_oil_frenzy (id 3), all appended after every shipped
    // id, so GameData.ContentHash moved through the new keys. The golden's one seat is a Malazan human seat, so its k lines
    // are byte for byte as before (header data-hash and checksum only).
    // D31 (data track, rule 51, BUG-0750's rule): regenerated once more, 67269D955D18D885 -> 27036D3A2F2BA1CB, because the
    // Teblor's finished numbers shipped in one move: the teblor_frenzied status (damageBonus 0.2, onEnd Slowed 0.2 for 4 s)
    // in common/statuses.json, blood_oil_frenzy applying it, Blood-oil Anointing's statusDuration +4 s, scale 1.25 on the ten
    // Teblor buildings, the Gatherer's carry 15, and V24 as the Teblor standard opening with its own reserveArmyShare 0.25
    // (all in GameData.ContentHash). The golden's one seat is a Malazan human seat, so its k lines are byte for byte as
    // before (header data-hash and checksum only).
    // D32 (data track, rule 51, BUG-0750's rule): regenerated once more, 27036D3A2F2BA1CB -> 5353DF8A1473FC6D, because the
    // Teblor standard opening changed in one move: its description (BUG-1390), the expand step first of the Age II steps,
    // the Shamans' Circle at 9:00 with a train step for two Shamans, the Carver and Breakers Hall at 10:00, the Anointing
    // at 11:30 and Armor at 12:00 (AI openings, words included, are in GameData.ContentHash; the Stone Cairn's scale field
    // moved in buildings.json, which loads the same). The golden's one seat is a Malazan human seat, so its k lines are
    // byte for byte as before (header data-hash and checksum only).
    // M6-S10 (sim track, BUG-0750's rule): regenerated once more, 5353DF8A1473FC6D -> B74DC7AE61B4862D, because the gather
    // overlap shipped: common/rules.json gains "gatherOverlap": 0.5 (hashed when non-zero, RulesDef.GatherOverlap). The
    // golden's army never gathers and the M6-S10 cure (EconomyConstants.QueueArrival) reads only footprint walkers, so its
    // k lines are byte for byte as before (header data-hash and checksum only).
    // D33 (data track, rule 51, BUG-0750's rule): regenerated once more, B74DC7AE61B4862D -> 318E8C4256456C05, because three AI
    // openings changed in one move (all in GameData.ContentHash): the Malazan cadre_rush waves at 16 army pop / 1,184 (was
    // 18 / 1,332) and the Whirlwind holy_war at 14 / 918 (was 20 / 1,312), each with its description, and the Teblor standard
    // opening gains a train step each for a Breaker, a Destrier Rider and a War-dog pack, with the Breakers Hall at 9:00
    // ahead of the expansion and the Stables first of the 10:00 steps, and a new description (BUG-0982, BUG-1553, BUG-1420).
    // The golden's one seat is a Malazan human seat, so its k lines are byte for byte as before (header data-hash and
    // checksum only).
    // D34 (data track, fix round 1, BUG-1641; rule 51, BUG-0750's rule): regenerated once more, 318E8C4256456C05 ->
    // A732F387C2BB7E4D, because the Teblor standard opening changed in one move (in GameData.ContentHash): the Bloodwood
    // Carver and the Blood-oil Anointing (due 10:00, was 11:30) moved to just after the Breaker's train step, and a softened
    // description (BUG-1612, BUG-1613). The golden's one seat is a Malazan human seat, so its k lines are byte for byte as
    // before (header data-hash and checksum only; checksum 807C173DF2B5F439 -> E7359ADD20818C67).
    // M6-S13 (sim track, the session's one hash move; BUG-0750's rule): regenerated once more, A732F387C2BB7E4D ->
    // 3D93D45345A3B4EA, because the owner's draw rule shipped: common/rules.json gains "draw": { "afterSeconds": 300 }
    // (RulesDef.DrawAfterTicks, hashed when the block is present). The golden's one human seat is never in a spent match (one
    // seat: fewer than two sides ever built), so the draw clock stays 0, its state hashes as before, and its k lines are byte
    // for byte as before (header data-hash and checksum only; checksum E7359ADD20818C67 -> 1708E5EC501A353D).
    // D38 (data track, the session's one hash move; rule 51, BUG-0750's rule): regenerated once more, 3D93D45345A3B4EA ->
    // B7947DC602B1E11B, because the Teblor standard opening ships a build order per difficulty (BUG-1612): its buildOrder is
    // D37's Normal order M1RS (one Shaman, throughReserve on the Destrier Rider and Shaman steps) and the new buildOrders
    // field holds Easy's K9KC10S and Hard's QRD1 (M7-9; the field and each step's switches are in GameData.ContentHash). The
    // golden's one seat is a Malazan human seat, so its k lines are byte for byte as before (header data-hash and checksum
    // only; checksum 1708E5EC501A353D -> C7D815619BB83C73).
    // M6-S14 (sim track, the session's one hash move; BUG-0750's rule): regenerated once more, B7947DC602B1E11B ->
    // 3BB926FD74795B9E, because the AI's worker floor shipped (BUG-1660): common/rules.json ai gains "minWorkers": 4
    // (AiRulesDef.MinWorkers, in GameData.ContentHash when it is not 1, after a marker: data with the floor at 1, the old
    // rule, hashes as before). The golden's one seat is a Malazan human seat, which no AI rule touches, so its k lines are
    // byte for byte as before (header data-hash and checksum only; checksum C7D815619BB83C73 -> C304BF4537406081). Item B's
    // commit wrote F498968DB96D5F07 (the field hashed unconditionally); item F made the term conditional and wrote the header
    // once more: one shipped value, one move from B7947DC602B1E11B.

    /// <summary>
    /// Ticks the replay must still match its recorded checkpoints: all of them (M4-H1, BUG-0211; before the re-recording
    /// only the first 19, the setup before the M4-2a fix changed the match, and from M4-3a none; M4-V5's re-recording,
    /// BUG-0273, held 12,131; M5-4f's, 7,682; M6-S10's, 7,653: the Keep-corner cure and gatherOverlap 0.5 moved the game from
    /// checkpoint 63 on, so it was re-recorded from M3PlayableTest -- --seed 21 --break 19 on the M6-S10 tree; M6-SH2's, 7,754:
    /// a construction site stopped taking loads (BUG-1520) and the recorded game diverged from it, so it was re-recorded the same
    /// way on the M6-SH2 tree, headless; its Research commands moved to ticks 4,363 and 5,571, Seed21ResearchTests).
    /// </summary>
    private const int CheckpointPrefixTicks = 7754;

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
