using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Rts.Sim.Replays;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M6-V13 (2026-10-18-0516): <see cref="AiOpeningLookup.Pick"/> against the live sim's pick on mixed human / AI seats, on
/// more openings than the dev's fixture and through a written-and-read replay header (the view's real path), and read-only
/// beside a ticking sim; <see cref="AttackAlerts"/>' Units -> Base upgrade fuzzed against a list oracle that checks every
/// alert's kind (not only the newest) and every Update's upgraded set.
/// </summary>
public class M6V13QaTests
{
    private readonly ITestOutputHelper _out;

    public M6V13QaTests(ITestOutputHelper output) => _out = output;

    private static readonly string[] Factions = { "malazan", "whirlwind" };

    // Malazan with the maximum 8 openings, Whirlwind with 5: copies of the first shipped one with their own ids.
    private static readonly Lazy<GameData> s_many = new(() => AiScenes.DataWith(dir =>
    {
        int[] counts = { DataLimits.MaxAiOpenings, 5 };
        for (int f = 0; f < Factions.Length; f++)
        {
            int n = counts[f];
            dir.EditJson($"factions/{Factions[f]}/ai.json", root =>
            {
                JsonArray openings = root["openings"]!.AsArray();
                JsonNode first = openings[0]!.DeepClone();
                openings.Clear();
                openings.Add(first);
                for (int k = 1; k < n; k++)
                {
                    JsonNode copy = first.DeepClone();
                    copy["id"] = $"qa_opening_{k}";
                    copy["displayName"] = $"QA Opening {k}";
                    openings.Add(copy);
                }
            });
        }
    }));

    /// <summary>Seats 2-8, each human or AI at random (seat 0 often human, as in the game), random factions and difficulties: Pick equals the live pick on every AI seat, and every human seat's live pick is -1.</summary>
    [Fact]
    public void Pick_EqualsLive_OnMixedHumanAndAiSeats_EightAndFiveOpenings()
    {
        GameData data = s_many.Value;
        var rnd = new Random(1306);
        int ai = 0, human = 0;
        var seen = new HashSet<(int Faction, int Opening)>();
        for (int trial = 0; trial < 600; trial++)
        {
            int players = 2 + rnd.Next(7);
            ulong seed = (ulong)rnd.NextInt64(0, long.MaxValue) * (trial % 2 == 0 ? 1UL : 2UL);
            var seats = new PlayerSeat[players];
            for (int p = 0; p < players; p++)
            {
                bool isAi = rnd.Next(3) != 0 && !(p == 0 && trial % 3 != 0);
                seats[p] = new PlayerSeat(data.FindFaction(Factions[rnd.Next(2)]), isAi ? SeatController.Ai : SeatController.Human, (AiDifficulty)rnd.Next(3));
            }
            var sim = new Simulation(new SimConfig(seed, players, 16, 16) { Data = data, Seats = ImmutableArray.Create(seats) }, LocalMovementTests.Flat(32));
            for (int p = 0; p < players; p++)
            {
                int live = sim.AiOpening(p);
                if (seats[p].Controller != SeatController.Ai)
                {
                    Assert.Equal(-1, live);
                    human++;
                    continue;
                }
                int count = data.Ai[seats[p].Faction].Openings.Length;
                Assert.True(live == AiOpeningLookup.Pick(seed, p, seats[p].Difficulty, count), $"trial {trial} seed {seed} seat {p} of {players}: live {live}, Pick {AiOpeningLookup.Pick(seed, p, seats[p].Difficulty, count)}");
                seen.Add((seats[p].Faction, live));
                ai++;
            }
        }
        _out.WriteLine($"{ai} AI seats, {human} human seats, {seen.Count} distinct (faction, opening) picks of {DataLimits.MaxAiOpenings + 5}");
        Assert.Equal(DataLimits.MaxAiOpenings + 5, seen.Count);
    }

    /// <summary>
    /// The view's real path: a live generated-map match with a human seat 0 and AI seats of every difficulty recorded,
    /// written, read back; the pick from the read header's seed and seats (faction id looked up in data, difficulty) equals
    /// the live sim's for every AI seat.
    /// </summary>
    [Theory]
    [InlineData(3UL)]
    [InlineData(11UL)]
    [InlineData(29UL)]
    public void Pick_FromAReadBackReplayHeader_EqualsTheLiveMatchsPick(ulong seed)
    {
        GameData data = s_many.Value;
        var seats = new[]
        {
            new PlayerSeat(data.FindFaction("malazan"), SeatController.Human, AiDifficulty.Normal),
            new PlayerSeat(data.FindFaction("whirlwind"), SeatController.Ai, AiDifficulty.Easy),
            new PlayerSeat(data.FindFaction("malazan"), SeatController.Ai, AiDifficulty.Normal),
            new PlayerSeat(data.FindFaction("whirlwind"), SeatController.Ai, AiDifficulty.Hard),
        };
        Simulation sim = AiScenes.Generated(seats, seed, record: true, out ReplayRecorder? recorder, data: data);
        for (int t = 0; t < 60; t++) sim.Tick();
        Replay replay = recorder!.ToReplay();
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(replay), out Replay? read));
        Assert.False(read!.Seats.IsDefaultOrEmpty);
        for (int p = 0; p < seats.Length; p++)
        {
            ReplaySeat rs = read.Seats[p];
            int live = sim.AiOpening(p);
            if (rs.Controller != SeatController.Ai)
            {
                Assert.Equal(-1, live);
                continue;
            }
            int faction = data.FindFaction(rs.Faction);
            Assert.Equal(seats[p].Difficulty, rs.Difficulty);
            Assert.Equal(live, AiOpeningLookup.Pick(read.Seed, p, rs.Difficulty, data.Ai[faction].Openings.Length));
        }
        _out.WriteLine($"seed {seed}: live picks [{string.Join(", ", Enumerable.Range(0, seats.Length).Select(sim.AiOpening))}]");
    }

    /// <summary>Pick called many times every tick beside a ticking AI match leaves its state hash equal to an untouched twin's at every tick.</summary>
    [Fact]
    public void Pick_BesideATickingAiMatch_MovesNoHash()
    {
        GameData data = s_many.Value;
        var seats = new[]
        {
            new PlayerSeat(data.FindFaction("malazan"), SeatController.Ai, AiDifficulty.Hard),
            new PlayerSeat(data.FindFaction("whirlwind"), SeatController.Ai, AiDifficulty.Normal),
        };
        Simulation a = AiScenes.Generated(seats, 7, record: false, out _, data: data);
        Simulation b = AiScenes.Generated(seats, 7, record: false, out _, data: data);
        for (int t = 0; t < 400; t++)
        {
            for (int k = 0; k < 20; k++) AiOpeningLookup.Pick(a.World.Config.Seed, k % 2, (AiDifficulty)(k % 3), 1 + k % 8);
            a.Tick();
            b.Tick();
            Assert.Equal(b.StateHash(), a.StateHash());
        }
        Assert.Equal(b.AiOpening(0), a.AiOpening(0));
        Assert.Equal(b.AiOpening(1), a.AiOpening(1));
    }

    // ---- BUG-0954 item 1 (M6-V13): the Units -> Base upgrade ----

    private sealed class Field
    {
        public readonly bool[] UAlive, BAlive;
        public readonly int[] UGen, UHp, UOwner, BGen, BHp, BOwner;
        public readonly Vector2[] UPos, BPos;

        public Field(int units, int buildings)
        {
            UAlive = new bool[units]; UGen = new int[units]; UHp = new int[units]; UOwner = new int[units]; UPos = new Vector2[units];
            BAlive = new bool[buildings]; BGen = new int[buildings]; BHp = new int[buildings]; BOwner = new int[buildings]; BPos = new Vector2[buildings];
        }

        public int Frame(AttackAlerts a, float dt) => a.Update(0, dt, UAlive, UGen, UHp, UOwner, UPos, BAlive, BGen, BHp, BOwner, BPos);
    }

    /// <summary>
    /// Random own units and buildings on a 200 m field, random hits each step (both kinds, several per step), random steps
    /// 0-1.5 s: after every Update every remembered alert's kind and position equal a list oracle's (the docs/03 rule:
    /// oldest matching alert joins, a building hit turns a Units alert Base, never back), the upgraded set is exactly the
    /// oracle's, and an upgraded alert reads Base. Ring capacity 64 so nothing wraps.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void UpgradeFuzz_EveryAlertsKind_EqualsAListOracle(int seed)
    {
        var rnd = new Random(seed * 7919);
        const int units = 40, buildings = 12;
        var f = new Field(units, buildings);
        for (int i = 0; i < units; i++) { f.UAlive[i] = true; f.UGen[i] = 1; f.UHp[i] = 1_000_000; f.UPos[i] = new Vector2(rnd.NextSingle() * 200, rnd.NextSingle() * 200); }
        for (int i = 0; i < buildings; i++) { f.BAlive[i] = true; f.BGen[i] = 1; f.BHp[i] = 1_000_000; f.BPos[i] = new Vector2(rnd.NextSingle() * 200, rnd.NextSingle() * 200); }
        var a = new AttackAlerts(units, buildings, capacity: 64);
        f.Frame(a, 0f);
        var all = new List<(Vector2 At, float When, AttackAlertKind Kind)>();
        float now = 0f;
        long upgradesTotal = 0;
        for (int step = 0; step < 400 && all.Count < 60; step++)
        {
            float dt = rnd.Next(4) == 0 ? 0f : rnd.NextSingle() * 1.5f;
            var hitB = new bool[buildings];
            var hitU = new bool[units];
            int n = rnd.Next(4);
            for (int k = 0; k < n; k++)
            {
                if (rnd.Next(3) == 0) hitB[rnd.Next(buildings)] = true;
                else hitU[rnd.Next(units)] = true;
            }
            for (int i = 0; i < buildings; i++) if (hitB[i]) f.BHp[i]--;
            for (int i = 0; i < units; i++) if (hitU[i]) f.UHp[i]--;
            // the Update, then the oracle
            int raised = f.Frame(a, dt);
            if (dt > 0f) now += dt;
            int before = all.Count;
            var upgraded = new List<int>();
            void Hit(Vector2 at, AttackAlertKind kind)
            {
                for (int k = 0; k < all.Count; k++)
                    if (now - all[k].When < a.Seconds && Vector2.DistanceSquared(all[k].At, at) <= a.Radius * a.Radius)
                    {
                        if (kind == AttackAlertKind.Base && all[k].Kind == AttackAlertKind.Units)
                        {
                            all[k] = (all[k].At, all[k].When, AttackAlertKind.Base);
                            upgraded.Add(k);
                        }
                        return;
                    }
                all.Add((at, now, kind));
            }
            for (int i = 0; i < buildings; i++) if (hitB[i]) Hit(f.BPos[i], AttackAlertKind.Base);
            for (int i = 0; i < units; i++) if (hitU[i]) Hit(f.UPos[i], AttackAlertKind.Units);
            Assert.Equal(all.Count - before, raised);
            Assert.Equal(all.Count, a.Raised);
            // every remembered alert: Recent(k) is the k-th oldest (no wrap at capacity 64 and < 60 alerts)
            for (int k = 0; k < all.Count; k++)
            {
                Assert.Equal(all[k].At, a.Position(a.Recent(k)));
                Assert.True(all[k].Kind == a.Kind(a.Recent(k)), $"seed {seed} step {step}: alert {k} is {a.Kind(a.Recent(k))}, oracle {all[k].Kind}");
            }
            Assert.Equal(upgraded.Count, a.UpgradedCount);
            var got = new List<int>();
            for (int k = 0; k < a.UpgradedCount; k++)
            {
                Assert.Equal(AttackAlertKind.Base, a.Kind(a.Upgraded(k)));
                got.Add(a.Upgraded(k));
            }
            Assert.Equal(upgraded.Select(a.Recent).OrderBy(x => x), got.OrderBy(x => x));
            upgradesTotal += upgraded.Count;
        }
        _out.WriteLine($"seed {seed}: {all.Count} alerts, {upgradesTotal} upgrades, {all.Count(x => x.Kind == AttackAlertKind.Base)} Base at the end");
        Assert.True(upgradesTotal > 0, "the fuzz never upgraded an alert");
    }

    /// <summary>One alert hit by both a unit and a building in the same Update: the building is scanned first, so the alert is Base from the start (no upgrade); a building hit later the same Update on a units alert raised that Update cannot happen.</summary>
    [Fact]
    public void BuildingAndUnitHitInOneUpdate_IsBaseFromTheStart_NoUpgrade()
    {
        var f = new Field(1, 1);
        f.UAlive[0] = true; f.UGen[0] = 1; f.UHp[0] = 100; f.UPos[0] = new Vector2(50, 50);
        f.BAlive[0] = true; f.BGen[0] = 1; f.BHp[0] = 100; f.BPos[0] = new Vector2(60, 50);
        var a = new AttackAlerts(1, 1);
        f.Frame(a, 0f);
        f.UHp[0]--;
        f.BHp[0]--;
        Assert.Equal(1, f.Frame(a, 0.05f));
        Assert.Equal(AttackAlertKind.Base, a.Kind(a.LastIndex));
        Assert.Equal(0, a.UpgradedCount);
        Assert.Equal(2, a.HitsOn(a.LastIndex));
    }

    /// <summary>
    /// A full ring (capacity 2): a building hit upgrades the oldest alert, then a unit hit far away in the same Update raises
    /// a new alert that overwrites that slot. <see cref="AttackAlerts.Upgraded"/> then names a slot holding a different,
    /// Units alert (BUG-0970): the HUD would read the new alert's position for an upgrade.
    /// </summary>
    [Fact(Skip = "BUG-0970: Upgraded(k) can name a slot that a later raise in the same Update overwrote (ring full); needs 32 alerts in 10 s at the default capacity")]
    public void UpgradedSlot_OverwrittenInTheSameUpdate_StillReadsBase()
    {
        var f = new Field(3, 1);
        for (int i = 0; i < 3; i++) { f.UAlive[i] = true; f.UGen[i] = 1; f.UHp[i] = 100; }
        f.UPos[0] = new Vector2(0, 0);
        f.UPos[1] = new Vector2(500, 0);
        f.UPos[2] = new Vector2(1000, 0);
        f.BAlive[0] = true; f.BGen[0] = 1; f.BHp[0] = 100; f.BPos[0] = new Vector2(10, 0);
        var a = new AttackAlerts(3, 1, capacity: 2);
        f.Frame(a, 0f);
        f.UHp[0]--;
        f.UHp[1]--;
        Assert.Equal(2, f.Frame(a, 0.05f)); // ring full: A (0, 0) and B (500, 0), both Units
        f.BHp[0]--;  // joins A: upgrade
        f.UHp[2]--;  // raises C: overwrites the oldest slot, A's
        f.Frame(a, 0.05f);
        Assert.Equal(1, a.UpgradedCount);
        Assert.Equal(AttackAlertKind.Base, a.Kind(a.Upgraded(0)));
    }
}
