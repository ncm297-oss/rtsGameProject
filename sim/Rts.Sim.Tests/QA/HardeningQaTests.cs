using System.Numerics;
using System.Reflection;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA attacks on M1-9, the M1 hardening batch (session 2026-10-06-0905): holders of both players as
/// hard walls in corridors and open-field shove storms (BUG-0055), clusters of own holders as plugs,
/// and the plug-answer cache (BUG-0044) against a fresh search for every query.
/// </summary>
public class HardeningQaTests
{
    private readonly ITestOutputHelper _out;

    public HardeningQaTests(ITestOutputHelper output) => _out = output;

    private static EntityHandle H(Simulation sim, int slot) => MoveScenario.Handle(sim, slot);

    private static void ApplyPending(Simulation sim)
    {
        while (sim.PendingCommandCount > 0) sim.Tick();
    }

    private static bool SameBits(Vector2 a, Vector2 b) =>
        BitConverter.SingleToInt32Bits(a.X) == BitConverter.SingleToInt32Bits(b.X)
        && BitConverter.SingleToInt32Bits(a.Y) == BitConverter.SingleToInt32Bits(b.Y);

    /// <summary>Every live unit at a finite position on passable ground.</summary>
    private static void AssertOnGround(Simulation sim)
    {
        UnitStore u = sim.World.Units;
        NavGrid g = sim.World.NavGrid;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            Vector2 p = u.Position[i];
            Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y), $"tick {sim.TickNumber}: unit {i} at {p}");
            Assert.True(g.WorldToCell(p, out int x, out int y) && g.IsPassable(x, y), $"tick {sim.TickNumber}: unit {i} on blocked ground or off the map at {p}");
        }
    }

    /// <summary>A 1-cell corridor along row 4 from cell x 10 to 10 + <paramref name="length"/> - 1, with 10x9 rooms at both ends.</summary>
    private static Heightmap RoomCorridorRoom(int length)
    {
        var rows = new string[9];
        for (int y = 0; y < 9; y++)
            rows[y] = new string('0', 10) + new string(y == 4 ? '0' : '1', length) + new string('0', 10);
        return LocalMovementTests.Rows(rows);
    }

    // ------------------------------------------------------------ holders of both players, 1-cell corridor

    /// <summary>
    /// BUG-0055 / QA focus: wide holders in a 1-cell corridor (player 0's at cell 15, player 1's at
    /// cell 24, or only one of them); 12 walkers of each player in each room are sent to the far room,
    /// then sent again at tick 600 and 1,200 (spam). Two sims, hash-equal every tick. No holder ever moves
    /// (bit-equal); nobody of either side gets past a holder from the room it started in; everyone
    /// ends Idle; everyone stays on passable ground.
    /// </summary>
    [Theory]
    [InlineData(true, true, 1UL)]
    [InlineData(true, false, 2UL)]
    [InlineData(false, true, 3UL)]
    [InlineData(true, true, 4UL)]
    public void HoldersOfBothPlayers_In1CellCorridor_NobodyOfEitherSidePasses(bool p0Holds, bool p1Holds, ulong seed)
    {
        const int perGroup = 12;
        int wide = LocalMovementTests.TypeWithRadius(0.9f);
        Simulation Make()
        {
            var sim = new Simulation(TestSim.ConfigNoCombat(Seed: seed, PlayerCount: 2, UnitCapacity: 2 + 4 * perGroup, CommandCapacity: 8 * perGroup + 16), RoomCorridorRoom(20));
            NavGrid g = sim.World.NavGrid;
            sim.Enqueue(Command.SpawnUnit(0, wide, g.CellCenter(15, 4))); // slot 0
            sim.Enqueue(Command.SpawnUnit(1, wide, g.CellCenter(24, 4))); // slot 1 (player 1 applies after player 0's spawns: re-checked below)
            var rng = new SimRng(seed, 955);
            for (int k = 0; k < perGroup; k++)
                for (int side = 0; side < 2; side++)
                    for (int p = 0; p < 2; p++)
                    {
                        int x = side == 0 ? 1 + k % 8 : 31 + k % 8;
                        int y = 1 + k / 8 * 3 + p;
                        sim.Enqueue(Command.SpawnUnit(p, rng.NextInt(0, TestSim.UnitTypeCount), g.CellCenter(x, y) + new Vector2(rng.NextFloat() - 0.5f, rng.NextFloat() - 0.5f)));
                    }
            ApplyPending(sim);
            return sim;
        }
        Simulation a = Make(), b = Make();
        UnitStore u = a.World.Units;
        NavGrid grid = a.World.NavGrid;
        int h0 = -1, h1 = -1;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Radius[i] == 0.9f && grid.WorldToCell(u.Position[i], out int cx, out int cy) && cy == 4 && cx is 15 or 24)
            {
                if (cx == 15) h0 = i;
                else h1 = i;
            }
        Assert.True(h0 >= 0 && h1 >= 0, "precondition: both corridor units spawned");
        foreach (Simulation s in new[] { a, b })
        {
            s.Enqueue(p0Holds ? Command.HoldPosition(0, H(s, h0)) : Command.Stop(0, H(s, h0)));
            s.Enqueue(p1Holds ? Command.HoldPosition(1, H(s, h1)) : Command.Stop(1, H(s, h1)));
        }
        Vector2 at0 = u.Position[h0], at1 = u.Position[h1];
        var westStart = new bool[u.Capacity];
        for (int i = 0; i < u.Capacity; i++) westStart[i] = u.Position[i].X < at0.X;

        void Send(Simulation s)
        {
            UnitStore su = s.World.Units;
            for (int i = 0; i < su.Capacity; i++)
            {
                if (!su.Alive[i] || i == h0 || i == h1) continue;
                Vector2 to = westStart[i] ? s.World.NavGrid.CellCenter(35, 4) : s.World.NavGrid.CellCenter(4, 4);
                s.Enqueue(Command.Move(su.Owner[i], H(s, i), to));
            }
        }

        int lastMoving = 0, touches = 0;
        string? passed = null;
        for (int t = 0; t < 3000; t++)
        {
            if (t is 0 or 600 or 1200) { Send(a); Send(b); }
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {a.TickNumber}");
            AssertOnGround(a);
            if (p0Holds) Assert.True(SameBits(at0, u.Position[h0]), $"tick {t}: player 0's holder moved {at0} -> {u.Position[h0]}");
            if (p1Holds) Assert.True(SameBits(at1, u.Position[h1]), $"tick {t}: player 1's holder moved {at1} -> {u.Position[h1]}");
            int moving = 0;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (i == h0 || i == h1) continue;
                if (u.State[i] == UnitState.Moving) moving++;
                if (p0Holds && Vector2.Distance(u.Position[i], at0) < u.Radius[i] + 0.95f) touches++;
                // From the west, the first holder in the way is player 0's (if it holds), else player 1's.
                if (westStart[i] && (p0Holds ? u.Position[i].X > at0.X : u.Position[i].X > at1.X))
                    passed ??= $"tick {t}: unit {i} (player {u.Owner[i]}, r {u.Radius[i]}) from the west got to {u.Position[i]}";
                if (!westStart[i] && (p1Holds ? u.Position[i].X < at1.X : u.Position[i].X < at0.X))
                    passed ??= $"tick {t}: unit {i} (player {u.Owner[i]}, r {u.Radius[i]}) from the east got to {u.Position[i]}";
            }
            if (moving > 0) lastMoving = t;
            else if (t > 1300 && t > lastMoving + 50) break;
        }
        _out.WriteLine($"p0 holds {p0Holds}, p1 holds {p1Holds}, seed {seed}: all Idle after tick {lastMoving + 1}, walker-ticks touching player 0's holder {touches}; {passed ?? "nobody passed"}");
        Assert.True(passed == null, passed);
        Assert.True(lastMoving < 2900, $"walkers still Moving at tick {lastMoving}");
    }

    // ------------------------------------------------------------ holders of both players, open-field shove storm

    /// <summary>
    /// QA focus: open ground, 6 holders (3 per player, every radius) in a column, each with a ring of 6
    /// goal-less units (alternating owners: chain-shove fodder for both sides); 240 walkers of both
    /// players cross the column both ways, with a queued leg back, re-sent every 400 ticks. Two sims,
    /// hash-equal every tick. No holder ever moves; nobody is ever closer to a holder than the pack
    /// limit (ShoveSpacing x the radii's sum); everybody on passable ground.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void HoldersOfBothPlayers_OpenFieldShoveStorm_NeverMove_NeverPressedPastThePackLimit(ulong seed)
    {
        const int holders = 6, ring = 6, walkers = 240;
        float[] radii = { 0.4f, 0.7f, 0.9f };
        Simulation Make()
        {
            var sim = new Simulation(TestSim.ConfigNoCombat(Seed: seed, PlayerCount: 2, UnitCapacity: holders * (ring + 1) + walkers, CommandCapacity: 4 * walkers + 128), LocalMovementTests.Flat(64));
            NavGrid g = sim.World.NavGrid;
            for (int h = 0; h < holders; h++)
            {
                float r = radii[h % 3];
                Vector2 at = g.CellCenter(32, 12 + 8 * h);
                sim.Enqueue(Command.SpawnUnit(h % 2, LocalMovementTests.TypeWithRadius(r), at));
                for (int k = 0; k < ring; k++)
                {
                    float a = k * MathF.PI * 2f / ring;
                    float rr = radii[k % 3];
                    sim.Enqueue(Command.SpawnUnit(k % 2, LocalMovementTests.TypeWithRadius(rr), at + new Vector2(MathF.Cos(a), MathF.Sin(a)) * ((r + rr) * 1.03f)));
                }
            }
            var rng = new SimRng(seed, 31);
            for (int k = 0; k < walkers; k++)
            {
                bool west = k % 2 == 0;
                Vector2 at = g.CellCenter(west ? 4 + k / 2 % 16 : 44 + k / 2 % 16, 8 + k / 32 * 6 % 48) + new Vector2(rng.NextFloat() - 0.5f, rng.NextFloat() - 0.5f);
                sim.Enqueue(Command.SpawnUnit(k / 2 % 2, rng.NextInt(0, TestSim.UnitTypeCount), at));
            }
            ApplyPending(sim);
            Assert.Equal(holders * (ring + 1) + walkers, sim.World.Units.Count);
            return sim;
        }
        Simulation a = Make(), b = Make();
        UnitStore u = a.World.Units;
        NavGrid grid = a.World.NavGrid;
        var holderSlots = new List<int>();
        for (int i = 0; i < u.Capacity; i++)
            if (grid.WorldToCell(u.Position[i], out int cx, out int cy) && cx == 32 && (cy - 12) % 8 == 0 && Vector2.Distance(u.Position[i], grid.CellCenter(32, cy)) < 1e-4f)
                holderSlots.Add(i);
        Assert.Equal(holders, holderSlots.Count);
        foreach (Simulation s in new[] { a, b })
            foreach (int h in holderSlots) s.Enqueue(Command.HoldPosition(s.World.Units.Owner[h], H(s, h)));
        ApplyPending(a);
        ApplyPending(b);
        var at = holderSlots.Select(h => u.Position[h]).ToArray();
        var isHolder = new bool[u.Capacity];
        foreach (int h in holderSlots) isHolder[h] = true;

        void Send(Simulation s, int round)
        {
            UnitStore su = s.World.Units;
            NavGrid g = s.World.NavGrid;
            for (int i = 0; i < su.Capacity; i++)
            {
                if (!su.Alive[i] || isHolder[i] || su.Position[i].X > 50f && su.Position[i].X < 80f) continue; // the rings stay put unless shoved
                bool west = su.Position[i].X < 64f;
                int y = 8 + (i * 7 + round * 13) % 48;
                s.Enqueue(Command.Move(su.Owner[i], H(s, i), g.CellCenter(west ? 56 : 6, y)));
                s.Enqueue(Command.Move(su.Owner[i], H(s, i), g.CellCenter(west ? 6 : 56, 63 - y), queued: true));
            }
        }

        float worst = float.MaxValue;
        string? worstAt = null;
        int touched = 0;
        for (int t = 0; t < 1600; t++)
        {
            if (t % 400 == 0) { Send(a, t / 400); Send(b, t / 400); }
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {a.TickNumber}");
            AssertOnGround(a);
            for (int k = 0; k < holderSlots.Count; k++)
            {
                int h = holderSlots[k];
                Assert.True(u.Hold[h] && SameBits(at[k], u.Position[h]), $"tick {t}: holder {h} (player {u.Owner[h]}) moved {at[k]} -> {u.Position[h]} (hold {u.Hold[h]})");
                for (int i = 0; i < u.Capacity; i++)
                {
                    if (i == h || !u.Alive[i]) continue;
                    float ratio = Vector2.Distance(u.Position[i], at[k]) / (u.Radius[i] + u.Radius[h]);
                    if (ratio < 1.02f) touched++;
                    if (ratio < worst) { worst = ratio; worstAt = $"tick {t}: unit {i} (player {u.Owner[i]}, {u.State[i]}, r {u.Radius[i]}) at {u.Position[i]}, holder {h} (player {u.Owner[h]}, r {u.Radius[h]})"; }
                }
            }
        }
        _out.WriteLine($"seed {seed}: holders never moved; unit-ticks touching a holder {touched}; closest {worst:F3} x the radii's sum ({worstAt})");
        Assert.True(touched > 100, $"the storm barely touched the holders ({touched})");
        Assert.True(worst >= MovementConstants.ShoveSpacing - 1e-3f, $"pressed past the pack limit into a holder: {worst:F3} ({worstAt})");
    }

    // ------------------------------------------------------------ a cluster of own holders plugs a corridor

    /// <summary>A 32-cell-wide map: rows 0-2 and from 3 + width open rooms joined by a corridor of <paramref name="width"/> cells from cell x 6 to 25.</summary>
    private static Heightmap WideCorridorWithRooms(int width)
    {
        int h = 7 + width;
        var rows = new string[h];
        for (int y = 0; y < h; y++)
        {
            char[] r = new string('0', 32).ToCharArray();
            if (y < 3 || y >= 3 + width) for (int x = 6; x < 26; x++) r[x] = '1';
            rows[y] = new string(r);
        }
        return LocalMovementTests.Rows(rows);
    }

    /// <summary>
    /// BUG-0055 + BUG-0045 together: a line of player 0's holders (5 small across a 2-cell corridor, 7
    /// across a 3-cell one, or mixed radii) plugs it. Crowds of both players (mixed types) are sent
    /// through from the west every 100 ticks for 1,000 ticks. Two sims, hash-equal every tick. No holder
    /// moves; nobody of either player gets fully past the line; nobody is pressed past the pack limit
    /// into a holder.
    /// </summary>
    [Theory]
    [InlineData(2, 1UL, new[] { 0.4f, 0.4f, 0.4f, 0.4f, 0.4f })]
    [InlineData(2, 2UL, new[] { 0.4f, 0.4f, 0.4f, 0.4f, 0.4f })]
    [InlineData(3, 3UL, new[] { 0.4f, 0.4f, 0.4f, 0.4f, 0.4f, 0.4f, 0.4f })]
    [InlineData(3, 4UL, new[] { 0.9f, 0.4f, 0.7f, 0.4f, 0.4f })]
    public void LineOfOwnHolders_PlugsACorridor_ForBothPlayers(int width, ulong seed, float[] radii)
    {
        float span = 2f * width, used = radii.Sum(r => 2f * r);
        float gap = (span - used) / (radii.Length + 1);
        Assert.True(gap >= 0f && gap < 0.8f, $"precondition: gap {gap:F3}");
        int line = radii.Length, crowd = 8 * width;
        Simulation Make()
        {
            var sim = new Simulation(TestSim.ConfigNoCombat(Seed: seed, PlayerCount: 2, UnitCapacity: line + 2 * crowd, CommandCapacity: 8 * (line + 2 * crowd)), WideCorridorWithRooms(width));
            NavGrid g = sim.World.NavGrid;
            float y = 6f + gap;
            foreach (float r in radii)
            {
                sim.Enqueue(Command.SpawnUnit(0, LocalMovementTests.TypeWithRadius(r), new Vector2(33f, y + r)));
                y += 2f * r + gap;
            }
            var rng = new SimRng(seed, 4471);
            for (int k = 0; k < 2 * crowd; k++)
                sim.Enqueue(Command.SpawnUnit(k % 2, rng.NextInt(0, TestSim.UnitTypeCount), g.CellCenter(1 + k / 2 % 4, 1 + k / 8 % (5 + width)) + new Vector2(rng.NextFloat() - 0.5f, rng.NextFloat() - 0.5f)));
            ApplyPending(sim);
            return sim;
        }
        Simulation a = Make(), b = Make();
        UnitStore u = a.World.Units;
        var isHolder = new bool[u.Capacity];
        var holderSlots = new List<int>();
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == 0 && MathF.Abs(u.Position[i].X - 33f) < 1e-4f) { isHolder[i] = true; holderSlots.Add(i); }
        Assert.Equal(line, holderSlots.Count);
        foreach (Simulation s in new[] { a, b })
            foreach (int h in holderSlots) s.Enqueue(Command.HoldPosition(0, H(s, h)));
        ApplyPending(a);
        ApplyPending(b);
        var at = holderSlots.Select(h => u.Position[h]).ToArray();
        float far = 33f + radii.Max();
        float worst = float.MaxValue;
        string? worstAt = null, through = null;
        for (int t = 0; t < 2500; t++)
        {
            if (t <= 1000 && t % 100 == 0)
                foreach (Simulation s in new[] { a, b })
                    for (int i = 0; i < u.Capacity; i++)
                        if (!isHolder[i]) s.Enqueue(Command.Move(s.World.Units.Owner[i], H(s, i), s.World.NavGrid.CellCenter(29, 3)));
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {a.TickNumber}");
            AssertOnGround(a);
            for (int k = 0; k < holderSlots.Count; k++)
            {
                int h = holderSlots[k];
                Assert.True(SameBits(at[k], u.Position[h]), $"tick {t}: holder {h} moved {at[k]} -> {u.Position[h]}");
                for (int i = 0; i < u.Capacity; i++)
                {
                    if (isHolder[i]) continue;
                    float ratio = Vector2.Distance(u.Position[i], at[k]) / (u.Radius[i] + u.Radius[h]);
                    if (ratio < worst) { worst = ratio; worstAt = $"tick {t}: unit {i} (player {u.Owner[i]}, {u.State[i]}, r {u.Radius[i]}) at {u.Position[i]}, holder {h} r {u.Radius[h]}"; }
                }
            }
            for (int i = 0; i < u.Capacity; i++)
                if (!isHolder[i] && u.Position[i].X - u.Radius[i] > far)
                    through ??= $"tick {t}: unit {i} (player {u.Owner[i]}, r {u.Radius[i]}) through to {u.Position[i]}";
        }
        _out.WriteLine($"width {width}, holders {string.Join("/", radii)}, seed {seed}: {through ?? "nobody through"}; closest to a holder {worst:F3} x the radii's sum ({worstAt})");
        Assert.True(through == null, through);
        Assert.True(worst >= MovementConstants.ShoveSpacing - 1e-3f, $"pressed past the pack limit into a holder: {worst:F3} ({worstAt})");
    }

    // ------------------------------------------------------------ plug-answer cache vs a fresh search

    private static readonly Func<World, int, int, bool> s_isPlug = (Func<World, int, int, bool>)Delegate.CreateDelegate(
        typeof(Func<World, int, int, bool>),
        typeof(MovementSystem).GetMethod("IsPlug", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("MovementSystem.IsPlug not found: update this QA test"));

    /// <summary>Mirror of the documented plug membership rule (docs/03 M1-9): another player's unit that isn't walking, or a holder of the walker's army.</summary>
    private static bool StandsHard(UnitStore u, int i, int k) =>
        u.Owner[k] != u.Owner[i] ? !(u.State[k] == UnitState.Moving && u.Velocity[k] != Vector2.Zero) : u.Hold[k];

    /// <summary>
    /// A random world of unit clusters (2-3 players, every radius, 10% holders, some units Moving with and
    /// without velocity) on a map with corridors of 1-4 cells, positions in sync with the spatial hash
    /// (what Plan sees). Returns the sim.
    /// </summary>
    private static Simulation RandomClusters(ulong seed, int players)
    {
        var rows = new string[40];
        for (int y = 0; y < 40; y++)
        {
            char[] r = new string('0', 48).ToCharArray();
            // Horizontal walls with gaps of 1-4 cells: corridors for plugs to span.
            if (y is 10 or 11 or 20 or 30 or 31)
                for (int x = 0; x < 48; x++) r[x] = '1';
            rows[y] = new string(r);
        }
        foreach (int y in new[] { 10, 11, 20, 30, 31 })
        {
            int at = 4 + (int)(seed * 7 + (ulong)y) % 30, w = 1 + y % 4;
            char[] r = rows[y].ToCharArray();
            for (int x = at; x < at + w; x++) r[x] = '0';
            rows[y] = new string(r);
        }
        var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: players, UnitCapacity: 600, CommandCapacity: 700), LocalMovementTests.Rows(rows));
        NavGrid g = sim.World.NavGrid;
        var rng = new SimRng(seed, 2024);
        int spawned = 0;
        while (spawned < 560)
        {
            // A cluster around a random passable point, 3-60 members, spacing jittered round touching.
            Vector2 c = g.CellCenter(rng.NextInt(1, 47), rng.NextInt(1, 39));
            int members = 3 + rng.NextInt(0, 58);
            for (int m = 0; m < members && spawned < 560; m++, spawned++)
            {
                float a = rng.NextFloat() * MathF.PI * 2f, d = MathF.Sqrt(m) * (0.7f + 0.6f * rng.NextFloat());
                sim.Enqueue(Command.SpawnUnit(rng.NextInt(0, players), rng.NextInt(0, TestSim.UnitTypeCount), c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * d));
            }
        }
        ApplyPending(sim);
        UnitStore u = sim.World.Units;
        Assert.True(u.Count > 400, $"precondition: {u.Count} units spawned");
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            int roll = rng.NextInt(0, 20);
            if (roll < 2) u.Hold[i] = true; // test seam: holders are Idle with no goal, as HoldPosition leaves them
            else if (roll < 4) { u.State[i] = UnitState.Moving; u.Velocity[i] = Vector2.Zero; } // waiting for a field: still hard to enemies
            else if (roll < 6) { u.State[i] = UnitState.Moving; u.Velocity[i] = new Vector2(0.1f, 0f); } // walking: never a member
        }
        sim.World.Spatial.Rebuild(u);
        return sim;
    }

    /// <summary>
    /// BUG-0044 cache claim ("a set property, so the visiting order changes nothing"): for every walker
    /// class (owner, radius) and every unit hard to it, IsPlug answered from the cache within one pass,
    /// in three random query orders, equals a fresh full search (a new pass per query). Also reports how
    /// many clusters were plugs and how many past MaxPlugCluster, so the scenario exercises both paths.
    /// </summary>
    [Theory]
    [InlineData(1UL, 2)]
    [InlineData(2UL, 2)]
    [InlineData(3UL, 3)]
    [InlineData(4UL, 2)]
    [InlineData(5UL, 3)]
    public void PlugCache_AnyQueryOrder_EqualsAFreshSearch(ulong seed, int players)
    {
        Simulation sim = RandomClusters(seed, players);
        World w = sim.World;
        UnitStore u = w.Units;
        // One walker per (owner, radius class): IsPlug reads only the walker's owner and radius class.
        var walkers = new List<int>();
        var seen = new HashSet<(int, float)>();
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && !u.Hold[i] && seen.Add((u.Owner[i], u.Radius[i]))) walkers.Add(i);
        var pairs = new List<(int I, int K)>();
        foreach (int i in walkers)
            for (int k = 0; k < u.Capacity; k++)
                if (k != i && u.Alive[k] && StandsHard(u, i, k)) pairs.Add((i, k));
        var fresh = new Dictionary<(int, int), bool>();
        int yes = 0;
        foreach (var p in pairs)
        {
            w.PlugEpoch++;
            bool r = s_isPlug(w, p.I, p.K);
            fresh[p] = r;
            if (r) yes++;
        }
        var rng = new SimRng(seed, 99);
        int mismatches = 0;
        string? first = null;
        for (int round = 0; round < 3; round++)
        {
            var order = pairs.ToArray();
            for (int n = order.Length - 1; n > 0; n--)
            {
                int j = rng.NextInt(0, n + 1);
                (order[n], order[j]) = (order[j], order[n]);
            }
            w.PlugEpoch++;
            foreach (var p in order)
            {
                bool r = s_isPlug(w, p.I, p.K);
                if (r != fresh[p]) { mismatches++; first ??= $"round {round}: walker {p.I} (owner {u.Owner[p.I]}, r {u.Radius[p.I]}), unit {p.K}: cached {r}, fresh {fresh[p]}"; }
            }
        }
        _out.WriteLine($"seed {seed}, {players} players: {walkers.Count} walker classes, {pairs.Count} pairs, {yes} plug answers; mismatches {mismatches} ({first ?? "none"})");
        Assert.True(pairs.Count > 1000 && yes > 0 && yes < pairs.Count, "precondition: the scenario should give both answers");
        Assert.True(mismatches == 0, first);
    }

    /// <summary>Allocation checks for the M1-9 paths (run alone: the probe counts this thread's allocations).</summary>
    [Collection(SerialCollection.Name)]
    public class Serial
    {
        /// <summary>
        /// A two-player contested blob of 600 units with 10% holders and enemies standing in a corridor
        /// plug (the plug cache, the cluster search, holders as hard walls, back-off settling): after 150
        /// warm-up ticks, 20 more ticks allocate nothing.
        /// </summary>
        [Fact]
        public void ContestedBlobWithHoldersAndPlugs_TicksAllocateNothing()
        {
            Simulation sim = MoveScenario.Spawn(seed: 5, units: 600, maxCost: 8f, out int goalCell, players: 2);
            UnitStore u = sim.World.Units;
            Vector2 goal = MoveScenario.Center(sim.World.NavGrid, goalCell);
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                sim.Enqueue(i % 10 == 0 ? Command.HoldPosition(u.Owner[i], MoveScenario.Handle(sim, i)) : Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), goal));
            }
            for (int t = 0; t < 150; t++) sim.Tick();
            int moving = 0;
            for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.State[i] == UnitState.Moving) moving++;
            Assert.True(moving > 0, "precondition: units still moving when measured");
            Action ticks = () => { for (int t = 0; t < 20; t++) sim.Tick(); };
            AllocationProbe.AssertZero(ticks);
        }
    }

    /// <summary>
    /// Constructed case of the stale-hash asymmetry: in a 2-cell corridor (y 6-10 m) enemy m (r 0.4)
    /// touches the south wall and enemy e (r 0.9) is hashed at y 8.62 but has since moved to 8.59 (0.03 m,
    /// under its speed), so the pair spans the corridor with a 0.79 m gap. From e the search finds m; from
    /// m it can't find e (its hashed point is past m's query radius). Without the cache each root gave
    /// its own answer every time; with it, the first root asked decides both. Asserts m's answer doesn't
    /// depend on which unit was asked first in the pass.
    /// </summary>
    [Fact] // BUG-0071 fixed in M3-H1: the plug search's query is widened by MaxUnitSpeed
    public void PlugCache_StaleHash_ConstructedPair_AnswerDoesNotDependOnQueryOrder()
    {
        var sim = new Simulation(TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: 3, CommandCapacity: 8), WideCorridorWithRooms(2));
        int small = LocalMovementTests.TypeWithRadius(0.4f), wide = LocalMovementTests.TypeWithRadius(0.9f);
        sim.Enqueue(Command.SpawnUnit(0, small, new Vector2(4f, 4f)));     // the walker class: player 0, r 0.4
        sim.Enqueue(Command.SpawnUnit(1, small, new Vector2(33f, 6.5f)));  // m
        sim.Enqueue(Command.SpawnUnit(1, wide, new Vector2(33f, 8.62f)));  // e, as hashed: 2.12 m from m, past m's query radius 0.4 + 0.9 + 0.8
        ApplyPending(sim);
        World w = sim.World;
        UnitStore u = w.Units;
        int i = 0, m = 1, e = 2;
        Assert.True(u.Owner[i] == 0 && u.Radius[m] == 0.4f && u.Radius[e] == 0.9f, "precondition: slots");
        w.Spatial.Rebuild(u);
        Assert.True(0.04f <= u.Speed[e], $"precondition: the move is within one tick (speed {u.Speed[e]})");
        u.Position[e] = new Vector2(33f, 8.59f); // 0.03 m closer: gap 0.79 m, under the walker's diameter; moved this tick, after the hash was built (as before the shove pass)
        w.PlugEpoch++;
        bool mFirst = s_isPlug(w, i, m);
        bool eAfter = s_isPlug(w, i, e);
        w.PlugEpoch++;
        bool eFirst = s_isPlug(w, i, e);
        bool mAfter = s_isPlug(w, i, m);
        _out.WriteLine($"m asked first: m {mFirst}, then e {eAfter}; e asked first: e {eFirst}, then m {mAfter}");
        Assert.True(mFirst == mAfter && eAfter == eFirst, $"the answers depend on the query order: m {mFirst}/{mAfter}, e {eAfter}/{eFirst}");
    }

    /// <summary>
    /// The same comparison in the shove pass's situation: the spatial hash holds start-of-tick
    /// positions while units that stopped this tick have already moved up to their speed. A cluster is
    /// found from each member through the stale hash, so one member can see a neighbor that does not
    /// see it back; then a cached answer can depend on which member was asked first. Reports how many
    /// pairs differ between query orders.
    /// </summary>
    [Theory]
    [InlineData(1UL, 2)]
    [InlineData(2UL, 2)]
    [InlineData(3UL, 3)]
    public void PlugCache_StaleHash_QueryOrderDependence_Report(ulong seed, int players)
    {
        Simulation sim = RandomClusters(seed, players);
        World w = sim.World;
        UnitStore u = w.Units;
        var rng = new SimRng(seed, 7);
        // Move every non-holder by up to its speed after the hash was built (as Apply does before the shove pass).
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.Hold[i]) continue;
            float a = rng.NextFloat() * MathF.PI * 2f, d = rng.NextFloat() * u.Speed[i];
            Vector2 p = u.Position[i] + new Vector2(MathF.Cos(a), MathF.Sin(a)) * d;
            if (w.NavGrid.WorldToCell(p, out int x, out int y) && w.NavGrid.IsPassable(x, y)) u.Position[i] = p;
        }
        var walkers = new List<int>();
        var seen = new HashSet<(int, float)>();
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && !u.Hold[i] && seen.Add((u.Owner[i], u.Radius[i]))) walkers.Add(i);
        var pairs = new List<(int I, int K)>();
        foreach (int i in walkers)
            for (int k = 0; k < u.Capacity; k++)
                if (k != i && u.Alive[k] && StandsHard(u, i, k)) pairs.Add((i, k));
        var answers = new bool[3][];
        for (int round = 0; round < 3; round++)
        {
            var order = Enumerable.Range(0, pairs.Count).ToArray();
            if (round == 1) Array.Reverse(order);
            if (round == 2)
                for (int n = order.Length - 1; n > 0; n--)
                {
                    int j = rng.NextInt(0, n + 1);
                    (order[n], order[j]) = (order[j], order[n]);
                }
            answers[round] = new bool[pairs.Count];
            w.PlugEpoch++;
            foreach (int q in order) answers[round][q] = s_isPlug(w, pairs[q].I, pairs[q].K);
        }
        int differ = 0;
        for (int q = 0; q < pairs.Count; q++)
            if (answers[0][q] != answers[1][q] || answers[0][q] != answers[2][q]) differ++;
        _out.WriteLine($"seed {seed}, {players} players, stale hash: {pairs.Count} pairs, {differ} answers depend on the query order");
    }
}
