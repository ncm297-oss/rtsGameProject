using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA (M4-4a, session 2026-10-09-0724): three players with combat on, mage-heavy armies, a hostile command stream mixing
/// UseAbility (any index, stale handles, foreign players, edge points), Stop, Hold, Move and attack-move, 2,000 ticks.
/// After every tick: twins hash-equal; no NaN or off-map position; 0 &lt;= HP &lt;= max; every status is known, has
/// 1..80 ticks left, a magnitude in its effect's range and a valid source player; every live unit's speed is its type's
/// speed times (1 - its strongest slow); no Casting unit without a cast; the cooldown never exceeds now + 500. Different
/// seeds give different hashes. M4-4b-3a: with <c>aura</c> the armies' Heavy Infantry carry the two test-only selfAura
/// abilities of <see cref="SelfAuraTests"/> (a rally that Frenzies own units within 4 m for 5 s, and a 3 m zone of Slowed
/// around the caster): a Frenzied entry is from its own owner, lasts up to 100 ticks and is at +50 %.
/// </summary>
public class AbilityInvariantFuzzStressTests
{
    private const int Ticks = 2000;
    private const int Players = 3;
    private const int PerSide = 24;

    private static readonly string[] Armies = { "malazan_cadre_mage", "malazan_cadre_mage", "malazan_cadre_mage", "malazan_heavy_infantry", "whirlwind_raider", "malazan_crossbowman" };

    private static Simulation NewSim(ulong seed, bool aura = false)
    {
        SimConfig config = TestSim.Config(Seed: seed, PlayerCount: Players, UnitCapacity: Players * PerSide, CommandCapacity: 1024);
        var sim = new Simulation(aura ? config with { Data = SelfAuraTests.Data } : config);
        NavGrid g = sim.World.NavGrid;
        var rng = new SimRng(seed, 300);
        // Each side spawns in a 24 x 24-cell box around a third of the map's center line, so the armies meet.
        int cx = g.Width / 2, cy = g.Height / 2;
        for (int k = 0; k < PerSide; k++)
            for (int p = 0; p < Players; p++)
            {
                for (int tries = 0; tries < 50; tries++)
                {
                    int x = cx - 12 + rng.NextInt(0, 24) + (p - 1) * 10, y = cy - 12 + rng.NextInt(0, 24);
                    if (x < 0 || y < 0 || x >= g.Width || y >= g.Height || !g.IsPassable(x, y)) continue;
                    sim.Enqueue(Command.SpawnUnit(p, TestSim.Data.FindUnit(Armies[rng.NextInt(0, Armies.Length)]), g.CellCenter(x, y)));
                    break;
                }
            }
        return sim;
    }

    private static void Spam(Simulation sim, ref SimRng rng, List<EntityHandle> ever)
    {
        UnitStore u = sim.World.Units;
        NavGrid g = sim.World.NavGrid;
        float maxX = g.Width * MapConstants.CellSize, maxY = g.Height * MapConstants.CellSize;
        for (int n = 0; n < 15; n++)
        {
            EntityHandle h;
            if (rng.NextInt(0, 8) == 0 && ever.Count > 0) h = ever[rng.NextInt(0, ever.Count)];
            else
            {
                int i = rng.NextInt(0, u.Capacity);
                h = new EntityHandle(i, u.Generation[i]);
            }
            int player = rng.NextInt(0, 12) == 0 ? rng.NextInt(0, Players) : (u.Alive[h.Index] ? u.Owner[h.Index] : 0);
            Vector2 at = u.Alive[h.Index] ? u.Position[h.Index] : new Vector2(maxX / 2, maxY / 2);
            Vector2 point = rng.NextInt(0, 10) switch
            {
                0 => new Vector2(0f, rng.NextFloat() * maxY),                 // the map's west edge
                1 => new Vector2(maxX - 0.001f, maxY - 0.001f),               // the far corner
                2 => new Vector2(maxX + 0.5f, 3f),                            // just off the map
                3 => at + new Vector2(16f, 0f),                               // the range edge
                _ => at + new Vector2(rng.NextFloat() * 30f - 15f, rng.NextFloat() * 30f - 15f),
            };
            int index = rng.NextInt(0, 6) == 0 ? rng.NextInt(-2, 6) : 0;
            bool queued = rng.NextInt(0, 3) == 0;
            switch (rng.NextInt(0, 12))
            {
                case < 7: sim.Enqueue(Command.UseAbility(player, h, index, point, queued)); break;
                case 7: sim.Enqueue(Command.Stop(player, h)); break;
                case 8: sim.Enqueue(Command.HoldPosition(player, h)); break;
                case 9: sim.Enqueue(Command.Move(player, h, point, queued)); break;
                default: sim.Enqueue(Command.AttackMove(player, h, point, queued)); break;
            }
        }
    }

    [Theory]
    [InlineData(11UL, false)]
    [InlineData(12UL, false)]
    [InlineData(13UL, false)]
    [InlineData(14UL, false)]
    [InlineData(15UL, true)] // M4-4b-3a: a selfAura caster in every army
    [InlineData(16UL, true)]
    public void ThreePlayerCastBrawl_TwinsEqual_InvariantsHoldEveryTick(ulong seed, bool aura)
    {
        Simulation a = NewSim(seed, aura), b = NewSim(seed, aura), other = NewSim(seed + 1000, aura);
        World w = a.World;
        UnitStore u = w.Units;
        StatusStore s = u.Statuses;
        NavGrid g = w.NavGrid;
        float maxX = g.Width * MapConstants.CellSize, maxY = g.Height * MapConstants.CellSize;
        var ra = new SimRng(seed, 301);
        var rb = new SimRng(seed, 301);
        var ro = new SimRng(seed + 1000, 301);
        var everA = new List<EntityHandle>();
        var everB = new List<EntityHandle>();
        var everO = new List<EntityHandle>();
        int resolves = 0, burnTicks = 0, slowTicks = 0, diffs = 0, auraResolves = 0, frenziedTicks = 0;
        int burning = w.Data.FindStatus("burning"), frenzied = w.Data.FindStatus("frenzied");
        for (int t = 0; t < Ticks; t++)
        {
            if (t % 4 == 1)
            {
                Spam(a, ref ra, everA);
                Spam(b, ref rb, everB);
                Spam(other, ref ro, everO);
            }
            a.Tick();
            b.Tick();
            other.Tick();
            ulong ha = a.StateHash();
            Assert.True(ha == b.StateHash(), $"seed {seed}: twins differ after tick {t}");
            if (ha != other.StateHash()) diffs++;
            foreach (AbilityEvent e in w.AbilityEvents)
            {
                if (!e.Resolved) continue;
                resolves++;
                if (w.Data.Abilities[e.Ability].Kind != AbilityKind.SelfAura) continue;
                auraResolves++;
                // The point of a selfAura resolve is where its caster stood (the command's point is ignored); it may have taken
                // a step or died since, in this tick's later phases.
                Assert.True(!u.IsAlive(e.Caster) || Vector2.Distance(u.Position[e.Caster.Index], e.Point) <= 1f, $"seed {seed} tick {t}: an aura resolved away from its caster");
            }
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                if (t % 100 == 0) { everA.Add(new EntityHandle(i, u.Generation[i])); everB.Add(new EntityHandle(i, b.World.Units.Generation[i])); }
                UnitDef def = w.Data.Units[u.TypeId[i]];
                Vector2 p = u.Position[i];
                Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y) && p.X >= 0 && p.Y >= 0 && p.X <= maxX && p.Y <= maxY, $"seed {seed} tick {t}: unit {i} at {p}");
                Assert.True(u.Hp[i] > 0 && u.Hp[i] <= def.Hp, $"seed {seed} tick {t}: unit {i} hp {u.Hp[i]}");
                Assert.True(u.State[i] != UnitState.Casting || u.CastAbility[i] >= 0, $"seed {seed} tick {t}: unit {i} Casting with no cast");
                for (int k = 0; k < DataLimits.MaxUnitAbilities; k++)
                    Assert.True(u.AbilityReadyTick[i * DataLimits.MaxUnitAbilities + k] <= a.TickNumber + 500, $"seed {seed} tick {t}: unit {i} cooldown past 25 s");
                float slow = 0f;
                for (int k = 0; k < s.Count[i]; k++)
                {
                    int at = i * StatusStore.PerUnit + k;
                    int id = s.StatusId[at];
                    if (id == frenzied)
                    {
                        // M4-4b-3a: the rally's: own units only, 5 s, +50 %.
                        frenziedTicks++;
                        Assert.True(s.TicksRemaining[at] is > 0 and <= 100 && s.SourcePlayer[at] == u.Owner[i] && s.Magnitude[at] == 0.5f,
                            $"seed {seed} tick {t}: unit {i} Frenzied {s.Magnitude[at]} for {s.TicksRemaining[at]} by {s.SourcePlayer[at]}");
                        continue;
                    }
                    Assert.True(s.TicksRemaining[at] is > 0 and <= 80, $"seed {seed} tick {t}: unit {i} status ticks {s.TicksRemaining[at]}");
                    Assert.True((uint)s.SourcePlayer[at] < Players && s.SourcePlayer[at] != u.Owner[i], $"seed {seed} tick {t}: unit {i} burned by its own owner {s.SourcePlayer[at]}");
                    if (id == burning) { burnTicks++; Assert.Equal(10f, s.Magnitude[at]); }
                    else { slowTicks++; slow = Math.Max(slow, s.Magnitude[at]); }
                    for (int k2 = 0; k2 < k; k2++) Assert.NotEqual(id, s.StatusId[i * StatusStore.PerUnit + k2]);
                }
                Assert.Equal(def.SpeedPerTick * (1f - slow), u.Speed[i]);
            }
        }
        Assert.True(resolves > 20, $"seed {seed}: only {resolves} resolves");
        Assert.True(burnTicks > 100, $"seed {seed}: only {burnTicks} burn-ticks");
        Assert.True(diffs > Ticks / 2, $"seed {seed}: a different seed hashed equal on {Ticks - diffs} ticks");
        if (aura) Assert.True(auraResolves > 20 && frenziedTicks > 100, $"seed {seed}: {auraResolves} aura resolves, {frenziedTicks} Frenzied unit-ticks");
        _ = slowTicks;
    }
}
