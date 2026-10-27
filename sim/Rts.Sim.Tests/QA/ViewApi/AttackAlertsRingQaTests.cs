using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>
/// QA M6-VH4 (session 2026-10-26-2108): BUG-0970's fix (<c>AttackAlerts.KeepUpgradedOldest</c>) changed ring code in the
/// view's own ViewApi file. (1) Fuzz: seeded random hits on small rings (capacity 1-5) over thousands of updates; after every
/// Update each <c>Upgraded(k)</c> names an alert that existed before the Update as Units and is now Base, distinct and inside
/// the ring; each <c>New(k)</c> is distinct, raised now, inside the ring; the ring stays oldest first; alerts that survive
/// keep their position and raise time. (2) Twin: alerts fed every tick from a fighting AI match leave the state hash equal to
/// an untouched twin's at every tick (the ViewApi rule: read-only).
/// </summary>
public class AttackAlertsRingQaTests
{
    private const int Me = 0;
    private readonly ITestOutputHelper _out;

    public AttackAlertsRingQaTests(ITestOutputHelper output) => _out = output;

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    [InlineData(5, 5)]
    [InlineData(3, 77)]
    [InlineData(2, 1234)]
    public void RandomHits_SmallRing_UpgradedAlwaysNamesTheUpgradedAlert(int capacity, int seed)
    {
        const int units = 24, buildings = 8;
        var rng = new Random(seed);
        var uAlive = new bool[units]; var uGen = new int[units]; var uHp = new int[units]; var uOwner = new int[units]; var uPos = new Vector2[units];
        var bAlive = new bool[buildings]; var bGen = new int[buildings]; var bHp = new int[buildings]; var bOwner = new int[buildings]; var bPos = new Vector2[buildings];
        // Spots on a coarse lattice, 40 m apart (over the 30 m radius), so hits both join and raise.
        Vector2 Spot() => new(rng.Next(0, 6) * 40f, rng.Next(0, 6) * 40f);
        for (int i = 0; i < units; i++) { uAlive[i] = true; uGen[i] = 1; uHp[i] = 1_000_000; uPos[i] = Spot(); }
        for (int i = 0; i < buildings; i++) { bAlive[i] = true; bGen[i] = 1; bHp[i] = 1_000_000; bPos[i] = Spot(); }
        var a = new AttackAlerts(units, buildings, capacity: capacity);
        a.Update(Me, 0f, uAlive, uGen, uHp, uOwner, uPos, bAlive, bGen, bHp, bOwner, bPos);
        int fullUpgradeRaises = 0, upgrades = 0;
        for (int step = 0; step < 4000; step++)
        {
            // Snapshot before the Update.
            var before = new List<(Vector2 pos, float at, AttackAlertKind kind, int hits)>();
            for (int k = 0; k < a.Count; k++) { int s = a.Recent(k); before.Add((a.Position(s), a.RaisedAt(s), a.Kind(s), a.HitsOn(s))); }
            int hitsU = rng.Next(0, 4), hitsB = rng.Next(0, 3);
            for (int h = 0; h < hitsU; h++) uHp[rng.Next(units)]--;
            for (int h = 0; h < hitsB; h++) bHp[rng.Next(buildings)]--;
            if (rng.Next(50) == 0) uPos[rng.Next(units)] = Spot();
            float dt = rng.Next(4) == 0 ? 3f : 0.05f;
            long raisedBefore = a.Raised;
            int raised = a.Update(Me, dt, uAlive, uGen, uHp, uOwner, uPos, bAlive, bGen, bHp, bOwner, bPos);
            string at = $"capacity {capacity} seed {seed} step {step}";

            Assert.True(a.Count <= capacity, at);
            Assert.Equal(raised, a.NewCount);
            var ring = new HashSet<int>();
            for (int k = 0; k < a.Count; k++) Assert.True(ring.Add(a.Recent(k)), $"{at}: ring slot listed twice");
            for (int k = 1; k < a.Count; k++)
                Assert.True(a.RaisedAt(a.Recent(k - 1)) <= a.RaisedAt(a.Recent(k)), $"{at}: ring not oldest first at {k}");
            var seenNew = new HashSet<int>();
            for (int k = 0; k < a.NewCount; k++)
            {
                int s = a.New(k);
                Assert.True(seenNew.Add(s) && ring.Contains(s), $"{at}: New({k}) = {s} repeated or outside the ring");
                Assert.Equal(a.Now, a.RaisedAt(s));
            }
            // The newest raise is the last New entry, unless the Update raised more than the ring holds (documented, BUG-0954).
            if (a.NewCount > 0 && a.Raised - raisedBefore <= capacity) Assert.Equal(a.New(a.NewCount - 1), a.LastIndex);
            var seenUp = new HashSet<int>();
            for (int k = 0; k < a.UpgradedCount; k++)
            {
                int s = a.Upgraded(k);
                Assert.True(seenUp.Add(s) && ring.Contains(s), $"{at}: Upgraded({k}) = {s} repeated or outside the ring");
                Assert.Equal(AttackAlertKind.Base, a.Kind(s));
                // BUG-0970: it is an alert that was there as Units before this Update.
                Vector2 p = a.Position(s);
                float t = a.RaisedAt(s);
                Assert.True(before.Exists(b => b.pos == p && b.at == t && b.kind == AttackAlertKind.Units), $"{at}: Upgraded({k}) names {p} raised {t}, not an alert upgraded this Update");
                Assert.False(seenNew.Contains(s), $"{at}: Upgraded({k}) is a new alert");
                upgrades++;
            }
            // Survivors keep their data; kinds only go Units -> Base; hits never drop.
            for (int k = 0; k < a.Count; k++)
            {
                int s = a.Recent(k);
                if (seenNew.Contains(s)) continue;
                int j = before.FindIndex(b => b.pos == a.Position(s) && b.at == a.RaisedAt(s));
                Assert.True(j >= 0, $"{at}: a surviving alert at {a.Position(s)} raised {a.RaisedAt(s)} was not in the ring before");
                Assert.True(a.HitsOn(s) >= before[j].hits && (before[j].kind == a.Kind(s) || a.Kind(s) == AttackAlertKind.Base), $"{at}: an alert's kind or hits went back");
            }
            if (a.Count == capacity && a.UpgradedCount > 0 && a.NewCount > 0) fullUpgradeRaises++;
        }
        _out.WriteLine($"capacity {capacity} seed {seed}: {upgrades} upgrades, {fullUpgradeRaises} updates with an upgrade and a raise on a full ring, {a.Raised} raised");
        // A ring of one drops the upgrade with the alert (the documented all-upgraded case), so the counter can't see it there.
        if (capacity > 1) Assert.True(fullUpgradeRaises > 0, "the fuzz never reached the BUG-0970 case");
    }

    [Fact]
    public void AlertsFedEveryTick_FromAFightingAiMatch_MoveNoHash()
    {
        GameData data = TestSim.Data;
        var seats = new[]
        {
            new PlayerSeat(data.FindFaction("malazan"), SeatController.Ai, AiDifficulty.Hard),
            new PlayerSeat(data.FindFaction("whirlwind"), SeatController.Ai, AiDifficulty.Normal),
        };
        Simulation a = AiScenes.Generated(seats, 5, record: false, out _, raiders: 12);
        Simulation b = AiScenes.Generated(seats, 5, record: false, out _, raiders: 12);
        World w = a.World;
        var alerts = new[] { new AttackAlerts(w.Units.Capacity, w.Buildings.Capacity, capacity: 2), new AttackAlerts(w.Units.Capacity, w.Buildings.Capacity, capacity: 2) };
        var centres = new Vector2[w.Buildings.Capacity];
        long raised = 0, upgraded = 0;
        for (int t = 0; t < 1200; t++)
        {
            for (int i = 0; i < centres.Length; i++)
                centres[i] = w.Buildings.Alive[i] ? StartBase.FootprintCenter(w.NavGrid, w.Data.Buildings[w.Buildings.TypeId[i]], w.Buildings.Cell[i]) : Vector2.Zero;
            for (int p = 0; p < 2; p++)
            {
                raised += alerts[p].Update(p, 0.05f, w.Units.Alive, w.Units.Generation, w.Units.Hp, w.Units.Owner, w.Units.Position,
                    w.Buildings.Alive, w.Buildings.Generation, w.Buildings.Hp, w.Buildings.Owner, centres);
                upgraded += alerts[p].UpgradedCount;
                for (int k = 0; k < alerts[p].UpgradedCount; k++) Assert.Equal(AttackAlertKind.Base, alerts[p].Kind(alerts[p].Upgraded(k)));
            }
            a.Tick();
            b.Tick();
            Assert.Equal(b.StateHash(), a.StateHash());
        }
        _out.WriteLine($"1200 ticks: {raised} alerts raised, {upgraded} upgrades");
        Assert.True(raised > 0, "no fighting: the twin test proved nothing");
    }
}
