using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using System.Text.Json.Nodes;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;
using static Rts.Sim.Tests.ForestScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M8-1, session 2026-11-14-1246): forest stealth attacked at its edges. A full status store, a reveal fitting into a
/// full store beside the forest's entry, a one-tick delay leaving the stealth dormant under Revealed, a ragged forest edge
/// toggling the hold over and over beside enemies, own workers felling the tree, two trees side by side, an ally's unit
/// detecting for the team, an enemy sandstorm over the hidden unit, a cliff, forged held bytes, the store's arrays in the
/// hash, and data shapes the schema rows don't cover.
/// </summary>
public class ForestStealthQaTests
{
    private readonly ITestOutputHelper _out;

    public ForestStealthQaTests(ITestOutputHelper output) => _out = output;

    private static GameData D => Data3;

    /// <summary>The 3 s copy plus three test-only slow statuses, so a unit can carry 8 statuses none of which is stealth, revealed or damage.</summary>
    private static readonly Lazy<GameData> s_full = new(() => AiScenes.DataWith(dir =>
    {
        dir.EditJson("factions/malazan/faction.json", r => r["forestStealth"] = Block(3));
        StealthTests.AddTo(dir);
        dir.EditJson("common/statuses.json", r =>
        {
            foreach (string id in new[] { "zz_qa_a", "zz_qa_b", "zz_qa_c" })
                r["statuses"]!.AsArray().Add(new JsonObject { ["id"] = id, ["displayName"] = "Test", ["description"] = "Test only.", ["kind"] = "slow" });
        });
    }));

    /// <summary>The block with the shortest delay (one tick), so the forest takes the unit back while Revealed still runs.</summary>
    private static readonly Lazy<GameData> s_oneTick = new(() => DataWith(0.05));

    private static int Infantry(GameData d) => d.FindUnit("malazan_heavy_infantry");

    private static int Laborer(GameData d) => d.FindUnit("malazan_laborer");

    private static int Follower(GameData d) => d.FindUnit("whirlwind_camp_follower");

    /// <summary>The non-stealth statuses of <see cref="s_full"/> that neither hurt nor reveal (8 of them).</summary>
    private static int[] EightHarmless(GameData d) => new[]
    {
        d.FindStatus("slowed"), d.FindStatus("blinded"), d.FindStatus("frenzied"), d.FindStatus("regenerating"),
        d.FindStatus("teblor_frenzied"), d.FindStatus("zz_qa_a"), d.FindStatus("zz_qa_b"), d.FindStatus("zz_qa_c"),
    };

    /// <summary>The forest's bookkeeping against the store after a tick: a held unit carries its faction's entry; the stealth count is the recount.</summary>
    private static void AssertConsistent(Simulation sim, string at)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        StatusStore s = u.Statuses;
        int carriers = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i])
            {
                if (u.Forest != null) Assert.True(u.Forest.Held[i] == 0 && u.Forest.LastAttackTick[i] == -1, $"{at}: free slot {i} keeps forest state");
                continue;
            }
            bool has = false;
            for (int k = 0; k < s.Count[i]; k++)
                has |= w.Data.Statuses[s.StatusId[i * StatusStore.PerUnit + k]].Kind == StatusKind.Stealthed;
            Assert.True(has == s.HasStealth(i), $"{at}: unit {i} stealth flag");
            if (has) carriers++;
            if (u.Forest == null || u.Forest.Held[i] == 0) continue;
            int status = w.Data.Factions[w.FactionOf(u.Owner[i])].ForestStealthStatus;
            int e = s.IndexOf(i, status);
            Assert.True(e >= 0 && s.TicksRemaining[e] >= 1, $"{at}: unit {i} held without its entry");
        }
        Assert.True(carriers == s.StealthedUnits, $"{at}: stealth count {s.StealthedUnits}, recount {carriers}");
    }

    // ---------- a full store ----------

    [Fact]
    public void EightOtherStatuses_TheForestCannotHold_AndHoldsTheTickOneEnds()
    {
        GameData d = s_full.Value;
        Simulation sim = Scene(d, combat: false);
        World w = sim.World;
        Tree(sim, 20, 20);
        EntityHandle h = CombatScenes.Place(sim, 0, Infantry(d), At(sim, 21, 20));
        sim.Enqueue(Command.HoldPosition(0, h));
        int[] eight = EightHarmless(d);
        for (int k = 0; k < eight.Length; k++)
            StatusSystem.Apply(w, h.Index, eight[k], 0.1f, k == eight.Length - 1 ? 40 : 1000, 1);
        Assert.Equal(8, w.Units.Statuses.Count[h.Index]);
        for (int t = 1; t < 40; t++)
        {
            sim.Tick();
            AssertConsistent(sim, $"tick {t}");
            Assert.False(w.IsStealthed(h.Index), $"tick {t}: stealthed with a full store");
            Assert.Equal(0, w.Units.Forest!.Held[h.Index]);
            Assert.Equal(8, w.Units.Statuses.Count[h.Index]);
        }
        sim.Tick(); // tick 40: the short one ends in the countdown, the forest pass right after takes the place
        AssertConsistent(sim, "tick 40");
        Assert.True(w.IsStealthed(h.Index), "not held the tick a place came free");
        Assert.True(Held(sim, h));
        Assert.Equal(8, w.Units.Statuses.Count[h.Index]);
    }

    [Fact]
    public void HeldWithSevenOthers_AnAttackFromStealth_TheRevealFits_AndTheForestTakesItBackAfterTheDelay()
    {
        GameData d = s_full.Value;
        int delay = d.Factions[d.FindFaction("malazan")].ForestStealthDelayTicks;
        Simulation sim = Scene(d);
        World w = sim.World;
        UnitStore u = w.Units;
        Tree(sim, 20, 20);
        EntityHandle h = CombatScenes.Place(sim, 0, Infantry(d), At(sim, 21, 20));
        sim.Enqueue(Command.HoldPosition(0, h));
        sim.Tick();
        Assert.True(Held(sim, h));
        int[] eight = EightHarmless(d);
        for (int k = 0; k < 7; k++) StatusSystem.Apply(w, h.Index, eight[k], 0.01f, 4000, 1);
        Assert.Equal(8, u.Statuses.Count[h.Index]);
        EntityHandle victim = CombatScenes.Place(sim, 1, Follower(d), At(sim, 21, 20, dx: 1.2f));
        int full = u.Hp[victim.Index];
        CombatScenes.RunUntil(sim, () => u.Hp[victim.Index] < full, 200);
        Assert.True(u.Hp[victim.Index] < full, "never hit");
        int t = sim.TickNumber - 1;
        AssertConsistent(sim, "the hit");
        Assert.True(u.Statuses.IndexOf(h.Index, d.FindStatus("revealed")) >= 0, "the reveal did not fit");
        Assert.True(u.Statuses.HasStealth(h.Index), "the forest's entry was pushed out by the reveal");
        Assert.False(w.IsStealthed(h.Index));
        Assert.Equal(8, u.Statuses.Count[h.Index]);
        u.Free(victim);
        bool back = false;
        while (sim.TickNumber < t + delay + 5 && !back)
        {
            sim.Tick();
            AssertConsistent(sim, $"tick {sim.TickNumber - 1}");
            back = w.IsStealthed(h.Index);
            if (back) Assert.True(sim.TickNumber - 1 >= t + delay, $"hidden {sim.TickNumber - 1 - t} ticks after its hit (delay {delay})");
        }
        Assert.True(back, "the forest never took it back");
        Assert.True(Held(sim, h));
        Assert.True(u.Statuses.Count[h.Index] <= StatusStore.PerUnit);
    }

    // ---------- a dormant Stealthed under Revealed ----------

    [Fact]
    public void OneTickDelay_TheHeldEntryIsDormantUnderRevealed_AndInForceTheTickRevealedEnds()
    {
        GameData d = s_oneTick.Value;
        Assert.Equal(1, d.Factions[d.FindFaction("malazan")].ForestStealthDelayTicks);
        Simulation sim = Scene(d);
        World w = sim.World;
        UnitStore u = w.Units;
        Tree(sim, 20, 20);
        EntityHandle h = CombatScenes.Place(sim, 0, Infantry(d), At(sim, 21, 20));
        EntityHandle victim = CombatScenes.Place(sim, 1, Follower(d), At(sim, 21, 20, dx: 1.2f));
        sim.Enqueue(Command.HoldPosition(0, h));
        sim.Tick();
        Assert.True(w.IsStealthed(h.Index));
        int full = u.Hp[victim.Index];
        CombatScenes.RunUntil(sim, () => u.Hp[victim.Index] < full, 100);
        int t = sim.TickNumber - 1;
        u.Free(victim);
        int reveal = d.Rules.RevealTicks;
        Assert.True(reveal > 2);
        for (int k = 1; k <= reveal + 2; k++)
        {
            sim.Tick();
            int now = sim.TickNumber - 1;
            AssertConsistent(sim, $"tick {now}");
            Assert.True(Held(sim, h), $"tick {now}: one tick after the hit the forest holds it again");
            Assert.True(u.Statuses.HasStealth(h.Index));
            Assert.True(w.IsStealthed(h.Index) == (now >= t + reveal), $"tick {now} (hit {t}, reveal {reveal}): stealthed {w.IsStealthed(h.Index)}");
        }
    }

    // ---------- a ragged forest edge ----------

    /// <summary>
    /// Trees every fourth cell along a row; a forest unit walks the row back and forth beside it while enemy fighters attack-move
    /// along the next row. Every tick: the bookkeeping, no enemy sees it undetected, no enemy takes it as a new target while it is
    /// hidden from it, none keeps it hidden past two scans.
    /// </summary>
    [Fact]
    public void ARaggedEdge_TheHoldTogglesAgainAndAgain_TheBookkeepingAndTheTargetRuleHold()
    {
        Simulation sim = Scene(D, size: 64);
        World w = sim.World;
        UnitStore u = w.Units;
        for (int x = 8; x <= 56; x += 4) Tree(sim, x, 30);
        var walkers = new List<EntityHandle>();
        foreach (int type in new[] { D.FindUnit("malazan_laborer"), D.FindUnit("malazan_crossbowman"), Infantry(D) })
            walkers.Add(CombatScenes.Place(sim, 0, type, At(sim, 8, 31)));
        var enemies = new List<EntityHandle>();
        foreach (string key in new[] { "whirlwind_raider", "whirlwind_desert_archer", "whirlwind_raider" })
            enemies.Add(CombatScenes.Place(sim, 1, D.FindUnit(key), At(sim, 56, 36)));
        var prevTarget = new EntityHandle[u.Capacity];
        var hiddenTicks = new int[u.Capacity];
        int toggles = 0, hiddenPairs = 0;
        var wasHeld = new byte[u.Capacity];
        for (int t = 0; t < 2400; t++)
        {
            if (t % 300 == 0)
            {
                bool east = t / 300 % 2 == 0;
                foreach (EntityHandle h in walkers)
                    if (u.IsAlive(h)) sim.Enqueue(Command.Move(0, h, At(sim, east ? 56 : 8, 31)));
                foreach (EntityHandle e in enemies)
                    if (u.IsAlive(e)) sim.Enqueue(Command.AttackMove(1, e, At(sim, east ? 8 : 56, 35)));
            }
            sim.Tick();
            AssertConsistent(sim, $"tick {t}");
            foreach (EntityHandle h in walkers)
            {
                if (!u.IsAlive(h)) continue;
                if (u.Forest!.Held[h.Index] != wasHeld[h.Index]) toggles++;
                wasHeld[h.Index] = u.Forest!.Held[h.Index];
                if (w.HiddenFrom(1, h.Index))
                {
                    hiddenPairs++;
                    Assert.False(w.Fog.CanSeeUnit(1, h.Index), $"tick {t}: player 1 sees hidden {h.Index}");
                }
            }
            for (int i = 0; i < u.Capacity; i++)
            {
                EntityHandle target = u.Alive[i] && !u.TargetIsBuilding[i] ? u.Target[i] : default;
                bool live = target.Generation != 0 && u.IsAlive(target);
                bool hidden = live && w.HiddenFrom(u.Owner[i], target.Index);
                if (live && target != prevTarget[i]) Assert.False(hidden, $"tick {t}: unit {i} took hidden {target.Index}");
                hiddenTicks[i] = hidden ? hiddenTicks[i] + 1 : 0;
                Assert.True(hiddenTicks[i] <= 2 * Combat.CombatConstants.ScanInterval, $"tick {t}: unit {i} keeps hidden {target.Index}");
                prevTarget[i] = live ? target : default;
            }
        }
        _out.WriteLine($"{toggles} hold toggles, {hiddenPairs} hidden walker-ticks; walkers alive {walkers.Count(u.IsAlive)}, enemies alive {enemies.Count(u.IsAlive)}");
        Assert.True(toggles > 40, $"only {toggles} toggles: the edge did not flicker");
        Assert.True(hiddenPairs > 0);
    }

    // ---------- trees felled ----------

    /// <summary>The faction's own Laborer chops the tree an Infantry hides by: the Laborer is hidden while chopping (docs/01 (d)(c)); the felling ends both that tick.</summary>
    [Fact]
    public void OwnWorkerFellsTheTreeItsInfantryHidesBy_BothAreSeenTheFellingTick()
    {
        Simulation sim = Scene(D, combat: false);
        World w = sim.World;
        EntityHandle tree = Tree(sim, 20, 20, wood: 30);
        int hall = StartBase.BuildingOfSlot(D, D.FindFaction("malazan"), BuildingSlot.TownHall);
        TowerTests.PlaceBuilding(sim, 0, hall, 20, 26);
        EntityHandle inf = CombatScenes.Place(sim, 0, Infantry(D), At(sim, 21, 20));
        sim.Enqueue(Command.HoldPosition(0, inf));
        EntityHandle lab = CombatScenes.Place(sim, 0, Laborer(D), At(sim, 19, 21));
        sim.Tick();
        Assert.True(w.IsStealthed(inf.Index));
        sim.Enqueue(Command.Gather(0, lab, At(sim, 20, 20)));
        bool felled = false, laborerHiddenWhileChopping = false;
        for (int t = 0; t < 3000 && !felled; t++)
        {
            sim.Tick();
            AssertConsistent(sim, $"tick {t}");
            felled = !w.Resources.IsAlive(tree);
            Assert.True(w.IsStealthed(inf.Index) == !felled, $"tick {t}: felled {felled}, stealthed {w.IsStealthed(inf.Index)}");
            if (!felled && w.IsStealthed(lab.Index) && w.Units.GatherNode[lab.Index] == tree) laborerHiddenWhileChopping = true;
            if (felled) Assert.False(Abilities.ForestStealthSystem.BesideTree(w, w.Units.Position[inf.Index]));
        }
        Assert.True(felled, "the tree was never felled");
        Assert.True(laborerHiddenWhileChopping, "the Laborer was never hidden while it chopped");
        Assert.False(w.Units.Statuses.HasStealth(inf.Index));
    }

    /// <summary>Two trees beside one unit: felling one leaves the other's cells marked and the unit hidden. The wood bits equal a recount from the live nodes.</summary>
    [Fact]
    public void TwoTreesSideBySide_FellingOne_KeepsTheOther_AndTheWoodBitsAreTheRecount()
    {
        Simulation sim = Scene(D, combat: false);
        World w = sim.World;
        EntityHandle a = Tree(sim, 20, 20), b = Tree(sim, 22, 20), c = Tree(sim, 21, 19);
        EntityHandle h = CombatScenes.Place(sim, 0, Infantry(D), At(sim, 21, 20));
        sim.Enqueue(Command.HoldPosition(0, h));
        sim.Tick();
        Assert.True(w.IsStealthed(h.Index));
        AssertWoodBits(w);
        foreach (EntityHandle node in new[] { a, c })
        {
            w.Resources.Take(node, int.MaxValue);
            Assert.False(w.Resources.IsAlive(node));
            AssertWoodBits(w);
            sim.Tick();
            Assert.True(w.IsStealthed(h.Index), "one tree left, but no longer hidden");
        }
        w.Resources.Take(b, int.MaxValue);
        AssertWoodBits(w);
        sim.Tick();
        Assert.False(w.IsStealthed(h.Index));
        Assert.False(w.Units.Statuses.HasStealth(h.Index));
    }

    private static void AssertWoodBits(World w)
    {
        NavGrid g = w.NavGrid;
        ResourceStore r = w.Resources;
        var expect = new bool[g.Width * g.Height];
        for (int i = 0; i < r.Capacity; i++)
        {
            if (!r.Alive[i]) continue;
            ResourceDef def = w.Data.Resources[r.TypeId[i]];
            if (def.Resource != ResourceKind.Wood) continue;
            int x0 = r.Cell[i] % g.Width, y0 = r.Cell[i] / g.Width;
            for (int y = y0; y < y0 + def.FootprintHeight; y++)
                for (int x = x0; x < x0 + def.FootprintWidth; x++) expect[y * g.Width + x] = true;
        }
        for (int cell = 0; cell < expect.Length; cell++)
            Assert.True(expect[cell] == r.IsWoodCell(cell), $"cell {cell}: bit {r.IsWoodCell(cell)}, live nodes {expect[cell]}");
    }

    // ---------- shared vision, a sandstorm, a cliff ----------

    /// <summary>Player 0 (forest) alone; players 1 and 2 a team. Player 2's unit beside the hidden one detects it for player 1 too; gone, neither does.</summary>
    [Fact]
    public void AnAllyOfTheEnemy_BesideTheHiddenUnit_DetectsItForTheTeam()
    {
        var seats = ImmutableArray.Create(
            new PlayerSeat(D.FindFaction("malazan"), SeatController.Human, AiDifficulty.Normal),
            new PlayerSeat(D.FindFaction("whirlwind"), SeatController.Human, AiDifficulty.Normal, Team: 1),
            new PlayerSeat(D.FindFaction("teblor"), SeatController.Human, AiDifficulty.Normal, Team: 1));
        var sim = new Simulation(new SimConfig(1, 3, 64, 544) { Data = D, Seats = seats, Combat = false }, LocalMovementTests.Flat(48));
        World w = sim.World;
        Tree(sim, 20, 20);
        EntityHandle h = CombatScenes.Place(sim, 0, Laborer(D), At(sim, 21, 20));
        EntityHandle far = CombatScenes.Place(sim, 1, D.FindUnit("whirlwind_raider"), At(sim, 21, 20, dx: 8f));
        EntityHandle near = CombatScenes.Place(sim, 2, D.FindUnit("teblor_gatherer"), At(sim, 21, 20, dy: 2.5f));
        foreach (EntityHandle e in new[] { far, near }) sim.Enqueue(Command.HoldPosition(w.Units.Owner[e.Index], e));
        Ticks(sim, 6);
        Assert.True(w.IsStealthed(h.Index));
        Assert.True(w.IsDetectedBy(2, h.Index));
        Assert.True(w.IsDetectedBy(1, h.Index), "the ally's detection is not the team's");
        Assert.True(w.Fog.CanSeeUnit(1, h.Index));
        w.Units.Free(near);
        Ticks(sim, 6);
        Assert.True(w.IsStealthed(h.Index));
        Assert.False(w.IsDetectedBy(1, h.Index));
        Assert.False(w.Fog.CanSeeUnit(1, h.Index));
        Assert.True(w.Units.IsAlive(far));
    }

    /// <summary>An enemy Priest's Sandstorm over the hidden unit: it takes the Blinded and Slowed (statuses, no hp loss), stays held and hidden from the Priest's side, whose Raider outside the storm and beyond 3 m never sees it.</summary>
    [Fact]
    public void InsideAnEnemySandstorm_TheForestUnitTakesItsStatuses_AndStaysHidden()
    {
        Simulation sim = Scene(D, size: 64);
        World w = sim.World;
        UnitStore u = w.Units;
        Tree(sim, 30, 30);
        EntityHandle h = CombatScenes.Place(sim, 0, Laborer(D), At(sim, 31, 30));
        sim.Enqueue(Command.HoldPosition(0, h));
        EntityHandle priest = CombatScenes.Place(sim, 1, D.FindUnit("whirlwind_priest"), At(sim, 31, 30, dx: 15f));
        EntityHandle raider = CombatScenes.Place(sim, 1, D.FindUnit("whirlwind_raider"), At(sim, 31, 30, dy: 9f));
        sim.Enqueue(Command.HoldPosition(1, raider));
        sim.Tick();
        Assert.True(w.IsStealthed(h.Index));
        sim.Enqueue(Command.UseAbility(1, priest, 0, u.Position[h.Index]));
        bool stormed = false;
        int full = u.Hp[h.Index];
        for (int t = 0; t < 200; t++)
        {
            sim.Tick();
            AssertConsistent(sim, $"tick {t}");
            if (u.Statuses.IndexOf(h.Index, D.FindStatus("blinded")) >= 0) stormed = true;
            Assert.Equal(full, u.Hp[h.Index]);
            Assert.True(w.IsStealthed(h.Index), $"tick {t}: the storm ended the stealth");
            Assert.False(w.Fog.CanSeeUnit(1, h.Index), $"tick {t}: player 1 sees it");
        }
        Assert.True(stormed, "the Sandstorm never took the hidden unit");
        Assert.True(w.Zones.BlockerCount > 0 || stormed);
        Assert.True(u.IsAlive(priest));
    }

    /// <summary>A two-level map: east of column 40 is a plateau. Across the cliff a unit 2 m away detects (inside the 4 m lip); a 10 m detector 6 m away detects from above, not from below (it can't see up).</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACliff_ProximityAcrossItDetects_ADetectorBelowCannotSeeUp(bool hiddenUp)
    {
        // The two-level map of HighGroundLookTests: a plateau east of column 40 (rows 14-46) with a ramp at rows 28-33; row 20 is a cliff.
        int n = AiScenes.FlatSize;
        var sim = new Simulation(new SimConfig(1, 2, 64, 544) { Data = D, Combat = false }, HighGroundLookTests.PlateauMap());
        World w = sim.World;
        int hx = hiddenUp ? 40 : 39, ex = hiddenUp ? 39 : 40;
        bool planted = false;
        foreach ((int tx, int ty) in hiddenUp ? new[] { (41, 20), (41, 21), (40, 21) } : new[] { (38, 20), (38, 21), (39, 21) })
            if (!planted && w.Resources.Spawn(D.FindResource("tree"), ty * n + tx, D.Rules.TreeWood, out _)) planted = true;
        Assert.True(planted, "no tree cell beside the hidden unit; passable 36-44 on row 20: " + string.Join(",", Enumerable.Range(36, 9).Select(x => w.NavGrid.IsPassable(x, 20) ? "1" : "0")));
        EntityHandle h = CombatScenes.Place(sim, 0, Laborer(D), At(sim, hx, 20));
        EntityHandle e = CombatScenes.Place(sim, 1, Follower(D), At(sim, ex, 20));
        sim.Enqueue(Command.HoldPosition(0, h));
        sim.Enqueue(Command.HoldPosition(1, e));
        Ticks(sim, 6);
        Assert.Equal(hiddenUp ? 1 : 0, w.NavGrid.LevelAt(hx, 20));
        Assert.True(w.IsStealthed(h.Index));
        // Within 3 m across the cliff: inside the 4 m lip every viewer sees, so detected either way (docs/02 "High ground").
        Assert.True(Vector2.Distance(w.Units.Position[h.Index], w.Units.Position[e.Index]) <= D.Rules.StealthProximity);
        Assert.True(w.IsDetectedBy(1, h.Index), "proximity across the cliff");
        w.Units.Free(e);
        // A 10 m detector (the Priest) 6 m off across the cliff: from above it sees down and detects; from below it can't see up.
        int px = hiddenUp ? 37 : 42;
        EntityHandle priest = CombatScenes.Place(sim, 1, D.FindUnit("whirlwind_priest"), At(sim, px, 20));
        sim.Enqueue(Command.HoldPosition(1, priest));
        Ticks(sim, 6);
        Assert.True(w.IsStealthed(h.Index));
        Assert.Equal(!hiddenUp, w.IsDetectedBy(1, h.Index));
        Assert.Equal(!hiddenUp, w.Fog.CanSeeUnit(1, h.Index));
    }

    // ---------- saves ----------

    /// <summary>The forest part's held byte set on an own, not excepted unit that carries no stealth entry: refused, never trusted.</summary>
    [Fact]
    public void AForgedHeldByte_OnAnOwnUnitWithoutTheEntry_IsRefused()
    {
        Simulation sim = Scene(D, combat: false);
        EntityHandle open = CombatScenes.Place(sim, 0, Laborer(D), At(sim, 30, 30));
        sim.Tick();
        int cap = sim.World.Units.Capacity;
        byte[] good = SaveTestRun.Save(sim);
        Assert.Equal(SaveError.None, SaveTestRun.TryLoad(D, good));
        int part = -1;
        for (int at = good.AsSpan().IndexOf("FRST"u8); at >= 0 && part < 0;)
        {
            int bytes = 4 + (4 + 4 * cap) + (4 + cap);
            if (at + bytes + 4 <= good.Length && good.AsSpan(at + bytes, 4).SequenceEqual("LEDR"u8)) part = at;
            int next = good.AsSpan(at + 1).IndexOf("FRST"u8);
            at = next < 0 ? -1 : at + 1 + next;
        }
        Assert.True(part > 0);
        byte[] forged = (byte[])good.Clone();
        forged[part + 8 + 4 * cap + 4 + open.Index] = 1;
        Assert.Equal(SaveError.Malformed, SaveTestRun.TryLoad(D, SaveBytes.Rechecksum(forged)));
    }

    /// <summary>An ability's timed Stealthed under the forest's hold, saved and loaded: the entry, the held byte and the run after are the same.</summary>
    [Fact]
    public void ATimedStealthUnderTheHold_SavedAndLoaded_RunsOnTheSame()
    {
        Simulation sim = Scene(D, combat: false);
        Tree(sim, 20, 20);
        EntityHandle h = CombatScenes.Place(sim, 0, Infantry(D), At(sim, 21, 20));
        StatusSystem.Apply(sim.World, h.Index, Stealthed(D), 0f, 120, 0);
        Ticks(sim, 10);
        Assert.True(Held(sim, h));
        Assert.Equal(110, OwnTicks(sim, h));
        Simulation loaded = SaveTestRun.Load(D, SaveTestRun.Save(sim));
        sim.Enqueue(Command.Move(0, h, At(sim, 34, 20)));
        loaded.Enqueue(Command.Move(0, h, At(loaded, 34, 20)));
        for (int t = 0; t < 200; t++)
        {
            sim.Tick();
            loaded.Tick();
            Assert.True(sim.StateHash() == loaded.StateHash(), $"differs {t + 1} ticks after the load");
            Assert.Equal(OwnTicks(sim, h), OwnTicks(loaded, h));
        }
        Assert.False(sim.World.Units.Statuses.HasStealth(h.Index));
    }

    // ---------- the hash ----------

    /// <summary>Every per-slot array of <see cref="ForestStealthStore"/> is in the state hash (the unit store's reflection audit does not reach a property's object).</summary>
    [Fact]
    public void EveryForestStoreArray_IsHashed()
    {
        Simulation sim = Scene(D, combat: false);
        CombatScenes.Place(sim, 0, Laborer(D), At(sim, 30, 30));
        CombatScenes.Place(sim, 0, Laborer(D), At(sim, 32, 30));
        sim.Tick();
        ForestStealthStore f = sim.World.Units.Forest!;
        ulong h0 = sim.StateHash();
        int audited = 0;
        foreach (FieldInfo field in typeof(ForestStealthStore).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            Assert.True(field.FieldType.IsArray, $"{field.Name} is not an array: extend the audit");
            var arr = (Array)field.GetValue(f)!;
            Assert.Equal(sim.World.Units.Capacity, arr.Length);
            object old = arr.GetValue(1)!;
            arr.SetValue(old switch { int x => (object)(x + 5), byte x => (object)(byte)(x ^ 1), _ => throw new InvalidOperationException(field.Name) }, 1);
            Assert.True(sim.StateHash() != h0, $"{field.Name} not in StateHash");
            arr.SetValue(old, 1);
            Assert.Equal(h0, sim.StateHash());
            audited++;
        }
        Assert.Equal(2, audited);
    }

    // ---------- data shapes ----------

    [Theory]
    [InlineData("5")]
    [InlineData("\"stealthed\"")]
    [InlineData("[]")]
    [InlineData("true")]
    [InlineData("{\"status\": 5, \"delay\": 3}")]
    [InlineData("{\"status\": \"stealthed\", \"delay\": true}")]
    [InlineData("{\"status\": \"stealthed\", \"delay\": 3, \"exceptSlots\": [5]}")]
    [InlineData("{\"status\": \"stealthed\", \"delay\": 3, \"exceptSlots\": [\"Siege\"]}")]
    [InlineData("{\"status\": \"Stealthed\", \"delay\": 3}")]
    [InlineData("{\"status\": \"\", \"delay\": 3}")]
    public void AMalformedBlock_IsRefusedWithErrorsAtTheFactionFile_NeverACrash(string block)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/malazan/faction.json", r => r["forestStealth"] = JsonNode.Parse(block));
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        _out.WriteLine(string.Join("\n", r.Errors));
        Assert.Null(r.Data);
        Assert.NotEmpty(r.Errors);
        Assert.All(r.Errors, e => Assert.Equal("factions/malazan/faction.json", e.File));
    }

    /// <summary>Only the documented range loads: the 60 s maximum itself, and a delay that rounds to one tick.</summary>
    [Theory]
    [InlineData(60.0, 1200)]
    [InlineData(0.05, 1)]
    [InlineData(0.026, 1)]
    [InlineData(3.0249, 60)]
    public void ADelayAtTheEdges_LoadsAsTicks(double seconds, int ticks)
    {
        GameData d = DataWith(seconds);
        Assert.Equal(ticks, d.Factions[d.FindFaction("malazan")].ForestStealthDelayTicks);
    }

    /// <summary>A JSON null block: the loader's reading (absent, or one error), reported, never a crash or a half-set bonus.</summary>
    [Fact]
    public void ANullBlock_EitherLoadsAsAbsent_OrIsOneError()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/malazan/faction.json", r => r["forestStealth"] = null);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        _out.WriteLine(r.Data != null ? "null block loads (as absent)" : "refused: " + string.Join("; ", r.Errors));
        if (r.Data != null)
        {
            Assert.False(r.Data.AnyForestStealth());
            Assert.Equal(TestSim.Data.ContentHash(), r.Data.ContentHash());
        }
        else Assert.Single(r.Errors);
    }
}
