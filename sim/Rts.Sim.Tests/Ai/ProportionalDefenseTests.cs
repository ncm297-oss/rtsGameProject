using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-4f (roadmap M5 criterion 7): <see cref="DefenseManager"/>'s answer sized by the threat's worth, on the flat scene
/// (combat off: the commands are what is checked). Player 0 (AI, malazan) has its hall at anchor (8, 30) and a billet in
/// sight of player 1's hall (so a wave has a target); a wave of 12 Heavy Infantry (12 pop) goes out with a start threshold
/// of 6 pop, then 3 Heavy Infantry stand at home. Enemies are player 1's Raiders, spotted for player 0 and holding. The
/// knobs are the tests' own (rule 12): defendRatio 1.5, defendMinValue 150, recallHallHp 0.5, defendRadius 48 m; costs come
/// from the data (a Heavy Infantry 54 + 20 = 74, a Raider 48 + 16 = 64).
/// </summary>
public class ProportionalDefenseTests
{
    private static int Heavy => TestSim.Data.FindUnit("malazan_heavy_infantry");

    private static readonly Lazy<GameData> s_data = new(() => AiScenes.DataWith(dir =>
    {
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            AiScenes.Opening(root)["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1));
            AiScenes.Opening(root)["attack"] = new JsonObject { ["minArmyPop"] = 6, ["minArmyCost"] = 0 };
            root["difficulties"]!["normal"]!["waveSizeMultiplier"] = 1;
        });
        dir.EditJson("common/rules.json", r =>
        {
            JsonNode ai = r["ai"]!;
            ai["waveCooldown"] = 60;
            ai["defendRadius"] = 48;
            ai["defendFor"] = 20;
            ai["workerFleeRadius"] = 16;
            ai["rallyReach"] = 6;
            ai["scoutAfter"] = 3600;
            ai["defendRatio"] = 1.5;
            ai["defendMinValue"] = 150;
            ai["recallHallHp"] = 0.5;
        });
    }));

    private static int Cost(GameData d, int type) => d.Units[type].CostGold + d.Units[type].CostWood;

    /// <summary>The scene with the wave out (12 members marching east, outside the radius) and 3 home units placed.</summary>
    private sealed class Scene
    {
        internal readonly Simulation Sim;
        internal readonly AiPlayer Ai;
        internal readonly List<EntityHandle> Members, Home;
        internal readonly EntityHandle Target;
        internal readonly List<Command> Log;

        internal Scene(int homeUnits = 3)
        {
            Sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), s_data.Value);
            BuildMaps.SetTotals(Sim, 0, 0, 0); // nothing trained meanwhile
            World w = Sim.World;
            Ai = Sim.AiOf(0)!;
            Assert.True(w.Buildings.Spawn(0, w.Data.FindBuilding("malazan_billet"), 31 * AiScenes.FlatSize + 47, out _));
            Members = Army(12, 38, 26);
            NextThink();
            Assert.True(Ai.Board.WaveActive, "the wave did not start");
            Target = Ai.Board.WaveTarget;
            NextThink();
            foreach (EntityHandle h in Members)
                Assert.True(Vector2.Distance(w.Units.Position[h.Index], Hall) > Ai.Rules.DefendRadius, "a member is inside the base");
            Home = Army(homeUnits, 12, 38);
            Log = AiScenes.Log(Sim, 0);
        }

        internal Vector2 Hall => Ai.HallPoint;

        internal List<EntityHandle> Army(int n, int x, int y)
        {
            var list = new List<EntityHandle>();
            for (int k = 0; k < n; k++) list.Add(CombatScenes.Place(Sim, 0, Heavy, Sim.World.NavGrid.CellCenter(x + k % 6, y + k / 6)));
            return list;
        }

        /// <summary>Player 1's Raiders at <paramref name="at"/> (1 m apart along x), spotted by player 0, holding.</summary>
        internal void Raiders(int n, Vector2 at)
        {
            for (int k = 0; k < n; k++)
            {
                EntityHandle h = CombatScenes.Place(Sim, 1, CombatScenes.Raider, at + new Vector2(k, 0f));
                CombatScenes.Spot(Sim, 0, h);
                Sim.World.Units.Hold[h.Index] = true;
            }
        }

        internal void NextThink()
        {
            int think = Ai.ThinkTicks;
            do Sim.Tick(); while ((Sim.TickNumber - 1) % think != 0);
        }

        /// <summary>The army units sent to the defense point (an AttackMove or a Move there; fleeing workers aside), in log order.</summary>
        internal List<EntityHandle> Defenders() =>
            Log.Where(c => c.Kind is CommandKind.AttackMove or CommandKind.Move && c.Position == Ai.Board.DefensePoint && Sim.World.Units.TypeId[c.Unit.Index] == Heavy)
                .Select(c => c.Unit).Distinct().ToList();

        /// <summary>The members sent a Move to the rally point (the wave's end).</summary>
        internal List<EntityHandle> SentHome() =>
            Log.Where(c => c.Kind == CommandKind.Move && c.Position == Ai.Board.Rally && Members.Contains(c.Unit)).Select(c => c.Unit).Distinct().ToList();

        internal EntityHandle HallHandle
        {
            get
            {
                (int x, int y) = AiScenes.HallAt(0);
                return Sim.World.Buildings.HandleOf(Sim.World.Buildings.SlotAt(x, y));
            }
        }
    }

    /// <summary>
    /// One Raider (worth 64; answer max(1.5 x 64, 150) = 150) 30 m from the hall with the wave out: the wave goes on (active,
    /// the same target, no member sent home or to the point) and two home units answer (74 + 74 = 148, within half a unit of
    /// 150), the two nearest the raider, over six thinks.
    /// </summary>
    [Fact]
    public void OneRaider_TheWaveGoesOn_AndTwoHomeUnitsAnswer()
    {
        var s = new Scene();
        s.Raiders(1, s.Hall + new Vector2(0f, 30f));
        for (int k = 0; k < 6; k++) s.NextThink();
        Assert.True(s.Ai.Board.Defending);
        Assert.True(s.Ai.Board.WaveActive);
        Assert.Equal(s.Target, s.Ai.Board.WaveTarget);
        List<EntityHandle> defenders = s.Defenders();
        Assert.Equal(2, defenders.Count);
        Assert.All(defenders, h => Assert.Contains(h, s.Home));
        Assert.Empty(s.SentHome());
        Assert.DoesNotContain(s.Log, c => s.Members.Contains(c.Unit) && c.Position == s.Ai.Board.DefensePoint);
        Assert.All(s.Log.Where(c => defenders.Contains(c.Unit) && c.Position == s.Ai.Board.DefensePoint), c => Assert.Equal(CommandKind.AttackMove, c.Kind));
        // The two nearest the raider (the point): the home rows are the same distance up, so the nearest along x.
        World w = s.Sim.World;
        List<EntityHandle> nearest = s.Home.OrderBy(h => Vector2.DistanceSquared(w.Units.Position[h.Index], s.Ai.Board.DefensePoint)).ThenBy(h => h.Index).Take(2).ToList();
        Assert.Equal(nearest.OrderBy(h => h.Index), defenders.OrderBy(h => h.Index));
        Assert.Equal(0, s.Ai.Board.WavePulls);
        Assert.Equal(0, s.Ai.Board.WaveRecalls);
    }

    /// <summary>
    /// Eight Raiders (worth 512, more than the 3 home units' 222; answer 768): every home unit answers and part of the wave
    /// comes home: the members nearest the hall, six of them (444), the most the wave can give and keep its 6-pop start
    /// threshold (222 + 444 = 666 covers the threat's 512, so the wave isn't recalled). The pulled get a Move to the point
    /// (outside the base, BUG-0670); the other six get nothing from the defense and the wave goes on with its target.
    /// </summary>
    [Fact]
    public void EightRaiders_PartOfTheWaveComesHome_TheRestKeepTheStartThreshold()
    {
        var s = new Scene();
        GameData d = s.Sim.World.Data;
        World w = s.Sim.World;
        s.Raiders(8, s.Hall + new Vector2(-4f, 30f));
        Assert.True(8 * Cost(d, CombatScenes.Raider) > s.Home.Count * Cost(d, Heavy), "the raid must outweigh the home army");
        var memberDistance = s.Members.ToDictionary(h => h, h => Vector2.DistanceSquared(w.Units.Position[h.Index], s.Hall));
        s.NextThink();
        Assert.True(s.Ai.Board.WaveActive);
        Assert.Equal(s.Target, s.Ai.Board.WaveTarget);
        List<EntityHandle> defenders = s.Defenders();
        List<EntityHandle> pulled = defenders.Where(s.Members.Contains).ToList();
        Assert.Equal(s.Home.OrderBy(h => h.Index), defenders.Where(s.Home.Contains).OrderBy(h => h.Index));
        Assert.Equal(6, pulled.Count);
        Assert.All(s.Log.Where(c => pulled.Contains(c.Unit)), c => Assert.Equal(CommandKind.Move, c.Kind));
        // The pulled are the six nearest the hall (as the think saw them; ties by slot).
        List<EntityHandle> nearest = s.Members.OrderBy(h => memberDistance[h]).ThenBy(h => h.Index).Take(6).ToList();
        Assert.Equal(nearest.OrderBy(h => h.Index), pulled.OrderBy(h => h.Index));
        // The rest keep the threshold: six Heavy Infantry, 12 half-pop, the 6 pop the wave was sent at.
        int left = s.Members.Where(h => !pulled.Contains(h)).Sum(h => d.Units[w.Units.TypeId[h.Index]].HalfPop);
        Assert.True(left >= s.Ai.Board.WaveMinHalfPop, $"{left} half-pop left, the threshold is {s.Ai.Board.WaveMinHalfPop}");
        Assert.Empty(s.SentHome());
        for (int k = 0; k < 5; k++) s.NextThink();
        Assert.True(s.Ai.Board.WaveActive, "the wave ended after the pull");
        Assert.Empty(s.SentHome());
        Assert.Equal(9, s.Defenders().Count);
        Assert.Equal(6, s.Ai.Board.WavePulls);
    }

    /// <summary>
    /// Twelve Raiders (worth 768) outweigh everything the AI may send without breaking the wave (222 + 444 = 666): the whole
    /// wave comes home (every member a Move to the rally, the wave over, the cooldown set) and every home unit answers.
    /// </summary>
    [Fact]
    public void ARaidWorthMoreThanTheHomeArmyAndTheSpareMembers_CallsTheWholeWave()
    {
        var s = new Scene();
        s.Raiders(12, s.Hall + new Vector2(-6f, 30f));
        s.NextThink();
        int at = s.Sim.TickNumber - 1;
        Assert.False(s.Ai.Board.WaveActive);
        Assert.Equal(at + s.Ai.Rules.WaveCooldownTicks, s.Ai.Board.WaveCooldownUntil);
        Assert.Equal(s.Members.OrderBy(h => h.Index), s.SentHome().OrderBy(h => h.Index));
        Assert.Equal(s.Home.OrderBy(h => h.Index), s.Defenders().OrderBy(h => h.Index));
        Assert.Equal(1, s.Ai.Board.WaveRecalls);
        // Then every member answers too (the Move to the point from outside the base, BUG-0670).
        for (int k = 0; k < 3; k++) s.NextThink();
        Assert.Equal(s.Home.Concat(s.Members).OrderBy(h => h.Index), s.Defenders().OrderBy(h => h.Index));
    }

    /// <summary>
    /// The hall hit and left below recallHallHp (0.5) of its max hp, nobody seen: the whole wave comes home and every home
    /// unit answers, though the threat counts only the minimum. Hit and left at 0.6, the answer is the minimum only.
    /// </summary>
    [Theory]
    [InlineData(0.4, true)]
    [InlineData(0.6, false)]
    public void TheHallHit_BelowRecallHallHp_CallsTheWholeWave(double hpLeft, bool recalled)
    {
        var s = new Scene();
        World w = s.Sim.World;
        EntityHandle hall = s.HallHandle;
        int max = w.Data.Buildings[w.Buildings.TypeId[hall.Index]].Hp;
        w.Buildings.SetHp(hall.Index, (int)(max * hpLeft));
        s.NextThink();
        Assert.True(s.Ai.Board.Defending);
        Assert.Equal(!recalled, s.Ai.Board.WaveActive);
        Assert.Equal(recalled ? s.Members.OrderBy(h => h.Index) : Enumerable.Empty<EntityHandle>(), s.SentHome().OrderBy(h => h.Index));
        Assert.Equal(recalled ? 3 : 2, s.Defenders().Count);
        Assert.All(s.Defenders(), h => Assert.Contains(h, s.Home));
    }

    /// <summary>
    /// A hit on an own building near the hall with nobody seen (a catapult in the fog): the minimum answer only (150: two
    /// home units, the two nearest the building), the wave goes on, over six thinks of the hold.
    /// </summary>
    [Fact]
    public void AHitWithNobodySeen_GetsTheMinimumAnswerOnly()
    {
        var s = new Scene();
        World w = s.Sim.World;
        int billetType = w.Data.FindBuilding("malazan_billet");
        Assert.True(w.Buildings.Spawn(0, billetType, 38 * AiScenes.FlatSize + 16, out EntityHandle billet));
        s.NextThink(); // its hp is kept
        Assert.False(s.Ai.Board.Defending);
        w.Buildings.SetHp(billet.Index, w.Buildings.Hp[billet.Index] - 10);
        for (int k = 0; k < 6; k++) s.NextThink();
        Assert.True(s.Ai.Board.Defending);
        Assert.True(s.Ai.Board.WaveActive);
        Assert.Equal(s.Target, s.Ai.Board.WaveTarget);
        Assert.Empty(s.SentHome());
        Vector2 point = s.Ai.BuildingCentre(billetType, w.Buildings.Cell[billet.Index]);
        Assert.Equal(point, s.Ai.Board.DefensePoint);
        List<EntityHandle> defenders = s.Defenders();
        List<EntityHandle> nearest = s.Home.OrderBy(h => Vector2.DistanceSquared(w.Units.Position[h.Index], point)).ThenBy(h => h.Index).Take(2).ToList();
        Assert.Equal(nearest.OrderBy(h => h.Index), defenders.OrderBy(h => h.Index));
        Assert.Equal(150f, s.Ai.Board.DefendAnswer);
    }

    /// <summary>
    /// One home unit is short of the answer to one Raider (74 of 150), so the member nearest the hall is pulled to make it
    /// up (148); a unit fresh from the hall then stays out (the answer is met). The home defender falls: the next unit fresh
    /// from the hall joins in its place while the threat holds (units from the halls keep joining, up to the answer).
    /// </summary>
    [Fact]
    public void NewUnits_JoinTheDefense_UpToTheAnswer()
    {
        var s = new Scene(homeUnits: 1);
        World w = s.Sim.World;
        EntityHandle nearestMember = s.Members.OrderBy(h => Vector2.DistanceSquared(w.Units.Position[h.Index], s.Hall)).ThenBy(h => h.Index).First();
        s.Raiders(1, s.Hall + new Vector2(0f, 30f));
        s.NextThink();
        Assert.Equal(new[] { s.Home[0], nearestMember }.OrderBy(h => h.Index), s.Defenders().OrderBy(h => h.Index));
        Assert.Equal(1, s.Ai.Board.WavePulls);
        EntityHandle idle = s.Army(1, 12, 40)[0];
        for (int k = 0; k < 2; k++) s.NextThink();
        Assert.DoesNotContain(idle, s.Defenders());
        w.Units.Free(s.Home[0]);
        s.NextThink();
        Assert.Contains(idle, s.Defenders());
        Assert.True(s.Ai.Board.WaveActive);
        Assert.Equal(1, s.Ai.Board.WavePulls);
    }

    /// <summary>The same scene twice gives the same commands (the picks and their ties are deterministic).</summary>
    [Fact]
    public void ThePicks_AreDeterministic()
    {
        List<Command> Run()
        {
            var s = new Scene();
            s.Raiders(8, s.Hall + new Vector2(-4f, 30f));
            for (int k = 0; k < 4; k++) s.NextThink();
            return s.Log;
        }
        Assert.Equal(Run(), Run());
    }
}
