using System.Buffers.Binary;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;
using Rts.Sim.Tests.ViewApi;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>
/// QA for M6-V16 (the view's team readers in <c>ViewApi</c>): <see cref="EnemyTable"/> through <see cref="MinimapRaster"/>'s
/// two enemy-dot picks and <see cref="UnitPicker.ResolveEnemy(System.ReadOnlySpan{bool}, System.ReadOnlySpan{int}, System.ReadOnlySpan{int}, int, float, BuildingStore, int, float, float, int, System.ReadOnlySpan{bool}, out EntityHandle, out bool)"/>
/// at <c>TeamScaleStressTests</c>' scale (2,500 units, 2v2 interleaved, attack-moving through each other), and
/// <see cref="SaveSummary"/>'s team bytes exhaustively.
/// <list type="bullet">
/// <item>Oracle: the alliance pick on the real owners equals the old free-for-all pick on owners rewritten so every non-enemy
/// owns the viewer's colour slot (the same dots at the same positions); it never returns an own or an ally's unit.</item>
/// <item>Read-only: a run that calls every team reader every tick hashes as an untouched twin at every tick.</item>
/// <item>Zero allocation at 2,500 units (the sweep the minimap's right-click and the 3D pick do).</item>
/// </list>
/// </summary>
[Collection(SerialCollection.Name)]
public class TeamReadersQaTests
{
    private readonly ITestOutputHelper _out;

    public TeamReadersQaTests(ITestOutputHelper output) => _out = output;

    private const int PerSide = 1250, Size = 192;
    private static readonly uint[] Rgb = { 0x3366CC, 0xE08020, 0x2EA35A, 0xB82633 };

    // TeamScaleStressTests' brawl: side A = players 0 and 2, side B = 1 and 3, interleaved, attack-moving through each other.
    private static Simulation Brawl(ulong seed)
    {
        var config = TestSim.Config(seed, 4, 2 * PerSide + 16, 8192) with { Seats = TeamTests.Seats(1, 2, 1, 2) };
        var sim = new Simulation(config, LocalMovementTests.Flat(Size));
        var rng = new Random((int)seed);
        int[] types = { HeavyInfantry, Crossbowman, HeavyInfantry, Laborer };
        float meters = Size * Map.MapConstants.CellSize;
        for (int k = 0; k < PerSide; k++)
        {
            for (int side = 0; side < 2; side++)
            {
                int owner = side + 2 * (k % 2);
                float x = side == 0 ? 40f + (k / 25) * 1.6f : meters - 40f - (k / 25) * 1.6f;
                float y = 60f + (k % 25) * 1.6f + (float)rng.NextDouble() * 0.2f;
                EntityHandle h = Place(sim, owner, types[k % types.Length], new Vector2(x, y));
                sim.Enqueue(Command.AttackMove(owner, h, new Vector2(side == 0 ? meters - 20f : 20f, y)));
            }
        }
        return sim;
    }

    [Theory]
    [InlineData(1UL, 0)]
    [InlineData(2UL, 1)]
    [InlineData(3UL, 2)]
    public void At2500Units_TheAlliancePicks_EqualTheFreeForAllPickOnRewrittenOwners_AndNeverPickAnAlly(ulong seed, int viewer)
    {
        Simulation sim = Brawl(seed);
        World w = sim.World;
        UnitStore u = w.Units;
        bool[] enemies = UnitPickerTests.EnemiesOf(w, viewer);
        var real = new MinimapRaster(w.Heightmap, w.NavGrid, Rgb, u.Capacity);
        var flat = new MinimapRaster(w.Heightmap, w.NavGrid, Rgb, u.Capacity);
        var rewritten = new int[u.Capacity];
        var probe = new Random((int)seed + 7);
        float meters = Size * Map.MapConstants.CellSize;
        int picks = 0, enemyPicks = 0;
        for (int t = 0; t <= 300; t++)
        {
            if (t > 0) sim.Tick();
            if (t % 50 != 0) continue;
            ReadOnlySpan<int> owners = u.Owner;
            for (int i = 0; i < u.Capacity; i++) rewritten[i] = enemies[(uint)owners[i] < 4u ? owners[i] : viewer] ? owners[i] : viewer;
            int drawn = real.DrawDots(u.Alive, u.Position, u.Owner);
            Assert.Equal(drawn, flat.DrawDots(u.Alive, u.Position, rewritten));
            // Dense sweep over the fighting band, plus random points anywhere.
            for (int k = 0; k < 4000; k++)
            {
                Vector2 p = k < 3000
                    ? new Vector2(30f + (k % 100) * (meters - 60f) / 100f, 50f + (k / 100) * 1.6f)
                    : new Vector2((float)probe.NextDouble() * meters, (float)probe.NextDouble() * meters);
                int a = real.DrawnEnemyDotAt(p, viewer, enemies);
                int b = flat.DrawnEnemyDotAt(p, viewer);
                Assert.True(a == b, $"tick {t} point {p}: alliance pick dot {a}, rewritten free-for-all pick dot {b}");
                if (a >= 0)
                {
                    int slot = real.DrawnSlot(a);
                    Assert.True(w.IsEnemy(viewer, u.Owner[slot]), $"tick {t} point {p}: picked slot {slot} of player {u.Owner[slot]}, no enemy of {viewer}");
                    enemyPicks++;
                }
                int la = real.EnemyDotAt(p, u.Alive, u.Position, u.Owner, viewer, enemies);
                int lb = real.EnemyDotAt(p, u.Alive, u.Position, rewritten, viewer);
                Assert.True(la == lb, $"tick {t} point {p}: live alliance pick {la}, rewritten {lb}");
                if (la >= 0) Assert.True(w.IsEnemy(viewer, u.Owner[la]));
                picks++;
            }
            // The 3D pick on every live unit: a target exactly when its owner is an enemy.
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                bool hit = UnitPicker.ResolveEnemy(u.Alive, u.Owner, u.Generation, i, 1f, w.Buildings, -1, float.PositiveInfinity, float.PositiveInfinity,
                    viewer, enemies, out EntityHandle target, out bool isB);
                Assert.Equal(w.IsEnemy(viewer, u.Owner[i]), hit);
                if (hit) Assert.True(target.Index == i && !isB);
            }
        }
        Assert.True(enemyPicks > 100, $"only {enemyPicks} enemy picks of {picks}: the sweep missed the armies");
        _out.WriteLine($"seed {seed} viewer {viewer}: {picks} picks, {enemyPicks} on an enemy dot, oracle equal");
    }

    [Fact]
    public void EveryTeamReader_CalledEveryTick_ChangesNoStateHash_At2500Units()
    {
        Simulation a = Brawl(4), b = Brawl(4);
        World w = a.World;
        UnitStore u = w.Units;
        var raster = new MinimapRaster(w.Heightmap, w.NavGrid, Rgb, u.Capacity);
        Assert.Equal(a.StateHash(), b.StateHash());
        for (int t = 0; t < 200; t++)
        {
            a.Tick();
            b.Tick();
            bool[] enemies = UnitPickerTests.EnemiesOf(w, t % 4);
            raster.DrawDots(u.Alive, u.Position, u.Owner);
            for (int k = 0; k < 32; k++)
            {
                var p = new Vector2(40f + k * 9f, 60f + k * 1.2f);
                raster.DrawnEnemyDotAt(p, t % 4, enemies);
                raster.EnemyDotAt(p, u.Alive, u.Position, u.Owner, t % 4, enemies);
                UnitPicker.ResolveEnemy(u.Alive, u.Owner, u.Generation, k * 71, 1f, w.Buildings, -1, float.PositiveInfinity, float.PositiveInfinity, t % 4, enemies, out _, out _);
                EnemyTable.IsEnemyOwner(k % 6 - 1, t % 4, enemies);
            }
            if (t % 50 == 0) Assert.True(SaveSummary.TryParse(SaveTestRun.Save(a), out _));
            Assert.True(a.StateHash() == b.StateHash(), $"tick {a.TickNumber}: the readers changed the state hash");
        }
    }

    [Fact]
    public void ThePickSweep_At2500Units_2v2_AllocatesZeroBytes()
    {
        Simulation sim = Brawl(5);
        for (int t = 0; t < 60; t++) sim.Tick();
        World w = sim.World;
        UnitStore u = w.Units;
        bool[] enemies = UnitPickerTests.EnemiesOf(w, 0);
        var raster = new MinimapRaster(w.Heightmap, w.NavGrid, Rgb, u.Capacity);
        int sum = 0;
        Action block = () =>
        {
            raster.DrawDots(u.Alive, u.Position, u.Owner);
            for (int k = 0; k < 256; k++)
            {
                var p = new Vector2(30f + k * 1.3f, 55f + (k % 32) * 1.4f);
                sum += raster.DrawnEnemyDotAt(p, 0, enemies);
                sum += raster.EnemyDotAt(p, u.Alive, u.Position, u.Owner, 0, enemies);
                if (UnitPicker.ResolveEnemy(u.Alive, u.Owner, u.Generation, k * 9, 1f, w.Buildings, -1, float.PositiveInfinity, float.PositiveInfinity, 0, enemies, out _, out _)) sum++;
            }
        };
        block();
        int runs = AllocationProbe.AssertZero(block, _out);
        _out.WriteLine($"2,500-unit pick sweep with the alliance table: 0 bytes (runs {runs}), checksum {sum}");
    }

    // ---- SaveSummary: every team byte value, and every cut through the team bytes ----

    [Fact]
    public void EveryTeamByte_0To8_Reads_9To255_IsRefused_OnEverySeat()
    {
        byte[] bytes = SaveTestRun.Save(AiSaveScenes.TwoVersusTwo(1).Build());
        int at = SaveSummaryTests.TeamsAt(bytes);
        for (int seat = 0; seat < 4; seat++)
        {
            for (int v = 0; v <= 255; v++)
            {
                byte[] b = (byte[])bytes.Clone();
                b[at + seat] = (byte)v;
                bool ok = SaveSummary.TryParse(b, out SaveSummary? s);
                Assert.True(ok == (v <= PlayerSeat.MaxTeam), $"seat {seat} team byte {v}: read {ok}");
                if (ok)
                {
                    Assert.Equal(v, s!.Teams[seat]);
                    Assert.Equal(v, s.Seats[seat].Team);
                    Assert.Equal(4, s.Teams.Length);
                }
            }
        }
        // The sim's loader agrees on the refusals (the list never offers a Load the sim would refuse for a team).
        foreach (byte v in new byte[] { 9, 255 })
        {
            byte[] b = (byte[])bytes.Clone();
            b[at + 2] = v;
            Assert.NotEqual(SaveError.None, SaveTestRun.TryLoad(TestSim.Data, b));
        }
    }

    [Fact]
    public void EveryCut_ThroughTheTeamBytes_IsNoHeader_AndAFormat2CutAtTheTickReads()
    {
        byte[] bytes = SaveTestRun.Save(AiSaveScenes.TwoVersusTwo(1).Build());
        int at = SaveSummaryTests.TeamsAt(bytes);
        for (int cut = at - 4; cut < at + 4; cut++) Assert.False(SaveSummary.TryParse(bytes.AsSpan(0, cut), out _), $"cut at {cut} (teams at {at}) read");
        Assert.True(SaveSummary.TryParse(bytes.AsSpan(0, at + 4), out _));
        // The same header as format 2 wrote it, then cut right after the tick: still a whole format-2 header.
        byte[] two = bytes.Take(at).Concat(bytes.Skip(at + 4)).ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(two.AsSpan(SaveFormat.MagicLength), 2);
        Assert.True(SaveSummary.TryParse(two.AsSpan(0, at), out SaveSummary? s));
        Assert.True(s!.Teams.Length == 4 && s.Teams.All(t => t == 0));
        // A format-2 save is listed but refused on Load with the version reason (rule 36: only the current format loads).
        Assert.Equal(SaveError.FormatVersionMismatch, SaveTestRun.TryLoad(TestSim.Data, two));
    }
}
