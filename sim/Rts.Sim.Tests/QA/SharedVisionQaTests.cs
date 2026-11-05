using System.Collections.Immutable;
using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Tests.Save;
using Rts.Sim.Vision;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA, M6-S5 (shared vision between allies). The union rule written out again and checked on the edges the developer's
/// scenes don't reach: a unit at the rim of an ally's sight circle, allies on two levels (a low-ground ally must not grant
/// high-ground sight), an ally defeated mid-match, an ally that never built, three allies on one team, team 0 beside a
/// team and non-contiguous teams whose lowest member is not player 0; a seeded fuzz over team layouts on a two-level map;
/// save / load at ticks 500 and 1,502 of a three-ally match; twin runs of the mirror-start 2v2; the allocation of a
/// scouting mirror-start tick and a three-ally tick.
/// </summary>
[Collection(SerialCollection.Name)]
public class SharedVisionQaTests
{
    private readonly ITestOutputHelper _out;

    public SharedVisionQaTests(ITestOutputHelper output) => _out = output;

    private static Simulation Scene(int[] teams, Heightmap? map = null, bool combat = false, int units = 1024)
    {
        ImmutableArray<PlayerSeat> seats = teams.Select((t, p) => new PlayerSeat(p % 2, SeatController.Human, AiDifficulty.Normal, t)).ToImmutableArray();
        return new Simulation(new SimConfig(1, teams.Length, units, 4 * units + 64) { Data = TestSim.Data, Combat = combat, Seats = seats }, map ?? LocalMovementTests.Flat(64));
    }

    /// <summary>Ticks until the fog updated at least once more (a fog update runs every 4th tick).</summary>
    private static void NextFogUpdate(Simulation sim)
    {
        int v = sim.World.Fog.Version(0);
        for (int t = 0; t < 8 && sim.World.Fog.Version(0) == v; t++) sim.Tick();
        Assert.NotEqual(v, sim.World.Fog.Version(0));
    }

    /// <summary>
    /// The union rule, every cell and every live unit and building, for every player: a cell is visible (explored) to p when
    /// any ally of p (p included, by the seats' teams) has it visible (not unexplored) in its own byte map; the byte map p's
    /// view gets is that union; an ally's unit / building is always seen; an enemy unit is seen when any ally sees its cell by
    /// its own eyes or has it revealed (no stealth in these scenes); allies keep one ghost list. Returns the cells seen only
    /// through an ally.
    /// </summary>
    internal static int AssertUnion(World w, string at, bool cells = true)
    {
        FogStore f = w.Fog;
        int n = w.Config.PlayerCount, count = f.Width * f.Height, throughAlly = 0;
        bool Allied(int a, int b) => a == b || (w.Config.SeatOf(a).Team != 0 && w.Config.SeatOf(a).Team == w.Config.SeatOf(b).Team);
        for (int p = 0; p < n; p++)
        {
            if (cells)
            {
                ReadOnlySpan<byte> vis = f.Visibility(p);
                for (int c = 0; c < count; c++)
                {
                    bool v = false, e = false;
                    for (int q = 0; q < n; q++)
                    {
                        if (!Allied(p, q)) continue;
                        byte o = f.OwnVisibility(q)[c];
                        v |= o == VisionConstants.Visible;
                        e |= o != VisionConstants.Unexplored;
                    }
                    byte want = v ? VisionConstants.Visible : e ? VisionConstants.Explored : VisionConstants.Unexplored;
                    if (f.IsVisible(p, c) != v || f.IsExplored(p, c) != e || vis[c] != want)
                        Assert.Fail($"{at}: player {p} cell {c}: IsVisible {f.IsVisible(p, c)} / IsExplored {f.IsExplored(p, c)} / byte {vis[c]}, the union says {v} / {e} / {want}");
                    if (v && f.OwnVisibility(p)[c] != VisionConstants.Visible) throughAlly++;
                }
            }
            UnitStore u = w.Units;
            int cursor = f.NextSeenUnit(p, 0);
            for (int s = 0; s < u.Capacity; s++)
            {
                bool expected = false;
                if (u.Alive[s])
                {
                    if (Allied(p, u.Owner[s])) expected = true;
                    else
                        for (int q = 0; q < n; q++)
                            if (Allied(p, q) && (f.SeesUnitCellOwn(q, s) || f.Revealed(q, s))) expected = true;
                }
                if (f.CanSeeUnit(p, s) != expected) Assert.Fail($"{at}: player {p} unit {s} (owner {u.Owner[s]}): CanSeeUnit {!expected}, the union says {expected}");
                bool cursorSees = cursor == s;
                if (cursorSees != expected) Assert.Fail($"{at}: player {p} unit {s}: NextSeenUnit {cursorSees}, the union says {expected}");
                if (cursorSees) cursor = f.NextSeenUnit(p, s + 1);
            }
            BuildingStore b = w.Buildings;
            for (int k = 0; k < b.Capacity; k++)
            {
                bool expected = false;
                if (b.Alive[k])
                {
                    if (Allied(p, b.Owner[k])) expected = true;
                    else
                    {
                        BuildingDef d = w.Data.Buildings[b.TypeId[k]];
                        for (int q = 0; q < n && !expected; q++)
                        {
                            if (!Allied(p, q)) continue;
                            if (f.BuildingRevealed(q, k)) expected = true;
                            for (int dy = 0; dy < d.FootprintHeight && !expected; dy++)
                                for (int dx = 0; dx < d.FootprintWidth && !expected; dx++)
                                    if (f.OwnVisibility(q)[b.Cell[k] + dy * f.Width + dx] == VisionConstants.Visible) expected = true;
                        }
                    }
                }
                if (f.CanSeeBuilding(p, k) != expected) Assert.Fail($"{at}: player {p} building {k}: CanSeeBuilding {!expected}, the union says {expected}");
                BuildingGhost g = f.GhostAt(p, k);
                if (g.Generation != 0 && Allied(p, g.Owner)) Assert.Fail($"{at}: player {p} has a ghost of its own or an ally's building {k}");
                for (int q = 0; q < n; q++)
                    if (q != p && Allied(p, q) && !f.GhostAt(q, k).Equals(g)) Assert.Fail($"{at}: allies {p} and {q} remember building slot {k} differently");
            }
        }
        return throughAlly;
    }

    // ---------- the rim of an ally's sight circle ----------

    /// <summary>
    /// 48 enemy units on a spiral round the ally's Heavy Infantry (sight 14 m), from 11 m to 17 m out: for each the player
    /// sees it exactly when the ally does (the rim falls where the ally's own circle falls, cell for cell); some are seen and
    /// some not. In the free-for-all twin the player sees none of them.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EnemiesOnTheRimOfTheAllysSightCircle_AreSeenExactlyWhereTheAllySeesThem(bool allies)
    {
        Simulation sim = Scene(allies ? new[] { 1, 1, 0 } : new[] { 0, 0, 0 });
        World w = sim.World;
        Place(sim, 0, HeavyInfantry, At(sim, 8, 8));
        Vector2 eye = At(sim, 40, 40);
        EntityHandle ally = Place(sim, 1, HeavyInfantry, eye);
        var enemies = new List<EntityHandle>();
        for (int k = 0; k < 48; k++)
        {
            float r = 11f + k * 0.125f, a = k * 2.39996f; // golden angle: no two on one ray
            enemies.Add(Place(sim, 2, HeavyInfantry, eye + new Vector2(r * MathF.Cos(a), r * MathF.Sin(a))));
        }
        NextFogUpdate(sim);
        NextFogUpdate(sim);
        int seen = 0, unseen = 0;
        foreach (EntityHandle e in enemies)
        {
            Assert.True(w.Units.IsAlive(e));
            bool byAlly = w.Fog.CanSeeUnit(1, e.Index);
            if (byAlly) seen++; else unseen++;
            Assert.Equal(allies && byAlly, w.Fog.CanSeeUnit(0, e.Index));
            int c = w.Fog.CellOf(w.Units.Position[e.Index]);
            Assert.Equal(allies && w.Fog.IsVisible(1, c), w.Fog.IsVisible(0, c));
        }
        _out.WriteLine($"allies {allies}: rim units the ally sees {seen}, not {unseen}");
        Assert.True(seen > 0 && unseen > 0, "setup: the spiral doesn't straddle the rim");
        AssertUnion(w, "rim");
    }

    // ---------- two levels ----------

    /// <summary>A 64 map, level 0, with a level-1 block over cells (32-47, 16-47) (no ramp: nobody walks).</summary>
    private static Heightmap Cliff()
    {
        var rows = new string[64];
        for (int y = 0; y < 64; y++)
        {
            var row = new char[64];
            for (int x = 0; x < 64; x++) row[x] = x >= 32 && x <= 47 && y >= 16 && y <= 47 ? '1' : '0';
            rows[y] = new string(row);
        }
        return LocalMovementTests.Rows(rows);
    }

    /// <summary>
    /// Allies on two levels. The ally stands on low ground at the cliff's foot, an enemy 3 cells up on the high ground
    /// (inside the ally's sight radius, past its lip): the ally can't see it, so neither can the player; no high-ground cell
    /// past the lip is visible to the team. Then an ally on the high ground with an enemy below: the player sees it.
    /// </summary>
    [Fact]
    public void ALowGroundAlly_GrantsNoHighGroundSight_AHighGroundAlly_SharesItsViewDown()
    {
        Simulation sim = Scene(new[] { 1, 1, 0 }, Cliff());
        World w = sim.World;
        Assert.True(w.Fog.MultiLevel, "setup: one level");
        Place(sim, 0, HeavyInfantry, At(sim, 8, 8));
        Place(sim, 1, HeavyInfantry, At(sim, 29, 30)); // low ground, 3 cells west of the cliff
        EntityHandle up = Place(sim, 2, HeavyInfantry, At(sim, 35, 30)); // high ground, 6 cells (12 m) east of the ally
        EntityHandle highAlly = Place(sim, 1, HeavyInfantry, At(sim, 44, 20)); // the ally's second unit, up top, 3 cells from the edge
        EntityHandle low = Place(sim, 2, HeavyInfantry, At(sim, 44 + 5, 20)); // low ground east of the block, 10 m from it
        NextFogUpdate(sim);
        NextFogUpdate(sim);
        Assert.Equal(1, w.Fog.LevelAt(w.Units.Position[up.Index]));
        Assert.Equal(0, w.Fog.LevelAt(w.Units.Position[low.Index]));
        Assert.Equal(1, w.Fog.LevelAt(w.Units.Position[highAlly.Index]));
        Assert.False(w.Fog.CanSeeUnit(1, up.Index), "setup: the ally sees up its cliff");
        Assert.False(w.Fog.CanSeeUnit(0, up.Index), "the player sees up the cliff through its low-ground ally");
        Assert.False(new PlayerView(w, 0).TryGetUnit(up.Index, out _));
        Assert.True(w.Fog.CanSeeUnit(1, low.Index), "setup: the high-ground ally sees down");
        Assert.True(w.Fog.CanSeeUnit(0, low.Index), "the player doesn't see what its high-ground ally sees below");
        // No high-ground cell more than 2 cells in from the west edge (past any lip) near the low ally is visible to the team.
        for (int y = 26; y <= 34; y++)
            for (int x = 35; x <= 37; x++)
                Assert.False(w.Fog.IsVisible(0, y * 64 + x), $"cell ({x}, {y}) on the high ground is visible to the team from below");
        int through = AssertUnion(w, "two levels");
        Assert.True(through > 0);
    }

    // ---------- a tower's high-ground reveal ----------

    /// <summary>
    /// BUG-0270's tower reveal, shared: an enemy watchtower far from every eye of the team hits player 1's unit from high
    /// ground (the reveal seam, as <c>CombatSystem</c> calls it): the tower is revealed to player 1 and to its ally player 0
    /// (seen, and a ghost from the next fog update), not to anyone in the free-for-all twin. Reverting
    /// <c>FogStore.RevealBuilding</c>'s team loop passed every other test (QA's M6-S5 mutation run).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ATowersHighGroundReveal_OnTheAlly_IsSharedWithThePlayer(bool allies)
    {
        Simulation sim = Scene(allies ? new[] { 1, 1, 0 } : new[] { 0, 0, 0 });
        World w = sim.World;
        Place(sim, 0, HeavyInfantry, At(sim, 8, 8));
        Place(sim, 1, HeavyInfantry, At(sim, 50, 50));
        Place(sim, 2, HeavyInfantry, At(sim, 8, 56));
        sim.Tick();
        EntityHandle tower = GatherMaps.Building(sim, 28, 54, player: 2, type: w.Data.FindBuilding("malazan_watchtower"));
        NextFogUpdate(sim);
        Assert.False(w.Fog.CanSeeBuilding(0, tower.Index) || w.Fog.CanSeeBuilding(1, tower.Index), "setup: the team sees the tower");
        VisionSystem.OnHit(w, tower, attackerIsBuilding: true, attackerLevel: 1, victimOwner: 1, victimLevel: 0);
        Assert.True(w.Fog.BuildingRevealed(1, tower.Index) && w.Fog.CanSeeBuilding(1, tower.Index), "setup: the victim's owner sees the tower");
        Assert.Equal(allies, w.Fog.BuildingRevealed(0, tower.Index));
        Assert.Equal(allies, w.Fog.CanSeeBuilding(0, tower.Index));
        Assert.Equal(allies, new PlayerView(w, 0).TryGetBuilding(tower.Index, out _));
        Assert.False(w.Fog.BuildingRevealed(2, tower.Index));
        NextFogUpdate(sim);
        Assert.True(w.Fog.HasGhost(1, tower));
        Assert.Equal(allies, w.Fog.HasGhost(0, tower));
        AssertUnion(w, "tower reveal");
    }

    // ---------- an ally defeated mid-match ----------

    /// <summary>
    /// Player 1 (the ally) has a Billet and one unit beside an enemy. Its unit dies: player 1 is defeated on that tick and its
    /// Billet collapses. From the first fog update after the collapse the player sees nothing through player 1 (the enemy
    /// there, the cells), while the ground player 1 explored stays explored for the team. Every tick in between the two
    /// allies' answers stay one (the team's), and the union rule holds.
    /// </summary>
    [Fact]
    public void AnAllyDefeatedMidMatch_StopsSharingAtTheNextFogUpdate_ItsExploredGroundStays()
    {
        Simulation sim = Scene(new[] { 1, 1, 0 });
        World w = sim.World;
        Place(sim, 0, HeavyInfantry, At(sim, 8, 8));
        EntityHandle allyUnit = Place(sim, 1, HeavyInfantry, At(sim, 50, 50));
        EntityHandle enemy = Place(sim, 2, HeavyInfantry, At(sim, 50, 50, dx: 5f));
        Place(sim, 2, HeavyInfantry, At(sim, 8, 56));
        sim.Tick();
        EntityHandle billet = GatherMaps.Building(sim, 45, 45, player: 1, type: w.Data.FindBuilding("malazan_billet"));
        NextFogUpdate(sim);
        int farCell = w.Fog.CellOf(At(sim, 50, 50));
        Assert.True(w.Fog.CanSeeUnit(0, enemy.Index) && w.Fog.IsVisible(0, farCell), "setup: the player sees through its ally");
        Assert.True(w.EverBuilt(1) && !w.IsDefeated(1));
        CombatSystem.HitUnit(w, new PendingHit(enemy, 2, allyUnit, false, w.Units.Hp[allyUnit.Index]));
        Assert.False(w.Units.IsAlive(allyUnit));
        sim.Tick();
        Assert.True(w.IsDefeated(1), "setup: the ally is out");
        Assert.False(w.Buildings.IsAlive(billet), "setup: its Billet collapsed");
        int defeatTick = sim.TickNumber;
        bool staleOnDefeatTick = w.Fog.IsVisible(0, farCell);
        int v = w.Fog.Version(0);
        while (w.Fog.Version(0) == v)
        {
            Assert.Equal(w.Fog.IsVisible(1, farCell), w.Fog.IsVisible(0, farCell));
            sim.Tick();
        }
        _out.WriteLine($"defeated on tick {defeatTick}; still visible on that tick: {staleOnDefeatTick}; next fog update after tick {sim.TickNumber}");
        Assert.False(w.Fog.IsVisible(0, farCell), "the player still sees through its defeated ally");
        Assert.False(w.Fog.CanSeeUnit(0, enemy.Index), "the player still sees the enemy beside its defeated ally");
        Assert.True(w.Fog.IsExplored(0, farCell), "the ground the ally explored is no longer explored");
        Assert.Equal(VisionConstants.Explored, w.Fog.Visibility(0)[farCell]);
        AssertUnion(w, "after the defeat");
        for (int c = 0; c < w.Fog.Width * w.Fog.Height; c++)
            Assert.True(w.Fog.IsVisible(0, c) == (w.Fog.OwnVisibility(0)[c] == VisionConstants.Visible), $"cell {c}: visible to the player beyond its own eyes after its ally's defeat");
        for (int t = 0; t < 40; t++)
        {
            sim.Tick();
            AssertUnion(w, $"tick {sim.TickNumber}", cells: t % 4 == 0);
        }
    }

    // ---------- an ally that never built, three allies, team 0 beside teams ----------

    /// <summary>
    /// Player 1 is an ally with nothing on the map (never built, never had a unit): the player's answers are its own eyes,
    /// and player 1, on the team, sees what the player sees. Player 2 (team 0) sees only its own.
    /// </summary>
    [Fact]
    public void AnAllyWithNothing_AddsNothing_AndSeesThePlayersEyes()
    {
        Simulation sim = Scene(new[] { 1, 1, 0 });
        World w = sim.World;
        EntityHandle own = Place(sim, 0, HeavyInfantry, At(sim, 8, 8));
        EntityHandle enemy = Place(sim, 2, HeavyInfantry, At(sim, 8, 8, dx: 6f));
        Place(sim, 2, HeavyInfantry, At(sim, 56, 56));
        NextFogUpdate(sim);
        Assert.False(w.EverBuilt(1) || w.IsDefeated(1));
        Assert.True(w.Fog.Visibility(0).SequenceEqual(w.Fog.OwnVisibility(0)));
        Assert.True(w.Fog.Visibility(1).SequenceEqual(w.Fog.OwnVisibility(0)));
        Assert.True(w.Fog.CanSeeUnit(0, enemy.Index) && w.Fog.CanSeeUnit(1, enemy.Index) && w.Fog.CanSeeUnit(1, own.Index));
        AssertUnion(w, "ally with nothing");
        for (int t = 0; t < 20; t++) sim.Tick();
        AssertUnion(w, "later");
    }

    /// <summary>
    /// Three allies at three corners (team 3) and a team-0 player: an enemy unit beside each ally is seen by all three, their
    /// byte maps are one, and the team-0 player sees none of the allies' ground. The same with non-contiguous teams
    /// (0, 2, 1, 2, 1: the lowest member of each team is not player 0).
    /// </summary>
    [Theory]
    [InlineData(new[] { 3, 3, 3, 0 })]
    [InlineData(new[] { 0, 2, 1, 2, 1 })]
    [InlineData(new[] { 0, 0, 4, 0, 4, 0, 0, 0 })]
    public void ThreeAllies_AndTeamZeroBesideTeams_TheUnionIsExactlyTheTeams(int[] teams)
    {
        Simulation sim = Scene(teams);
        World w = sim.World;
        int n = teams.Length;
        var eyes = new (int X, int Y)[] { (8, 8), (56, 8), (8, 56), (56, 56), (32, 8), (32, 56), (8, 32), (56, 32) };
        var watched = new EntityHandle[n];
        for (int p = 0; p < n; p++)
        {
            Place(sim, p, HeavyInfantry, At(sim, eyes[p].X, eyes[p].Y));
            // Beside each player's eye a unit of the next player (an enemy unless they're allies: then of the one after).
            int other = (p + 1) % n;
            while (other == p || (teams[p] != 0 && teams[other] == teams[p])) other = (other + 1) % n;
            watched[p] = Place(sim, other, Crossbowman, At(sim, eyes[p].X, eyes[p].Y, dx: 5f));
        }
        NextFogUpdate(sim);
        NextFogUpdate(sim);
        for (int p = 0; p < n; p++)
            for (int q = 0; q < n; q++)
            {
                bool allied = p == q || (teams[p] != 0 && teams[p] == teams[q]);
                EntityHandle e = watched[q];
                if (w.Units.Owner[e.Index] == p || w.AreAllied(p, w.Units.Owner[e.Index])) continue; // its own side's unit
                Assert.True(w.Fog.CanSeeUnit(p, e.Index) == allied, $"player {p} and the unit beside player {q}'s eye: seen {!allied}, allied {allied}");
                if (allied && p < q) Assert.True(w.Fog.Visibility(p).SequenceEqual(w.Fog.Visibility(q)), $"allies {p} and {q} see two maps");
            }
        int through = AssertUnion(w, string.Join(",", teams));
        Assert.True(through > 0);
    }

    // ---------- fuzz ----------

    /// <summary>A 96 map: level 0 with a level-1 plateau over (40-63, 30-65), ramps on its west and south sides.</summary>
    private static Heightmap TwoLevels()
    {
        var rows = new string[96];
        for (int y = 0; y < 96; y++)
        {
            var row = new char[96];
            for (int x = 0; x < 96; x++) row[x] = x >= 40 && x <= 63 && y >= 30 && y <= 65 ? '1' : '0';
            if (y >= 46 && y <= 48) row[39] = 'r';
            if (y == 66) row[50] = row[51] = row[52] = 'r';
            rows[y] = new string(row);
        }
        return RampRows(rows);
    }

    /// <summary>Level digits per cell, 'r' a ramp (level 0, half a level high), as NavGridTests' rows.</summary>
    private static Heightmap RampRows(string[] rows)
    {
        int w = rows[0].Length, h = rows.Length;
        var levels = new byte[w * h];
        var elevations = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                char c = rows[y][x];
                int i = y * w + x;
                levels[i] = c == 'r' ? (byte)0 : (byte)(c - '0');
                elevations[i] = c == 'r' ? MapConstants.LevelHeight / 2 : levels[i] * MapConstants.LevelHeight;
            }
        return new Heightmap(w, h, levels, elevations);
    }

    /// <summary>
    /// Seeded fuzz: team layouts (2v2 both ways, three allies and a team 0, a 2v1, non-contiguous teams, four teams of two,
    /// a free-for-all control) on a two-level 96 map, combat on, each player a Keep and 14 units of three types scattered
    /// over both levels, Move / AttackMove / Stop orders every 15 ticks, 700 ticks. The union rule (cells, units, buildings,
    /// ghosts) holds after every tick (cells at every fog update); the free-for-all sees nothing through anyone.
    /// </summary>
    [Theory]
    [InlineData(1, new[] { 1, 1, 2, 2 })]
    [InlineData(2, new[] { 1, 2, 1, 2 })]
    [InlineData(3, new[] { 5, 5, 5, 0 })]
    [InlineData(4, new[] { 0, 1, 1 })]
    [InlineData(5, new[] { 0, 2, 1, 2, 1 })]
    [InlineData(6, new[] { 1, 2, 3, 4, 4, 3, 2, 1 })]
    [InlineData(7, new[] { 0, 0, 0, 0 })]
    public void Fuzz_TeamLayouts_TwoLevels_TheUnionHoldsEveryTick(int seed, int[] teams)
    {
        Simulation sim = Scene(teams, TwoLevels(), combat: true, units: 512);
        World w = sim.World;
        var rng = new Random(seed);
        int n = teams.Length;
        int[] types = { HeavyInfantry, Crossbowman, Laborer };
        var keeps = new (int X, int Y)[] { (6, 6), (84, 6), (6, 84), (84, 84), (20, 44), (72, 44), (44, 6), (44, 84) };
        for (int p = 0; p < n; p++)
        {
            Place(sim, p, HeavyInfantry, At(sim, keeps[p].X + 2, keeps[p].Y + 6));
            GatherMaps.Building(sim, keeps[p].X, keeps[p].Y, player: p);
        }
        var units = new List<EntityHandle>();
        for (int p = 0; p < n; p++)
            for (int k = 0; k < 14; k++)
            {
                Vector2 at;
                do at = At(sim, rng.Next(2, 94), rng.Next(2, 94), (float)rng.NextDouble(), (float)rng.NextDouble());
                while (!w.NavGrid.IsPassable((int)(at.X / MapConstants.CellSize), (int)(at.Y / MapConstants.CellSize)));
                units.Add(Place(sim, p, types[rng.Next(types.Length)], at));
            }
        int through = 0, highUnits = 0, lastVersion = -1;
        for (int t = 0; t < 700; t++)
        {
            if (t % 15 == 0)
                foreach (EntityHandle h in units)
                {
                    if (!w.Units.IsAlive(h) || rng.Next(3) != 0) continue;
                    int owner = w.Units.Owner[h.Index];
                    Vector2 to = At(sim, rng.Next(2, 94), rng.Next(2, 94));
                    int kind = rng.Next(4);
                    sim.Enqueue(kind == 0 ? Command.Stop(owner, h) : kind == 1 ? Command.AttackMove(owner, h, to) : Command.Move(owner, h, to));
                }
            sim.Tick();
            bool cells = w.Fog.Version(0) != lastVersion;
            lastVersion = w.Fog.Version(0);
            through += AssertUnion(w, $"seed {seed} tick {sim.TickNumber}", cells);
            if (t == 350)
                foreach (EntityHandle h in units)
                    if (w.Units.IsAlive(h) && w.Fog.LevelAt(w.Units.Position[h.Index]) == 1) highUnits++;
        }
        int alive = units.Count(h => w.Units.IsAlive(h));
        _out.WriteLine($"teams {string.Join(",", teams)}: cells seen only through an ally (summed over updates) {through}; units alive {alive} / {units.Count}; on the high ground at tick 350: {highUnits}; losses {w.Losses.ToArray().Sum()}");
        if (teams.All(x => x == 0)) Assert.Equal(0, through);
        else Assert.True(through > 0, "shared vision never showed anything");
        Assert.True(w.Losses.ToArray().Sum() > 0, "setup: nobody fought");
    }

    // ---------- save / load ----------

    /// <summary>Three AI allies (team 1) against an AI with no team on a 128 map, start bases and armies, one raider each.</summary>
    private static SaveScene ThreeAllies(ulong seed) => new($"three_allies_vs_one_seed{seed}", () =>
    {
        PlayerSeat[] seats = Enumerable.Range(0, 4).Select(p => new PlayerSeat(p % 2, SeatController.Ai, AiDifficulty.Normal, p < 3 ? 1 : 0)).ToArray();
        var map = MapGenParams.Default with { Width = 128, Height = 128, Forests = 12, GoldMines = 8, StartLocations = 4 };
        var sim = new Simulation(new SimConfig(seed, 4, 2000, 4096) { Data = TestSim.Data, Map = map, Seats = seats.ToImmutableArray() });
        SaveScenes.PlaceBases(sim, army: true);
        return sim;
    }, (_, _) => { });

    /// <summary>
    /// Save / load of a team match with three allies and a team-0 seat at tick 1,502 (between fog updates): right after the
    /// load every team answer (every cell, the byte map, every unit and building) equals the unsaved run's; then the round
    /// trip at 500 and 1,502 runs 1,000 ticks hash-equal each.
    /// </summary>
    [Fact]
    public void ThreeAlliesAndATeamZero_SaveLoadAt500And1502_TheTeamAnswersAndTheHashFollow()
    {
        SaveScene scene = ThreeAllies(3);
        Simulation sim = scene.Build();
        for (int t = 0; t < 1502; t++) sim.Tick();
        Assert.NotEqual(VisionConstants.UpdatePhase, sim.TickNumber % VisionConstants.UpdateInterval);
        Simulation loaded = SaveTestRun.Load(TestSim.Data, SaveTestRun.Save(sim));
        World a = sim.World, b = loaded.World;
        int cells = a.Fog.Width * a.Fog.Height;
        for (int p = 0; p < 4; p++)
        {
            Assert.True(a.Fog.Visibility(p).SequenceEqual(b.Fog.Visibility(p)), $"player {p}'s byte map");
            for (int c = 0; c < cells; c++)
                Assert.True(a.Fog.IsVisible(p, c) == b.Fog.IsVisible(p, c) && a.Fog.IsExplored(p, c) == b.Fog.IsExplored(p, c), $"player {p} cell {c}");
            for (int s = 0; s < a.Units.Capacity; s++) Assert.True(a.Fog.CanSeeUnit(p, s) == b.Fog.CanSeeUnit(p, s), $"player {p} unit {s}");
            for (int s = 0; s < a.Buildings.Capacity; s++) Assert.True(a.Fog.CanSeeBuilding(p, s) == b.Fog.CanSeeBuilding(p, s), $"player {p} building {s}");
        }
        Assert.True(AssertUnion(b, "loaded") > 0, "setup: nothing seen through an ally");
        SaveTestRun.Result r = SaveTestRun.RoundTrip(scene, new[] { 500, 1502 }, after: 1000);
        Assert.Equal(2000, r.TicksCompared);
    }

    // ---------- BUG-1045: the AI's wood with an ally's eyes ----------

    /// <summary>
    /// BUG-1045: BUG-1042's mirror-start 2v2 (seed 1, one raider each). Every seat knows a forest near home by tick 2,000
    /// (within 120 m of its hall; at e5c4265 each knew one 17-21 m off). With shared vision seat 3 (south) knows a forest, a
    /// gold mine and an enemy building through its ally from the first think, so its scout is never sent, its own forest
    /// 17 m away stays unknown, and its wood gatherers walk to its ally's forests 300-450 m north: 0 wood gathered by tick
    /// 4,000 (370 at e5c4265), no army, no wave (it waved at e5c4265). Prints each seat's gathered wood too.
    /// Un-skipped with the fix (M6-S5, <c>ai.gatherReach</c>).
    /// </summary>
    [Fact]
    public void MirrorAllies_EverySeat_GathersWoodNearHome()
    {
        Simulation sim = AiTeamTests.MirrorAllies(1);
        World w = sim.World;
        var nearest = new float[4];
        while (sim.TickNumber < 4000)
        {
            sim.Tick();
            if (sim.TickNumber != 2000) continue;
            for (int p = 0; p < 4; p++)
            {
                AiPlayer ai = sim.AiOf(p)!;
                nearest[p] = float.MaxValue;
                int r = -1;
                while (ai.View.NextResource(ref r, out SeenResource n))
                    if (w.Data.Resources[n.TypeId].Resource == ResourceKind.Wood)
                        nearest[p] = Math.Min(nearest[p], Vector2.Distance(ai.NodeCentre(n.TypeId, n.Cell), ai.HallPoint));
            }
        }
        int[] wood = w.GatheredWood.ToArray();
        _out.WriteLine($"gathered wood by tick 4,000: {string.Join(", ", wood)}; nearest known forest at tick 2,000 (m): {string.Join(", ", nearest.Select(d => d.ToString("F0")))}");
        for (int p = 0; p < 4; p++) Assert.True(nearest[p] <= 120f, $"seat {p}'s nearest known forest is {nearest[p]:F0} m from its hall at tick 2,000 (gathered {wood[p]} wood by tick 4,000)");
    }

    // ---------- determinism and allocation ----------

    /// <summary>The mirror-start 2v2 (BUG-1042's layout, one raider each) twice: equal hashes every tick for 3,000 ticks; and its seed-2 twin differs.</summary>
    [Fact]
    public void MirrorAllies_TwoRuns_SameHashEveryTick_AnotherSeedDiffers()
    {
        Simulation a = AiTeamTests.MirrorAllies(1), b = AiTeamTests.MirrorAllies(1), c = AiTeamTests.MirrorAllies(2);
        bool differs = false;
        for (int t = 0; t < 3000; t++)
        {
            a.Tick();
            b.Tick();
            c.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"the runs differ after tick {a.TickNumber}");
            differs |= a.StateHash() != c.StateHash();
        }
        Assert.True(differs, "seeds 1 and 2 hash the same");
    }

    /// <summary>A mirror-start tick while the scouts sweep (the new farthest-from-the-ally pick) and a three-ally tick allocate nothing.</summary>
    [Fact]
    public void ATick_OfTheMirrorStart2v2WhileScouting_AndOfThreeAllies_AllocatesNothing()
    {
        Simulation mirror = AiTeamTests.MirrorAllies(1, raiders: 0);
        while (mirror.TickNumber < 1400) mirror.Tick();
        int scouting = 0;
        for (int p = 0; p < 4; p++) if (mirror.AiOf(p)!.Board.ScoutPhase == 1) scouting++;
        Assert.True(scouting > 0, "setup: no scout out at tick 1,400");
        Action ticks = () => { for (int t = 0; t < 200; t++) mirror.Tick(); };
        AllocationProbe.AssertZero(ticks, _out);

        Simulation three = ThreeAllies(5).Build();
        for (int t = 0; t < 600; t++) three.Tick();
        Action more = () => { for (int t = 0; t < 60; t++) three.Tick(); };
        AllocationProbe.AssertZero(more, _out);
    }
}
