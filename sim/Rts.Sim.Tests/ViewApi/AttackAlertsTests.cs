using System.Numerics;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M6-V12: the attack alerts the HUD raises (text, minimap ping, sound) from the own stores' hp, like the hit flash.</summary>
[Collection(SerialCollection.Name)]
public class AttackAlertsTests
{
    private const int Me = 0, Enemy = 1;
    private readonly ITestOutputHelper _out;

    public AttackAlertsTests(ITestOutputHelper output) => _out = output;

    // A tiny pair of stores: units and buildings as the spans the view hands over.
    private sealed class Stores
    {
        public readonly bool[] UnitAlive, BuildingAlive;
        public readonly int[] UnitGen, UnitHp, UnitOwner, BuildingGen, BuildingHp, BuildingOwner;
        public readonly Vector2[] UnitPos, BuildingPos;

        public Stores(int units, int buildings)
        {
            UnitAlive = new bool[units];
            UnitGen = new int[units];
            UnitHp = new int[units];
            UnitOwner = new int[units];
            UnitPos = new Vector2[units];
            BuildingAlive = new bool[buildings];
            BuildingGen = new int[buildings];
            BuildingHp = new int[buildings];
            BuildingOwner = new int[buildings];
            BuildingPos = new Vector2[buildings];
        }

        public void Unit(int slot, int owner, Vector2 at, int hp = 100, int gen = 1)
        {
            UnitAlive[slot] = true;
            UnitOwner[slot] = owner;
            UnitPos[slot] = at;
            UnitHp[slot] = hp;
            UnitGen[slot] = gen;
        }

        public void Building(int slot, int owner, Vector2 at, int hp = 1000, int gen = 1)
        {
            BuildingAlive[slot] = true;
            BuildingOwner[slot] = owner;
            BuildingPos[slot] = at;
            BuildingHp[slot] = hp;
            BuildingGen[slot] = gen;
        }

        public int Frame(AttackAlerts a, float dt = 1f / 60f, int player = Me) =>
            a.Update(player, dt, UnitAlive, UnitGen, UnitHp, UnitOwner, UnitPos, BuildingAlive, BuildingGen, BuildingHp, BuildingOwner, BuildingPos);
    }

    [Fact]
    public void AHit_RaisesOneAlert_AtTheUnit()
    {
        var s = new Stores(8, 4);
        s.Unit(3, Me, new Vector2(50, 60));
        var a = new AttackAlerts(8, 4);
        Assert.Equal(0, s.Frame(a)); // first sight: remembered, nothing raised
        s.UnitHp[3] = 90;
        Assert.Equal(1, s.Frame(a));
        int k = a.New(0);
        Assert.Equal(AttackAlertKind.Units, a.Kind(k));
        Assert.Equal(new Vector2(50, 60), a.Position(k));
        Assert.Equal(1, a.HitsOn(k));
        Assert.True(a.HasLast);
        Assert.Equal(k, a.LastIndex);
        Assert.Equal(1, a.Raised);
        Assert.Equal(0, s.Frame(a)); // no new fall, nothing
    }

    [Fact]
    public void RepeatedHitsWithin30mAnd10s_RaiseNone_AndCountOnTheAlert()
    {
        var s = new Stores(8, 4);
        s.Unit(0, Me, new Vector2(100, 100));
        s.Unit(1, Me, new Vector2(129, 100)); // 29 m from unit 0
        var a = new AttackAlerts(8, 4);
        s.Frame(a);
        s.UnitHp[0] -= 10;
        Assert.Equal(1, s.Frame(a, 0.1f));
        int first = a.LastIndex;
        // 9.8 s of hits on both, every 0.5 s
        for (int t = 0; t < 19; t++)
        {
            s.UnitHp[0] -= 1;
            s.UnitHp[1] -= 1;
            Assert.Equal(0, s.Frame(a, 0.5f));
        }
        Assert.Equal(1, a.Raised);
        Assert.Equal(1 + 19 * 2, a.HitsOn(first));
        Assert.Equal(39, a.Hits);
    }

    [Fact]
    public void AHit40mAway_RaisesASecond()
    {
        var s = new Stores(8, 4);
        s.Unit(0, Me, new Vector2(100, 100));
        s.Unit(1, Me, new Vector2(140, 100));
        var a = new AttackAlerts(8, 4);
        s.Frame(a);
        s.UnitHp[0] -= 5;
        Assert.Equal(1, s.Frame(a));
        s.UnitHp[1] -= 5;
        Assert.Equal(1, s.Frame(a));
        Assert.Equal(new Vector2(140, 100), a.Position(a.LastIndex));
        Assert.Equal(2, a.Raised);
        Assert.Equal(2, a.Count);
    }

    [Fact]
    public void TheSameSpotAfter10s_RaisesAgain()
    {
        var s = new Stores(4, 1);
        s.Unit(0, Me, new Vector2(10, 10));
        var a = new AttackAlerts(4, 1);
        s.Frame(a);
        s.UnitHp[0] -= 1;
        Assert.Equal(1, s.Frame(a, 0f));
        s.UnitHp[0] -= 1;
        Assert.Equal(0, s.Frame(a, 9.9f));
        s.UnitHp[0] -= 1;
        Assert.Equal(1, s.Frame(a, 0.2f)); // 10.1 s after the first
        Assert.Equal(2, a.Raised);
    }

    [Fact]
    public void ANewGeneration_RaisesNone_EvenHurt()
    {
        var s = new Stores(4, 2);
        s.Unit(0, Me, new Vector2(10, 10), hp: 100, gen: 1);
        s.Building(0, Me, new Vector2(30, 30), hp: 500, gen: 1);
        var a = new AttackAlerts(4, 2);
        s.Frame(a);
        // The slot freed and re-used by a new unit already below its maximum (trained into a fight), and a new building.
        s.Unit(0, Me, new Vector2(10, 10), hp: 40, gen: 2);
        s.Building(0, Me, new Vector2(30, 30), hp: 10, gen: 2);
        Assert.Equal(0, s.Frame(a));
        // A dead slot is forgotten; alive again in the same generation counts as first sight too.
        s.UnitAlive[0] = false;
        s.Frame(a);
        s.UnitAlive[0] = true;
        s.UnitHp[0] = 20;
        Assert.Equal(0, s.Frame(a));
        Assert.Equal(0, a.Raised);
        Assert.Equal(0, a.Hits);
    }

    [Fact]
    public void OnlyThePlayersOwn_AndBuildingsComeFirst()
    {
        var s = new Stores(4, 2);
        s.Unit(0, Enemy, new Vector2(10, 10));
        s.Unit(1, Me, new Vector2(12, 10));
        s.Building(0, Me, new Vector2(15, 10));
        var a = new AttackAlerts(4, 2);
        s.Frame(a);
        s.UnitHp[0] -= 50; // the enemy's: no alert for player 0
        Assert.Equal(0, s.Frame(a));
        // A raid that hits a building and a unit 3 m apart in one frame: one alert, about the base.
        s.UnitHp[1] -= 5;
        s.BuildingHp[0] -= 5;
        Assert.Equal(1, s.Frame(a));
        Assert.Equal(AttackAlertKind.Base, a.Kind(a.New(0)));
        Assert.Equal(new Vector2(15, 10), a.Position(a.New(0)));
        Assert.Equal(2, a.HitsOn(a.New(0)));
        // Player 1's view of the same stores: its unit's earlier fall was seen first-sight, so nothing yet; a new fall raises.
        var b = new AttackAlerts(4, 2);
        s.Frame(b, player: Enemy);
        s.UnitHp[0] -= 1;
        Assert.Equal(1, s.Frame(b, player: Enemy));
    }

    // ---- BUG-0954 item 1 (M6-V13): a building hit joining a Units alert upgrades it to Base; never a downgrade ----

    /// <summary>A unit hit raises a Units alert; a building hit 30 m away 9.95 s later joins it and makes it Base (no second alert).</summary>
    [Fact]
    public void ABuildingHitJoiningAUnitsAlert_UpgradesItToBase_AtThe30mAnd10sEdges()
    {
        var s = new Stores(4, 2);
        s.Unit(0, Me, new Vector2(100, 100));
        s.Building(0, Me, new Vector2(130, 100)); // exactly 30 m: inside (the join is <= Radius)
        var a = new AttackAlerts(4, 2);
        s.Frame(a, 0f);
        s.UnitHp[0] -= 5;
        Assert.Equal(1, s.Frame(a, 0f));
        int alert = a.New(0);
        Assert.Equal(AttackAlertKind.Units, a.Kind(alert));
        s.BuildingHp[0] -= 5;
        Assert.Equal(0, s.Frame(a, 9.95f)); // 9.95 s: inside the 10 s
        Assert.Equal(AttackAlertKind.Base, a.Kind(alert));
        Assert.Equal(1, a.Raised);
        Assert.Equal(2, a.HitsOn(alert));
        Assert.Equal(new Vector2(100, 100), a.Position(alert)); // the kind changes, not the place
        Assert.Equal(1, a.UpgradedCount);
        Assert.Equal(alert, a.Upgraded(0));
        // The next step reports no upgrade again.
        s.BuildingHp[0] -= 5;
        Assert.Equal(0, s.Frame(a, 0f));
        Assert.Equal(0, a.UpgradedCount);
    }

    /// <summary>Just past the edges the building hit joins nothing: it raises its own Base alert and the Units one stays Units.</summary>
    [Theory]
    [InlineData(30.5f, 1f)]  // 30.5 m: outside the radius
    [InlineData(10f, 10f)]   // 10 s later: the first alert no longer suppresses
    public void ABuildingHitPastTheEdges_RaisesItsOwnBaseAlert_TheUnitsAlertStaysUnits(float metres, float seconds)
    {
        var s = new Stores(4, 2);
        s.Unit(0, Me, new Vector2(100, 100));
        s.Building(0, Me, new Vector2(100 + metres, 100));
        var a = new AttackAlerts(4, 2);
        s.Frame(a, 0f);
        s.UnitHp[0] -= 5;
        Assert.Equal(1, s.Frame(a, 0f));
        int units = a.New(0);
        s.BuildingHp[0] -= 5;
        Assert.Equal(1, s.Frame(a, seconds));
        Assert.Equal(AttackAlertKind.Units, a.Kind(units));
        Assert.Equal(AttackAlertKind.Base, a.Kind(a.New(0)));
        Assert.Equal(0, a.UpgradedCount);
    }

    /// <summary>A unit hit joining a Base alert leaves it Base: never a downgrade; a building first is Base from the start, no upgrade.</summary>
    [Fact]
    public void AUnitHitJoiningABaseAlert_NeverDowngrades_AndABuildingFirstIsNoUpgrade()
    {
        var s = new Stores(4, 2);
        s.Unit(0, Me, new Vector2(105, 100));
        s.Building(0, Me, new Vector2(100, 100));
        var a = new AttackAlerts(4, 2);
        s.Frame(a, 0f);
        s.BuildingHp[0] -= 5;
        Assert.Equal(1, s.Frame(a, 0f));
        int alert = a.New(0);
        Assert.Equal(AttackAlertKind.Base, a.Kind(alert));
        Assert.Equal(0, a.UpgradedCount);
        for (int t = 0; t < 5; t++)
        {
            s.UnitHp[0] -= 1;
            Assert.Equal(0, s.Frame(a, 1f));
            Assert.Equal(AttackAlertKind.Base, a.Kind(alert));
            Assert.Equal(0, a.UpgradedCount);
        }
        // A second building hit on the Base alert: still no upgrade reported.
        s.BuildingHp[0] -= 1;
        s.Frame(a, 1f);
        Assert.Equal(0, a.UpgradedCount);
        Assert.Equal(7, a.HitsOn(alert));
    }

    /// <summary>Two buildings hit in one step on one Units alert: one upgrade reported, once.</summary>
    [Fact]
    public void TwoBuildingHitsInOneStep_UpgradeOnce()
    {
        var s = new Stores(4, 4);
        s.Unit(0, Me, new Vector2(100, 100));
        s.Building(0, Me, new Vector2(110, 100));
        s.Building(1, Me, new Vector2(90, 100));
        var a = new AttackAlerts(4, 4);
        s.Frame(a, 0f);
        s.UnitHp[0] -= 5;
        s.Frame(a, 0f);
        int alert = a.New(0);
        s.BuildingHp[0] -= 5;
        s.BuildingHp[1] -= 5;
        Assert.Equal(0, s.Frame(a, 1f));
        Assert.Equal(AttackAlertKind.Base, a.Kind(alert));
        Assert.Equal(1, a.UpgradedCount);
        Assert.Equal(alert, a.Upgraded(0));
    }

    [Fact]
    public void BadDt_CountsAsZero_AndTheRingKeepsTheNewest()
    {
        var s = new Stores(40, 1);
        for (int i = 0; i < 40; i++) s.Unit(i, Me, new Vector2(i * 100, 0));
        var a = new AttackAlerts(40, 1, capacity: 8);
        s.Frame(a, float.NaN);
        Assert.Equal(0f, a.Now);
        s.Frame(a, -3f);
        Assert.Equal(0f, a.Now);
        for (int i = 0; i < 40; i++) s.UnitHp[i] -= 1;
        Assert.Equal(8, s.Frame(a)); // 40 alerts 100 m apart; the new list holds the ring's 8
        Assert.Equal(40, a.Raised);
        Assert.Equal(8, a.Count);
        Assert.Equal(new Vector2(3900, 0), a.Position(a.LastIndex));
        Assert.Equal(new Vector2(3200, 0), a.Position(a.Recent(0)));
    }

    // BUG-0952: the HUD sets the clock to tick x tick length before each tick's step. A raid hitting one unit every tick for
    // 30 s of game time raises at ticks 1, 201, 401 (every 10 s of game time) however many ticks a frame ran, and the same
    // run stepped again (a replay at another speed) raises at the same ticks.
    [Fact]
    public void SteppedPerTick_TheTenSecondsAreGameTime_SameTicksOnReplay()
    {
        float tick = (float)FixedStepClock.TickSeconds;
        var first = RaidTicks(tick, ticksPerFrame: 1);
        Assert.Equal(new[] { 1, 201, 401 }, first);
        Assert.Equal(first, RaidTicks(tick, ticksPerFrame: 8));   // 8x: eight ticks per frame
        Assert.Equal(first, RaidTicks(tick, ticksPerFrame: 3));
    }

    // The tick numbers that raised an alert. Frames only group ticks (the runner raises Ticked per tick), so the grouping
    // must not matter; it is passed to prove that.
    private static List<int> RaidTicks(float tickSeconds, int ticksPerFrame)
    {
        var s = new Stores(2, 1);
        s.Unit(0, Me, new Vector2(10, 10), hp: 100_000);
        var a = new AttackAlerts(2, 1);
        var raised = new List<int>();
        for (int t = 0; t <= 600; t += ticksPerFrame)
        {
            for (int k = t; k < t + ticksPerFrame && k <= 600; k++)
            {
                if (k > 0) s.UnitHp[0] -= 1;
                a.SetNow(k * tickSeconds);
                if (s.Frame(a, 0f) > 0) raised.Add(k);
            }
        }
        return raised;
    }

    [Fact]
    public void SetNow_IgnoresBadValues()
    {
        var a = new AttackAlerts(1, 1);
        a.SetNow(12.5f);
        Assert.Equal(12.5f, a.Now);
        a.SetNow(float.NaN);
        a.SetNow(-1f);
        a.SetNow(float.PositiveInfinity);
        Assert.Equal(12.5f, a.Now);
    }

    [Fact]
    public void AFrame_2000Units256Buildings_AllocatesZeroBytes()
    {
        const int units = 2000, buildings = 256;
        var s = new Stores(units, buildings);
        for (int i = 0; i < units; i++) s.Unit(i, i % 2, new Vector2(i % 50 * 7f, i / 50 * 7f));
        for (int i = 0; i < buildings; i++) s.Building(i, i % 2, new Vector2(i * 9f, 400f));
        var a = new AttackAlerts(units, buildings);
        s.Frame(a);
        int frame = 0;
        Action block = () =>
        {
            // A brawl: a tenth of everything hit each frame.
            for (int i = frame % 10; i < units; i += 10) s.UnitHp[i]--;
            for (int i = frame % 10; i < buildings; i += 10) s.BuildingHp[i]--;
            s.Frame(a, 0.05f);
            frame++;
        };
        block();
        int runs = AllocationProbe.AssertZero(block, _out);
        _out.WriteLine($"AttackAlerts frame, {units} units + {buildings} buildings: 0 bytes (runs {runs}), {a.Raised} alerts from {a.Hits} hits");
        Assert.True(a.Raised > 0 && a.Hits > a.Raised);
    }
}
