using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using static Rts.Sim.Tests.HitBonusScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// QA (M8-3, session 2026-11-18-0926): attacks on the first shot from stealth and the pack bonus beyond the developer's
/// scene rows. A shot whose shooter's slot was freed and reused by a new hidden unit; a second shot in flight when the first
/// lands (a tick later, and in the same tick); a lob with splash and friendly fire; a melee splash; a shot on a building
/// splashing a unit; a ranged pack unit whose mate dies in the same phase 11 before its shot lands (and a melee one, whose
/// count was taken at the strike); the pack count against a brute-force count across spatial-grid bucket borders, with the
/// 0.75 m Teblor War-dog, after a step of movement since the grid was built, in a 2v2; and the rounding of stacked factors
/// against a hand calculation. On the <see cref="HitBonusScenes"/> copy plus the rows below (test data only).
/// </summary>
public class HitBonusQaTests
{
    // ---------- the QA copy ----------

    /// <summary>
    /// The developer's copy plus: the Malazan Sapper (a lob, splash 2 m, friendly fire) with <c>fromStealthBonus</c> 0.5; the
    /// Crossbowman with a <c>nearbyOwn</c> passive counting Crossbowmen (a ranged pack unit, 8 m, +10 %, at most 3).
    /// </summary>
    private static GameData Qa => s_qa.Value;

    private static readonly Lazy<GameData> s_qa = new(() => AiScenes.DataWith(dir =>
    {
        Edit(dir);
        dir.SetUnitField("malazan", "malazan_sapper", "attack.fromStealthBonus", "0.5");
        AddPack(dir, "malazan", "malazan_crossbowman");
        AddPack(dir, "teblor", "teblor_warrior"); // the 0.75 m radius class
    }));

    /// <summary>The copy with the Heavy Infantry's melee given a 1.5 m splash (none ships; the Q1 ordering).</summary>
    private static GameData MeleeSplash => s_meleeSplash.Value;

    private static readonly Lazy<GameData> s_meleeSplash = new(() => AiScenes.DataWith(dir =>
    {
        Edit(dir);
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "attack.splash", "1.5");
    }));

    /// <summary>The copy with the Heavy Infantry's attack value 45 (45 x 1.3 = 58.5 exactly, a rounding edge).</summary>
    private static GameData Value45 => s_value45.Value;

    private static readonly Lazy<GameData> s_value45 = new(() => AiScenes.DataWith(dir =>
    {
        Edit(dir);
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "attack.value", "45");
    }));

    private static void AddPack(TestDataDir dir, string faction, string unit) =>
        dir.EditJson($"factions/{faction}/units.json", root =>
        {
            foreach (JsonNode? row in root["units"]!.AsArray())
            {
                if ((string?)row!["id"] != unit) continue;
                if (row["passives"] is not JsonArray list) row["passives"] = list = new JsonArray();
                BloodOilScenes.RemoveWhere(list, n => (string?)n!["trigger"] == "nearbyOwn");
                list.Add(Pack(unit));
            }
        });

    private static int Damage(Simulation sim, EntityHandle victim, Action tick)
    {
        int before = sim.World.Units.Hp[victim.Index];
        tick();
        return before - sim.World.Units.Hp[victim.Index];
    }

    // ---------- the shooter's slot freed and reused ----------

    /// <summary>
    /// A hidden Javelin Thrower (12 pierce x 1.25 = 15 plain, 23 from stealth) throws, dies with the javelin in the air, and a
    /// new Javelin Thrower of the same owner takes its slot at once (the free list is a stack) and is hidden too. The old
    /// javelin lands plain (its handle's generation is stale: neither the new occupant's stealth nor its type's field may
    /// reach it), and the landing does not reveal the new occupant.
    /// </summary>
    [Fact]
    public void AShotWhoseShooterSlotWasReused_LandsPlain_AndDoesNotRevealTheNewOccupant()
    {
        Simulation sim = Scene(data: Qa);
        World w = sim.World;
        EntityHandle a = Place(sim, 2, "teblor_javelin_thrower", At(sim, 10, 20));
        EntityHandle t = Dummy(sim, 0, At(sim, 15, 20));
        Hide(sim, a);
        sim.Enqueue(Command.Attack(2, a, t, false));
        Assert.True(CombatScenes.RunUntil(sim, () => w.Projectiles.Count > 0, 200) < 200);
        w.Units.Free(a);
        EntityHandle b = Place(sim, 2, "teblor_javelin_thrower", At(sim, 40, 40)); // far from everything: it never throws
        Assert.Equal(a.Index, b.Index);
        Assert.NotEqual(a.Generation, b.Generation);
        Hide(sim, b);
        Assert.True(w.IsStealthed(b.Index));
        Assert.Equal(1.5f, HitFactor.Of(w, b.Index));
        Assert.Equal(1f, CombatSystem.AttackerFactor(w, a));
        int landed = 0;
        for (int k = 0; k < 40 && landed == 0; k++) landed = Damage(sim, t, () => sim.Tick());
        Assert.Equal(15, landed);
        Assert.True(w.IsStealthed(b.Index)); // the old javelin is not the new unit's attack
    }

    // ---------- a second shot in flight ----------

    /// <summary>
    /// A hidden Javelin Thrower's javelin is in the air; a second one is thrown from the same unit (a manual
    /// <see cref="ProjectileSystem.Fire"/>, standing in for any shot already in flight) <paramref name="laterBy"/> ticks
    /// later. The first landing carries the bonus and reveals the thrower; the second lands plain. With 0 both land in the
    /// same tick: slot order, so 23 + 15 that tick.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void ASecondShotInFlight_WhenTheFirstLands_IsPlain(int laterBy)
    {
        Simulation sim = Scene(data: Qa);
        World w = sim.World;
        EntityHandle a = Place(sim, 2, "teblor_javelin_thrower", At(sim, 10, 20));
        EntityHandle t = Dummy(sim, 0, At(sim, 15, 20));
        Hide(sim, a);
        sim.Enqueue(Command.Attack(2, a, t, false));
        Assert.True(CombatScenes.RunUntil(sim, () => w.Projectiles.Count > 0, 200) < 200);
        for (int k = 0; k < laterBy; k++) sim.Tick();
        Assert.True(w.IsStealthed(a.Index));
        ProjectileSystem.Fire(w, a.Index);
        Assert.Equal(2, w.Projectiles.Count);
        var hits = new List<int>();
        for (int k = 0; k < 60 && hits.Sum() < 38; k++)
        {
            int d = Damage(sim, t, () => sim.Tick());
            if (d > 0) hits.Add(d);
        }
        if (laterBy == 0) Assert.Equal(new[] { 38 }, hits.ToArray());
        else Assert.Equal(new[] { 23, 15 }, hits.ToArray());
        Assert.False(w.IsStealthed(a.Index));
    }

    // ---------- a shot that misses ----------

    /// <summary>
    /// Today's reveal rule (M4-5) reveals on a hit, not on a miss: a hidden Javelin Thrower's javelin misses (its target is
    /// killed by something else while the javelin flies), so the thrower stays hidden and its next javelin, at another
    /// target, still carries the bonus (23, not 15). Pins the current reading of docs/02 "from the moment its attack lands"
    /// (a question for the Producer in the QA report: a miss does not use up the first shot).
    /// </summary>
    [Fact]
    public void AMissedShot_DoesNotReveal_SoTheNextHitStillCarriesTheBonus()
    {
        Simulation sim = Scene(data: Qa);
        World w = sim.World;
        EntityHandle a = Place(sim, 2, "teblor_javelin_thrower", At(sim, 10, 20));
        EntityHandle t = Dummy(sim, 0, At(sim, 15, 20));
        Hide(sim, a);
        sim.Enqueue(Command.Attack(2, a, t, false));
        Assert.True(CombatScenes.RunUntil(sim, () => w.Projectiles.Count > 0, 200) < 200);
        w.Units.Free(t);
        Assert.True(CombatScenes.RunUntil(sim, () => w.Projectiles.Count == 0, 60) < 60);
        Assert.True(w.IsStealthed(a.Index)); // the miss revealed nothing
        EntityHandle next = Dummy(sim, 0, At(sim, 15, 21));
        sim.Enqueue(Command.Attack(2, a, next, false));
        Assert.Equal(23, FirstHits(sim, next, 1)[0].Damage);
    }

    // ---------- a lob, its splash and friendly fire ----------

    /// <summary>
    /// A hidden Sapper (a lob: 20 siege x 0.5 v light = 10, splash 2 m: full to 0.8 m, half at the edge; friendly fire at
    /// half) lobs at an enemy dummy with an own Laborer 0.5 m from it and a second enemy dummy 1.5 m off. From stealth every
    /// victim of that one lob carries x 1.5: 15 / 8 (7.5 rounded half up) / 15 x 0.708 = 10.6 -> 11; the next lob plain: 10 /
    /// 5 / 7.08 -> 7.
    /// </summary>
    [Fact]
    public void ALobFromStealth_CarriesTheBonusToEveryVictim_FriendlyFireToo_ThenPlain()
    {
        Simulation sim = Scene(data: Qa);
        World w = sim.World;
        EntityHandle a = Place(sim, 0, "malazan_sapper", At(sim, 10, 20));
        w.Units.Hold[a.Index] = true;
        EntityHandle t = Dummy(sim, 1, At(sim, 13, 20));
        EntityHandle own = Dummy(sim, 0, At(sim, 13, 20, 0f, 0.5f));
        EntityHandle side = Dummy(sim, 1, At(sim, 13, 20, 0f, -1.5f));
        Hide(sim, a);
        Assert.True(w.IsStealthed(a.Index));
        sim.Enqueue(Command.Attack(0, a, t, false));
        int[] Hp() => new[] { w.Units.Hp[t.Index], w.Units.Hp[own.Index], w.Units.Hp[side.Index] };
        var lobs = new List<int[]>();
        for (int k = 0; k < 600 && lobs.Count < 2; k++)
        {
            int[] before = Hp();
            sim.Tick();
            int[] after = Hp();
            if (after[0] < before[0]) lobs.Add(new[] { before[0] - after[0], before[1] - after[1], before[2] - after[2] });
        }
        Assert.Equal(2, lobs.Count);
        Assert.Equal(new[] { 15, 8, 11 }, lobs[0]);
        // The Sapper's 3 s cooldown outlasts the 3 s reveal and the timed stealth is still on it, so the second lob is again
        // from stealth (docs/02: Revealed only overrides Stealthed for its duration).
        Assert.Equal(new[] { 15, 8, 11 }, lobs[1]);
    }

    /// <summary>The same lob scene from a Sapper that is not hidden: 10 / 5 / 7; and from a hidden one the landing reveals it.</summary>
    [Fact]
    public void ALob_NotHidden_IsPlain_AndALobFromStealth_RevealsItsThrower()
    {
        foreach (bool hide in new[] { false, true })
        {
            Simulation sim = Scene(data: Qa);
            World w = sim.World;
            EntityHandle a = Place(sim, 0, "malazan_sapper", At(sim, 10, 20));
            w.Units.Hold[a.Index] = true;
            EntityHandle t = Dummy(sim, 1, At(sim, 13, 20));
            EntityHandle own = Dummy(sim, 0, At(sim, 13, 20, 0f, 0.5f));
            EntityHandle side = Dummy(sim, 1, At(sim, 13, 20, 0f, -1.5f));
            if (hide) Hide(sim, a);
            sim.Enqueue(Command.Attack(0, a, t, false));
            for (int k = 0; k < 600; k++)
            {
                int[] before = { w.Units.Hp[t.Index], w.Units.Hp[own.Index], w.Units.Hp[side.Index] };
                sim.Tick();
                if (w.Units.Hp[t.Index] == before[0]) continue;
                int[] got = { before[0] - w.Units.Hp[t.Index], before[1] - w.Units.Hp[own.Index], before[2] - w.Units.Hp[side.Index] };
                Assert.Equal(hide ? new[] { 15, 8, 11 } : new[] { 10, 5, 7 }, got);
                Assert.False(w.IsStealthed(a.Index));
                break;
            }
        }
    }

    // ---------- a melee splash (Question 1's ordering) ----------

    /// <summary>
    /// A hidden Heavy Infantry whose melee splashes 1.5 m (test data): the direct blow and a neighbour 0.5 m beyond the
    /// target (inside the full 0.6 m) take 15 from stealth, then 10 and 10: the splash reads the factor before the direct
    /// hit reveals the striker.
    /// </summary>
    [Fact]
    public void AMeleeSplashFromStealth_CarriesTheBonus_ThenPlain()
    {
        Simulation sim = Scene(data: MeleeSplash);
        World w = sim.World;
        EntityHandle a = Place(sim, 0, "malazan_heavy_infantry", At(sim, 20, 20));
        EntityHandle t = Dummy(sim, 1, At(sim, 20, 20, -1f));
        EntityHandle n = Dummy(sim, 1, At(sim, 20, 20, -1f, 0.5f));
        Hide(sim, a);
        sim.Enqueue(Command.Attack(0, a, t, false));
        var blows = new List<int[]>();
        for (int k = 0; k < 300 && blows.Count < 2; k++)
        {
            int bt = w.Units.Hp[t.Index], bn = w.Units.Hp[n.Index];
            sim.Tick();
            if (w.Units.Hp[t.Index] < bt) blows.Add(new[] { bt - w.Units.Hp[t.Index], bn - w.Units.Hp[n.Index] });
        }
        Assert.Equal(new[] { 15, 15 }, blows[0]);
        Assert.Equal(new[] { 10, 10 }, blows[1]);
    }

    // ---------- a shot on a building whose splash reaches a unit ----------

    /// <summary>
    /// A hidden Cadre Mage (9 magic, splash 1.5 m) shoots a Whirlwind Tent with an enemy dummy standing at the bolt's impact
    /// point. The dummy's splash damage is the formula's with x 1.5 from stealth and with x 1 for the next bolt, at the same
    /// falloff (worked out from the impact point the store holds).
    /// </summary>
    [Fact]
    public void AShotOnABuilding_SplashesAUnitWithTheBonus()
    {
        Simulation sim = Scene(data: Qa);
        World w = sim.World;
        EntityHandle a = Place(sim, 0, "malazan_cadre_mage", At(sim, 12, 20));
        Assert.True(w.Buildings.Spawn(1, w.Data.FindBuilding("whirlwind_tent"), 20 * 48 + 18, out EntityHandle tent));
        Dummy(sim, 1, At(sim, 40, 40)); // a live unit, so player 1 is not defeated
        Hide(sim, a);
        w.Fog.Update();
        sim.Enqueue(Command.Attack(0, a, tent, true));
        Assert.True(CombatScenes.RunUntil(sim, () => w.Projectiles.Count > 0, 300) < 300);
        Vector2 impact = default;
        for (int k = 0; k < w.Projectiles.End; k++)
            if (w.Projectiles.Alive[k]) impact = w.Projectiles.Target[k];
        EntityHandle d = Dummy(sim, 1, impact + new Vector2(0f, 0.3f)); // inside the full 0.6 m
        w.Units.Hold[d.Index] = true;
        int[] got = new int[2];
        int shots = 0;
        for (int k = 0; k < 600 && shots < 2; k++)
        {
            int bt = w.Buildings.Hp[tent.Index];
            int dd = Damage(sim, d, () => sim.Tick());
            if (w.Buildings.Hp[tent.Index] < bt) got[shots++] = dd;
        }
        Assert.Equal(2, shots);
        AttackDef attack = w.Data.Units[w.Data.FindUnit("malazan_cadre_mage")].Attack;
        UnitDef vdef = w.Data.Units[w.Units.TypeId[d.Index]];
        int bonus = DamageCalc.Compute(w.Data.DamageTable, attack, 0, vdef.ArmorClass, vdef.Armor, 1.5f);
        int plain = DamageCalc.Compute(w.Data.DamageTable, attack, 0, vdef.ArmorClass, vdef.Armor, 1f);
        Assert.NotEqual(plain, bonus);
        Assert.Equal(new[] { bonus, plain }, got);
    }

    // ---------- pack mates dying in the same phase 11 ----------

    /// <summary>
    /// One run of a pack scene: the striker ordered onto a dummy from the start; an enemy Whirlwind Raider (holding, a
    /// million hit points) placed before tick <paramref name="raiderAt"/> beside mate <c>m1</c>, swinging at it every 1.5 s;
    /// <c>m1</c> keeps a million hit points until just before tick <paramref name="dropAt"/>, then has 1, so the raider's next
    /// blow (phase 11, before any projectile lands) kills it. Returns the ticks the raider's blows landed on <c>m1</c>, the
    /// tick it died in, and every hit on the dummy (tick, damage). <paramref name="ranged"/>: the QA copy's Crossbowman (two
    /// holding mates 6 m behind it) shooting a dummy 10 m off; else the copy's Heavy Infantry striking a dummy 1 m off, its
    /// two holding mates 2 m and 4 m east.
    /// </summary>
    private static (List<int> Blows, int Death, List<(int Tick, int Damage)> Hits) KillRun(bool ranged, int raiderAt, int dropAt, int gap, int ticks = 400)
    {
        Simulation sim = Scene(data: Qa);
        World w = sim.World;
        string type = ranged ? "malazan_crossbowman" : "malazan_heavy_infantry";
        Vector2 at = ranged ? At(sim, 10, 20) : At(sim, 20, 20);
        EntityHandle a = Place(sim, 0, type, at);
        w.Units.Hold[a.Index] = true;
        EntityHandle m1 = Place(sim, 0, type, at + (ranged ? new Vector2(-6f, 0f) : new Vector2(2f, 0f)));
        EntityHandle m2 = Place(sim, 0, type, at + (ranged ? new Vector2(-6f, 2f) : new Vector2(4f, 0f)));
        w.Units.Hold[m1.Index] = w.Units.Hold[m2.Index] = true;
        w.Units.Hp[m1.Index] = 1_000_000;
        EntityHandle t = Dummy(sim, 1, ranged ? At(sim, 10 + gap, 20) : at + new Vector2(-1f, 0f));
        sim.Enqueue(Command.Attack(0, a, t, false));
        var blows = new List<int>();
        var hits = new List<(int, int)>();
        int death = -1;
        for (int k = 0; k < ticks && (death < 0 || sim.TickNumber <= death + 10); k++)
        {
            if (sim.TickNumber == raiderAt)
            {
                EntityHandle r = Place(sim, 1, "whirlwind_raider", w.Units.Position[m1.Index] + (ranged ? new Vector2(-0.9f, 0f) : new Vector2(0f, -0.9f)));
                w.Units.Hp[r.Index] = 1_000_000;
                w.Units.Hold[r.Index] = true;
            }
            if (sim.TickNumber == dropAt) w.Units.Hp[m1.Index] = 1;
            int hp = w.Units.Hp[m1.Index], before = w.Units.Hp[t.Index];
            sim.Tick();
            if (death < 0 && !w.Units.IsAlive(m1)) death = sim.TickNumber - 1;
            else if (death < 0 && w.Units.Hp[m1.Index] < hp) blows.Add(sim.TickNumber - 1);
            if (w.Units.Hp[t.Index] < before) hits.Add((sim.TickNumber - 1, before - w.Units.Hp[t.Index]));
        }
        return (blows, death, hits);
    }

    /// <summary>
    /// A run in which <c>m1</c> dies exactly <paramref name="offset"/> ticks after one of the striker's hits: found from a run
    /// with no drop (the raider's blow ticks against the striker's hit ticks), then replayed with the drop just before the
    /// blow. Returns the damage of that hit.
    /// </summary>
    private static int KilledAt(bool ranged, int offset)
    {
        for (int gap = ranged ? 4 : 0; gap <= (ranged ? 7 : 0); gap++)
        for (int raiderAt = 1; raiderAt < 60; raiderAt++)
        {
            var free = KillRun(ranged, raiderAt, dropAt: -1, gap);
            foreach ((int tick, _) in free.Hits)
            {
                if (!free.Blows.Contains(tick + offset)) continue;
                var run = KillRun(ranged, raiderAt, dropAt: tick + offset, gap);
                Assert.Equal(tick + offset, run.Death);
                int k = run.Hits.FindIndex(h => h.Tick == tick);
                Assert.True(k >= 0, $"no hit at {tick} in the replay");
                return run.Hits[k].Damage;
            }
        }
        throw new InvalidOperationException($"no placement kills the mate {offset} ticks after a hit");
    }

    /// <summary>
    /// A ranged pack unit's mate cut down by a melee blow in the landing tick's phase 11 (melee hits resolve before the
    /// projectiles land) no longer counts: 9 pierce x 1.25 = 11.25 v light x 1.1 = 12.375 -> 12. Killed a tick later, both
    /// mates counted: x 1.2 = 13.5 -> 14; a tick earlier, 12.
    /// </summary>
    [Theory]
    [InlineData(0, 12)]
    [InlineData(1, 14)]
    [InlineData(-1, 12)]
    public void ARangedPackUnitsMate_KilledInTheLandingPhase_BeforeTheLanding_DoesNotCount(int offset, int expected)
    {
        Assert.Equal(expected, KilledAt(true, offset));
    }

    /// <summary>
    /// A melee pack striker's count is taken at the strike (phase 10): a mate cut down in the same tick's phase 11, before
    /// the blow is applied, still counted for that blow (the documented moment, docs/02 "Unit passives"): 10 x 1.2 = 12. A
    /// few ticks earlier, it did not: 11 (the blow and the raider's swings share a 1.5 s period, so only some offsets occur).
    /// </summary>
    [Theory]
    [InlineData(0, 12)]
    [InlineData(-2, 11)]
    [InlineData(-4, 11)]
    public void AMeleePackStrikersMate_KilledInTheSamePhase11_StillCountedForThatBlow(int offset, int expected)
    {
        Assert.Equal(expected, KilledAt(false, offset));
    }

    // ---------- the pack count against a brute-force count ----------

    /// <summary>
    /// Many random scenes of Teblor War-dogs (radius 0.4) and Teblor Warriors (radius 0.75), each counting its own kind (8 m,
    /// at most 3), and Gatherers (listed by neither) for three or four players (some seeds a 2v2, so an ally's dogs stand among them), positions at arbitrary
    /// floats over a 40 m box straddling many 4 m grid buckets, some mates put exactly 8 m off a striker along an axis; the
    /// grid built, then every unit stepped up to the fastest unit's speed (the grid is a step stale by phase 10). For every
    /// pack unit, <see cref="HitFactor.PackCount"/> equals a count walking every slot, capped, and <see cref="HitFactor.Of"/> the oracle.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void PackCount_EqualsABruteForceCount_AcrossGridBuckets_AfterAStep(int seed)
    {
        var rng = new Random(seed * 7919);
        bool teams = seed % 2 == 0;
        var seats = teams
            ? System.Collections.Immutable.ImmutableArray.Create(new PlayerSeat(2, SeatController.Human, AiDifficulty.Normal, 1),
                new PlayerSeat(2, SeatController.Human, AiDifficulty.Normal, 2), new PlayerSeat(2, SeatController.Human, AiDifficulty.Normal, 1),
                new PlayerSeat(2, SeatController.Human, AiDifficulty.Normal, 2))
            : default;
        Simulation sim = Scene(players: teams ? 4 : 3, units: 400, seats: seats, data: Qa);
        World w = sim.World;
        int players = teams ? 4 : 3;
        var dogs = new List<EntityHandle>();
        Vector2 origin = At(sim, 10, 10);
        for (int k = 0; k < 160; k++)
        {
            int owner = rng.Next(players);
            int kind = rng.Next(4); // 0-1 a War-dog (0.4 m), 2 a Teblor Warrior (0.75 m; its own pack type in the QA copy), 3 a Gatherer (listed by neither)
            bool dog = kind < 3;
            Vector2 at = origin + new Vector2((float)(rng.NextDouble() * 40), (float)(rng.NextDouble() * 40));
            if (dogs.Count > 0 && rng.Next(5) == 0)
            {
                // Exactly 8 m off an earlier dog along an axis (exact in float).
                Vector2 c = w.Units.Position[dogs[rng.Next(dogs.Count)].Index];
                at = c + (rng.Next(4) switch { 0 => new Vector2(8f, 0f), 1 => new Vector2(-8f, 0f), 2 => new Vector2(0f, 8f), _ => new Vector2(0f, -8f) });
            }
            EntityHandle h = Place(sim, owner, kind < 2 ? "teblor_war_dog" : kind == 2 ? "teblor_warrior" : "teblor_gatherer", at);
            if (dog) dogs.Add(h);
        }
        w.Spatial.Rebuild(w.Units);
        float step = w.MaxUnitSpeed;
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || rng.Next(2) == 0) continue;
            double ang = rng.NextDouble() * Math.PI * 2;
            float len = (float)(rng.NextDouble() * step);
            u.Position[i] += new Vector2((float)Math.Cos(ang) * len, (float)Math.Sin(ang) * len);
        }
        int checkedAt8 = 0;
        foreach (EntityHandle h in dogs)
        {
            UnitDef def = w.Data.Units[u.TypeId[h.Index]];
            UnitPassive p = def.Passives[def.NearbyPassive];
            int brute = 0;
            for (int j = 0; j < u.Capacity; j++)
            {
                if (j == h.Index || !u.Alive[j] || u.Owner[j] != u.Owner[h.Index] || !p.Units.Contains(u.TypeId[j])) continue;
                float d2 = Vector2.DistanceSquared(u.Position[j], u.Position[h.Index]);
                if (d2 <= p.Radius * p.Radius) brute++;
                if (d2 == 64f) checkedAt8++;
            }
            Assert.Equal(Math.Min(brute, p.MaxStacks), HitFactor.PackCount(w, h.Index, p));
            Assert.Equal(Oracle(w, h.Index), HitFactor.Of(w, h.Index));
        }
        _ = checkedAt8;
    }

    /// <summary>
    /// The striker on a grid-bucket corner (multiples of 4 m) and mates exactly 8 m off along each axis and 8.0001 m off,
    /// every one in another bucket: the exact ones count (cap 3 of 4), the ones just beyond do not.
    /// </summary>
    [Fact]
    public void PackCount_OnABucketCorner_ExactRadiusAcrossBuckets()
    {
        Simulation sim = Scene(players: 3, units: 64);
        World w = sim.World;
        Vector2 c = new(48f, 48f); // a bucket corner (4 m buckets)
        Assert.Equal(0f, c.X % Spatial.SpatialHash.BucketSize);
        EntityHandle a = Place(sim, 2, "teblor_war_dog", c);
        UnitDef def = w.Data.Units[w.Data.FindUnit("teblor_war_dog")];
        UnitPassive p = def.Passives[def.NearbyPassive];
        Place(sim, 2, "teblor_war_dog", c + new Vector2(8f, 0f));
        Place(sim, 2, "teblor_war_dog", c + new Vector2(0f, -8f));
        w.Spatial.Rebuild(w.Units);
        Assert.Equal(2, HitFactor.PackCount(w, a.Index, p));
        Place(sim, 2, "teblor_war_dog", c + new Vector2(-8.001f, 0f));
        Place(sim, 2, "teblor_war_dog", c + new Vector2(0f, 8.001f));
        w.Spatial.Rebuild(w.Units);
        Assert.Equal(2, HitFactor.PackCount(w, a.Index, p));
        Place(sim, 2, "teblor_war_dog", c + new Vector2(-8f, 0f));
        Place(sim, 2, "teblor_war_dog", c + new Vector2(0f, 8f));
        w.Spatial.Rebuild(w.Units);
        Assert.Equal(3, HitFactor.PackCount(w, a.Index, p)); // four exact, capped at 3
    }

    // ---------- rounding of stacked factors ----------

    /// <summary>
    /// BUG-1620, a documented limit (docs/01 2026-11-25 (1): stays as built): a Heavy Infantry with attack 45 (test data) and
    /// a full pack of three. docs/02 "Damage formula" rounds half up, so by hand 45 x 1.0 x 1.3 = 58.5 would deal 59; the
    /// float factor 1 + 3 x 0.1f is 1.29999995, so the sim deals 58, as docs/03 "Implementation (M8-3)" Known limits (3)
    /// states. This row pins 58: a change to how hits round (the exact arithmetic the ruling leaves for a pin-moving task)
    /// turns it red on purpose, and that task re-pins it with the docs.
    /// </summary>
    [Fact]
    public void AFullPack_45x1point3_DealsFiftyEight_TheDocumentedLimit()
    {
        Simulation sim = Scene(data: Value45);
        World w = sim.World;
        EntityHandle a = Place(sim, 0, "malazan_heavy_infantry", At(sim, 20, 20));
        EntityHandle t = Dummy(sim, 1, At(sim, 20, 20, -1f));
        for (int k = 0; k < 3; k++) w.Units.Hold[Place(sim, 0, "malazan_heavy_infantry", At(sim, 20, 20, 2f + k)).Index] = true;
        sim.Enqueue(Command.Attack(0, a, t, false));
        int dealt = FirstHits(sim, t, 1)[0].Damage;
        Assert.True(dealt == 58, $"dealt {dealt}: docs/03 \"Implementation (M8-3)\" Known limits (3) says a full pack (1.3) rounds an exact "
            + "half down (45 x 1.3 = 58.5 deals 58), against docs/02's round half up (59 by hand); BUG-1620, docs/01 2026-11-25 (1)");
    }

    /// <summary>
    /// The rounding of every combination the copy can stack (status 0 / 0.2, stealth 0 / 0.5, pack 0-3) for attack values
    /// 1-60 on the damage table's multipliers, through the sim's own factor (<see cref="HitFactor.Of"/> on a real unit)
    /// against a decimal hand calculation, counting the disagreements. The pack's 1.1 and 1.2 and the 1.5 and 1.2 never
    /// disagree; every disagreement is a full pack (1.3f below 1.3) on an exact half.
    /// </summary>
    [Fact]
    public void StackedFactors_DisagreeWithAHandCalculation_OnlyOnAFullPacksExactHalves()
    {
        Simulation sim = Scene(units: 16);
        World w = sim.World;
        EntityHandle a = Place(sim, 0, "malazan_heavy_infantry", At(sim, 20, 20));
        var mates = new List<EntityHandle>();
        for (int k = 0; k < 3; k++) mates.Add(Place(sim, 0, "malazan_heavy_infantry", At(sim, 20, 20, 2f + k)));
        float[] mults = { 1f, 1.25f, 0.6f, 0.5f, 0.75f, 3f, 0.35f, 0.7f };
        decimal[] dmults = { 1m, 1.25m, 0.6m, 0.5m, 0.75m, 3m, 0.35m, 0.7m };
        int disagree = 0, notFullPack = 0;
        for (int n = 0; n <= 3; n++)
        {
            for (int k = 0; k < 3; k++) w.Units.Position[mates[k].Index] = At(sim, 20, 20, k < n ? 2f + k : 30f);
            w.Spatial.Rebuild(w.Units);
            for (int st = 0; st < 2; st++)
            {
                if (st == 1) Hide(sim, a);
                float factor = HitFactor.Of(w, a.Index);
                decimal exact = (1m + 0.1m * n) * (st == 1 ? 1.5m : 1m);
                for (int v = 1; v <= 60; v++)
                    for (int m = 0; m < mults.Length; m++)
                    {
                        float raw = v * mults[m] * 1f * factor;
                        int sim1 = (int)MathF.Floor(raw + 0.5f);
                        int hand = (int)decimal.Floor(v * dmults[m] * exact + 0.5m);
                        if (sim1 == hand) continue;
                        disagree++;
                        if (n != 3) notFullPack++;
                    }
            }
            Abilities.StatusSystem.RemoveEntry(w, a.Index, 0 * Abilities.StatusStore.PerUnit + 0);
        }
        Assert.Equal(0, notFullPack);
        Assert.True(disagree > 0); // BUG-1620: the full pack's exact halves round down
    }
}
