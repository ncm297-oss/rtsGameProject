using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-6 (Request 52, docs/03 "Implementation (M7-6)"): an opening's optional <c>attack.reserveArmyShare</c>. The reserve's
/// gate (<see cref="AiPlayer.MayReserve"/>, M5-4g / BUG-1021) reads the seat's own opening's share when its file carries one,
/// else <c>rules.json</c> <c>ai.reserveArmyShare</c>; the difficulty's <c>waveSizeMultiplier</c> applies either way. Every
/// opening here is a test opening: no shipped file carries the field.
/// </summary>
public class OpeningReserveShareTests
{
    private readonly ITestOutputHelper _out;

    public OpeningReserveShareTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// The shipped data with every opening of <paramref name="faction"/> (every faction when null) carrying
    /// <paramref name="share"/> (left out when null); optionally every Malazan opening's build order Age II alone and its
    /// army Heavy Infantry, the scout home (ReserveTests' gate scene, the rules' share as shipped).
    /// </summary>
    private static GameData Data(string? faction, double? share, bool ageTwoOnly = false) => AiScenes.DataWith(dir =>
    {
        foreach (string f in TestSim.ShippedFactions)
            dir.EditJson($"factions/{f}/ai.json", root =>
            {
                foreach (JsonNode? o in root["openings"]!.AsArray())
                {
                    if (share != null && (faction == null || faction == f)) o!["attack"]!["reserveArmyShare"] = share.Value;
                    if (ageTwoOnly && f == "malazan")
                    {
                        o!["buildOrder"] = AiScenes.BuildOrder(("time", 0, "research", "age_ii", 0));
                        o["compositions"] = JsonNode.Parse("""[{ "age": 1, "units": { "malazan_heavy_infantry": 1 } }, { "age": 2, "units": { "malazan_heavy_infantry": 1 } }]""");
                    }
                }
            });
        if (ageTwoOnly) dir.EditJson("common/rules.json", r => r["ai"]!["scoutAfter"] = 3600);
    });

    /// <summary>
    /// Units of type <paramref name="type"/> needed for <paramref name="share"/> of <paramref name="ai"/>'s wave (its opening's
    /// half-pop and gold + wood, each x the difficulty's <c>waveSizeMultiplier</c>), rounded up: the more of the two.
    /// </summary>
    private static int Needed(AiPlayer ai, int type, double share)
    {
        UnitDef u = ai.Data.Units[type];
        double wave = share * ai.Knobs.WaveSizeMultiplier;
        int forPop = (int)Math.Ceiling(wave * ai.Opening.AttackMinHalfPop / u.HalfPop - 1e-6);
        int forCost = (int)Math.Ceiling(wave * ai.Opening.AttackMinCost / u.UnitWorth - 1e-6);
        return Math.Max(forPop, forCost);
    }

    /// <summary>Places <paramref name="count"/> units of <paramref name="type"/> for player <paramref name="p"/> beside its hall, toward the map's middle.</summary>
    private static void Army(Simulation sim, int p, int type, int count)
    {
        (int hx, int hy) = AiScenes.HallAt(p);
        int toward = p % 2 == 0 ? 1 : -1;
        for (int k = 0; k < count; k++)
            CombatScenes.Place(sim, p, type, sim.World.NavGrid.CellCenter(hx + toward * (6 + k % 6), hy + 1 + k / 6));
    }

    // ---------- (a) a seat's own share names the reserve at half the wave ----------

    private static readonly Lazy<GameData> s_without = new(() => Data(null, null, ageTwoOnly: true));
    private static readonly Lazy<GameData> s_half = new(() => Data("malazan", 0.5, ageTwoOnly: true));

    /// <summary>
    /// Criterion 4 (a): ReserveTests' gate scene (a Hard Malazan seat, Age II its only step, a Barracks and a Crossbow Range,
    /// the purse 300 / 300 against Age II's 400 / 200). With half the wave in Heavy Infantry, the seat whose opening carries
    /// 0.5 names Age II as its reserved step; the same seat without the field (the rules' 1) does not; one Heavy Infantry
    /// fewer than half names none either way.
    /// </summary>
    [Theory]
    [InlineData(true, 0, 0)]
    [InlineData(false, 0, -1)]
    [InlineData(true, -1, -1)]
    [InlineData(false, -1, -1)]
    public void AnOpeningsShareOfHalf_NamesTheReserve_AtHalfTheWave(bool carries, int extra, int step)
    {
        GameData d = carries ? s_half.Value : s_without.Value;
        Assert.Equal(1f, d.AiRules.ReserveArmyShare); // the scene's premise: the rules gate at the whole wave
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Human), d);
        World w = sim.World;
        Assert.True(w.Buildings.Spawn(0, d.FindBuilding("malazan_barracks"), 44 * AiScenes.FlatSize + 8, out _));
        Assert.True(w.Buildings.Spawn(0, d.FindBuilding("malazan_crossbow_range"), 44 * AiScenes.FlatSize + 16, out _));
        AiPlayer ai = sim.AiOf(0)!;
        Assert.Equal(carries ? 0.5f : null, ai.Opening.ReserveArmyShare);
        int hi = d.FindUnit("malazan_heavy_infantry");
        int half = Needed(ai, hi, 0.5);
        Assert.True(Needed(ai, hi, 1) > half && half + extra > 0, $"half {half}, whole {Needed(ai, hi, 1)}");
        Army(sim, 0, hi, half + extra);
        int named = -2;
        ai.ThoughtForTests = a => named = a.ReserveStep;
        BuildMaps.SetTotals(sim, 0, 300, 300);
        sim.Tick(); // tick 0: the first think
        Assert.Equal(step, named);
    }

    // ---------- (b) two AI seats, each its own share ----------

    /// <summary>
    /// Criterion 4 (b): a Hard Malazan seat and a Hard Whirlwind seat, each with half its own wave in its line unit. Each
    /// seat's gate opens exactly when its own faction's openings carry 0.5 (else the rules' 1 holds it shut), whatever the
    /// other's carry: the opponent's gate does not move.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void TwoAiSeats_EachFollowItsOwnOpeningsShare(bool malazanHalf, bool whirlwindHalf)
    {
        GameData d = AiScenes.DataWith(dir =>
        {
            foreach ((string f, bool half) in new[] { ("malazan", malazanHalf), ("whirlwind", whirlwindHalf) })
                if (half)
                    dir.EditJson($"factions/{f}/ai.json", root =>
                    {
                        foreach (JsonNode? o in root["openings"]!.AsArray()) o!["attack"]!["reserveArmyShare"] = 0.5;
                    });
        });
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Ai), d);
        var may = new bool?[2];
        for (int p = 0; p < 2; p++)
        {
            AiPlayer ai = sim.AiOf(p)!;
            int line = StartBase.UnitOfSlot(d, sim.World.FactionOf(p), UnitSlot.Line);
            int half = Needed(ai, line, 0.5);
            Assert.True(Needed(ai, line, 1) > half, $"player {p}: half {half}");
            Army(sim, p, line, half);
            int seat = p;
            ai.ThoughtForTests = a =>
            {
                Assert.False(a.Board.Defending);
                may[seat] = a.MayReserve();
            };
        }
        sim.Tick(); // tick 0: both seats' first think
        Assert.Equal(new bool?[] { malazanHalf, whirlwindHalf }, may);
    }

    // ---------- (c) the rules' own value, written on every opening, plays as absent ----------

    /// <summary>
    /// Criterion 4 (c): every opening of every faction carrying the <c>rules.json</c> value plays MatchCriterionTests' literal
    /// row (Vathar Crossing seed 1, Normal v Normal, to its end or its 24,000-tick bound) with the same per-tick state hashes,
    /// end tick and winner as the shipped data (no field). The content hash differs (the files do).
    /// </summary>
    [Fact]
    public void TheRulesValue_OnEveryOpening_PlaysHashIdenticalToNoField_OverTheLiteralMatch()
    {
        float rules = TestSim.Data.AiRules.ReserveArmyShare;
        GameData same = Data(null, rules);
        Assert.All(same.Ai.SelectMany(a => a.Openings), o => Assert.Equal(rules, o.ReserveArmyShare));
        Assert.NotEqual(TestSim.Data.ContentHash(), same.ContentHash());
        MatchCriterionTests.Row row = MatchCriterionTests.RowOf("vathar_crossing");
        CriterionMatch.Result shipped = CriterionMatch.Play(Literal(row, TestSim.Data), row.Bound, checks: false);
        CriterionMatch.Result carried = CriterionMatch.Play(Literal(row, same), row.Bound, checks: false);
        _out.WriteLine($"shipped {shipped.TwinLine}; field {carried.TwinLine}");
        Assert.True(shipped.Over, "the literal row ends inside its bound (MatchCriterionTests)");
        Assert.Equal(shipped.TwinLine, carried.TwinLine);
    }

    /// <summary>MatchCriterionTests' literal row's match on <paramref name="data"/> (as <c>HuntStressTests.Match</c> builds it).</summary>
    private static Simulation Literal(MatchCriterionTests.Row row, GameData data)
    {
        var seats = new PlayerSeat[row.Players];
        for (int p = 0; p < row.Players; p++) seats[p] = new PlayerSeat(p % 2, SeatController.Ai, AiDifficulty.Normal);
        int size = data.Maps[data.FindMap(row.Map)].Size;
        return AiScenes.Generated(seats, row.Seed, false, out _, units: 400, data: data,
            configure: c => c with { MapId = row.Map, Map = c.Map with { Width = size, Height = size } });
    }

    // ---------- criterion 5: the save ----------

    private static readonly Lazy<GameData> s_saveHalf = new(() => Data(null, 0.5));

    /// <summary>
    /// Criterion 5: the save format is unchanged (version 10: the share is data, read from the seat's opening each think,
    /// not saved), and AiSaveScenes' two-seat AI match with every opening at 0.5 saved mid-match (ticks 137 and 1,000) loads
    /// and continues hash-identical for 2,000 ticks, a reserve named on the way.
    /// </summary>
    [Fact]
    public void AMidMatchSave_OfSeatsWithAPerOpeningShare_LoadsAndContinuesHashIdentical()
    {
        Assert.Equal(10, SaveFormat.CurrentVersion);
        GameData d = s_saveHalf.Value;
        SaveScene scene = AiSaveScenes.AiMatch(5, 2, AiDifficulty.Normal, data: d);
        int reserved = 0;
        bool hooked = false;
        SaveTestRun.Result r = SaveTestRun.RoundTrip(scene, new[] { 137, 1000 }, after: 2000, observe: sim =>
        {
            if (hooked) return;
            hooked = true;
            for (int p = 0; p < 2; p++)
            {
                Assert.Equal(0.5f, sim.AiOf(p)!.Opening.ReserveArmyShare);
                sim.AiOf(p)!.ThoughtForTests = a => { if (a.ReserveStep >= 0) reserved++; };
            }
        });
        _out.WriteLine($"compared {r.TicksCompared} ticks; thinks naming a reserve {reserved}");
        Assert.True(r.TicksCompared >= 4000);
        Assert.True(reserved > 0, "no reserve named: the scene does not exercise the share");
    }
}
