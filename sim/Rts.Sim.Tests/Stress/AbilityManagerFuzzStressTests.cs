using System.Diagnostics;
using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// QA M5-4c (session 2026-10-13-1616): the AI's ability manager under seeded random scenes, checked by an oracle at every
/// cast it sends, a four-seat casting match's hash twin, and the cost of a think while the AI's own Sandstorms block vision
/// (the shipped data).
/// </summary>
[Collection(SerialCollection.Name)]
public class AbilityManagerFuzzStressTests
{
    private readonly ITestOutputHelper _out;

    public AbilityManagerFuzzStressTests(ITestOutputHelper output) => _out = output;

    private static readonly Lazy<GameData>[] s_data =
    {
        new(() => AbilityManagerQaTests.Data(castMinTargets: 1)),
        new(() => AbilityManagerQaTests.Data(castMinTargets: 2)),
        new(() => AbilityManagerQaTests.Data(castMinTargets: 3)),
    };

    private sealed class Tally
    {
        public int Scenes, Casts, AtBuildings, Landed, Dropped, Thinks;
        public override string ToString() => $"{Scenes} scenes, {Thinks} thinks, {Casts} casts ({AtBuildings} at a building), {Landed} landed, {Dropped} dropped";
    }

    /// <summary>
    /// Random scenes: an AI seat (either faction, Easy / Normal / Hard, castMinTargets 1-3) with 1-6 casters by its rally
    /// point, 0-14 enemy units in 1-3 groups (some never spotted), 0-4 own units among them, maybe an enemy building; the enemies
    /// wander. Every UseAbility the AI sends is checked when sent: an own live caster, not casting, the ability listed and
    /// ready by the tick it applies in, at most one per caster a think; the point on the map and within range; at least
    /// castMinTargets enemy units the AI sees within the radius (or, for an ability that hits buildings, a seen enemy
    /// building's footprint within it); for a friendly-fire ability no own unit within the radius; a seat whose difficulty has useAbilities false never casts.
    /// The same scene twice hashes equal after every tick.
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData(1, 40)]
    [InlineData(1001, 40)]
    [InlineData(2001, 40)]
    [InlineData(5001, 200)]
    public void RandomScenes_EveryCastObeysTheTargetRule(int firstSeed, int scenes)
    {
        var tally = new Tally();
        for (int seed = firstSeed; seed < firstSeed + scenes; seed++)
        {
            ulong hashA = RunScene(seed, tally, check: true, out var hashesA);
            ulong hashB = RunScene(seed, new Tally(), check: false, out var hashesB);
            Assert.Equal(hashesA.Count, hashesB.Count);
            for (int t = 0; t < hashesA.Count; t++)
                Assert.True(hashesA[t] == hashesB[t], $"seed {seed}: the twins differ after tick {t}");
            Assert.Equal(hashA, hashB);
            tally.Scenes++;
        }
        _out.WriteLine(tally.ToString());
        Assert.True(tally.Casts > scenes, $"too few casts to mean anything: {tally}");
        Assert.True(tally.AtBuildings > 0, $"no cast at a building: {tally}");
    }

    private static ulong RunScene(int seed, Tally tally, bool check, out List<ulong> hashes)
    {
        var rng = new Random(seed);
        GameData data = s_data[rng.Next(3)].Value;
        int faction = rng.Next(2);
        AiDifficulty difficulty = (AiDifficulty)rng.Next(3);
        Simulation sim = AbilityManagerQaTests.Scene(data, difficulty, faction, combat: rng.Next(2) == 0);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        Vector2 rally = ai.Board.Rally;
        int min = ai.Rules.CastMinTargets;
        float mapSize = AiScenes.FlatSize * Map.MapConstants.CellSize;
        Vector2 Clamp(Vector2 p) => Vector2.Clamp(p, new Vector2(3f), new Vector2(mapSize - 3f));
        int[] casterTypes = data.Factions[faction].Units.Where(u => data.Units[u].Abilities.Length > 0).ToArray();
        int[] enemyTypes = data.Factions[1 - faction].Units.Where(u => data.Units[u].Slot != UnitSlot.Worker).ToArray();
        int[] ownTypes = data.Factions[faction].Units.Where(u => data.Units[u].Slot != UnitSlot.Worker && data.Units[u].Abilities.Length == 0).ToArray();
        int casters = 1 + rng.Next(6);
        for (int k = 0; k < casters; k++)
            CombatScenes.Place(sim, 0, casterTypes[rng.Next(casterTypes.Length)], Clamp(rally + new Vector2(rng.Next(-30, 31) / 10f, rng.Next(-30, 31) / 10f)));
        var enemies = new List<EntityHandle>();
        int groups = 1 + rng.Next(3);
        for (int g = 0; g < groups; g++)
        {
            float angle = rng.Next(360) * MathF.PI / 180f, dist = rng.Next(0, 29), spread = rng.Next(0, 50) / 10f;
            Vector2 at = rally + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * dist;
            int n = rng.Next(0, 6);
            for (int k = 0; k < n; k++)
            {
                EntityHandle h = CombatScenes.Place(sim, 1, enemyTypes[rng.Next(enemyTypes.Length)], Clamp(at + new Vector2((float)(rng.NextDouble() - 0.5) * spread, (float)(rng.NextDouble() - 0.5) * spread)));
                if (rng.Next(10) < 7) CombatScenes.Spot(sim, 0, h);
                enemies.Add(h);
            }
            if (rng.Next(3) == 0)
                CombatScenes.Place(sim, 0, ownTypes[rng.Next(ownTypes.Length)], Clamp(at + new Vector2(rng.Next(-20, 21) / 10f, rng.Next(-20, 21) / 10f)));
        }
        if (rng.Next(2) == 0)
        {
            // An enemy building 4-14 m from the rally point (a house of the other faction).
            int house = Rts.Sim.ViewApi.StartBase.BuildingOfSlot(data, 1 - faction, BuildingSlot.House);
            int cx = (int)(rally.X / 2f) + rng.Next(-7, 8), cy = (int)(rally.Y / 2f) + (rng.Next(2) == 0 ? -1 : 1) * rng.Next(2, 8);
            w.Buildings.Spawn(1, house, cy * AiScenes.FlatSize + cx, out _);
            w.Fog.Update();
        }
        var sentThisThink = new HashSet<int>();
        int lastStamp = -1;
        if (check)
            ai.SentForTests = c =>
            {
                if (c.Kind != CommandKind.UseAbility) return;
                Assert.True(ai.Knobs.UseAbilities, $"seed {seed}: a {difficulty} seat with useAbilities false cast");
                string at = $"seed {seed} tick {sim.TickNumber}: UseAbility {c.Unit} #{c.TypeId} at {c.Position}";
                if (c.Tick != lastStamp) { sentThisThink.Clear(); lastStamp = c.Tick; }
                UnitStore u = w.Units;
                int i = c.Unit.Index;
                Assert.True(u.IsAlive(c.Unit) && u.Owner[i] == 0, $"{at}: not an own live unit");
                Assert.True(sentThisThink.Add(i), $"{at}: a second cast for one caster in a think");
                UnitDef def = data.Units[u.TypeId[i]];
                Assert.True((uint)c.TypeId < (uint)def.Abilities.Length, $"{at}: an ability its type lacks");
                Assert.True(u.CastAbility[i] < 0 && u.State[i] != UnitState.Casting, $"{at}: already casting");
                Assert.True(u.AbilityReadyTick[i * DataLimits.MaxUnitAbilities + c.TypeId] <= c.Tick, $"{at}: on cooldown");
                AbilityDef a = data.Abilities[def.Abilities[c.TypeId]];
                Assert.True(w.NavGrid.WorldToCell(c.Position, out _, out _), $"{at}: off the map");
                Vector2 from = u.Position[i];
                bool atCaster = a.Kind is AbilityKind.SelfAura or AbilityKind.Summon;
                Vector2 point = atCaster ? from : c.Position;
                if (!atCaster) Assert.True(Vector2.Distance(from, c.Position) <= a.Range + 1e-3f, $"{at}: {Vector2.Distance(from, c.Position):F3} m from a caster of range {a.Range}");
                float r2 = a.Radius * a.Radius;
                int seen = 0;
                bool own = false;
                for (int j = 0; j < u.Capacity; j++)
                {
                    if (!u.Alive[j]) continue;
                    if (Vector2.DistanceSquared(u.Position[j], point) > r2) continue;
                    if (u.Owner[j] == 0) own = true;
                    else if (w.Fog.CanSeeUnit(0, j)) seen++;
                }
                bool building = false;
                if (seen < min)
                {
                    Assert.True(a.HitsBuildings, $"{at}: {seen} enemies seen in its radius, castMinTargets {min}");
                    for (int b = 0; b < w.Buildings.Capacity; b++)
                        if (w.Buildings.Alive[b] && w.Buildings.Owner[b] != 0 && w.Fog.CanSeeBuilding(0, b) && CombatSystem.BuildingDistanceSquared(w, b, point) <= r2) building = true;
                    Assert.True(building, $"{at}: no seen enemy building in its radius, {seen} enemies");
                    tally.AtBuildings++;
                }
                bool friendly = a.Affects == AbilityAffects.AllUnits || a.Effects.Any(fx => fx.Kind == AbilityEffectKind.Damage && fx.FriendlyFire > 0f);
                Assert.False(friendly && own, $"{at}: a friendly-fire ability with an own unit in its radius");
                tally.Casts++;
            };
        hashes = new List<ulong>();
        int thinks = 14;
        int until = sim.TickNumber + thinks * ai.ThinkTicks;
        while (sim.TickNumber < until)
        {
            if (sim.TickNumber % 40 == 0)
                foreach (EntityHandle e in enemies)
                    if (w.Units.IsAlive(e) && rng.Next(4) == 0)
                        sim.Enqueue(Command.Move(1, e, Clamp(w.Units.Position[e.Index] + new Vector2(rng.Next(-60, 61) / 10f, rng.Next(-60, 61) / 10f))));
            sim.Tick();
            hashes.Add(sim.StateHash());
            foreach (Abilities.AbilityEvent e in w.AbilityEvents)
                if (e.Owner == 0) { if (e.Resolved) tally.Landed++; if (e.Dropped) tally.Dropped++; }
        }
        tally.Thinks += thinks;
        return sim.StateHash();
    }

    /// <summary>
    /// Four Normal AI seats (both factions twice), every seat with two of each of its caster types in the next seat's base,
    /// combat on, 3,000 ticks: two runs hash equal after every tick; every seat casts; and no seat's sends run away from its
    /// landed casts (a re-send loop would show as sends far above lands).
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void FourCastingSeats_HashTwins_AndSendsStayNearLands()
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai, SeatController.Ai, SeatController.Ai);
        Simulation a = AiScenes.Generated(seats, 9, record: false, out _, units: 600, casters: 2);
        Simulation b = AiScenes.Generated(seats, 9, record: false, out _, units: 600, casters: 2);
        var landed = new int[4];
        var dropped = new int[4];
        for (int t = 0; t < 3000; t++)
        {
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"the runs differ after tick {a.TickNumber}");
            foreach (Abilities.AbilityEvent e in a.World.AbilityEvents)
            {
                if (e.Resolved) landed[e.Owner]++;
                if (e.Dropped) dropped[e.Owner]++;
            }
        }
        for (int p = 0; p < 4; p++)
        {
            int sent = a.AiOf(p)!.Board.CastsSent;
            _out.WriteLine($"seat {p}: {sent} casts sent, {landed[p]} landed, {dropped[p]} dropped; defeated {a.World.IsDefeated(p)}");
            Assert.Equal(sent, b.AiOf(p)!.Board.CastsSent);
            Assert.True(sent > 0, $"seat {p} never cast");
            Assert.True(sent <= 2 * landed[p] + 10, $"seat {p}: {sent} sent for {landed[p]} landed");
        }
    }

    /// <summary>
    /// Measurement (no gate on the think cost; reported): the cost of an AI think with N live Sandstorms of the shipped data
    /// (they block vision). Two Normal AI seats on a generated map with 600 units; storms of a third, human, player are put
    /// down across the map by its Priests (cooldowns cleared), then the AI seats' thinks are timed. Asserts only that the
    /// storms are alive. The numbers go in the QA report (BUG-0702).
    /// </summary>
    [Theory]
    [Trait("Category", "Perf")]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(24)]
    public void AThink_WithShippedSandstormsAlive_Measured(int storms)
    {
        PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Hard), new(1, SeatController.Ai, AiDifficulty.Hard), new(1, SeatController.Human, AiDifficulty.Hard) };
        Simulation sim = AiScenes.Generated(seats, 5, record: false, out _, units: 2600, raiders: 0);
        for (int t = 0; t < 400; t++) sim.Tick();
        World w = sim.World;
        GameData d = w.Data;
        int priest = d.FindUnit("whirlwind_priest");
        AbilityDef storm = AbilityManagerQaTests.Ability(d, "sandstorm");
        // Fill to ~2,500 units: line units of seats 0 and 1 spread over the map (the census walks every slot).
        var rng = new Random(5);
        float size = w.NavGrid.Width * Map.MapConstants.CellSize;
        int line0 = Rts.Sim.ViewApi.StartBase.UnitOfSlot(d, d.Factions[0].Id, UnitSlot.Line), line1 = Rts.Sim.ViewApi.StartBase.UnitOfSlot(d, d.Factions[1].Id, UnitSlot.Line);
        for (int k = 0; w.Units.Count < 2400 && k < 20000; k++)
        {
            Vector2 p = new(8f + (float)rng.NextDouble() * (size - 16f), 8f + (float)rng.NextDouble() * (size - 16f));
            if (!w.NavGrid.WorldToCell(p, out int cx, out int cy) || !w.NavGrid.IsPassable(cx, cy)) continue;
            w.Units.TrySpawn(k % 2, k % 2 == 0 ? line0 : line1, d.Units[k % 2 == 0 ? line0 : line1], p, out _);
        }
        for (int k = 0; k < storms; k++)
        {
            Vector2 p;
            int cx, cy;
            do p = new(10f + (float)rng.NextDouble() * (size - 20f), 10f + (float)rng.NextDouble() * (size - 20f));
            while (!w.NavGrid.WorldToCell(p, out cx, out cy) || !w.NavGrid.IsPassable(cx, cy));
            Assert.True(w.Units.TrySpawn(2, priest, d.Units[priest], p, out EntityHandle h));
            sim.Enqueue(Command.UseAbility(2, h, 0, p + new Vector2(1f, 0f)));
        }
        for (int t = 0; t < storm.CastTicks + 4; t++) sim.Tick();
        Assert.Equal(storms, w.Zones.BlockerCount);
        AiPlayer[] ais = { sim.AiOf(0)!, sim.AiOf(1)! };
        var sw = new Stopwatch();
        var times = new List<double>();
        // Only real thinks are timed (Think() is a no-op off its tick), and only while every storm still lives (12 s).
        int thinks = 0;
        while (thinks < 14 && w.Zones.BlockerCount == storms)
        {
            foreach (AiPlayer ai in ais)
            {
                sw.Restart();
                bool thought = ai.Think();
                sw.Stop();
                if (thought && thinks >= 2) times.Add(sw.Elapsed.TotalMilliseconds);
                if (thought && ai == ais[1]) thinks++;
            }
            sim.Tick();
        }
        Assert.True(times.Count >= 10, $"{times.Count} thinks timed");
        _out.WriteLine($"{storms} storms, {w.Units.Count} units: think avg {times.Average():F3} ms, worst {times.Max():F3} ms; blockers {w.Zones.BlockerCount}");
    }

    private static GameData WaveData(int castMinTargets) => AiScenes.DataWith(dir =>
    {
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            root["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1));
            root["attack"] = new JsonObject { ["minArmyPop"] = 1, ["minArmyCost"] = 0 };
            root["difficulties"]!["normal"]!["waveSizeMultiplier"] = 1;
        });
        dir.EditJson("common/rules.json", r =>
        {
            r["ai"]!["scoutAfter"] = 3600;
            r["ai"]!["defendRadius"] = 8;
            r["ai"]!["castMinTargets"] = castMinTargets;
        });
    });

    /// <summary>
    /// A wave with Cadre Mages in it meets an enemy group in the field and the fight stands still (every unit is healed every
    /// tick). Casting must not make the mages leave the wave: with casting on (castMinTargets 2) the mages are sent home out
    /// of the wave no more often than with it off (castMinTargets 50: no cast is ever possible). Reported: casts, Moves home.
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void AWaveWithMages_InAStandingFight_CastingDoesNotSendThemHome()
    {
        (int casts, int home, int leftTicks) on = WaveRun(WaveData(2));
        (int casts, int home, int leftTicks) off = WaveRun(WaveData(50));
        _out.WriteLine($"casting on: {on.casts} casts, {on.home} Moves home to mages, {on.leftTicks} mage-ticks out of the active wave; off: {off.casts} casts, {off.home} Moves home, {off.leftTicks} mage-ticks out");
        Assert.True(on.casts > 0, "no cast in the casting run");
        Assert.Equal(0, off.casts);
        Assert.True(on.home <= off.home, $"casting sent the mages home {on.home} times, {off.home} without");
    }

    private (int Casts, int Home, int LeftTicks) WaveRun(GameData data)
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), data, combat: true);
        World w = sim.World;
        Assert.True(w.Buildings.Spawn(0, data.FindBuilding("malazan_billet"), 31 * AiScenes.FlatSize + 47, out _));
        int mageType = data.FindUnit("malazan_cadre_mage"), heavy = data.FindUnit("malazan_heavy_infantry");
        int line1 = Rts.Sim.ViewApi.StartBase.UnitOfSlot(data, data.Factions[1].Id, UnitSlot.Line);
        var mages = new List<EntityHandle>();
        for (int k = 0; k < 3; k++) mages.Add(CombatScenes.Place(sim, 0, mageType, w.NavGrid.CellCenter(14 + k, 27)));
        for (int k = 0; k < 3; k++) CombatScenes.Place(sim, 0, heavy, w.NavGrid.CellCenter(14 + k, 33));
        for (int k = 0; k < 8; k++) CombatScenes.Place(sim, 1, line1, w.NavGrid.CellCenter(29 + k % 4, 29 + k / 4));
        AiPlayer ai = sim.AiOf(0)!;
        int casts = 0, home = 0, left = 0;
        ai.SentForTests = c =>
        {
            if (c.Kind == CommandKind.UseAbility) casts++;
            if (c.Kind == CommandKind.Move && mages.Contains(c.Unit) && Vector2.Distance(c.Position, ai.Board.Rally) < 0.01f) home++;
        };
        for (int t = 0; t < 4000; t++)
        {
            UnitStore u = w.Units;
            for (int i = 0; i < u.Capacity; i++)
                if (u.Alive[i]) u.Hp[i] = data.Units[u.TypeId[i]].Hp;
            sim.Tick();
            if (ai.Board.WaveActive)
                foreach (EntityHandle m in mages)
                    if (ai.Board.WaveGeneration[m.Index] != m.Generation) left++;
        }
        Assert.True(ai.Board.WavesStarted > 0, "no wave started");
        return (casts, home, left);
    }
}
