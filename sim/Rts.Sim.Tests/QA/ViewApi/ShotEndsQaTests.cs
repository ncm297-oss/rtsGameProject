using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Tests.ViewApi;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>
/// QA M7-VH1 (session 2026-11-10-2116, BUG-1179): <see cref="ShotEnds"/>, the read-only accessor the projectile views
/// read a shot's ends from. Its shape has no way to write; and over mixed brawls (aimed bolts, arrows and magic bolts, lobbed
/// stones and sharpers, a watchtower per side, tents as building victims) read every tick the way the view reads it, every
/// live slot's ends equal the store's own arrays, a shot's ends never change in flight, every shot new this tick names its
/// real shooter's type (a shooter killed the tick it fired included) and its victim's (alive or its corpse), a freed slot
/// is never refilled before an every-tick reader sees it free, and reading changes no hash (twin).
/// </summary>
[Collection(SerialCollection.Name)]
public class ShotEndsQaTests
{
    private readonly ITestOutputHelper _out;

    public ShotEndsQaTests(ITestOutputHelper output) => _out = output;

    private static GameData Data => TestSim.Data;

    [Fact]
    public void Shape_IsAReadonlyStruct_WithNoSetterNoMutableStaticAndNoStoreParameter()
    {
        Type t = typeof(ShotEnds);
        Assert.True(t.IsValueType);
        Assert.NotNull(t.GetCustomAttribute<IsReadOnlyAttribute>());
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (PropertyInfo p in t.GetProperties(all))
            Assert.True(p.SetMethod == null, $"ShotEnds.{p.Name} has a setter");
        foreach (FieldInfo f in t.GetFields(all))
            Assert.True(f.IsInitOnly || f.IsLiteral, $"ShotEnds field {f.Name} is writable");
        foreach (MethodInfo m in t.GetMethods(all))
        {
            if (m.IsSpecialName || m.DeclaringType != t) continue;
            foreach (ParameterInfo pi in m.GetParameters())
            {
                // Spans are read-only; the store is passed by reference but Of only reads it (the hash rows below prove it).
                Assert.False(pi.ParameterType.IsByRef, $"ShotEnds.{m.Name}({pi.Name}) takes a ref");
                Assert.False(pi.ParameterType == typeof(UnitStore) || pi.ParameterType == typeof(World) || pi.ParameterType == typeof(Simulation),
                    $"ShotEnds.{m.Name}({pi.Name}) takes a {pi.ParameterType.Name}");
                if (pi.ParameterType.IsGenericType && pi.ParameterType.GetGenericTypeDefinition() == typeof(Span<>))
                    Assert.Fail($"ShotEnds.{m.Name}({pi.Name}) takes a writable Span");
            }
        }
    }

    [Fact]
    public void Source_CallsNoStoreMutator_AndWritesNoStoreArray()
    {
        // ShotEnds takes the ProjectileStore itself, whose internal mutators the general ViewApi source guard doesn't name.
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "RtsGame.sln"))) dir = Path.GetDirectoryName(dir)!;
        Assert.NotNull(dir);
        string[] lines = File.ReadAllLines(Path.Combine(dir!, "sim", "Rts.Sim", "ViewApi", "ShotEnds.cs"));
        string[] forbidden =
        {
            @"\.(Free|Steer|Fly|TrySpawn|TryAlloc|AddToHash|Clear)\s*\(", @"\w+\s*\[[^\]]*\]\s*(=[^=>]|\+=|-=|\+\+|--)",
            @"\bref\s", @"\bout\s", @"Span<(?!.*ReadOnly)", @"\bUnitStore\b", @"\bWorld\b",
        };
        var hits = new List<string>();
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimStart();
            if (line.StartsWith("//")) continue;
            foreach (string p in forbidden)
                if (System.Text.RegularExpressions.Regex.IsMatch(line, p) && !line.Contains("ReadOnlySpan<")) hits.Add($"{i + 1}: {p}: {line}");
        }
        Assert.True(hits.Count == 0, string.Join(Environment.NewLine, hits));
    }

    [Fact]
    public void ASlotFreedAndRefilled_ReadsTheNewShot_NeverTheOld()
    {
        var s = new ProjectileStore(2);
        var u = new UnitStore(8);
        int bolt = Data.FindProjectile("bolt");
        int xbow = Data.FindUnit("malazan_crossbowman"), archer = Data.FindUnit("whirlwind_desert_archer");
        Assert.True(u.TrySpawn(0, xbow, Data.Units[xbow], new Vector2(5f, 5f), out EntityHandle a));
        Assert.True(u.TrySpawn(1, archer, Data.Units[archer], new Vector2(9f, 5f), out EntityHandle b));
        Assert.True(s.TrySpawn(new Vector2(5f, 5f), new Vector2(9f, 5f), 1f, bolt, 0, xbow, a, b, false));
        s.Free(0);
        Assert.False(ShotEnds.Of(s, 0).Live);
        Assert.Equal(-1, ShotEnds.Of(s, 0).ShooterUnitType);
        Assert.True(s.TrySpawn(new Vector2(9f, 5f), new Vector2(5f, 5f), 1f, bolt, 1, archer, b, a, false));
        ShotEnds e = ShotEnds.Of(s, 0);
        Assert.Equal(b, e.Attacker);
        Assert.Equal(a, e.Victim);
        Assert.Equal(archer, e.ShooterUnitType);
        Assert.Equal(xbow, e.VictimUnitType(u.Alive, u.Generation, u.TypeId));
        // Short or empty spans (a hand-built reader) name no one rather than throw.
        Assert.Equal(-1, e.VictimUnitType(ReadOnlySpan<bool>.Empty, u.Generation, u.TypeId));
        Assert.Equal(-1, e.VictimUnitType(u.Alive, ReadOnlySpan<int>.Empty, u.TypeId));
        Assert.Equal(-1, e.VictimUnitType(u.Alive, u.Generation, ReadOnlySpan<int>.Empty));
        Assert.Equal(-1, ShotEnds.Of(s, int.MinValue).ShooterUnitType);
        Assert.Equal(-1, ShotEnds.Of(s, int.MaxValue).ShooterUnitType);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(3UL)]
    [InlineData(11UL)]
    [InlineData(42UL)]
    public void MixedBrawl_EveryTickReader_ExactEnds_NoHashMove(ulong seed)
    {
        Simulation a = Brawl(seed), b = Brawl(seed);
        ProjectileStore p = a.World.Projectiles;
        UnitStore u = a.World.Units;
        BuildingStore bs = a.World.Buildings;
        int cap = p.Capacity;
        var wasAlive = new bool[cap];
        var lastAttacker = new EntityHandle[cap];
        var lastVictim = new EntityHandle[cap];
        var lastAttackerType = new int[cap];
        var lastType = new int[cap];
        int fresh = 0, aimed = 0, lob = 0, tower = 0, deadShooter = 0, corpseVictim = 0, buildingVictim = 0, victimDeadInFlight = 0, refills = 0;
        var freedAt = new long[cap];
        for (int t = 0; t < 900; t++)
        {
            a.Tick();
            b.Tick();
            ulong before = a.StateHash();
            for (int i = 0; i < cap; i++)
            {
                ShotEnds e = ShotEnds.Of(p, i);
                Assert.Equal(p.Alive[i], e.Live);
                if (!e.Live)
                {
                    if (wasAlive[i]) freedAt[i] = a.World.TickNumber;
                    wasAlive[i] = false;
                    Assert.Equal(-1, e.ShooterUnitType);
                    Assert.Equal(-1, e.VictimUnitType(u.Alive, u.Generation, u.TypeId));
                    continue;
                }
                Assert.Equal(p.Attacker[i], e.Attacker);
                Assert.Equal(p.Victim[i], e.Victim);
                Assert.Equal(p.AttackerType[i], e.AttackerType);
                Assert.Equal(p.AttackerIsBuilding[i], e.AttackerIsBuilding);
                Assert.Equal(p.VictimIsBuilding[i], e.VictimIsBuilding);
                int victimType = e.VictimUnitType(u.Alive, u.Generation, u.TypeId);
                if (wasAlive[i])
                {
                    // The same shot in flight: its ends never change (the view keys "a new shot" on them).
                    Assert.Equal(lastAttacker[i], e.Attacker);
                    Assert.Equal(lastVictim[i], e.Victim);
                    Assert.Equal(lastAttackerType[i], e.AttackerType);
                    Assert.Equal(lastType[i], p.ProjectileTypeId[i]);
                    if (!e.VictimIsBuilding && !u.IsAlive(e.Victim)) victimDeadInFlight++;
                    continue;
                }
                fresh++;
                if (freedAt[i] != 0)
                {
                    // Freed at an earlier read than this one: an every-tick reader always saw the gap.
                    Assert.True(freedAt[i] < a.World.TickNumber, $"slot {i} refilled the tick it was freed");
                    refills++;
                }
                if (Data.Projectiles[p.ProjectileTypeId[i]].Kind == ProjectileKind.Lob) lob++; else aimed++;
                if (e.AttackerIsBuilding)
                {
                    tower++;
                    Assert.Equal(-1, e.ShooterUnitType);
                    Assert.Equal(bs.TypeId[e.Attacker.Index], e.AttackerType);
                }
                else
                {
                    int k = e.Attacker.Index;
                    bool alive = u.IsAlive(e.Attacker);
                    bool corpse = !u.Alive[k] && u.Generation[k] == e.Attacker.Generation + 1;
                    Assert.True(alive || corpse, $"tick {a.World.TickNumber} slot {i}: a new shot's shooter is neither alive nor its corpse");
                    if (!alive) deadShooter++;
                    Assert.Equal(u.TypeId[k], e.ShooterUnitType);
                }
                if (e.VictimIsBuilding)
                {
                    buildingVictim++;
                    Assert.Equal(-1, victimType);
                }
                else
                {
                    Assert.True(victimType >= 0, $"tick {a.World.TickNumber} slot {i}: a new shot's victim unknowable");
                    Assert.Equal(u.TypeId[e.Victim.Index], victimType);
                    if (!u.IsAlive(e.Victim)) corpseVictim++;
                }
                wasAlive[i] = true;
                lastAttacker[i] = e.Attacker;
                lastVictim[i] = e.Victim;
                lastAttackerType[i] = e.AttackerType;
                lastType[i] = p.ProjectileTypeId[i];
            }
            Assert.Equal(before, a.StateHash());
            Assert.Equal(b.StateHash(), a.StateHash());
        }
        _out.WriteLine($"seed {seed}: {fresh} new shots ({aimed} aimed, {lob} lob, {tower} tower), shooter dead on firing tick {deadShooter}, " +
            $"victim corpse on firing tick {corpseVictim}, building victims {buildingVictim}, victim dead in flight {victimDeadInFlight} slot-ticks, slot refills {refills}");
        Assert.True(aimed > 200, $"only {aimed} aimed shots");
        Assert.True(lob > 5, $"only {lob} lobs");
        Assert.True(tower > 5, $"only {tower} tower shots");
        Assert.True(refills > 50, $"only {refills} slot refills");
        Assert.True(victimDeadInFlight > 0, "no victim died with a shot in flight at it");
    }

    // Two mixed armies on a flat 64 map, a watchtower and a tent per side between them, attack-moved through each other.
    private static Simulation Brawl(ulong seed)
    {
        var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: 256, CommandCapacity: 1024), LocalMovementTests.Flat(64));
        TestSim.Explored(sim);
        string[] west = { "malazan_crossbowman", "malazan_cadre_mage", "malazan_catapult", "malazan_sapper", "malazan_heavy_infantry", "malazan_wickan_lancer" };
        string[] east = { "whirlwind_desert_archer", "whirlwind_priest", "whirlwind_raider", "whirlwind_horse_raider", "whirlwind_zealot", "whirlwind_desert_archer" };
        int towerType = Data.FindBuilding("malazan_watchtower"), tent = Data.FindBuilding("whirlwind_tent");
        sim.Enqueue(Command.SpawnBuilding(0, towerType, new Vector2(54f, 64f)));
        sim.Enqueue(Command.SpawnBuilding(1, towerType, new Vector2(74f, 64f)));
        sim.Enqueue(Command.SpawnBuilding(0, tent, new Vector2(40f, 40f)));
        sim.Enqueue(Command.SpawnBuilding(1, tent, new Vector2(88f, 88f)));
        var rng = new Rts.Sim.Determinism.SimRng(seed, 77UL);
        for (int k = 0; k < 48; k++)
        {
            float y = 40f + (k % 24) * 2f;
            float dx = (k / 24) * 2.5f;
            sim.Enqueue(Command.SpawnUnit(0, Data.FindUnit(west[k % west.Length]), new Vector2(36f - dx, y + rng.NextInt(0, 3) * 0.3f)));
            sim.Enqueue(Command.SpawnUnit(1, Data.FindUnit(east[k % east.Length]), new Vector2(92f + dx, y + rng.NextInt(0, 3) * 0.3f)));
        }
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            int pl = u.Owner[i];
            sim.Enqueue(Command.AttackMove(pl, new EntityHandle(i, u.Generation[i]), new Vector2(pl == 0 ? 100f : 28f, 64f)));
        }
        return sim;
    }
}
