using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA, M5-9b (session 2026-11-09-0426, full tier): the far expansion's edges, on the developer's far scene (the flat 64 map
/// with four start locations, every gold mine emptied, one full far mine by unused start 2): gold exactly at the pick's cost;
/// the far mine emptied the think it is first seen, or while the depot is a site; the leg's worker killed on the way; the
/// depot's anchor taken by an enemy building as the Build is sent; an enemy drop-off appearing by the mine mid-expansion;
/// the site destroyed; the armed think's allocation; the far state outside the state hash; and a byte fuzz of the far
/// fields in <c>CTRM</c>.
/// </summary>
[Collection(SerialCollection.Name)]
public class FarExpandQaTests
{
    private readonly ITestOutputHelper _out;

    public FarExpandQaTests(ITestOutputHelper output) => _out = output;

    private static readonly (int X, int Y)[] Starts = { (10, 32), (54, 32), (32, 56), (32, 8) };
    private const int FarMineX = 33, FarMineY = 60;

    private static readonly Lazy<GameData> Shipped = new(() => AiScenes.DataWith(dir =>
    {
        foreach (string f in TestSim.ShippedFactions) // M7-H1 (BUG-1173 item 3): every faction, its own ids by slot
            dir.EditJson($"factions/{f}/ai.json", root =>
            {
                foreach (JsonNode? o in root["openings"]!.AsArray())
                    o!["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "build", AiScenes.BuildingKey(f, BuildingSlot.House), 0));
            });
        dir.EditJson("common/rules.json", r => r["ai"]!["starvedWaveAfter"] = 0);
    }));

    /// <summary>The developer's far scene (FarExpandTests.Scene), rebuilt here: QA's suites don't reach into a private helper.</summary>
    private static Simulation Scene(GameData data, int gold = 0, int wood = 200, bool farMine = true, bool combat = false)
    {
        PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Human, AiDifficulty.Normal) };
        var starts = new StartLocation[Starts.Length];
        for (int k = 0; k < starts.Length; k++) starts[k] = new StartLocation(Starts[k].Y * AiScenes.FlatSize + Starts[k].X, 0);
        Simulation sim = AiScenes.Flat(seats, data, map: LocalMovementTests.Flat(AiScenes.FlatSize).WithStartLocations(starts), explored: false, combat: combat);
        World w = sim.World;
        for (int i = 0; i < w.Resources.Capacity; i++)
            if (w.Resources.Alive[i] && data.Resources[w.Resources.TypeId[i]].Resource == ResourceKind.Gold)
                w.Resources.Take(w.Resources.HandleOf(i), int.MaxValue);
        w.Ledger.Gold[0] = gold;
        w.Ledger.Wood[0] = wood;
        w.Ledger.Gold[1] = 0;
        w.Ledger.Wood[1] = wood;
        if (farMine) Assert.True(w.Resources.Spawn(data.FindResource("gold_mine"), FarMineY * AiScenes.FlatSize + FarMineX, data.Rules.ExpansionMineGold, out _));
        sim.AiOf(0)!.Board.ScoutPhase = 2;
        return sim;
    }

    private static EntityHandle Mine(Simulation sim)
    {
        ResourceStore r = sim.World.Resources;
        for (int i = 0; i < r.Capacity; i++)
            if (r.Alive[i] && r.Cell[i] == FarMineY * AiScenes.FlatSize + FarMineX) return r.HandleOf(i);
        return default;
    }

    private static int Workers(Simulation sim, int p)
    {
        int n = 0;
        for (int i = 0; i < sim.World.Units.Capacity; i++)
            if (sim.World.Units.Alive[i] && sim.World.Units.Owner[i] == p && sim.World.Data.Units[sim.World.Units.TypeId[i]].IsWorker) n++;
        return n;
    }

    private static int CellOf(Vector2 p) => (int)(p.Y / MapConstants.CellSize) * AiScenes.FlatSize + (int)(p.X / MapConstants.CellSize);

    /// <summary>Counts the seat's depot Builds (all, and how many applied as a site) while the sim runs.</summary>
    private sealed class Builds
    {
        public int Sent, Halls, FirstAt = -1;
        public readonly List<Vector2> At = new();

        public Builds(AiPlayer ai, Action<Command>? onBuild = null)
        {
            ai.SentForTests = c =>
            {
                if (c.Kind != CommandKind.Build) return;
                if (c.TypeId == ai.TownHallType) Halls++;
                if (c.TypeId != ai.DepotType) return;
                Sent++;
                At.Add(c.Position);
                if (FirstAt < 0) FirstAt = ai.Now;
                onBuild?.Invoke(c);
            };
        }
    }

    /// <summary>
    /// QA focus "gold exactly at the pick's cost": a seat holding exactly the gold its pick costs (wood ample) is not short and
    /// does not arm on its first think; one gold under, it arms. The threshold is read from the seat itself at 0 gold.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GoldExactlyAtThePicksCost_DoesNotArm_OneUnderDoes(int offset)
    {
        GameData data = Shipped.Value;
        // The threshold: the pick at 0 gold (nothing trains, so the census and the end of think see the same pick).
        Simulation probe = Scene(data, gold: 0, wood: 5000);
        int threshold = -1;
        probe.AiOf(0)!.ThoughtForTests = a =>
        {
            if (threshold >= 0) return;
            Assert.True(a.ShortForPick(out int pick));
            if (pick >= 0) threshold = a.Data.Units[pick].CostGold;
            else
            {
                AiComposition mix = a.Opening.Compositions[Math.Clamp(a.View.Age, 1, a.Opening.Compositions.Length) - 1];
                threshold = int.MaxValue;
                foreach (int u in mix.Units) threshold = Math.Min(threshold, a.Data.Units[u].CostGold);
            }
        };
        while (threshold < 0) probe.Tick();
        Assert.True(threshold > 0, "the pick costs no gold");
        Simulation sim = Scene(data, gold: threshold + offset, wood: 5000);
        AiPlayer ai = sim.AiOf(0)!;
        int phase = -1;
        ai.ThoughtForTests = a => { if (phase < 0) phase = a.Board.FarPhase; };
        while (phase < 0) sim.Tick();
        _out.WriteLine($"pick cost {threshold} gold, held {threshold + offset}: phase {phase}");
        Assert.Equal(offset < 0 ? FarExpansion.Armed : 0, phase);
    }

    /// <summary>
    /// QA focus "the scout dying on the leg": the leg's worker is killed half way to start 2; another worker takes the leg from
    /// the point it was walking to, the mine is seen, one depot is built by it and stands.
    /// </summary>
    [Fact]
    public void TheLegWorker_KilledOnTheWay_AnotherTakesTheLeg_AndOneDepotStands()
    {
        GameData data = Shipped.Value;
        Simulation sim = Scene(data);
        AiPlayer ai = sim.AiOf(0)!;
        var builds = new Builds(ai);
        while (sim.TickNumber < 100) sim.Tick();
        int first = ai.Board.FarScoutSlot, gen = ai.Board.FarScoutGeneration;
        Assert.True(first >= 0, "no leg worker at tick 100");
        var h = new EntityHandle(first, gen);
        Assert.True(sim.World.Units.IsAlive(h));
        CombatSystem.HitUnit(sim.World, new PendingHit(default, 1, h, false, sim.World.Units.Hp[first]));
        Assert.False(sim.World.Units.IsAlive(h));
        int second = -1;
        while (sim.TickNumber < 8000)
        {
            sim.Tick();
            if (second < 0 && ai.Board.FarScoutSlot >= 0 && (ai.Board.FarScoutSlot != first || ai.Board.FarScoutGeneration != gen))
                second = ai.Board.FarScoutSlot;
        }
        _out.WriteLine($"killed slot {first}, next leg worker {second}, depot builds {builds.Sent} (first at {builds.FirstAt}), expansions {ai.Board.FarExpansions}, phase {ai.Board.FarPhase}");
        Assert.True(second >= 0, "no worker took the leg over");
        Assert.Equal(1, builds.Sent);
        Assert.Equal(1, ai.Board.FarExpansions);
        Assert.Equal(0, ai.Board.FarPhase);
    }

    /// <summary>
    /// QA focus "a mine emptied the same think": the far mine is emptied on the tick the seat first sees it (before its next
    /// think): no depot is ever ordered by it, and the leg goes on to the other start.
    /// </summary>
    [Fact]
    public void TheFarMine_EmptiedTheTickItIsFirstSeen_IsNeverBuiltBy()
    {
        GameData data = Shipped.Value;
        Simulation sim = Scene(data);
        AiPlayer ai = sim.AiOf(0)!;
        EntityHandle mine = Mine(sim);
        var builds = new Builds(ai);
        int seenAt = -1;
        while (sim.TickNumber < 6000)
        {
            sim.Tick();
            if (seenAt < 0 && ai.View.TryGetResource(mine.Index, out _))
            {
                seenAt = sim.TickNumber;
                sim.World.Resources.Take(mine, int.MaxValue);
            }
        }
        _out.WriteLine($"seen and emptied at {seenAt}; depot builds {builds.Sent}, halls {builds.Halls}, phase {ai.Board.FarPhase}, looked {ai.Board.FarLooked}, start {ai.Board.FarStart}");
        Assert.True(seenAt > 0, "the mine was never seen");
        Assert.Equal(0, builds.Sent);
        Assert.Equal(0, builds.Halls);
        Assert.Equal(FarExpansion.Armed, ai.Board.FarPhase);
        Assert.Equal((1 << 2) | (1 << 3), ai.Board.FarLooked);
    }

    /// <summary>
    /// QA focus "the far mine emptied while the depot is a site": the depot is finished (the builder stays on it), the
    /// expansion ends, the seat arms again (no gold), looks again, and never sends a second depot Build (no mine left).
    /// </summary>
    [Fact]
    public void TheFarMine_EmptiedWhileTheDepotIsASite_NoSecondDepot()
    {
        GameData data = Shipped.Value;
        Simulation sim = Scene(data);
        AiPlayer ai = sim.AiOf(0)!;
        EntityHandle mine = Mine(sim);
        var builds = new Builds(ai);
        int emptiedAt = -1, rearmedAt = -1;
        while (sim.TickNumber < 10_000)
        {
            sim.Tick();
            if (emptiedAt < 0 && ai.Board.FarSiteType >= 0)
            {
                emptiedAt = sim.TickNumber;
                sim.World.Resources.Take(mine, int.MaxValue);
            }
            if (emptiedAt >= 0 && rearmedAt < 0 && ai.Board.FarExpansions == 1 && ai.Board.FarPhase == FarExpansion.Armed) rearmedAt = sim.TickNumber;
        }
        _out.WriteLine($"emptied at {emptiedAt}, re-armed {rearmedAt}; depot builds {builds.Sent}, expansions {ai.Board.FarExpansions}, phase {ai.Board.FarPhase}, looked {ai.Board.FarLooked}");
        Assert.True(emptiedAt > 0, "no site");
        Assert.Equal(1, builds.Sent);
        Assert.Equal(1, ai.Board.FarExpansions);
        Assert.True(rearmedAt > emptiedAt, "the seat did not arm again with no gold");
    }

    /// <summary>
    /// QA focus "the depot site taken by an enemy building": as the depot Build is sent, an enemy building (a Tent, no drop-off) is put on its
    /// anchor. The Build is refused when it applies; the seat marks that anchor, arms again, orders the depot elsewhere by the
    /// same mine, and it stands: a handful of Builds, not a spam.
    /// </summary>
    [Fact]
    public void TheDepotAnchor_TakenByAnEnemyBuilding_IsRetriedElsewhere()
    {
        GameData data = Shipped.Value;
        Simulation sim = Scene(data);
        AiPlayer ai = sim.AiOf(0)!;
        int blocker = data.FindBuilding("whirlwind_tent"); // not a drop-off: a cache would make the mine an enemy's (no candidate)
        bool blocked = false;
        int blockedCell = -1;
        var builds = new Builds(ai, c =>
        {
            if (blocked) return;
            blocked = true;
            BuildingDef def = data.Buildings[ai.DepotType];
            // The Build's point is its anchor cell's centre: put the enemy cache on that anchor.
            blockedCell = CellOf(c.Position);
            Assert.True(sim.World.Buildings.Spawn(1, blocker, blockedCell, out _), "could not put the blocker on the anchor");
        });
        while (sim.TickNumber < 10_000) sim.Tick();
        _out.WriteLine($"blocked anchor {blockedCell}; depot builds {builds.Sent} at {string.Join(", ", builds.At)}, expansions {ai.Board.FarExpansions}, phase {ai.Board.FarPhase}");
        Assert.True(blocked, "no depot Build sent");
        Assert.InRange(builds.Sent, 2, 4);
        Assert.Equal(1, ai.Board.FarExpansions);
        Assert.Equal(0, ai.Board.FarPhase);
    }

    /// <summary>
    /// QA focus "an enemy drop-off appearing by the far mine mid-expansion": once the depot's site is accepted, an enemy cache
    /// appears in gather reach of the mine, in the seat's sight. Measured: the seat finishes its depot (the rule judged the mine
    /// free when it ordered) and never orders a second one.
    /// </summary>
    [Fact]
    public void AnEnemyDropOff_AppearingByTheMine_MidExpansion_NoSecondDepot()
    {
        GameData data = Shipped.Value;
        Simulation sim = Scene(data);
        AiPlayer ai = sim.AiOf(0)!;
        var builds = new Builds(ai);
        int spawnedAt = -1;
        while (sim.TickNumber < 10_000)
        {
            sim.Tick();
            if (spawnedAt < 0 && ai.Board.FarSiteType >= 0)
            {
                spawnedAt = sim.TickNumber;
                Assert.True(sim.World.Buildings.Spawn(1, data.FindBuilding("whirlwind_supply_cache"), 57 * AiScenes.FlatSize + 37, out _));
            }
        }
        _out.WriteLine($"enemy cache at {spawnedAt}; depot builds {builds.Sent}, expansions {ai.Board.FarExpansions}, phase {ai.Board.FarPhase}, gathering gold {ai.Gathering(EconomyManager.Gold)}");
        Assert.True(spawnedAt > 0, "no site");
        Assert.Equal(1, builds.Sent);
        Assert.Equal(0, builds.Halls);
    }

    /// <summary>
    /// The site destroyed: the far expansion gives it up (phase 0) and does not rebuild in a loop. The seat may build once more
    /// (a later arm), but never more than twice in 10,000 ticks.
    /// </summary>
    [Fact]
    public void TheDepotSite_Destroyed_IsNotRebuiltInALoop()
    {
        GameData data = Shipped.Value;
        Simulation sim = Scene(data);
        AiPlayer ai = sim.AiOf(0)!;
        var builds = new Builds(ai);
        int kills = 0;
        while (sim.TickNumber < 10_000)
        {
            sim.Tick();
            if (ai.Board.FarSiteType < 0) continue;
            BuildingStore b = sim.World.Buildings;
            for (int i = 0; i < b.Capacity; i++)
            {
                if (!b.Alive[i] || b.Owner[i] != 0 || b.TypeId[i] != ai.DepotType || !b.UnderConstruction[i]) continue;
                CombatSystem.HitBuilding(sim.World, new PendingHit(default, 1, new EntityHandle(i, b.Generation[i]), true, b.Hp[i] + 1));
                kills++;
            }
        }
        _out.WriteLine($"sites destroyed {kills}; depot builds {builds.Sent}, expansions {ai.Board.FarExpansions}, phase {ai.Board.FarPhase}, gathering gold {ai.Gathering(EconomyManager.Gold)}");
        Assert.True(kills >= 1, "no site to destroy");
        Assert.InRange(builds.Sent, 1, 2);
    }

    /// <summary>
    /// QA focus "the scout dying on the leg", repeated: an enemy tower stands on each free start (a free start an enemy has
    /// taken since, or a defeated seat's start the winner holds). Every leg worker walks into its range and dies; the leg
    /// sends the next worker to the same point at once, with no limit (the scout's sweep has <c>ai.scoutReplacements</c>).
    /// Asserts the seat keeps at least one worker over 12,000 ticks (10 minutes): it must not feed its whole economy to the
    /// tower one worker at a time.
    /// </summary>
    [Fact]
    public void ALegWorker_KilledAtAGuardedStart_IsNotReplacedWithoutLimit()
    {
        GameData data = Shipped.Value;
        Simulation sim = Scene(data, farMine: false, combat: true);
        AiPlayer ai = sim.AiOf(0)!;
        int tower = data.FindBuilding("whirlwind_lookout_tower");
        Assert.True(sim.World.Buildings.Spawn(1, tower, (Starts[2].Y - 1) * AiScenes.FlatSize + Starts[2].X - 1, out _));
        Assert.True(sim.World.Buildings.Spawn(1, tower, (Starts[3].Y - 1) * AiScenes.FlatSize + Starts[3].X - 1, out _));
        int legs = 0, lastSlot = -1, lastGen = 0, workersAt0 = Workers(sim, 0);
        while (sim.TickNumber < 12_000)
        {
            sim.Tick();
            if (ai.Board.FarScoutSlot >= 0 && (ai.Board.FarScoutSlot != lastSlot || ai.Board.FarScoutGeneration != lastGen))
            {
                legs++;
                lastSlot = ai.Board.FarScoutSlot;
                lastGen = ai.Board.FarScoutGeneration;
            }
        }
        int workers = Workers(sim, 0);
        _out.WriteLine($"leg workers sent {legs}; workers left {workers} (of {workersAt0} at the start, plus any trained), phase {ai.Board.FarPhase}, looked {ai.Board.FarLooked}, start {ai.Board.FarStart}");
        Assert.True(workers > 0, $"the seat fed all its workers to the guarded start ({legs} leg workers sent)");
    }

    /// <summary>
    /// BUG-1159 item 1: with <c>ai.farExpandReach</c> 20 m no free start's mine can ever be a candidate (both free starts are
    /// about 65 m from the hall), yet the leg walks a worker to both. Asserts no leg starts toward a start outside the reach.
    /// </summary>
    [Fact]
    public void TheLeg_DoesNotWalkToAStartOutsideTheReach()
    {
        GameData data = AiScenes.DataWith(dir =>
        {
            foreach (string f in TestSim.ShippedFactions) // M7-H1 (BUG-1173 item 3): every faction, its own ids by slot
                dir.EditJson($"factions/{f}/ai.json", root =>
                {
                    foreach (JsonNode? o in root["openings"]!.AsArray())
                        o!["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "build", AiScenes.BuildingKey(f, BuildingSlot.House), 0));
                });
            dir.EditJson("common/rules.json", r => { r["ai"]!["starvedWaveAfter"] = 0; r["ai"]!["farExpandReach"] = 20; });
        });
        Simulation sim = Scene(data);
        AiPlayer ai = sim.AiOf(0)!;
        int legAt = -1;
        while (sim.TickNumber < 3000)
        {
            sim.Tick();
            if (legAt < 0 && ai.Board.FarStart >= 0) legAt = sim.TickNumber;
        }
        _out.WriteLine($"reach 20 m: armed {ai.Board.FarPhase}, first leg at {legAt}, looked {ai.Board.FarLooked}");
        Assert.Equal(-1, legAt);
    }

    /// <summary>Constraint "no per-think allocation": an armed seat walking its leg, and one ordering its depot, think with 0 B.</summary>
    [Fact]
    public void AnArmedSeatsThink_AllocatesNothing()
    {
        GameData data = Shipped.Value;
        Simulation sim = Scene(data);
        AiPlayer ai = sim.AiOf(0)!;
        while (sim.TickNumber < 60) sim.Tick();
        Assert.Equal(FarExpansion.Armed, ai.Board.FarPhase);
        Assert.True(ai.Board.FarStart >= 0);
        AllocationProbe.AssertZero(() => ai.Think(), _out);
        AllocationProbe.AssertZero(() => FarExpansion.Candidate(ai, out _), _out);
        AllocationProbe.AssertZero(() => FarExpansion.NextStart(ai), _out);
    }

    /// <summary>Constraint "the AI's state out of StateHash": forging every far field leaves the state hash unchanged.</summary>
    [Fact]
    public void TheFarState_IsNotInTheStateHash()
    {
        Simulation sim = Scene(Shipped.Value);
        while (sim.TickNumber < 200) sim.Tick();
        ulong before = sim.StateHash();
        Blackboard b = sim.AiOf(0)!.Board;
        b.FarPhase = FarExpansion.Building;
        b.FarLooked = 0b1111;
        b.FarStart = 3;
        b.FarLook = 4;
        b.FarMoved = !b.FarMoved;
        b.FarScoutSlot = 1;
        b.FarScoutGeneration = 99;
        b.FarSiteType = 0;
        b.FarSiteCell = 7;
        b.FarNoSiteUntil = 12345;
        b.FarExpansions = 9;
        Assert.Equal(before, sim.StateHash());
    }

    /// <summary>
    /// QA focus "save / load fuzz of the new CTRM fields": a save taken mid-arm (the leg walking, on a map with four starts);
    /// every byte of the seat's 67 far bytes set to 0, 1, 2, 4, 0x7F, 0x80, 0xFF and its value + 1, re-checksummed. Each
    /// forgery is refused with a <see cref="SaveError"/> (never an exception) or loads, and a loaded one ticks 400 ticks without
    /// throwing and re-saves byte for byte.
    /// </summary>
    [Fact]
    public void TheFarBytes_ByteFuzzed_RefuseOrLoadAndRun()
    {
        GameData data = Shipped.Value;
        Simulation sim = Scene(data);
        while (sim.TickNumber < 151) sim.Tick();
        Assert.Equal(FarExpansion.Armed, sim.AiOf(0)!.Board.FarPhase);
        byte[] good = SaveTestRun.Save(sim);
        // One AI seat: its far bytes are the last before END!.
        int far = good.Length - SaveFormat.ChecksumLength - 4 - PreMapFixtures.FarBytes;
        byte[] values = { 0, 1, 2, 4, 0x7F, 0x80, 0xFF };
        int loaded = 0, refused = 0;
        var errors = new Dictionary<SaveError, int>();
        for (int i = 0; i < PreMapFixtures.FarBytes; i++)
        {
            for (int v = 0; v <= values.Length; v++)
            {
                byte[] bad = (byte[])good.Clone();
                byte value = v < values.Length ? values[v] : (byte)(good[far + i] + 1);
                if (value == good[far + i]) continue;
                bad[far + i] = value;
                bad = SaveBytes.Rechecksum(bad);
                SaveError e;
                try
                {
                    e = SaveTestRun.TryLoad(data, bad);
                }
                catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
                {
                    throw new Xunit.Sdk.XunitException($"byte {i} = {value}: the loader threw {ex}");
                }
                errors[e] = errors.TryGetValue(e, out int n) ? n + 1 : 1;
                if (e != SaveError.None)
                {
                    refused++;
                    continue;
                }
                loaded++;
                Simulation load = SaveTestRun.Load(data, bad);
                Assert.Equal(bad, SaveTestRun.Save(load));
                try
                {
                    for (int t = 0; t < 400; t++) load.Tick();
                }
                catch (Exception ex)
                {
                    throw new Xunit.Sdk.XunitException($"byte {i} = {value} loaded, then a tick threw: {ex}");
                }
            }
        }
        _out.WriteLine($"far bytes at {far}: {loaded} loaded and ran, {refused} refused ({string.Join(", ", errors.Select(kv => $"{kv.Key} {kv.Value}"))})");
        Assert.True(refused > 0 && loaded > 0);
    }
}
