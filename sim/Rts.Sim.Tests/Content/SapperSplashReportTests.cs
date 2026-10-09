using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D6 report (BUG-0182 item 2): what the Sapper's self-splash costs in a fight. A Sapper (range 8, no minimum range,
/// splash 2 m, friendly fire) shooting a melee unit next to it lobs its sharper about a meter from itself, inside its own
/// splash. On a flat map, 4 Sappers + 4 Heavy Infantry (and 4 Sappers alone) meet 8 Horse Raiders under (a) the shipped
/// data, (b) a scratch copy with <c>attack.minRange</c> 2 m on the Sapper, (c) a copy with <c>attack.splash</c> 1 m. Each
/// row prints friendly-fire deaths, the splash damage the Malazan side took from its own sharpers (and how much of it hit
/// the Sapper that threw it), and the outcome. Only that the rows run is asserted: the trade-off is the owner's call.
/// <para>
/// The scratch copies are <see cref="TestDataDir"/> temp folders (as <see cref="TestSim.DataWithoutBuildingRequires"/>),
/// never the shipped files. Friendly splash damage is reconstructed with the sim's own formulas
/// (<see cref="DamageCalc"/>, <see cref="ProjectileSystem.Falloff"/>, <see cref="CombatConstants.FriendlyFireFactor"/>)
/// from each sharper landing this tick (its thrower and impact point, read before the tick) and the units' positions after
/// it; a unit's share is capped at the hit points it had before the tick.
/// </para>
/// </summary>
public class SapperSplashReportTests
{
    private const string Sapper = "malazan_sapper";
    private const int MaxTicks = 3000;

    private readonly ITestOutputHelper _out;

    public SapperSplashReportTests(ITestOutputHelper output) => _out = output;

    private static GameData Variant(string field, string rawJson)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", Sapper, field, rawJson);
        DataLoadResult result = DataLoader.LoadAll(dir.Path);
        Assert.True(result.Ok, string.Join(Environment.NewLine, result.Errors));
        return result.Data!;
    }

    private static readonly Lazy<GameData> s_minRange2 = new(() => Variant("attack.minRange", "2"));
    private static readonly Lazy<GameData> s_splash1 = new(() => Variant("attack.splash", "1.0"));

    /// <summary>One row of the report.</summary>
    private readonly record struct Row(
        int FriendlyDeaths, int FriendlySapperDeaths, int FriendlyDamage, int SelfDamage, int Throws,
        int SappersLeft, int InfantryLeft, int RaidersLeft, int Ticks);

    /// <summary>A sharper landing this tick: who threw it (for "self"), its type's attack, where it explodes.</summary>
    private readonly record struct Landing(EntityHandle Thrower, int AttackerType, Vector2 At);

    /// <summary>
    /// Player 0: <paramref name="infantry"/> Heavy Infantry in a front rank and 4 Sappers 1.8 m behind them; player 1: 8
    /// Horse Raiders in two ranks; the fronts 24 m apart, everyone attack-moved to the far block's center.
    /// </summary>
    private static Row Run(GameData data, int infantry)
    {
        const int raiders = 8, sappers = 4;
        var sim = new Simulation(new SimConfig(7, 2, 32, 8 * 32 + 32) { Data = data }, LocalMovementTests.Flat(64));
        World w = sim.World;
        UnitStore u = w.Units;
        int sapper = data.FindUnit(Sapper), hi = data.FindUnit("malazan_heavy_infantry"), hr = data.FindUnit("whirlwind_horse_raider");
        var center = new Vector2(64f, 64f);
        const float spacing = 1.8f;
        var mine = new List<EntityHandle>();
        var theirs = new List<EntityHandle>();
        for (int k = 0; k < infantry; k++)
            mine.Add(Place(sim, 0, hi, center + new Vector2(-12f, (k - (infantry - 1) / 2f) * spacing)));
        float back = infantry > 0 ? -12f - spacing : -12f;
        for (int k = 0; k < sappers; k++)
            mine.Add(Place(sim, 0, sapper, center + new Vector2(back, (k - (sappers - 1) / 2f) * spacing)));
        for (int k = 0; k < raiders; k++)
            theirs.Add(Place(sim, 1, hr, center + new Vector2(12f + (k / 4) * spacing, (k % 4 - 1.5f) * spacing)));
        foreach (EntityHandle h in mine) sim.Enqueue(Command.AttackMove(0, h, center + new Vector2(13f, 0f)));
        foreach (EntityHandle h in theirs) sim.Enqueue(Command.AttackMove(1, h, center + new Vector2(-12f, 0f)));

        ProjectileStore p = w.Projectiles;
        var landing = new List<Landing>();
        var hpBefore = new int[u.Capacity];
        int friendlyDeaths = 0, friendlySapperDeaths = 0, friendlyDamage = 0, selfDamage = 0, throws = 0, ticks = 0;
        while (ticks < MaxTicks && mine.Any(u.IsAlive) && theirs.Any(u.IsAlive))
        {
            // Phase 10 steps every shot in flight before new ones spawn, so a shot one tick out lands in this tick's phase 11.
            landing.Clear();
            for (int k = 0; k < p.Capacity; k++)
                if (p.Alive[k] && p.Owner[k] == 0 && p.TicksLeft[k] == 1 && data.Units[p.AttackerType[k]].Attack.FriendlyFire)
                    landing.Add(new Landing(p.Attacker[k], p.AttackerType[k], p.Target[k]));
            for (int i = 0; i < u.Capacity; i++) hpBefore[i] = u.Alive[i] ? u.Hp[i] : 0;

            sim.Tick();
            ticks++;

            int landed = 0;
            foreach (ProjectileImpact e in w.Impacts)
                if (e.Owner == 0 && data.Units[sapper].Attack.ProjectileTypeId == e.ProjectileTypeId) landed++;
            Assert.True(landed == landing.Count, $"tick {ticks}: {landed} sharpers landed, {landing.Count} expected from the flight times");
            throws += landed;

            // Own units still alive, and own units a friendly splash killed this tick (the death event has where).
            for (int i = 0; i < u.Capacity; i++)
                if (u.Alive[i] && u.Owner[i] == 0)
                    Count(new EntityHandle(i, u.Generation[i]), u.TypeId[i], u.Position[i], hpBefore[i]);
            foreach (DeathEvent e in w.Deaths)
            {
                if (e.IsBuilding || e.VictimOwner != 0 || e.KillerOwner != 0) continue;
                friendlyDeaths++;
                if (e.VictimType == sapper) friendlySapperDeaths++;
                Count(e.Victim, e.VictimType, e.Position, hpBefore[e.Victim.Index]);
            }
        }
        return new Row(friendlyDeaths, friendlySapperDeaths, friendlyDamage, selfDamage, throws,
            mine.Count(h => u.IsAlive(h) && u.TypeId[h.Index] == sapper), mine.Count(h => u.IsAlive(h) && u.TypeId[h.Index] == hi),
            theirs.Count(u.IsAlive), ticks);

        void Count(EntityHandle victim, int type, Vector2 at, int cap)
        {
            int total = 0, self = 0;
            UnitDef v = data.Units[type];
            foreach (Landing l in landing)
            {
                AttackDef a = data.Units[l.AttackerType].Attack;
                float d = Vector2.Distance(at, l.At);
                if (!(d <= a.Splash)) continue;
                int full = DamageCalc.Compute(data.DamageTable, a, 0, v.ArmorClass, v.Armor);
                float factor = ProjectileSystem.Falloff(d, a.Splash) * CombatConstants.FriendlyFireFactor;
                int dmg = Math.Max(1, (int)MathF.Floor(full * factor + 0.5f));
                total += dmg;
                if (l.Thrower == victim) self += dmg;
            }
            friendlyDamage += Math.Min(total, cap);
            selfDamage += Math.Min(self, cap);
        }
    }

    [Fact]
    public void SapperSelfSplash_ThreeVariants_PrintFriendlyFireAndOutcome()
    {
        GameData shipped = TestSim.Data;
        AttackDef a = shipped.Units[shipped.FindUnit(Sapper)].Attack;
        _out.WriteLine(FormattableString.Invariant($"Shipped Sapper: {a.Value} siege, range {a.Range} m, minRange {a.MinRange} m, splash {a.Splash} m, friendlyFire {a.FriendlyFire}"));
        var variants = new (string Name, GameData Data)[]
        {
            ("(a) shipped", shipped),
            ("(b) minRange 2 m", s_minRange2.Value),
            ("(c) splash 1 m", s_splash1.Value),
        };
        foreach ((string scene, int infantry) in new[] { ("4 Sappers + 4 Heavy Infantry v 8 Horse Raiders", 4), ("4 Sappers v 8 Horse Raiders", 0) })
        {
            _out.WriteLine("");
            _out.WriteLine(scene);
            _out.WriteLine("| Variant | Sharpers landed | Friendly-fire deaths (Sappers) | Own splash damage taken | of it on the thrower itself | Sappers left | HI left | Horse Raiders left | Time |");
            _out.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach ((string name, GameData data) in variants)
            {
                Row r = Run(data, infantry);
                string time = FormattableString.Invariant($"{r.Ticks / 20f:F1} s");
                _out.WriteLine($"| {name} | {r.Throws} | {r.FriendlyDeaths} ({r.FriendlySapperDeaths}) | {r.FriendlyDamage} | {r.SelfDamage} | {r.SappersLeft} / 4 | {r.InfantryLeft} / {infantry} | {r.RaidersLeft} / 8 | {time} |");
                Assert.True(r.Ticks > 0, $"{scene} {name}: the fight did not run");
            }
        }
    }
}
