using System.Numerics;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M4-V2: <see cref="UnitPicker"/> (the Attack order's ray pick of a unit's drawn capsule and the enemy rule), <see cref="TargetMark"/> (the red ring's timer) and the hit flash's first-sight rule (BUG-0160 item 2).</summary>
[Collection(SerialCollection.Name)]
public class UnitPickerTests
{
    // The game side's constants: UnitViews.ExtraBodyHeight, BuildingViews.BoxHeight / SiteMinHeight.
    private const float Extra = 1f, BoxHeight = 3f, SiteMin = 0.15f;

    private readonly ITestOutputHelper _out;

    public UnitPickerTests(ITestOutputHelper output) => _out = output;

    // The pickers take the unit store's spans (ViewApi never names the store); these pass a store's arrays.
    private static int Pick(UnitStore u, Heightmap map, float alpha, Vector3 origin, Vector3 direction, float extra, out float entry) =>
        UnitPicker.PickRay(u.Alive, u.PrevPosition, u.Position, u.Radius, map, alpha, origin, direction, extra, out entry);

    private static bool Resolve(UnitStore u, int unit, float unitT, BuildingStore b, int building, float buildingT, float nodeT, int local,
        out EntityHandle target, out bool isBuilding) =>
        UnitPicker.ResolveEnemy(u.Alive, u.Owner, u.Generation, unit, unitT, b, building, buildingT, nodeT, local, out target, out isBuilding);

    // ---- BUG-0190 item 2: a NaN entry counts as nearest ----

    [Fact]
    public void ResolveEnemy_NaNEntry_CountsAsNearest_NeverPicksTheBuildingBehindTheCallersUnit()
    {
        Simulation sim = CombatViewScene.Create();
        CombatViewScene.Start(sim);
        UnitStore u = sim.World.Units;
        BuildingStore b = sim.World.Buildings;
        int tent = -1, own = -1, enemy = -1;
        for (int i = 0; i < b.Capacity; i++) if (b.Alive[i] && b.Owner[i] == 1) tent = i;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            if (u.Owner[i] == 0 && own < 0) own = i;
            if (u.Owner[i] == 1 && enemy < 0) enemy = i;
        }
        Assert.True(tent >= 0 && own >= 0 && enemy >= 0);
        float inf = float.PositiveInfinity;
        // The QA row: an own unit with a NaN entry in front of the enemy Tent at 9 resolved to the Tent. Now: no target.
        Assert.False(Resolve(u, own, float.NaN, b, tent, 9f, inf, 0, out EntityHandle target, out bool isB));
        Assert.Equal(default, target);
        Assert.False(isB);
        // An enemy unit with a NaN entry is the target, not the Tent behind it.
        Assert.True(Resolve(u, enemy, float.NaN, b, tent, 9f, inf, 0, out target, out isB));
        Assert.Equal(enemy, target.Index);
        Assert.False(isB);
        // A NaN building entry is nearest among building and prop; a NaN prop entry blocks both.
        Assert.True(Resolve(u, -1, inf, b, tent, float.NaN, 4f, 0, out target, out isB));
        Assert.True(isB);
        Assert.False(Resolve(u, enemy, 3f, b, tent, 2f, float.NaN, 0, out _, out _));
        // No unit passed: its NaN entry changes nothing.
        Assert.True(Resolve(u, -1, float.NaN, b, tent, 9f, inf, 0, out target, out isB));
        Assert.True(isB);
    }

    // ---- The capsule ----

    [Fact]
    public void Capsule_SideTopBottomAndMisses()
    {
        var a = new Vector3(0f, 0.5f, 0f);
        var b = new Vector3(0f, 1.5f, 0f);
        const float r = 0.5f;
        // From the side at mid height: enters at x = -0.5.
        Assert.Equal(9.5f, UnitPicker.Capsule(new Vector3(-10f, 1f, 0f), Vector3.UnitX, a, b, r), 4);
        // Straight down onto the top cap: y = 2.
        Assert.Equal(8f, UnitPicker.Capsule(new Vector3(0f, 10f, 0f), -Vector3.UnitY, a, b, r), 4);
        // Straight up from below into the bottom cap: y = 0.
        Assert.Equal(10f, UnitPicker.Capsule(new Vector3(0f, -10f, 0f), Vector3.UnitY, a, b, r), 4);
        // Vertical ray just outside the radius, and a side ray over the top.
        Assert.True(UnitPicker.Capsule(new Vector3(0.51f, 10f, 0f), -Vector3.UnitY, a, b, r) < 0f);
        Assert.True(UnitPicker.Capsule(new Vector3(-10f, 2.01f, 0f), Vector3.UnitX, a, b, r) < 0f);
        // Pointing away.
        Assert.True(UnitPicker.Capsule(new Vector3(-10f, 1f, 0f), -Vector3.UnitX, a, b, r) < 0f);
        // Grazing the cap's rim from the side at y = 1.9 (inside the top sphere's cross-section, radius 0.3).
        float t = UnitPicker.Capsule(new Vector3(-10f, 1.9f, 0f), Vector3.UnitX, a, b, r);
        Assert.Equal(10f - MathF.Sqrt(r * r - 0.4f * 0.4f), t, 4);
    }

    [Fact]
    public void PickRay_BadInput_IsNone_AndAlphaIsClamped()
    {
        Heightmap map = LocalMovementTests.Flat(16);
        var u = new UnitStore(4);
        EntityHandle h = u.Alloc();
        u.Owner[h.Index] = 1;
        u.Radius[h.Index] = 0.5f;
        u.PrevPosition[h.Index] = new Vector2(10f, 10f);
        u.Position[h.Index] = new Vector2(20f, 10f);
        var down = new Vector3(0f, -1f, 0f);
        Assert.Equal(-1, Pick(u, map, 0f, new Vector3(float.NaN, 10f, 10f), down, Extra, out float e));
        Assert.True(float.IsPositiveInfinity(e));
        Assert.Equal(-1, Pick(u, map, 0f, new Vector3(10f, 10f, 10f), Vector3.Zero, Extra, out _));
        // Drawn at the previous position for alpha 0 (and below), at the current one for 1 (and above, and NaN).
        Assert.Equal(h.Index, Pick(u, map, -3f, new Vector3(10f, 10f, 10f), down, Extra, out _));
        Assert.Equal(-1, Pick(u, map, 0f, new Vector3(20f, 10f, 10f), down, Extra, out _));
        Assert.Equal(h.Index, Pick(u, map, 7f, new Vector3(20f, 10f, 10f), down, Extra, out _));
        Assert.Equal(h.Index, Pick(u, map, float.NaN, new Vector3(20f, 10f, 10f), down, Extra, out _));
        Assert.Equal(h.Index, Pick(u, map, 0.5f, new Vector3(15f, 10f, 10f), down, Extra, out float mid));
        // Top of the capsule: ground 0, height 2r + 1 = 2 m; the ray starts 10 m up, so 8 m down (direction length 1).
        Assert.Equal(8f, mid, 4);
        // An unnormalized direction gives the parameter in its own units.
        Pick(u, map, 0.5f, new Vector3(15f, 10f, 10f), down * 4f, Extra, out float scaled);
        Assert.Equal(2f, scaled, 4);
        u.Free(h);
        Assert.Equal(-1, Pick(u, map, 0.5f, new Vector3(15f, 10f, 10f), down, Extra, out _));
    }

    [Fact]
    public void PickRay_NearestOfTwoInLine_AndTheEnemyRule()
    {
        Heightmap map = LocalMovementTests.Flat(16);
        var u = new UnitStore(4);
        // The scene's two buildings: player 0's Billet and player 1's Tent.
        Simulation sim = CombatViewScene.Create();
        CombatViewScene.Start(sim);
        BuildingStore b = sim.World.Buildings;
        int billet = -1, tent = -1;
        for (int i = 0; i < b.Capacity; i++)
            if (b.Alive[i]) { if (b.Owner[i] == 0) billet = i; else tent = i; }
        Assert.True(billet >= 0 && tent >= 0);
        EntityHandle own = u.Alloc(), enemy = u.Alloc();
        foreach ((EntityHandle h, int owner, float x) in new[] { (own, 0, 10f), (enemy, 1, 14f) })
        {
            u.Owner[h.Index] = owner;
            u.Radius[h.Index] = 0.5f;
            u.Position[h.Index] = u.PrevPosition[h.Index] = new Vector2(x, 8f);
        }
        // A ray from the west at body height meets the own unit first: no target, though an enemy stands behind it.
        var o = new Vector3(0f, 1f, 8f);
        int hit = Pick(u, map, 1f, o, Vector3.UnitX, Extra, out float t);
        Assert.Equal(own.Index, hit);
        Assert.False(Resolve(u, hit, t, b, -1, float.PositiveInfinity, float.PositiveInfinity, 0, out _, out _));
        // From the east the enemy is first: an Attack target.
        hit = Pick(u, map, 1f, new Vector3(30f, 1f, 8f), -Vector3.UnitX, Extra, out t);
        Assert.Equal(enemy.Index, hit);
        Assert.True(Resolve(u, hit, t, b, -1, float.PositiveInfinity, float.PositiveInfinity, 0, out EntityHandle target, out bool isB));
        Assert.Equal(enemy, target);
        Assert.False(isB);
        // A prop or a building nearer than the unit wins; a nearer building of the enemy is a building target.
        Assert.False(Resolve(u, hit, t, b, -1, float.PositiveInfinity, t - 1f, 0, out _, out _));
        Assert.True(Resolve(u, hit, t, b, tent, t - 1f, float.PositiveInfinity, 0, out target, out isB));
        Assert.Equal(new EntityHandle(tent, b.Generation[tent]), target);
        Assert.True(isB);
        Assert.False(Resolve(u, hit, t, b, billet, t - 1f, float.PositiveInfinity, 0, out _, out _));
        // A building behind the unit doesn't matter.
        Assert.True(Resolve(u, hit, t, b, billet, t + 1f, float.PositiveInfinity, 0, out target, out isB));
        Assert.Equal(enemy, target);
        // Seen as local player 1, the roles swap.
        Assert.True(Resolve(u, own.Index, 1f, b, -1, float.PositiveInfinity, float.PositiveInfinity, 1, out target, out _));
        Assert.Equal(own, target);
        // A dead slot or -1 resolves to nothing.
        Assert.False(Resolve(u, -1, float.PositiveInfinity, b, -1, float.PositiveInfinity, float.PositiveInfinity, 0, out _, out _));
    }

    // ---- 1,000 rays over a brawl (acceptance criterion 1) ----

    [Theory]
    [InlineData(1UL)]
    [InlineData(6UL)]
    public void ThousandRays_OverABrawl_PickTheDrawnUnitOrNone_NeverAnOwnTarget(ulong seed)
    {
        Simulation sim = CombatViewScene.Create(seed);
        CombatViewScene.Start(sim);
        for (int t = 0; t < 60; t++) sim.Tick();
        RunRays(sim, seed, ticks: 10, perTick: 100, out int named, out int none, out int attacks);
        _out.WriteLine($"seed {seed}: 1,000 rays, {named} named their aimed unit, {none} none, {attacks} enemy targets");
        Assert.True(named >= 400, $"only {named} of 600 aimed rays named their unit");
        Assert.True(attacks >= 200, $"only {attacks} enemy targets");
    }

    [Fact]
    public void ARidgeBetweenCameraAndUnit_HidesIt_SeenFromAbove_ItIsPicked()
    {
        const int n = 16;
        var levels = new byte[n * n];
        var elev = new float[n * n];
        for (int y = 0; y < n; y++)
        {
            levels[y * n + 8] = 1;
            elev[y * n + 8] = MapConstants.LevelHeight; // a 4 m wall over x = 16-18 m
        }
        var ridge = new Heightmap(n, n, levels, elev);
        Heightmap flat = LocalMovementTests.Flat(n);
        var u = new UnitStore(2);
        EntityHandle h = u.Alloc();
        u.Owner[h.Index] = 1;
        u.Radius[h.Index] = 0.5f;
        u.Position[h.Index] = u.PrevPosition[h.Index] = new Vector2(24f, 16f);
        var body = new Vector3(24f, 1f, 16f);
        var low = new Vector3(0f, 5f, 16f);
        Assert.Equal(h.Index, Pick(u, flat, 1f, low, body - low, Extra, out _));
        Assert.Equal(-1, Pick(u, ridge, 1f, low, body - low, Extra, out _));
        var high = new Vector3(4f, 40f, 16f);
        Assert.Equal(h.Index, Pick(u, ridge, 1f, high, body - high, Extra, out _));
        // A unit standing at the wall's foot, its body against the slope: the ground met at its own feet doesn't hide it.
        u.Position[h.Index] = u.PrevPosition[h.Index] = new Vector2(19f, 16f);
        var top = new Vector3(19f, 40f, 16f);
        Assert.Equal(h.Index, Pick(u, ridge, 1f, top, new Vector3(0f, -1f, 0f), Extra, out _));
    }

    [Fact]
    public void Rays_OnAGeneratedMap_PicksAgreeWithTheOracle()
    {
        (Simulation sim, Vector2[][] blocks, StartBasePlan plan) = StartBaseTests.MatchSetup(3, 60, 5);
        StartBaseTests.Apply(sim, blocks, plan);
        RunRays(sim, 3, ticks: 4, perTick: 150, out int named, out _, out _);
        Assert.True(named >= 250, $"only {named} aimed rays named their unit");
    }

    // perTick rays after each of `ticks` ticks: 60 % aimed at a point inside a random live unit's drawn capsule from a
    // camera-like origin, 40 % at a random ground point near the units; each checked against a marching oracle.
    private void RunRays(Simulation sim, ulong seed, int ticks, int perTick, out int named, out int none, out int attacks)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        var rng = new Random((int)seed * 7919 + 17);
        named = none = attacks = 0;
        int own = 0, checkedRays = 0;
        var live = new List<int>();
        for (int tick = 0; tick < ticks; tick++)
        {
            sim.Tick();
            live.Clear();
            for (int i = 0; i < u.Capacity; i++) if (u.Alive[i]) live.Add(i);
            Assert.NotEmpty(live);
            for (int k = 0; k < perTick; k++)
            {
                float alpha = (float)rng.NextDouble();
                int aimed = live[rng.Next(live.Count)];
                Vector3 target;
                bool aim = k % 5 < 3;
                if (aim)
                {
                    Capsule(w, aimed, alpha, out Vector3 a, out Vector3 b, out float r);
                    target = Vector3.Lerp(a, b, (float)rng.NextDouble()) + new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * r;
                }
                else
                {
                    Vector2 p = Lerp(u, aimed, alpha) + new Vector2((float)rng.NextDouble() * 6f - 3f, (float)rng.NextDouble() * 6f - 3f);
                    target = new Vector3(p.X, TerrainHeight.At(w.Heightmap, p.X, p.Y), p.Y);
                }
                // The RTS camera: 20-45 m up, 10-35 m toward +z, up to 20 m to either side.
                var origin = target + new Vector3((float)rng.NextDouble() * 40f - 20f, 20f + (float)rng.NextDouble() * 25f, 10f + (float)rng.NextDouble() * 25f);
                Vector3 dir = target - origin;
                int pick = Pick(u, w.Heightmap, alpha, origin, dir, Extra, out float entry);
                int building = BuildingPicker.PickRay(w.Buildings, w.Data.Buildings, w.NavGrid, w.Heightmap, -1, origin, dir, BoxHeight, SiteMin, out float buildingT);
                if (Resolve(u, pick, entry, w.Buildings, building, buildingT, float.PositiveInfinity, 0, out EntityHandle enemy, out bool isB))
                {
                    attacks++;
                    int owner = isB ? w.Buildings.Owner[enemy.Index] : u.Owner[enemy.Index];
                    if (owner == 0) own++;
                    Assert.True(isB ? w.Buildings.Generation[enemy.Index] == enemy.Generation : u.IsAlive(enemy));
                }
                Check(w, alpha, origin, dir, pick, entry, $"seed {seed} tick {w.TickNumber} ray {k}");
                checkedRays++;
                if (pick < 0) none++;
                if (aim && pick == aimed) named++;
            }
        }
        Assert.Equal(ticks * perTick, checkedRays);
        Assert.Equal(0, own);
    }

    // The oracle: march the ray in 4 mm steps until it goes under the terrain; the first unit whose capsule (shrunk by 1 mm)
    // holds a step is certainly under the ray there. The pick must agree: none only if no unit is certainly hit; a pick's
    // entry point lies on its capsule's surface, no other unit is certainly hit before it, and any ground met before it is
    // at the picked unit's own feet (the picker's occlusion rule).
    private static void Check(World w, float alpha, Vector3 origin, Vector3 dir, int pick, float entry, string at)
    {
        UnitStore u = w.Units;
        float len = dir.Length();
        Vector3 d = dir / len;
        var near = new List<int>();
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            Vector2 c = Lerp(u, i, alpha);
            if (DistToLine2(new Vector2(origin.X, origin.Z), new Vector2(d.X, d.Z), c) <= u.Radius[i] + 0.01f) near.Add(i);
        }
        const float step = 0.004f, eps = 1e-3f;
        float max = len * 1.6f, groundStop = float.PositiveInfinity, hitT = float.PositiveInfinity;
        int hitUnit = -1;
        for (float t = 0f; t <= max && hitUnit < 0; t += step)
        {
            Vector3 p = origin + d * t;
            if (p.Y < TerrainHeight.At(w.Heightmap, p.X, p.Z) - eps)
            {
                groundStop = t;
                break;
            }
            foreach (int i in near)
            {
                Capsule(w, i, alpha, out Vector3 a, out Vector3 b, out float r);
                if (DistToSegment(p, a, b) <= r - eps)
                {
                    hitUnit = i;
                    hitT = t;
                    break;
                }
            }
        }
        if (pick < 0)
        {
            Assert.True(hitUnit < 0, $"{at}: picked none, but unit {hitUnit} is under the ray at {hitT:0.000} m");
            return;
        }
        float entryM = entry * len;
        Capsule(w, pick, alpha, out Vector3 pa, out Vector3 pb, out float pr);
        float surface = DistToSegment(origin + d * entryM, pa, pb);
        Assert.True(MathF.Abs(surface - pr) < 0.01f, $"{at}: unit {pick}'s entry point is {surface:0.0000} m from its axis, radius {pr}");
        if (hitUnit >= 0 && hitUnit != pick)
            Assert.True(hitT >= entryM - 0.01f, $"{at}: picked {pick} at {entryM:0.000} m, but unit {hitUnit} is under the ray at {hitT:0.000} m");
        if (groundStop < entryM - 0.01f)
        {
            Vector3 g = origin + d * groundStop;
            Vector2 c = Lerp(u, pick, alpha);
            Assert.True(Vector2.Distance(new Vector2(g.X, g.Z), c) <= pr + 0.05f,
                $"{at}: picked {pick} at {entryM:0.000} m behind the ground met at {groundStop:0.000} m away from its feet");
        }
    }

    private static Vector2 Lerp(UnitStore u, int i, float alpha) => Vector2.Lerp(u.PrevPosition[i], u.Position[i], Math.Clamp(alpha, 0f, 1f));

    // The drawn body: ground at the interpolated position, a capsule of radius r and height 2r + Extra.
    private static void Capsule(World w, int i, float alpha, out Vector3 a, out Vector3 b, out float r)
    {
        Vector2 p = Lerp(w.Units, i, alpha);
        r = w.Units.Radius[i];
        float y = TerrainHeight.At(w.Heightmap, p.X, p.Y);
        a = new Vector3(p.X, y + r, p.Y);
        b = new Vector3(p.X, y + r + Extra, p.Y);
    }

    private static float DistToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float s = Math.Clamp(Vector3.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f);
        return Vector3.Distance(p, a + ab * s);
    }

    // Distance from c to the 2D line through o along v (a vertical ray: distance to o).
    private static float DistToLine2(Vector2 o, Vector2 v, Vector2 c)
    {
        float l2 = v.LengthSquared();
        if (l2 < 1e-12f) return Vector2.Distance(o, c);
        float s = Vector2.Dot(c - o, v) / l2;
        return Vector2.Distance(o + v * s, c);
    }

    // ---- Allocation ----

    [Fact]
    public void PickRay_Resolve_Mark_At2000Units_AllocateZeroBytes()
    {
        Heightmap map = TerrainHeightTests.GeneratedMap(5);
        var u = new UnitStore(2000);
        BuildingStore b = CombatViewScene.Create().World.Buildings;
        for (int i = 0; i < 2000; i++)
        {
            EntityHandle h = u.Alloc();
            u.Owner[h.Index] = i % 2;
            u.Radius[h.Index] = 0.4f + i % 3 * 0.1f;
            u.Position[h.Index] = u.PrevPosition[h.Index] = new Vector2(10f + i % 50 * 1.5f, 10f + i / 50 * 1.5f);
        }
        var mark = new TargetMark();
        int sum = 0;
        Action block = () =>
        {
            for (int k = 0; k < 20; k++)
            {
                var o = new Vector3(20f + k * 2f, 40f, 60f);
                int p = Pick(u, map, 0.5f, o, new Vector3(0f, -1f, -0.8f), Extra, out float t);
                if (Resolve(u, p, t, b, -1, float.PositiveInfinity, float.PositiveInfinity, 0, out EntityHandle e, out bool isB))
                    mark.Mark(e, isB);
                mark.Update(0.016f);
                sum += p;
            }
        };
        block();
        int runs = AllocationProbe.AssertZero(block, _out);
        _out.WriteLine($"20 unit picks at 2,000 units + resolve + mark: 0 bytes (runs {runs}), checksum {sum}");
    }

    // ---- The ring's timer ----

    [Fact]
    public void TargetMark_LastsHalfASecond_ANewMarkRestartsAndMoves_BadTimeIsZero()
    {
        var m = new TargetMark();
        Assert.Equal(0.5f, TargetMark.DefaultSeconds);
        Assert.False(m.Active);
        var a = new EntityHandle(3, 2);
        m.Mark(a, isBuilding: false);
        Assert.True(m.Active);
        Assert.Equal(a, m.Target);
        for (int f = 0; f < 7; f++) m.Update(1f / 15f); // 0.467 s
        Assert.True(m.Active);
        m.Update(float.NaN);
        m.Update(-1f);
        Assert.True(m.Active);
        m.Update(1f / 15f); // 0.533 s
        Assert.False(m.Active);
        Assert.Equal(0f, m.Left);
        var b = new EntityHandle(7, 1);
        m.Mark(a, false);
        m.Update(0.4f);
        m.Mark(b, isBuilding: true);
        Assert.Equal(b, m.Target);
        Assert.True(m.IsBuilding);
        Assert.Equal(0.5f, m.Left);
        m.Clear();
        Assert.False(m.Active);
        Assert.Equal(3, m.Marks);
    }

    // ---- BUG-0160 item 2: the first-sight hit ----

    [Fact]
    public void HitFlash_UnitFirstSeenBelowItsTypeMax_FlashesOnce()
    {
        var f = new HitFlash(3);
        int[] maxHp = { 100, 60 };
        bool[] alive = { true, true, true };
        int[] gen = { 1, 1, 1 };
        int[] type = { 0, 1, 0 };
        int[] hp = { 100, 59, 0 };
        hp[2] = 40;
        f.Update(alive, gen, hp, type, maxHp, 0.05f);
        Assert.False(f.IsLit(0)); // full: no flash
        Assert.True(f.IsLit(1) && f.WasHit(1)); // 59 of 60 on first sight
        Assert.True(f.IsLit(2) && f.WasHit(2));
        Assert.Equal(2, f.HitCount);
        // Once: the next frame with the same hp fades it as any flash; no second first-sight hit.
        f.Update(alive, gen, hp, type, maxHp, 0.1f);
        Assert.False(f.WasHit(1));
        Assert.True(f.IsLit(1));
        f.Update(alive, gen, hp, type, maxHp, 0.1f);
        Assert.False(f.IsLit(1));
        // A new unit in a slot (generation changed) at full hp: no flash; below: one.
        gen[0] = 2;
        gen[1] = 2;
        hp[1] = 60;
        hp[0] = 99;
        f.Update(alive, gen, hp, type, maxHp, 0.01f);
        Assert.True(f.WasHit(0));
        Assert.False(f.WasHit(1) || f.IsLit(1));
        // The overload without types keeps the M4-V1 rule (first sight never lights).
        var g = new HitFlash(1);
        g.Update(new[] { true }, new[] { 1 }, new[] { 5 }, 0.01f);
        Assert.False(g.IsLit(0));
        // A type id out of range never flashes on first sight.
        var h = new HitFlash(1);
        h.Update(new[] { true }, new[] { 1 }, new[] { 5 }, new[] { 9 }, maxHp, 0.01f);
        Assert.False(h.IsLit(0));
    }
}
