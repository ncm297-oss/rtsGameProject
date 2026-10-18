using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Tests.ViewApi;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M6-V12 (2026-10-17-2344): <see cref="AttackAlerts"/> at its edges (29 / 30 / 31 m, 9.9 / 10.1 s, two raids on opposite
/// sides in one frame, a building and a unit in one frame, healing, more alerts in a frame than the ring holds) and driven
/// the way the HUD drives it on a real brawl (frames of 0 to 8 ticks, a random view delta), checked against an independent
/// oracle after every frame, with the sim's hash equal to a bare twin's (the alerts read the stores and never write them).
/// </summary>
public class AttackAlertsQaTests
{
    private const int Me = 0;
    private readonly ITestOutputHelper _out;

    public AttackAlertsQaTests(ITestOutputHelper output) => _out = output;

    // One own unit per slot, all alive, generation 1, hp 100.
    private sealed class Units
    {
        public readonly bool[] Alive;
        public readonly int[] Gen, Hp, Owner;
        public readonly Vector2[] Pos;
        public readonly bool[] BAlive = new bool[4];
        public readonly int[] BGen = new int[4], BHp = new int[4], BOwner = new int[4];
        public readonly Vector2[] BPos = new Vector2[4];

        public Units(params Vector2[] at)
        {
            Alive = new bool[at.Length];
            Gen = new int[at.Length];
            Hp = new int[at.Length];
            Owner = new int[at.Length];
            Pos = at;
            for (int i = 0; i < at.Length; i++) { Alive[i] = true; Gen[i] = 1; Hp[i] = 100; }
        }

        public int Frame(AttackAlerts a, float dt = 0.05f) => a.Update(Me, dt, Alive, Gen, Hp, Owner, Pos, BAlive, BGen, BHp, BOwner, BPos);
    }

    [Theory]
    [InlineData(29f, 0)]
    [InlineData(30f, 0)] // the radius itself is inside (<=)
    [InlineData(31f, 1)]
    public void ASecondHit_AtTheRadiusEdge(float metres, int secondAlerts)
    {
        var s = new Units(new Vector2(200, 200), new Vector2(200 + metres, 200));
        var a = new AttackAlerts(2, 4);
        s.Frame(a);
        s.Hp[0]--;
        Assert.Equal(1, s.Frame(a));
        s.Hp[1]--;
        Assert.Equal(secondAlerts, s.Frame(a));
        Assert.Equal(1 + secondAlerts, a.Raised);
    }

    [Theory]
    [InlineData(198, 0)] // 9.9 s of 0.05 s frames after the first alert
    [InlineData(202, 1)] // 10.1 s
    public void TheSameSpot_AroundTenSeconds_OnFrameSizedSteps(int frames, int alerts)
    {
        var s = new Units(new Vector2(10, 10));
        var a = new AttackAlerts(1, 4);
        s.Frame(a);
        s.Hp[0]--;
        Assert.Equal(1, s.Frame(a));
        for (int f = 1; f < frames; f++) s.Frame(a);
        s.Hp[0]--;
        Assert.Equal(alerts, s.Frame(a));
    }

    [Fact]
    public void TwoRaids_OppositeSidesOfTheBase_SameFrame_TwoAlerts()
    {
        // A hall 12 m across: raids on its west and east ends, 70 m apart (each side's buildings), in one frame.
        var s = new Units(new Vector2(65, 100), new Vector2(135, 100));
        s.BAlive[0] = s.BAlive[1] = true;
        s.BGen[0] = s.BGen[1] = 1;
        s.BHp[0] = s.BHp[1] = 1000;
        s.BPos[0] = new Vector2(60, 100);
        s.BPos[1] = new Vector2(140, 100);
        var a = new AttackAlerts(2, 4);
        s.Frame(a);
        s.BHp[0] -= 10;
        s.BHp[1] -= 10;
        s.Hp[0] -= 5;
        s.Hp[1] -= 5;
        Assert.Equal(2, s.Frame(a));
        Assert.Equal(AttackAlertKind.Base, a.Kind(a.New(0)));
        Assert.Equal(AttackAlertKind.Base, a.Kind(a.New(1)));
        Assert.NotEqual(a.Position(a.New(0)), a.Position(a.New(1)));
        Assert.Equal(2, a.HitsOn(a.New(0)));
        Assert.Equal(2, a.HitsOn(a.New(1)));
        Assert.Equal(a.New(1), a.LastIndex);
    }

    [Fact]
    public void HealingAndConstruction_RaiseNothing_ThenAHitDoes()
    {
        var s = new Units(new Vector2(10, 10));
        s.BAlive[0] = true;
        s.BGen[0] = 1;
        s.BHp[0] = 50; // a site, its hp rising as it is built
        s.BPos[0] = new Vector2(30, 30);
        s.Hp[0] = 40; // hurt before the first frame (first sight), healed below
        var a = new AttackAlerts(1, 4);
        s.Frame(a);
        for (int f = 0; f < 20; f++)
        {
            s.BHp[0] += 25;
            s.Hp[0] = 60 + f; // a heal
            Assert.Equal(0, s.Frame(a));
        }
        s.BHp[0]--;
        Assert.Equal(1, s.Frame(a));
        Assert.Equal(AttackAlertKind.Base, a.Kind(a.LastIndex));
        Assert.Equal(1, a.Hits);
    }

    [Fact]
    public void MoreAlertsInOneFrameThanTheRingHolds_NewListsDistinctKeptOnes_LastIsTheNewest()
    {
        var pos = new Vector2[12];
        for (int i = 0; i < pos.Length; i++) pos[i] = new Vector2(i * 100f, 0f);
        var s = new Units(pos);
        var a = new AttackAlerts(12, 4, capacity: 8);
        s.Frame(a);
        for (int i = 0; i < pos.Length; i++) s.Hp[i]--;
        Assert.Equal(8, s.Frame(a));
        var seen = new HashSet<Vector2>();
        for (int k = 0; k < a.NewCount; k++) Assert.True(seen.Add(a.Position(a.New(k))), $"New({k}) repeats {a.Position(a.New(k))}");
        Assert.Equal(new Vector2(1100, 0), a.Position(a.LastIndex));
        Assert.Contains(new Vector2(1100, 0), seen);
    }

    // QA M6-V12 fix round 1 (BUG-0952): driven as AlertHud does it now, SetNow(tick x 0.05 s) then a dt-0 step per tick,
    // a unit hit on every tick re-alerts every 200 ticks (10 s of game time). The clock is a float product, so the
    // difference of two tick times can round just under 10 s (2 of 19 gaps in the first 4,000 ticks): the gap is then 201,
    // never 199 or 202+. A pure function of the tick, so a replay agrees.
    [Theory]
    [InlineData(0)]
    [InlineData(72_000)] // 1 h of game time
    [InlineData(288_000)] // 4 h
    public void TickClock_HitEveryTick_ReAlertsEvery200Ticks_Or201FromFloatRounding(int startTick)
    {
        var s = new Units(new Vector2(10, 10));
        s.Hp[0] = 1_000_000;
        var a = new AttackAlerts(1, 4);
        a.SetNow(startTick * 0.05f);
        s.Frame(a, 0f);
        int last = -1, gaps200 = 0, gaps201 = 0;
        for (int t = startTick + 1; t <= startTick + 4_000; t++)
        {
            s.Hp[0]--; // a hit on every tick
            a.SetNow(t * 0.05f);
            if (s.Frame(a, 0f) == 0) continue;
            if (last >= 0)
            {
                int gap = t - last;
                Assert.True(gap is 200 or 201, $"start {startTick}: alerts at ticks {last} and {t}, {gap} apart");
                if (gap == 200) gaps200++;
                if (gap == 201) gaps201++;
            }
            last = t;
        }
        _out.WriteLine($"start tick {startTick}: gaps of 200 ticks {gaps200}, of 201 ticks {gaps201}");
        Assert.True(gaps200 + gaps201 >= 15, $"start {startTick}: only {gaps200 + gaps201} re-alerts in 4,000 ticks");
    }

    [Theory]
    [InlineData(1UL, AttackAlerts.DefaultRadius, AttackAlerts.DefaultSeconds)]
    [InlineData(5UL, AttackAlerts.DefaultRadius, AttackAlerts.DefaultSeconds)]
    [InlineData(23UL, AttackAlerts.DefaultRadius, AttackAlerts.DefaultSeconds)]
    [InlineData(1UL, 3f, 0.5f)] // tight: many alerts, the ring wraps
    [InlineData(5UL, 3f, 0.5f)]
    public void BrawlFrames_MatchAnOracle_AndTheSimHashEqualsABareTwin(ulong seed, float radius, float seconds)
    {
        Simulation a = CombatViewScene.Create(seed), b = CombatViewScene.Create(seed);
        CombatViewScene.Start(a, b);
        foreach (Simulation s in new[] { a, b })
        {
            TestSim.KeepIn(s, 0, nearCorner: true);
            TestSim.KeepIn(s, 1);
        }
        World w = a.World;
        UnitStore u = w.Units;
        BuildingStore bs = w.Buildings;
        var rng = new Random((int)seed * 104729);
        var alerts = new[] { new AttackAlerts(u.Capacity, bs.Capacity, radius, seconds), new AttackAlerts(u.Capacity, bs.Capacity, radius, seconds) };
        var oracle = new[] { new Oracle(u.Capacity, bs.Capacity, radius, seconds), new Oracle(u.Capacity, bs.Capacity, radius, seconds) };
        var centres = new Vector2[bs.Capacity];
        int frames = 0, multi = 0, upgrades = 0;
        while (w.TickNumber < 2500)
        {
            int ticks = rng.Next(9);
            if (ticks > 1) multi++;
            for (int t = 0; t < ticks; t++)
            {
                a.Tick();
                b.Tick();
            }
            for (int i = 0; i < bs.Capacity; i++)
            {
                int c = bs.Cell[i];
                centres[i] = w.NavGrid.CellCenter(c % w.NavGrid.Width, c / w.NavGrid.Width);
            }
            float dt = ticks == 0 ? 0f : 0.004f + (float)rng.NextDouble() * 0.05f;
            for (int p = 0; p < 2; p++)
            {
                int raised = alerts[p].Update(p, dt, u.Alive, u.Generation, u.Hp, u.Owner, u.Position, bs.Alive, bs.Generation, bs.Hp, bs.Owner, centres);
                int want = oracle[p].Update(p, dt, u, bs, centres);
                Assert.True(want == raised, $"seed {seed} tick {w.TickNumber} player {p}: {raised} alerts, oracle {want}");
                Assert.Equal(oracle[p].Raised, alerts[p].Raised);
                Assert.Equal(oracle[p].Hits, alerts[p].Hits);
                Assert.Equal(oracle[p].LastUpgrades, alerts[p].UpgradedCount); // M6-V13
                upgrades += alerts[p].UpgradedCount;
                if (alerts[p].HasLast)
                {
                    Assert.Equal(oracle[p].LastAt, alerts[p].Position(alerts[p].LastIndex));
                    Assert.Equal(oracle[p].LastKind, alerts[p].Kind(alerts[p].LastIndex));
                }
            }
            Assert.Equal(b.StateHash(), a.StateHash());
            frames++;
        }
        _out.WriteLine($"seed {seed} r {radius} s {seconds}: {frames} frames ({multi} multi-tick) to tick {w.TickNumber}; alerts p0 {alerts[0].Raised} from {alerts[0].Hits} hits, p1 {alerts[1].Raised} from {alerts[1].Hits}; {upgrades} upgraded to Base");
        Assert.True(alerts[0].Raised + alerts[1].Raised >= 2, "the brawl raised fewer than two alerts");
        Assert.True(alerts[0].Hits + alerts[1].Hits > 20, "the brawl had almost no hits");
    }

    // An independent restatement of the docs/03 rule: an own slot whose hp fell in the same generation is a hit; a hit within
    // 30 m of an alert raised less than 10 s ago joins it (a building hit turns a Units alert Base, M6-V13), else raises one;
    // buildings before units. Lists, no ring tricks.
    private sealed class Oracle
    {
        private readonly int[] _uHp, _uGen, _bHp, _bGen;
        private readonly List<(Vector2 At, float When, AttackAlertKind Kind)> _all = new();
        private readonly float _radius, _seconds;
        private float _now;

        public Oracle(int units, int buildings, float radius, float seconds)
        {
            _radius = radius;
            _seconds = seconds;
            _uHp = new int[units];
            _uGen = Enumerable.Repeat(-1, units).ToArray();
            _bHp = new int[buildings];
            _bGen = Enumerable.Repeat(-1, buildings).ToArray();
        }

        public long Raised => _all.Count;
        public long Hits { get; private set; }
        public long Upgrades { get; private set; }
        public Vector2 LastAt => _all[^1].At;
        public AttackAlertKind LastKind => _all[^1].Kind;

        public int Update(int player, float dt, UnitStore u, BuildingStore b, Vector2[] centres)
        {
            if (dt > 0f) _now += dt;
            long upgradesBefore = Upgrades;
            int before = _all.Count;
            for (int i = 0; i < b.Capacity; i++) Look(b.Alive[i] && b.Owner[i] == player, b.Generation[i], b.Hp[i], _bHp, _bGen, i, centres[i], AttackAlertKind.Base);
            for (int i = 0; i < u.Capacity; i++) Look(u.Alive[i] && u.Owner[i] == player, u.Generation[i], u.Hp[i], _uHp, _uGen, i, u.Position[i], AttackAlertKind.Units);
            LastUpgrades = (int)(Upgrades - upgradesBefore);
            return _all.Count - before;
        }

        public int LastUpgrades { get; private set; }

        private void Look(bool own, int gen, int hp, int[] lastHp, int[] lastGen, int i, Vector2 at, AttackAlertKind kind)
        {
            if (!own) { lastGen[i] = -1; return; }
            if (gen != lastGen[i]) { lastGen[i] = gen; lastHp[i] = hp; return; }
            bool hit = hp < lastHp[i];
            lastHp[i] = hp;
            if (!hit) return;
            Hits++;
            // Only the newest 32 count (the ring), as documented.
            int from = Math.Max(0, _all.Count - AttackAlerts.DefaultCapacity);
            for (int k = from; k < _all.Count; k++)
                if (_now - _all[k].When < _seconds && Vector2.DistanceSquared(_all[k].At, at) <= _radius * _radius)
                {
                    // M6-V13 (BUG-0954 item 1): a building hit joining a Units alert makes it Base; never the other way.
                    if (kind == AttackAlertKind.Base && _all[k].Kind == AttackAlertKind.Units)
                    {
                        _all[k] = (_all[k].At, _all[k].When, AttackAlertKind.Base);
                        Upgrades++;
                    }
                    return;
                }
            _all.Add((at, _now, kind));
        }
    }
}
