using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Tests.ViewApi;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>
/// QA M4-V2 (session 2026-10-08-0913): adversarial rows for <see cref="UnitPicker"/>, <see cref="TargetMark"/> and the
/// hit flash's first-sight rule, beyond the developer's <c>UnitPickerTests</c>: the read-only proof (a sim the view picks
/// and orders through every frame hashes equal to a bare twin, and no frame of picks moves the hash), an own unit in front
/// of an enemy from camera angles, an enemy unit in front of / behind an enemy building, a dead unit's slot (a corpse) never
/// picked, Attack spam through the pick path versus one order (identical hashes), three queued Attacks then Stop, and
/// bad inputs to the resolver and the mark.
/// </summary>
[Collection(SerialCollection.Name)]
public class UnitPickerQaTests
{
    // The game side's constants: UnitViews.ExtraBodyHeight, BuildingViews.BoxHeight / SiteMinHeight.
    private const float Extra = 1f, BoxHeight = 3f, SiteMin = 0.15f;

    private readonly ITestOutputHelper _out;

    public UnitPickerQaTests(ITestOutputHelper output) => _out = output;

    private static int Pick(World w, float alpha, Vector3 o, Vector3 d, out float t) =>
        UnitPicker.PickRay(w.Units.Alive, w.Units.PrevPosition, w.Units.Position, w.Units.Radius, w.Heightmap, alpha, o, d, Extra, out t);

    // The controller's EnemyAt, minus the camera and the props.
    private static bool EnemyAt(World w, float alpha, Vector3 o, Vector3 d, int local, out EntityHandle target, out bool isBuilding)
    {
        int unit = Pick(w, alpha, o, d, out float ut);
        int b = BuildingPicker.PickRay(w.Buildings, w.Data.Buildings, w.NavGrid, w.Heightmap, -1, o, d, BoxHeight, SiteMin, out float bt);
        return UnitPicker.ResolveEnemy(w.Units.Alive, w.Units.Owner, w.Units.Generation, unit, ut, w.Buildings, b, bt, float.PositiveInfinity, local, out target, out isBuilding);
    }

    // ---------- read-only proof ----------

    /// <summary>
    /// Twin A is driven as the view drives it: three frames a tick, each with 40 camera rays picked and resolved, the hit
    /// flash (with types) and a target mark updated; on some frames the resolved enemy, if player 0 sees it, gets an Attack
    /// from a random subset of player 0's units (queued or not). Twin B gets exactly A's commands. Hash equal every tick,
    /// and no frame of picks moves A's hash.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(6UL)]
    [InlineData(23UL)]
    public void PickAndOrderEveryFrame_HashEqualsBareTwin_EveryTick(ulong seed)
    {
        Simulation a = CombatViewScene.Create(seed), b = CombatViewScene.Create(seed);
        CombatViewScene.Start(a, b);
        World w = a.World;
        UnitStore u = w.Units;
        var rng = new Random((int)seed * 31 + 5);
        var flash = new HitFlash(u.Capacity);
        var mark = new TargetMark();
        var maxHp = new int[w.Data.Units.Length];
        for (int t = 0; t < maxHp.Length; t++) maxHp[t] = w.Data.Units[t].Hp;
        int attacks = 0, picks = 0, enemies = 0, seen = 0;
        var frameOrders = new List<Command>();
        for (int tick = 0; tick < 900; tick++)
        {
            for (int f = 0; f < 3; f++)
            {
                ulong before = a.StateHash();
                frameOrders.Clear();
                float alpha = f / 3f;
                flash.Update(u.Alive, u.Generation, u.Hp, u.TypeId, maxHp, 1f / 60f);
                mark.Update(1f / 60f);
                for (int k = 0; k < 40; k++)
                {
                    var target = new Vector3(20f + (float)rng.NextDouble() * 60f, 0.5f + (float)rng.NextDouble(), 38f + (float)rng.NextDouble() * 20f);
                    var origin = target + new Vector3((float)rng.NextDouble() * 20f - 10f, 25f + (float)rng.NextDouble() * 10f, 15f + (float)rng.NextDouble() * 10f);
                    if (Pick(w, alpha, origin, target - origin, out _) >= 0) picks++;
                    if (!EnemyAt(w, alpha, origin, target - origin, 0, out EntityHandle e, out bool isB)) continue;
                    enemies++;
                    // Order only what player 0 sees, as the player can (an Attack on an unseen target is dropped since
                    // M4-3a, so it would prove nothing): the M4-VH1 sweep.
                    if (!(isB ? w.Fog.CanSeeBuilding(0, e.Index) : w.Fog.CanSeeUnit(0, e.Index))) continue;
                    seen++;
                    if (rng.Next(25) != 0) continue;
                    mark.Mark(e, isB);
                    bool queued = rng.Next(3) == 0;
                    for (int i = 0; i < u.Capacity; i++)
                    {
                        if (!u.Alive[i] || u.Owner[i] != 0 || rng.Next(2) == 0) continue;
                        frameOrders.Add(Command.Attack(0, new EntityHandle(i, u.Generation[i]), e, isB, queued));
                    }
                }
                // The frame's reads moved nothing (pending commands are hashed, so this is checked before enqueuing).
                Assert.Equal(before, a.StateHash());
                foreach (Command c in frameOrders)
                {
                    a.Enqueue(c);
                    b.Enqueue(c);
                    attacks++;
                }
            }
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ at tick {a.World.TickNumber}");
        }
        _out.WriteLine($"seed {seed}: 900 ticks x 3 frames x 40 rays: {picks} unit picks, {enemies} enemy resolutions ({seen} seen), {attacks} Attack commands; twins equal every tick; {w.Kills[0] + w.Kills[1]} kills");
        Assert.True(attacks > 50 && enemies > 500, $"too few orders ({attacks}) or enemy picks ({enemies}) to mean anything");
    }

    // ---------- geometry: what hides what ----------

    /// <summary>
    /// An own Heavy Infantry 1.2 m in front of an enemy (toward the camera), seen from 30 RTS-like camera positions; a fan
    /// of rays through both bodies. Every ray whose first body (by a marching oracle) is the own unit resolves to no target;
    /// every ray that meets the enemy first resolves to the enemy; no ray ever names the enemy through the own body.
    /// </summary>
    [Fact]
    public void OwnUnitInFrontOfEnemy_NeverTargetsTheEnemyThroughIt()
    {
        Simulation sim = Flat(units: 8);
        World w = sim.World;
        EntityHandle enemy = Place(sim, 1, Raider, new Vector2(40f, 40f));
        EntityHandle own = Place(sim, 0, HeavyInfantry, new Vector2(40f, 41.2f)); // +z: toward the camera
        var rng = new Random(11);
        int throughOwn = 0, onEnemy = 0, wrong = 0;
        for (int cam = 0; cam < 30; cam++)
        {
            var origin = new Vector3(40f + (float)rng.NextDouble() * 16f - 8f, 18f + (float)rng.NextDouble() * 20f, 41.2f + 12f + (float)rng.NextDouble() * 20f);
            for (int k = 0; k < 200; k++)
            {
                var aim = new Vector3(40f + (float)rng.NextDouble() * 1.6f - 0.8f, (float)rng.NextDouble() * 2.6f, 39.4f + (float)rng.NextDouble() * 2.6f);
                Vector3 d = aim - origin;
                bool hit = EnemyAt(w, 1f, origin, d, 0, out EntityHandle t, out _);
                int first = FirstBody(w, origin, d);
                if (first == own.Index)
                {
                    throughOwn++;
                    if (hit) wrong++;
                }
                else if (first == enemy.Index)
                {
                    onEnemy++;
                    if (!hit || t != enemy) wrong++;
                }
            }
        }
        _out.WriteLine($"own-in-front: {throughOwn} rays met the own unit first, {onEnemy} the enemy first, {wrong} wrong");
        Assert.True(throughOwn > 500 && onEnemy > 200, $"geometry didn't exercise both cases ({throughOwn} / {onEnemy})");
        Assert.Equal(0, wrong);
    }

    /// <summary>
    /// An enemy Raider in front of the enemy Tent (camera side) is the target where its body covers the box; behind the
    /// Tent (hidden by the box) the Tent is the target, never the hidden unit; a ray past the box's side meets the unit.
    /// </summary>
    [Fact]
    public void EnemyUnitAndEnemyBuilding_Overlapping_TheNearerIsTheTarget()
    {
        Simulation sim = CombatViewScene.Create();
        foreach (Command c in CombatViewScene.Spawns(sim)) sim.Enqueue(c);
        sim.Tick();
        sim.Tick();
        World w = sim.World;
        BuildingStore bs = w.Buildings;
        UnitStore u = w.Units;
        int tent = -1;
        for (int i = 0; i < bs.Capacity; i++) if (bs.Alive[i] && bs.Owner[i] == 1) tent = i;
        Assert.True(tent >= 0);
        // Clear the armies out of the way (staging, between ticks): only the two units below matter.
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i]) u.Free(new EntityHandle(i, u.Generation[i]));
        var def = w.Data.Buildings[bs.TypeId[tent]];
        int gw = w.NavGrid.Width;
        float x0 = bs.Cell[tent] % gw * MapConstants.CellSize, z0 = bs.Cell[tent] / gw * MapConstants.CellSize;
        float x1 = x0 + def.FootprintWidth * MapConstants.CellSize, z1 = z0 + def.FootprintHeight * MapConstants.CellSize;
        float cx = (x0 + x1) / 2f;
        EntityHandle front = Place(sim, 1, Raider, new Vector2(cx, z1 + 0.8f)); // camera side
        EntityHandle back = Place(sim, 1, Raider, new Vector2(cx, z0 - 0.8f));  // hidden side
        var origin = new Vector3(cx, 30f, z1 + 25f);
        // Through the front unit's body centre: the unit (it is nearer than the box).
        Assert.True(EnemyAt(w, 1f, origin, new Vector3(cx, 1f, z1 + 0.8f) - origin, 0, out EntityHandle t, out bool isB));
        Assert.Equal(front, t);
        Assert.False(isB);
        // Through the box top towards the back unit: the Tent, never the hidden unit.
        int tentHits = 0, backHits = 0;
        for (int k = 0; k < 50; k++)
        {
            var aim = new Vector3(cx - 0.4f + k * 0.016f, 1f, z0 - 0.8f);
            if (!EnemyAt(w, 1f, origin, aim - origin, 0, out t, out isB)) continue;
            if (isB && t.Index == tent) tentHits++;
            if (!isB && t == back) backHits++;
        }
        _out.WriteLine($"rays at the hidden unit: {tentHits} name the Tent, {backHits} the hidden unit");
        Assert.Equal(0, backHits);
        Assert.Equal(50, tentHits);
        // From straight above the back unit (no box between), it is picked.
        var above = new Vector3(cx, 40f, z0 - 0.8f);
        Assert.True(EnemyAt(w, 1f, above, new Vector3(0f, -1f, 0f), 0, out t, out isB));
        Assert.Equal(back, t);
    }

    /// <summary>A unit killed this tick (a corpse disc where it stood) is never picked: the ray goes to the ground; the slot re-used far away is picked there, not at the corpse.</summary>
    [Fact]
    public void DeadUnit_IsNeverPicked_ItsSlotReusedElsewhere_IsPickedAtItsNewSpot()
    {
        Simulation sim = Flat(units: 8);
        World w = sim.World;
        EntityHandle enemy = Place(sim, 1, Raider, new Vector2(40f, 40f));
        var origin = new Vector3(40f, 30f, 60f);
        Vector3 d = new Vector3(40f, 1f, 40f) - origin;
        Assert.True(EnemyAt(w, 1f, origin, d, 0, out EntityHandle t, out _));
        Assert.Equal(enemy, t);
        w.Units.Free(enemy);
        Assert.Equal(-1, Pick(w, 1f, origin, d, out _));
        Assert.False(EnemyAt(w, 1f, origin, d, 0, out _, out _));
        EntityHandle reborn = Place(sim, 1, Raider, new Vector2(20f, 20f));
        Assert.Equal(enemy.Index, reborn.Index);
        Assert.NotEqual(enemy.Generation, reborn.Generation);
        Assert.False(EnemyAt(w, 1f, origin, d, 0, out _, out _));
        var o2 = new Vector3(20f, 30f, 40f);
        Assert.True(EnemyAt(w, 1f, o2, new Vector3(20f, 1f, 20f) - o2, 0, out t, out _));
        Assert.Equal(reborn, t);
    }

    // ---------- the orders the pick feeds ----------

    /// <summary>
    /// QA focus "50 right-clicks on one enemy in a second": ten Heavy Infantry swinging at a Tent-side Raider get the same
    /// unqueued Attack 50 times over 20 ticks (2-3 per tick, as clicks land between ticks); a twin gets it once. The
    /// sim's ReaffirmAttack must make every re-issue a no-op: every unit's hp, state, position, target, mode, cooldown,
    /// wind-up, chase memory and queue equal every tick for 400 ticks. (The state hash itself differs by design: it covers
    /// each player's command sequence counter, which counts the 49 extra clicks.)
    /// </summary>
    [Fact]
    public void FiftyClicksOnOneEnemy_SameStateAsOneClick_EveryTick()
    {
        Simulation a = Flat(units: 32), b = Flat(units: 32);
        var spawns = new List<(int, int, Vector2)>();
        for (int k = 0; k < 10; k++) spawns.Add((0, HeavyInfantry, new Vector2(34f + k % 5 * 1.2f, 38f + k / 5 * 1.2f)));
        spawns.Add((1, Raider, new Vector2(42f, 39f)));
        EntityHandle[] ha = Spawn(a, spawns.ToArray()), hb = Spawn(b, spawns.ToArray());
        EntityHandle target = ha[10];
        Assert.Equal(target, hb[10]);
        // The first click in both.
        for (int k = 0; k < 10; k++)
        {
            a.Enqueue(Command.Attack(0, ha[k], target, false));
            b.Enqueue(Command.Attack(0, hb[k], target, false));
        }
        // Let them close in and start swinging. The Raider is in their sight, so the first click is taken (not dropped as
        // unseen, which would make the twin trivially equal: the M4-VH1 sweep).
        for (int t = 0; t < 2; t++) { a.Tick(); b.Tick(); } // a command applies on the second tick after it is enqueued
        for (int k = 0; k < 10; k++) Assert.True(a.World.Units.Target[ha[k].Index] == target && a.World.Units.Mode[ha[k].Index] == CombatMode.Ordered, $"unit {k} did not take the Attack");
        for (int t = 2; t < 30; t++) { a.Tick(); b.Tick(); }
        Assert.Equal(a.StateHash(), b.StateHash());
        int clicks = 1;
        for (int t = 0; t < 400; t++)
        {
            if (t < 20)
            {
                int perTick = t % 2 == 0 ? 3 : 2; // 50 clicks over 20 ticks (1 s)
                for (int c = 0; c < perTick && clicks < 50; c++, clicks++)
                    for (int k = 0; k < 10; k++)
                        if (a.World.Units.IsAlive(ha[k])) a.Enqueue(Command.Attack(0, ha[k], target, false));
            }
            a.Tick();
            b.Tick();
            UnitStore ua = a.World.Units, ub = b.World.Units;
            for (int k = 0; k < 11; k++)
                Assert.True(ua.Hp[ha[k].Index] == ub.Hp[hb[k].Index] && ua.State[ha[k].Index] == ub.State[hb[k].Index] && ua.Alive[ha[k].Index] == ub.Alive[hb[k].Index],
                    $"spam twin: unit {k} differs at tick {a.World.TickNumber}: hp {ua.Hp[ha[k].Index]} vs {ub.Hp[hb[k].Index]}, state {ua.State[ha[k].Index]} vs {ub.State[hb[k].Index]}");
            for (int k = 0; k < 11; k++)
            {
                int i = ha[k].Index, j = hb[k].Index;
                Assert.True(ua.Position[i] == ub.Position[j] && ua.Target[i] == ub.Target[j] && ua.Mode[i] == ub.Mode[j]
                    && ua.CooldownTicks[i] == ub.CooldownTicks[j] && ua.WindupTicks[i] == ub.WindupTicks[j] && ua.ChaseBest[i] == ub.ChaseBest[j]
                    && ua.QueueCount[i] == ub.QueueCount[j], $"spam twin: unit {k}'s combat state differs at tick {a.World.TickNumber}");
            }
        }
        _out.WriteLine($"{clicks} clicks x 10 units: identical to one click for 400 ticks; target alive {a.World.Units.IsAlive(target)}, hp {a.World.Units.Hp[target.Index]}");
    }

    /// <summary>QA focus "Shift-queue of three targets then Stop": three queued Attacks on three enemies, then an unqueued Stop: the queue is empty, no target, no combat mode, Idle; it never swings at any of them afterwards.</summary>
    [Fact]
    public void ThreeQueuedAttacks_ThenStop_ClearsEverything()
    {
        Simulation sim = Flat(units: 16);
        EntityHandle[] h = Spawn(sim, (0, HeavyInfantry, new Vector2(20f, 40f)), (1, Raider, new Vector2(40f, 30f)),
            (1, Raider, new Vector2(40f, 40f)), (1, Raider, new Vector2(40f, 50f)));
        // BUG-0219: since the fog, an Attack on an unseen target is dropped; reveal the Raiders to player 0 so the three
        // orders are accepted, while they stay outside the infantry's own 14 m scan.
        Spot(sim, 0, h[1]);
        Spot(sim, 0, h[2]);
        Spot(sim, 0, h[3]);
        UnitStore u = sim.World.Units;
        EntityHandle me = h[0];
        sim.Enqueue(Command.Attack(0, me, h[1], false));
        sim.Enqueue(Command.Attack(0, me, h[2], false, queued: true));
        sim.Enqueue(Command.Attack(0, me, h[3], false, queued: true));
        sim.Tick();
        sim.Tick();
        Assert.Equal(h[1], u.Target[me.Index]);
        Assert.Equal(2, u.QueueCount[me.Index]);
        for (int t = 0; t < 5; t++) sim.Tick();
        sim.Enqueue(Command.Stop(0, me));
        sim.Tick();
        sim.Tick();
        Assert.Equal(0, u.QueueCount[me.Index]);
        Assert.Equal(default, u.Target[me.Index]);
        Assert.Equal(CombatMode.None, u.Mode[me.Index]);
        Assert.Equal(UnitState.Idle, u.State[me.Index]);
        // The Raiders are 20 m off (beyond the infantry's 14 m sight): nothing should pull it back to any of them.
        int[] hp = { u.Hp[h[1].Index], u.Hp[h[2].Index], u.Hp[h[3].Index] };
        for (int t = 0; t < 200; t++) sim.Tick();
        Assert.True(u.Target[me.Index] == default && u.State[me.Index] != UnitState.Attacking, $"after Stop: target {u.Target[me.Index]}, state {u.State[me.Index]}");
        Assert.Equal(hp, new[] { u.Hp[h[1].Index], u.Hp[h[2].Index], u.Hp[h[3].Index] });
    }

    // ---------- bad inputs ----------

    [Fact]
    public void ResolveEnemy_BadSlots_DeadEntities_Ties_AndNaN()
    {
        Simulation sim = CombatViewScene.Create();
        foreach (Command c in CombatViewScene.Spawns(sim)) sim.Enqueue(c);
        sim.Tick();
        sim.Tick();
        World w = sim.World;
        UnitStore u = w.Units;
        BuildingStore bs = w.Buildings;
        int tent = -1, billet = -1, enemyUnit = -1, ownUnit = -1;
        for (int i = 0; i < bs.Capacity; i++) if (bs.Alive[i]) { if (bs.Owner[i] == 1) tent = i; else billet = i; }
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i]) { if (u.Owner[i] == 1 && enemyUnit < 0) enemyUnit = i; if (u.Owner[i] == 0 && ownUnit < 0) ownUnit = i; }
        bool R(int unit, float ut, int b, float bt, float nt, out EntityHandle t, out bool isB) =>
            UnitPicker.ResolveEnemy(u.Alive, u.Owner, u.Generation, unit, ut, bs, b, bt, nt, 0, out t, out isB);
        float inf = float.PositiveInfinity;
        // Out-of-range slots never throw.
        Assert.False(R(u.Capacity, 1f, -1, inf, inf, out _, out _));
        Assert.False(R(int.MaxValue, 1f, bs.Capacity, 1f, inf, out _, out _));
        Assert.False(R(int.MinValue, 1f, int.MinValue, 1f, inf, out _, out _));
        // An exact tie between an enemy unit and the enemy Tent: the unit; between the own unit and the enemy Tent: no target.
        Assert.True(R(enemyUnit, 5f, tent, 5f, inf, out EntityHandle t, out bool isB));
        Assert.False(isB);
        Assert.Equal(enemyUnit, t.Index);
        Assert.False(R(ownUnit, 5f, tent, 5f, inf, out _, out _));
        // A prop exactly tied with the Tent: the Tent (ties go to the building).
        Assert.True(R(-1, inf, tent, 5f, 5f, out t, out isB));
        Assert.True(isB);
        // A NaN unit entry never makes a hidden building win over a nearer own unit... it is simply not a unit hit.
        bool nan = R(ownUnit, float.NaN, tent, 9f, inf, out t, out isB);
        _out.WriteLine($"NaN unit entry with the Tent at 9: resolves {nan} (building {isB})");
        // A dead enemy unit / dead Tent resolve to nothing.
        u.Free(new EntityHandle(enemyUnit, u.Generation[enemyUnit]));
        Assert.False(R(enemyUnit, 1f, -1, inf, inf, out _, out _));
        Assert.True(billet >= 0);
        // The own Billet nearer than an enemy unit: no target.
        int other = -1;
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.Owner[i] == 1) { other = i; break; }
        Assert.False(R(other, 6f, billet, 5f, inf, out _, out _));
    }

    [Fact]
    public void PickRay_DegenerateRays_NeverThrow_AndPickNothingOdd()
    {
        Simulation sim = Flat(units: 8);
        World w = sim.World;
        Place(sim, 1, Raider, new Vector2(40f, 40f));
        var target = new Vector3(40f, 1f, 40f);
        // Horizontal at body height from far outside the map: the unit (ground never met).
        Assert.True(Pick(w, 1f, new Vector3(-500f, 1f, 40f), Vector3.UnitX, out _) >= 0);
        // Infinite / NaN direction parts, tiny directions, origin inside the body: no throw, no garbage.
        Assert.Equal(-1, Pick(w, 1f, new Vector3(40f, 30f, 60f), new Vector3(float.PositiveInfinity, -1f, 0f), out _));
        Assert.Equal(-1, Pick(w, 1f, new Vector3(40f, 30f, 60f), new Vector3(0f, float.NaN, 0f), out _));
        Assert.True(Pick(w, 1f, new Vector3(40f, 30f, 60f), (target - new Vector3(40f, 30f, 60f)) * 1e-6f, out float tiny) >= 0, "a tiny (unnormalized) direction should still pick");
        Assert.True(float.IsFinite(tiny));
        Assert.Equal(-1, Pick(w, 1f, target, Vector3.UnitY, out _)); // starts inside: a camera never does
        // Ray from below the ground (camera under the map): nothing, or the unit, but never a throw.
        Pick(w, 1f, new Vector3(40f, -5f, 45f), new Vector3(0f, 1f, -1f), out _);
    }

    [Fact]
    public void HitFlash_FirstSight_EdgeRows()
    {
        var f = new HitFlash(4);
        int[] maxHp = { 100 };
        bool[] alive = { true, true, false, true };
        int[] gen = { 1, 1, 1, 1 };
        int[] type = { 0, 0, 0, -1 };
        int[] hp = { 100, 100, 50, 10 };
        // A match's first frame: full hp units don't flash, a dead slot doesn't, a negative type doesn't.
        f.Update(alive, gen, hp, type, maxHp, 0f);
        Assert.Equal(0, f.LitCount);
        // A slot that dies and is re-used between two frames by a hurt unit: one flash. Re-used again at full hp: none.
        gen[1] = 2;
        hp[1] = 99;
        f.Update(alive, gen, hp, type, maxHp, 0f);
        Assert.True(f.WasHit(1) && f.IsLit(1));
        gen[1] = 3;
        hp[1] = 100;
        f.Update(alive, gen, hp, type, maxHp, 0.01f);
        Assert.False(f.IsLit(1));
        // Paused frames (dt 0) don't fade a first-sight flash; a flash never lasts past Seconds of view time.
        gen[0] = 2;
        hp[0] = 1;
        for (int k = 0; k < 10; k++) f.Update(alive, gen, hp, type, maxHp, 0f);
        Assert.True(f.IsLit(0));
        float total = 0f;
        while (f.IsLit(0) && total < 5f) { f.Update(alive, gen, hp, type, maxHp, 0.016f); total += 0.016f; }
        Assert.InRange(total, HitFlash.DefaultSeconds - 0.02f, HitFlash.DefaultSeconds + 0.02f);
        // A maxHp span shorter than the type id: no flash, no throw.
        var g = new HitFlash(1);
        g.Update(new[] { true }, new[] { 1 }, new[] { 1 }, new[] { 3 }, maxHp, 0.01f);
        Assert.False(g.IsLit(0));
    }

    [Fact]
    public void TargetMark_BadDurations_NeverStickActive()
    {
        var inf = new TargetMark();
        inf.Mark(new EntityHandle(1, 1), false);
        inf.Update(float.PositiveInfinity);
        Assert.False(inf.Active);
        var neg = new TargetMark(-1f);
        neg.Mark(new EntityHandle(1, 1), false);
        Assert.False(neg.Active);
        var nan = new TargetMark(float.NaN);
        nan.Mark(new EntityHandle(1, 1), false);
        Assert.False(nan.Active);
        nan.Update(0.1f);
        Assert.False(nan.Active);
        // 0.5 s at 144 fps: gone after 72-73 frames, never later.
        var m = new TargetMark();
        m.Mark(new EntityHandle(2, 1), true);
        int frames = 0;
        while (m.Active && frames < 1000) { m.Update(1f / 144f); frames++; }
        Assert.InRange(frames, 72, 73);
    }

    // The first live unit (slot) whose capsule a 2 mm march along the ray enters, stopping at the ground; -1 for none.
    private static int FirstBody(World w, Vector3 o, Vector3 dir)
    {
        UnitStore u = w.Units;
        float len = dir.Length();
        Vector3 d = dir / len;
        for (float t = 0f; t < len * 2f; t += 0.002f)
        {
            Vector3 p = o + d * t;
            if (p.Y < TerrainHeight.At(w.Heightmap, p.X, p.Z) - 1e-3f) return -1;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                Vector2 c = u.Position[i];
                float r = u.Radius[i], y = TerrainHeight.At(w.Heightmap, c.X, c.Y);
                var a = new Vector3(c.X, y + r, c.Y);
                var b = new Vector3(c.X, y + r + Extra, c.Y);
                Vector3 ab = b - a;
                float s = Math.Clamp(Vector3.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f);
                if (Vector3.Distance(p, a + ab * s) <= r - 1e-3f) return i;
            }
        }
        return -1;
    }
}
