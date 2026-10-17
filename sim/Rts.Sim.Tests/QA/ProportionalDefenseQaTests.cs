using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M5-4f (session 2026-10-16-2036): the proportional defense (<see cref="DefenseManager"/>, docs/03 "Implementation
/// (M5-4f)", docs/02 "AI opponent") judged <b>through the command log only</b>: who is sent where is read from the AI's
/// accepted commands, the defense point is computed by the test (the raiders' centroid, or the hit building's centre),
/// costs come from the data. The flat scene: player 0 (AI, malazan) has its hall at anchor (8, 30) and a billet in sight of
/// player 1's hall; a wave of 12 Heavy Infantry goes out with a 6-pop start threshold, then the home army is placed. The
/// defense knobs are written into the test's own rules.json at the shipped values (rule 12).
/// </summary>
public class ProportionalDefenseQaTests
{
    private readonly ITestOutputHelper _out;

    public ProportionalDefenseQaTests(ITestOutputHelper output) => _out = output;

    private static int Heavy => TestSim.Data.FindUnit("malazan_heavy_infantry");

    internal static GameData Data(double ratio = 1.5, double min = 150, double recall = 0.5, double cooldown = 60, double defendFor = 20) => AiScenes.DataWith(dir =>
    {
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            root["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1));
            root["attack"] = new JsonObject { ["minArmyPop"] = 6, ["minArmyCost"] = 0 };
            root["difficulties"]!["normal"]!["waveSizeMultiplier"] = 1;
        });
        dir.EditJson("common/rules.json", r =>
        {
            JsonNode ai = r["ai"]!;
            ai["waveCooldown"] = cooldown;
            ai["defendRadius"] = 48;
            ai["defendFor"] = defendFor;
            ai["workerFleeRadius"] = 16;
            ai["rallyReach"] = 6;
            ai["scoutAfter"] = 3600;
            ai["defendRatio"] = ratio;
            ai["defendMinValue"] = min;
            ai["recallHallHp"] = recall;
        });
    });

    private static readonly Lazy<GameData> s_shipped = new(() => Data());

    private static int Cost(GameData d, int type) => d.Units[type].CostGold + d.Units[type].CostWood;

    /// <summary>The scene; everything judged from <see cref="Log"/>.</summary>
    private sealed class Scene
    {
        internal readonly Simulation Sim;
        internal readonly List<EntityHandle> Members = new(), Home = new();
        internal readonly Vector2 Hall, WaveTarget;
        internal readonly List<Command> Log;

        internal Scene(GameData data, int homeUnits = 3, bool wave = true)
        {
            Sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), data);
            BuildMaps.SetTotals(Sim, 0, 0, 0);
            World w = Sim.World;
            (int hx, int hy) = AiScenes.HallAt(0);
            BuildingDef hall = w.Data.Buildings[w.Buildings.TypeId[w.Buildings.SlotAt(hx, hy)]];
            Hall = new Vector2((hx + hall.FootprintWidth * 0.5f) * MapConstants.CellSize, (hy + hall.FootprintHeight * 0.5f) * MapConstants.CellSize);
            List<Command> early = AiScenes.Log(Sim, 0);
            if (wave)
            {
                Assert.True(w.Buildings.Spawn(0, w.Data.FindBuilding("malazan_billet"), 31 * AiScenes.FlatSize + 47, out _));
                Members.AddRange(Army(12, 38, 26));
                NextThink();
                NextThink();
                // The wave's target from the log: the AttackMove every member got.
                Command first = early.First(c => c.Kind == CommandKind.AttackMove && Members.Contains(c.Unit));
                WaveTarget = first.Position;
                Assert.All(Members, h => Assert.Contains(early, c => c.Unit == h && c.Kind == CommandKind.AttackMove && c.Position == WaveTarget));
                foreach (EntityHandle h in Members)
                    Assert.True(Vector2.Distance(w.Units.Position[h.Index], Hall) > 48f, "a member is inside the base");
            }
            Home.AddRange(Army(homeUnits, 12, 38));
            Log = AiScenes.Log(Sim, 0);
        }

        internal List<EntityHandle> Army(int n, int x, int y)
        {
            var list = new List<EntityHandle>();
            for (int k = 0; k < n; k++) list.Add(CombatScenes.Place(Sim, 0, Heavy, Sim.World.NavGrid.CellCenter(x + k % 6, y + k / 6)));
            return list;
        }

        /// <summary>Player 1's Raiders at <paramref name="at"/> + (k, 0), spotted, holding; returns their centroid (the defense point).</summary>
        internal Vector2 Raiders(int n, Vector2 at)
        {
            Vector2 sum = Vector2.Zero;
            for (int k = 0; k < n; k++)
            {
                EntityHandle h = CombatScenes.Place(Sim, 1, CombatScenes.Raider, at + new Vector2(k, 0f));
                CombatScenes.Spot(Sim, 0, h);
                Sim.World.Units.Hold[h.Index] = true;
                sum += Sim.World.Units.Position[h.Index];
            }
            return sum / n;
        }

        internal void NextThink()
        {
            int think = Sim.AiOf(0)!.ThinkTicks;
            do Sim.Tick(); while ((Sim.TickNumber - 1) % think != 0);
        }

        /// <summary>Army units the log sends to <paramref name="point"/> (AttackMove, or the Move from outside the base).</summary>
        internal List<EntityHandle> SentTo(Vector2 point) =>
            Log.Where(c => c.Kind is CommandKind.AttackMove or CommandKind.Move && c.Position == point && Sim.World.Units.TypeId[c.Unit.Index] == Heavy)
                .Select(c => c.Unit).Distinct().ToList();

        /// <summary>Members the log sends a Move anywhere but <paramref name="point"/> (the wave's end: home to the rally).</summary>
        internal List<EntityHandle> MembersSentHome(Vector2 point) =>
            Log.Where(c => c.Kind == CommandKind.Move && c.Position != point && Members.Contains(c.Unit)).Select(c => c.Unit).Distinct().ToList();

        internal int Worth(IEnumerable<EntityHandle> units) => units.Sum(h => Cost(Sim.World.Data, Sim.World.Units.TypeId[h.Index]));
    }

    /// <summary>
    /// One Raider 30 m from the hall (64 worth; answer max(96, 150) = 150) with the wave out: at most two home units answer
    /// (74 + 74 = 148, within half a unit of 150), no member is sent to the point or home, and over 8 thinks the members
    /// get only the wave's orders.
    /// </summary>
    [Fact]
    public void OneRaider_AtMostTwoHomeUnits_TheWaveOn()
    {
        var s = new Scene(s_shipped.Value);
        Vector2 point = s.Raiders(1, s.Hall + new Vector2(0f, 30f));
        for (int k = 0; k < 8; k++) s.NextThink();
        List<EntityHandle> sent = s.SentTo(point);
        _out.WriteLine($"sent {sent.Count} ({s.Worth(sent)} worth) of {s.Home.Count} home; member orders: {string.Join(", ", s.Log.Where(c => s.Members.Contains(c.Unit)).Select(c => c.Kind))}");
        Assert.Equal(2, sent.Count);
        Assert.All(sent, h => Assert.Contains(h, s.Home));
        Assert.Empty(s.MembersSentHome(point));
        Assert.All(s.Log.Where(c => s.Members.Contains(c.Unit)), c => Assert.Equal(s.WaveTarget, c.Position));
    }

    /// <summary>
    /// The half-unit rule at its edge: two Raiders (128; answer 192). Two Heavy Infantry (148) are short by 44, more than half
    /// a Heavy's 74 (37), so a third is sent (222); one Raider's 150 stops at two (short by 2).
    /// </summary>
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 4)] // 192 x 1.5 = 288: three (222) short by 66 > 37, four (296)
    public void TheHalfUnitRule_SendsTheExpectedCount(int raiders, int expected)
    {
        var s = new Scene(s_shipped.Value, homeUnits: 6);
        GameData d = s.Sim.World.Data;
        Vector2 point = s.Raiders(raiders, s.Hall + new Vector2(0f, 30f));
        s.NextThink();
        List<EntityHandle> sent = s.SentTo(point);
        float answer = Math.Max(1.5f * raiders * Cost(d, CombatScenes.Raider), 150f);
        _out.WriteLine($"{raiders} raider(s): answer {answer}, sent {sent.Count} worth {s.Worth(sent)}");
        Assert.Equal(expected, sent.Count);
        Assert.All(sent, h => Assert.Contains(h, s.Home));
        // Independently: the picked worth is within half a unit of the answer.
        Assert.InRange(s.Worth(sent), answer - Cost(d, Heavy) / 2f, answer + Cost(d, Heavy) / 2f);
    }

    /// <summary>
    /// Eight Raiders (512; answer 768) outweigh the 3 home units (222): every home unit answers and part of the wave is
    /// pulled to the point (a Move, from outside the base), the rest of the members are never sent home and keep the
    /// wave's target: at least 6 of the 12 stay (the 6-pop threshold), at least one is pulled.
    /// </summary>
    [Fact]
    public void EightRaiders_PartOfTheWave_TheRestGoOn()
    {
        var s = new Scene(s_shipped.Value);
        Vector2 point = s.Raiders(8, s.Hall + new Vector2(-4f, 30f));
        for (int k = 0; k < 6; k++) s.NextThink();
        List<EntityHandle> sent = s.SentTo(point);
        List<EntityHandle> pulled = sent.Where(s.Members.Contains).ToList();
        _out.WriteLine($"home sent {sent.Count(s.Home.Contains)}/{s.Home.Count}, members pulled {pulled.Count}/{s.Members.Count}");
        Assert.Equal(s.Home.OrderBy(h => h.Index), sent.Where(s.Home.Contains).OrderBy(h => h.Index));
        Assert.InRange(pulled.Count, 1, s.Members.Count - 6);
        Assert.Empty(s.MembersSentHome(point));
        foreach (EntityHandle h in s.Members.Where(m => !pulled.Contains(m)))
            Assert.All(s.Log.Where(c => c.Unit == h), c => Assert.Equal(s.WaveTarget, c.Position));
    }

    /// <summary>Twelve Raiders (768) outweigh home + the spare members (222 + 444): every member is sent home (a Move away from the point), then to the point.</summary>
    [Fact]
    public void TwelveRaiders_TheWholeWave()
    {
        var s = new Scene(s_shipped.Value);
        Vector2 point = s.Raiders(12, s.Hall + new Vector2(-6f, 30f));
        s.NextThink();
        Assert.Equal(s.Members.OrderBy(h => h.Index), s.MembersSentHome(point).OrderBy(h => h.Index));
        for (int k = 0; k < 4; k++) s.NextThink();
        Assert.Equal(s.Home.Concat(s.Members).OrderBy(h => h.Index), s.SentTo(point).OrderBy(h => h.Index));
    }

    /// <summary>
    /// The hall hit with nobody seen, at the recallHallHp edge (0.5, strictly below): left at max/2 - 1 the whole wave comes
    /// home and every home unit answers; left at exactly max/2 (or above) only the minimum answer (two home units) goes and
    /// the wave goes on.
    /// </summary>
    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(100, false)]
    public void TheHallHit_AtTheRecallEdge(int offsetFromHalf, bool whole)
    {
        var s = new Scene(s_shipped.Value);
        World w = s.Sim.World;
        (int hx, int hy) = AiScenes.HallAt(0);
        int slot = w.Buildings.SlotAt(hx, hy);
        int max = w.Data.Buildings[w.Buildings.TypeId[slot]].Hp;
        Assert.True(max % 2 == 0, "the edge row needs an even max hp");
        w.Buildings.SetHp(slot, max / 2 + offsetFromHalf);
        for (int k = 0; k < 3; k++) s.NextThink();
        List<EntityHandle> sent = s.SentTo(s.Hall);
        _out.WriteLine($"hp {max / 2 + offsetFromHalf}/{max}: members home {s.MembersSentHome(s.Hall).Count}, sent to the hall {sent.Count}");
        Assert.Equal(whole ? s.Members.Count : 0, s.MembersSentHome(s.Hall).Count);
        Assert.Equal(whole ? s.Home.Count + s.Members.Count : 2, sent.Count);
    }

    /// <summary>A hit on a billet near the hall with nobody seen: 150 answered, two home units to the billet's centre, the wave on, for 8 thinks.</summary>
    [Fact]
    public void AHitWithNobodySeen_TheMinimumOnly()
    {
        var s = new Scene(s_shipped.Value, homeUnits: 5);
        World w = s.Sim.World;
        int billetType = w.Data.FindBuilding("malazan_billet");
        // Clear of the home army (cells 12-16, row 38), 21 m from the hall's centre.
        Assert.True(w.Buildings.Spawn(0, billetType, 34 * AiScenes.FlatSize + 20, out EntityHandle billet));
        BuildingDef bd = w.Data.Buildings[billetType];
        Vector2 centre = new((20 + bd.FootprintWidth * 0.5f) * MapConstants.CellSize, (34 + bd.FootprintHeight * 0.5f) * MapConstants.CellSize);
        s.NextThink();
        w.Buildings.SetHp(billet.Index, w.Buildings.Hp[billet.Index] - 10);
        for (int k = 0; k < 8; k++) s.NextThink();
        List<EntityHandle> sent = s.SentTo(centre);
        Assert.Equal(2, sent.Count);
        Assert.Empty(s.MembersSentHome(centre));
    }

    /// <summary>
    /// The recall's cooldown (waveCooldown 30 s, defendFor 2 s): twelve Raiders call the whole wave at think T and are then
    /// removed; no new wave (an AttackMove of the army to an enemy building) before T + waveCooldown, and one within a few
    /// thinks after it. A partial pull (eight Raiders) leaves the wave on, so no second wave start at all in that window.
    /// </summary>
    [Fact]
    public void AWholeWaveRecall_StartsTheCooldown_NoWaveBeforeItEnds()
    {
        var s = new Scene(Data(cooldown: 30, defendFor: 2));
        World w = s.Sim.World;
        var raiders = new List<EntityHandle>();
        Vector2 at = s.Hall + new Vector2(-6f, 30f);
        Vector2 point = s.Raiders(12, at);
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 1 && w.Units.TypeId[i] == CombatScenes.Raider) raiders.Add(new EntityHandle(i, w.Units.Generation[i]));
        s.NextThink();
        int recalledAt = s.Sim.TickNumber - 1;
        Assert.Equal(s.Members.Count, s.MembersSentHome(point).Count);
        foreach (EntityHandle h in raiders) w.Units.Free(h);
        int cooldown = 30 * 20, think = s.Sim.AiOf(0)!.ThinkTicks;
        int waveAt = -1;
        while (s.Sim.TickNumber < recalledAt + cooldown + 10 * think && waveAt < 0)
        {
            s.NextThink();
            // A new wave: an AttackMove to the enemy building (the old target) sent to several army units in one think.
            List<Command> attack = s.Log.Where(c => c.Kind == CommandKind.AttackMove && c.Position == s.WaveTarget && c.Tick - 1 > recalledAt).ToList();
            if (attack.Count >= 2) waveAt = attack.Min(c => c.Tick - 1);
        }
        _out.WriteLine($"recalled at {recalledAt}; next wave at {waveAt} (cooldown ends {recalledAt + cooldown})");
        Assert.True(waveAt >= recalledAt + cooldown, $"a wave started at {waveAt}, before the cooldown ends at {recalledAt + cooldown}");
        Assert.True(waveAt > 0 && waveAt <= recalledAt + cooldown + 4 * think, $"no wave within 4 thinks of the cooldown's end ({waveAt})");
    }

    /// <summary>
    /// Ties in distance keep slot order (docs/03: "(distance, slot) orders every pair"): two home units at exactly the same
    /// distance from a lone Raider (integer coordinates, so the squared distances are exact), an answer that picks one
    /// (defendRatio 1, defendMinValue 0: answer 64, the first pick always, a second at 74 + 37 &gt; 64 never): the lower slot
    /// is sent. Mirrored placement order, so the lower slot is once the west unit and once the east one. A mutant that
    /// breaks slot ties the other way sends the other unit (QA ran it: the log changes).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AHomeTie_TheLowerSlotIsSent(bool eastFirst)
    {
        var s = new Scene(Data(ratio: 1, min: 0), homeUnits: 0, wave: false);
        World w = s.Sim.World;
        Vector2 at = w.NavGrid.CellCenter(10, 45), west = w.NavGrid.CellCenter(4, 45), east = w.NavGrid.CellCenter(16, 45); // 12 m either side, past rallyReach
        EntityHandle a = CombatScenes.Place(s.Sim, 0, Heavy, eastFirst ? east : west);
        EntityHandle b = CombatScenes.Place(s.Sim, 0, Heavy, eastFirst ? west : east);
        Assert.Equal(Vector2.DistanceSquared(w.Units.Position[a.Index], at), Vector2.DistanceSquared(w.Units.Position[b.Index], at));
        Vector2 point = s.Raiders(1, at);
        s.NextThink();
        List<EntityHandle> sent = s.SentTo(point);
        EntityHandle lower = a.Index < b.Index ? a : b;
        Assert.Equal(new[] { lower }, sent);
    }

    /// <summary>
    /// The same tie among wave members (heapsorted by distance to the hall): 7 members out at a 6-pop threshold (one spare),
    /// no home army, one Raider (answer 150; the recall test: 64 against the spare 74, no recall). Right before the
    /// defense's think two members stand at exactly mirrored spots about the hall's centre (integer coordinates), nearer the
    /// hall than the other five: exactly one member is pulled, the lower slot of the two. Mirrored, so the lower slot is
    /// once the north unit and once the south one.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AMemberTie_TheLowerSlotIsPulled(bool southFirst)
    {
        var s = new Scene(s_shipped.Value, homeUnits: 0, wave: false);
        World w = s.Sim.World;
        Assert.True(w.Buildings.Spawn(0, w.Data.FindBuilding("malazan_billet"), 31 * AiScenes.FlatSize + 47, out _));
        List<EntityHandle> members = s.Army(7, 38, 26);
        List<Command> log = AiScenes.Log(s.Sim, 0);
        s.NextThink();
        s.NextThink();
        Vector2 target = log.First(c => c.Kind == CommandKind.AttackMove).Position;
        Assert.All(members, h => Assert.Contains(log, c => c.Unit == h && c.Position == target));
        Vector2 hall = s.Hall;
        Assert.Equal(MathF.Round(hall.Y), hall.Y);
        Vector2 north = new(hall.X + 60f, hall.Y - 8f), south = new(hall.X + 60f, hall.Y + 8f);
        w.Units.Position[members[0].Index] = southFirst ? south : north;
        w.Units.Position[members[1].Index] = southFirst ? north : south;
        for (int k = 2; k < members.Count; k++) w.Units.Position[members[k].Index] = new Vector2(hall.X + 70f + 2f * k, hall.Y);
        Assert.Equal(Vector2.DistanceSquared(w.Units.Position[members[0].Index], hall), Vector2.DistanceSquared(w.Units.Position[members[1].Index], hall));
        int mark = log.Count;
        Vector2 point = s.Raiders(1, hall + new Vector2(0f, 30f));
        // Set the tie on the tick the think runs at (it thinks first in the tick, before anything moves).
        int think = s.Sim.AiOf(0)!.ThinkTicks;
        while (s.Sim.TickNumber % think != 0) s.Sim.Tick();
        w.Units.Position[members[0].Index] = southFirst ? south : north;
        w.Units.Position[members[1].Index] = southFirst ? north : south;
        for (int k = 2; k < members.Count; k++) w.Units.Position[members[k].Index] = new Vector2(hall.X + 70f + 2f * k, hall.Y);
        s.Sim.Tick();
        List<EntityHandle> pulled = log.Skip(mark).Where(c => c.Position == point && members.Contains(c.Unit)).Select(c => c.Unit).Distinct().ToList();
        EntityHandle lower = members[0].Index < members[1].Index ? members[0] : members[1];
        _out.WriteLine($"pulled {string.Join(", ", pulled.Select(h => h.Index))}; tied {members[0].Index} / {members[1].Index}");
        Assert.Equal(new[] { lower }, pulled);
    }

    /// <summary>
    /// The M5-4f Blackboard state is outside <see cref="Simulation.StateHash"/> (docs/03: save state outside the hash, like
    /// M5-4b's marks): scribbling every new field (and the defense scratch) on both seats of a running AI-v-AI match leaves
    /// the hash as it was.
    /// </summary>
    [Fact]
    public void TheNewBlackboardFields_AreOutsideTheStateHash()
    {
        Simulation sim = AiScenes.Generated(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai), 4, record: false, out _, data: AiScenes.EarlyWaves(1), raiders: 4);
        for (int t = 0; t < 600; t++) sim.Tick();
        ulong before = sim.StateHash();
        for (int p = 0; p < 2; p++)
        {
            Blackboard b = sim.AiOf(p)!.Board;
            Array.Fill(b.DefenderGeneration, 12345);
            Array.Fill(b.DefenseOrder, 77);
            Array.Fill(b.DefenseKey, 3.5f);
            b.DefenseOrderCount = 99;
            b.DefendAnswer = 9e6f;
            b.DefendAll = !b.DefendAll;
            b.WaveMinHalfPop = -4f;
            b.WaveMinCost = 1e9f;
            b.WavePulls += 1000;
            b.WaveRecalls += 1000;
        }
        Assert.Equal(before, sim.StateHash());
    }
}
