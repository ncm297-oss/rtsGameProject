using System.Numerics;
using System.Reflection;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Vision;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA for M4-V6c (session 2026-10-10-1015): try to make a zone disc leak information. An independent oracle (every cell
/// of the map, float distance from its centre, the player's visibility byte) against <see cref="ZoneDiscs.Collect"/>
/// every tick of seeded fuzz runs on a flat map and on a two-level map (storms on the plateau, on the low ground, across
/// the cliff, overlapping, own and enemy, blocking and not), with the alpha checked by its own formula; the rim ring's
/// exact boundary; a storm on a plateau above the player's units; a zone freed between fog updates; an own unit standing
/// inside an enemy storm always draws it; and a collecting sim stays hash-equal to a bare twin.
/// </summary>
[Collection(SerialCollection.Name)]
public class ZoneDiscsQaTests
{
    private readonly ITestOutputHelper _out;

    public ZoneDiscsQaTests(ITestOutputHelper output) => _out = output;

    private static AbilityDef Sandstorm => TestSim.Data.Abilities[TestSim.Data.FindAbility("sandstorm")];

    private static int Lancer => TestSim.Data.FindUnit("malazan_wickan_lancer");

    /// <summary>A Sandstorm copy whose zone does not block vision (no shipped zone ability is non-blocking).</summary>
    private static AbilityDef Dust()
    {
        AbilityDef s = Sandstorm;
        var copy = (AbilityDef)typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(s, null)!;
        typeof(AbilityDef).GetProperty(nameof(AbilityDef.Effects))!.SetValue(copy, s.Effects.SetItem(s.ZoneEffect, s.Effects[s.ZoneEffect] with { BlocksVision = false }));
        return copy;
    }

    private static Simulation Make(Heightmap map, ulong seed) =>
        new(TestSim.ConfigNoCombat(Seed: seed, PlayerCount: 2, UnitCapacity: 64, CommandCapacity: 512), map);

    private static void NextUpdate(Simulation sim)
    {
        do sim.Tick();
        while (!VisionSystem.IsUpdateTick(sim.TickNumber - 1));
    }

    /// <summary>The oracle's alpha: 0.35 while more than 20 ticks are left, then 0.35 x (left - 1) / 20.</summary>
    private static float OracleAlpha(int left) => left > 20 ? 0.35f : Math.Max(0, left - 1) * 0.35f / 20f;

    /// <summary>The oracle: whether <paramref name="player"/> may see zone <paramref name="k"/> (own; fog off; or a visible cell centre within radius + 2 m, every cell of the map checked).</summary>
    private static bool OracleSees(World w, int player, bool fogOn, int k)
    {
        ZoneStore z = w.Zones;
        if (z.Owner[k] == player || !fogOn) return true;
        ReadOnlySpan<byte> vis = w.Fog.Visibility(player);
        float reach = z.Radius(k) + 2f;
        for (int y = 0; y < w.Fog.Height; y++)
            for (int x = 0; x < w.Fog.Width; x++)
            {
                var c = new Vector2((x + 0.5f) * MapConstants.CellSize, (y + 0.5f) * MapConstants.CellSize);
                if (Vector2.Distance(c, z.Center[k]) <= reach + 1e-4f && vis[y * w.Fog.Width + x] == VisionConstants.Visible) return true;
            }
        return false;
    }

    private static int CheckAgainstOracle(Simulation sim, int player, bool fogOn, ZoneDisc[] discs, string ctx)
    {
        World w = sim.World;
        ZoneStore z = w.Zones;
        int n = ZoneDiscs.Collect(z, w.Fog, player, fogOn, discs);
        int at = 0;
        for (int k = 0; k < z.Capacity; k++)
        {
            if (!z.Alive[k] || !OracleSees(w, player, fogOn, k)) continue;
            Assert.True(at < n, $"{ctx}: zone {k} (owner {z.Owner[k]}, {z.Center[k]}) should be drawn for player {player}, fog {fogOn}; {n} drawn");
            ZoneDisc d = discs[at++];
            Assert.True(d.Slot == k, $"{ctx}: disc {at - 1} is slot {d.Slot}, want {k}");
            Assert.Equal(z.Center[k], d.Center);
            Assert.Equal(z.Radius(k), d.Radius);
            Assert.Equal(z.Owner[k] == player, d.Own);
            Assert.True(MathF.Abs(OracleAlpha(z.TicksRemaining[k]) - d.Alpha) < 1e-6f, $"{ctx}: slot {k} alpha {d.Alpha} with {z.TicksRemaining[k]} left, want {OracleAlpha(z.TicksRemaining[k])}");
            Assert.InRange(d.Alpha, 0f, 0.35f);
        }
        Assert.True(n == at, $"{ctx}: player {player} fog {fogOn} drew {n} discs, the oracle {at} (a leak if more)");
        return n;
    }

    /// <summary>
    /// Seeded fuzz on a flat 48 x 48 map and the two-level 40 x 40 map: both players' Crossbowmen, Lancers and Raiders
    /// walking about at random, a Sandstorm or a non-blocking copy cast at a random point (edges and off-map included) by
    /// a random owner every 17 ticks; every tick both players' discs, fog on and off, equal the oracle's; the collecting
    /// sim stays hash-equal to a bare twin.
    /// </summary>
    [Theory]
    [InlineData(1UL, false)]
    [InlineData(2UL, false)]
    [InlineData(3UL, false)]
    [InlineData(4UL, true)]
    [InlineData(5UL, true)]
    [InlineData(6UL, true)]
    public void Fuzz_EveryTick_DiscsEqualTheOracle_AndTheHashEqualsABareTwin(ulong seed, bool twoLevel)
    {
        Heightmap Map() => twoLevel ? FogMaps.TwoLevel() : LocalMovementTests.Flat(48);
        Simulation a = Make(Map(), seed), b = Make(Map(), seed);
        AbilityDef storm = Sandstorm, dust = Dust();
        var rng = new Random((int)seed * 7919); // the test's own randomness, applied identically to both twins
        int size = a.World.Fog.Width;
        int[] types = { Crossbowman, Lancer, Raider };
        var units = new List<(EntityHandle A, EntityHandle B, int Owner)>();
        for (int i = 0; i < 12; i++)
        {
            int owner = i & 1, type = types[rng.Next(types.Length)];
            Vector2 p = At(a, 2 + rng.Next(size - 4), 2 + rng.Next(size - 4));
            units.Add((Place(a, owner, type, p), Place(b, owner, type, p), owner));
        }
        var discs = new ZoneDisc[a.World.Zones.Capacity];
        int maxLive = 0, enemyDrawn = 0, enemyHidden = 0;
        for (int t = 0; t < 900; t++)
        {
            if (t % 17 == 3)
            {
                int owner = rng.Next(2);
                var p = new Vector2(-4f + (float)rng.NextDouble() * (size * 2f + 8f), -4f + (float)rng.NextDouble() * (size * 2f + 8f));
                AbilityDef def = rng.Next(3) == 0 ? dust : storm;
                ZoneSystem.Create(a.World, owner, def, p);
                ZoneSystem.Create(b.World, owner, def, p);
            }
            if (t % 23 == 0)
            {
                var (ha, hb, owner) = units[rng.Next(units.Count)];
                Vector2 to = At(a, 2 + rng.Next(size - 4), 2 + rng.Next(size - 4));
                a.Enqueue(Command.Move(owner, ha, to));
                b.Enqueue(Command.Move(owner, hb, to));
            }
            ulong before = a.StateHash();
            for (int p = 0; p < 2; p++)
            {
                CheckAgainstOracle(a, p, true, discs, $"seed {seed} tick {a.TickNumber}");
                CheckAgainstOracle(a, p, false, discs, $"seed {seed} tick {a.TickNumber}");
            }
            int n0 = ZoneDiscs.Collect(a.World.Zones, a.World.Fog, 0, true, discs);
            for (int k = 0; k < n0; k++) if (!discs[k].Own) enemyDrawn++;
            for (int k = 0; k < a.World.Zones.Capacity; k++) if (a.World.Zones.Alive[k] && a.World.Zones.Owner[k] == 1) enemyHidden++;
            maxLive = Math.Max(maxLive, a.World.Zones.Count);
            Assert.Equal(before, a.StateHash());
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed} tick {a.TickNumber}: diverged from the bare twin");
        }
        enemyHidden -= enemyDrawn;
        Assert.True(enemyDrawn > 0 && enemyHidden > 0, $"seed {seed}: the run never exercised both sides of the rule ({enemyDrawn} enemy discs drawn, {enemyHidden} hidden)");
        _out.WriteLine($"seed {seed} {(twoLevel ? "two-level" : "flat")}: up to {maxLive} live zones; player 0 drew {enemyDrawn} enemy disc-ticks and hid {enemyHidden}; oracle-equal every tick, hash-equal to the twin");
    }

    /// <summary>The rim ring's outer edge by cell centre: a visible cell at exactly radius + 2 m counts; the visible ring one cell further out (every cell in (8, 10] m) does not.</summary>
    [Fact]
    public void RimRing_ExactBoundary_AndAFullRingOneCellOutsideIt_DrawsNothing()
    {
        const int w = 30, h = 30;
        var vis = new byte[w * h];
        var centre = new Vector2(31f, 31f); // cell (15, 15)'s centre
        int outside = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float d = Vector2.Distance(new Vector2(x * 2f + 1f, y * 2f + 1f), centre);
                if (d > 8f && d <= 10f) { vis[y * w + x] = VisionConstants.Visible; outside++; }
                else if (d <= 8f) vis[y * w + x] = VisionConstants.Explored;
            }
        Assert.True(outside > 20);
        Assert.False(ZoneDiscs.SeesStorm(vis, w, h, centre, 6f));
        vis[15 * w + 19] = VisionConstants.Visible; // exactly 8 m east
        Assert.True(ZoneDiscs.SeesStorm(vis, w, h, centre, 6f));
        vis[15 * w + 19] = VisionConstants.Explored;
        vis[16 * w + 19] = VisionConstants.Visible; // sqrt(68) = 8.25 m
        Assert.False(ZoneDiscs.SeesStorm(vis, w, h, centre, 6f));
    }

    /// <summary>
    /// A real Lancer walking toward an enemy storm on flat ground, one cell at a time: no disc while the farthest cell its
    /// sight covers lies even one cell short of the rim ring; a disc from the first update that shows a rim cell.
    /// </summary>
    [Fact]
    public void AnOwnUnitOneCellShortOfTheRimRing_NoDisc_OneCellCloser_Disc()
    {
        Simulation sim = Make(LocalMovementTests.Flat(48), 1);
        var discs = new ZoneDisc[64];
        Vector2 centre = At(sim, 34, 24);
        ZoneSystem.Create(sim.World, 1, Sandstorm, centre);
        float sight = TestSim.Data.Units[Lancer].Sight;
        EntityHandle scout = Place(sim, 0, Lancer, At(sim, 4, 24));
        sim.World.Units.Hold[scout.Index] = true;
        int firstDrawnX = -1;
        for (int x = 4; x <= 30 && firstDrawnX < 0; x++)
        {
            sim.World.Units.Position[scout.Index] = At(sim, x, 24);
            NextUpdate(sim);
            // The nearest visible cell to the storm's centre along any direction decides.
            float nearest = float.MaxValue;
            ReadOnlySpan<byte> vis = sim.World.Fog.Visibility(0);
            for (int c = 0; c < vis.Length; c++)
                if (vis[c] == VisionConstants.Visible)
                    nearest = Math.Min(nearest, Vector2.Distance(At(sim, c % 48, c / 48), centre));
            int n = ZoneDiscs.Collect(sim.World.Zones, sim.World.Fog, 0, true, discs);
            Assert.True(n == (nearest <= 8f + 1e-4f ? 1 : 0), $"scout at cell {x}: nearest visible cell {nearest:0.00} m from the centre, {n} discs");
            if (n == 1) firstDrawnX = x;
            else if (nearest <= 10f + 1e-4f) _out.WriteLine($"scout at cell {x}: nearest visible cell {nearest:0.00} m (one cell outside the ring): no disc");
        }
        Assert.True(firstDrawnX > 0, "the scout never saw the rim");
        _out.WriteLine($"Lancer (sight {sight} m) first draws the storm from cell {firstDrawnX} (centre cell 34)");
    }

    /// <summary>
    /// docs/02 high ground: low ground cannot see up past the 4 m lip. An enemy storm on the plateau (rim wholly on the
    /// plateau) is not drawn for Crossbowmen below the cliff even when its rim is well within their 18 m sight; the owner
    /// and --no-fog draw it. A storm whose rim ring crosses the cliff onto visible low ground is drawn (the rule's rim).
    /// </summary>
    [Fact]
    public void AStormOnThePlateau_AboveThePlayersUnits_IsNotDrawn_ARimAcrossTheCliffIs()
    {
        Simulation sim = Make(FogMaps.TwoLevel(), 1);
        var discs = new ZoneDisc[64];
        // Crossbowmen at the cliff's foot (x 18, level 0), rows 6..34.
        for (int y = 6; y <= 34; y += 4) Place(sim, 0, Crossbowman, FogMaps.Cell(18, y));
        Vector2 high = FogMaps.Cell(28, 10); // level 1; rim ring reaches x 24..32: all plateau
        ZoneSystem.Create(sim.World, 1, Sandstorm, high);
        NextUpdate(sim);
        ReadOnlySpan<byte> vis = sim.World.Fog.Visibility(0);
        Assert.Equal(VisionConstants.Visible, vis[10 * 40 + 17]);
        Assert.NotEqual(VisionConstants.Visible, vis[10 * 40 + 24]); // 12 m away, in sight, but up the cliff past the lip
        Assert.Equal(0, ZoneDiscs.Collect(sim.World.Zones, sim.World.Fog, 0, true, discs));
        Assert.Equal(1, ZoneDiscs.Collect(sim.World.Zones, sim.World.Fog, 1, true, discs));
        Assert.Equal(1, ZoneDiscs.Collect(sim.World.Zones, sim.World.Fog, 0, false, discs));
        // A storm on the plateau's edge: its rim ring (8 m) reaches down to x 19, the low ground the Crossbowmen see.
        Vector2 edge = FogMaps.Cell(23, 26);
        ZoneSystem.Create(sim.World, 1, Sandstorm, edge);
        NextUpdate(sim);
        int n = ZoneDiscs.Collect(sim.World.Zones, sim.World.Fog, 0, true, discs);
        Assert.Equal(1, n);
        Assert.Equal(edge, discs[0].Center);
        _out.WriteLine("plateau storm (rim all on level 1) hidden from the low ground; a storm whose rim crosses onto visible low ground drawn");
    }

    /// <summary>
    /// A seen enemy storm and an own storm, each expiring on a tick that is not a fog update: each disc is gone the very
    /// tick its slot frees, not at the next update; and every tick an own unit standing inside the enemy storm right after
    /// an update has the enemy disc drawn.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AZoneFreedBetweenFogUpdates_ItsDiscGoesTheSameTick(int phase)
    {
        Simulation sim = Make(LocalMovementTests.Flat(48), 1);
        var discs = new ZoneDisc[64];
        EntityHandle inside = Place(sim, 0, Raider, At(sim, 25, 25)); // stands inside the enemy storm below (2.8 m from its centre)
        sim.World.Units.Hold[inside.Index] = true;
        NextUpdate(sim);
        for (int t = 0; t < phase; t++) sim.Tick();
        ZoneSystem.Create(sim.World, 1, Sandstorm, At(sim, 24, 24));
        ZoneSystem.Create(sim.World, 0, Sandstorm, At(sim, 28, 26)); // an own storm overlapping it
        int ticks = 0, freedOffUpdate = 0, insideChecked = 0;
        while (sim.World.Zones.Count > 0)
        {
            sim.Tick();
            ticks++;
            int n0 = ZoneDiscs.Collect(sim.World.Zones, sim.World.Fog, 0, true, discs);
            int live = 0;
            for (int k = 0; k < 64; k++) if (sim.World.Zones.Alive[k]) live++;
            if (live < 2 && !VisionSystem.IsUpdateTick(sim.TickNumber - 1)) freedOffUpdate++;
            Assert.True(n0 <= live, $"tick {ticks}: {n0} discs for {live} live zones");
            for (int k = 0; k < n0; k++) Assert.True(sim.World.Zones.Alive[discs[k].Slot], $"tick {ticks}: disc for freed slot {discs[k].Slot}");
            if (sim.World.Zones.Alive[0] && VisionSystem.IsUpdateTick(sim.TickNumber - 1) && sim.World.Zones.Contains(0, sim.World.Units.Position[inside.Index]))
            {
                insideChecked++;
                bool drawn = false;
                for (int k = 0; k < n0; k++) if (discs[k].Slot == 0) drawn = true;
                Assert.True(drawn, $"tick {ticks}: an own unit stands inside the enemy storm, its disc not drawn");
            }
            Assert.True(ticks < 400);
        }
        Assert.Equal(0, ZoneDiscs.Collect(sim.World.Zones, sim.World.Fog, 0, true, discs));
        Assert.True(insideChecked > 50, $"inside checked on {insideChecked} updates");
        _out.WriteLine($"phase {phase}: both storms over after {ticks} ticks; {freedOffUpdate} ticks with a freed zone before the next update; inside rows {insideChecked}");
    }

    /// <summary>A player index with no fog grid (a spectator or out of range) draws no enemy zone with the fog on and never throws; an empty output draws nothing.</summary>
    [Fact]
    public void NoSuchPlayer_OrAnEmptyOutput_DrawsNothing()
    {
        Simulation sim = Make(LocalMovementTests.Flat(48), 1);
        ZoneSystem.Create(sim.World, 1, Sandstorm, At(sim, 20, 20));
        ZoneSystem.Create(sim.World, 0, Sandstorm, At(sim, 30, 20));
        var discs = new ZoneDisc[64];
        Assert.Equal(0, ZoneDiscs.Collect(sim.World.Zones, sim.World.Fog, -1, true, discs));
        Assert.Equal(0, ZoneDiscs.Collect(sim.World.Zones, sim.World.Fog, 7, true, discs));
        Assert.Equal(0, ZoneDiscs.Collect(sim.World.Zones, sim.World.Fog, 0, true, Span<ZoneDisc>.Empty));
        Assert.Equal(2, ZoneDiscs.Collect(sim.World.Zones, sim.World.Fog, 7, false, discs));
    }
}
