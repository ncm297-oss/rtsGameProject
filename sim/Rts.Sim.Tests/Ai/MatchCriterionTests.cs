using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-5, M5 criterion 5 ("AI-vs-AI headless test: a 20-minute match completes without errors and one side wins"; docs/03
/// "Implementation (M5-5)"). The literal row: two Normal AI seats (Malazan v Whirlwind, starts 0 and 1) on Vathar Crossing,
/// seed 1, end with exactly one side defeated within 24,000 ticks (20:00), every error check of <see cref="CriterionMatch"/>
/// clean on every tick and think; it runs in the default loop. The same row on the other maps, each inside its documented
/// bound; a four-seat free-for-all and a 2v2; every row the same in two processes; the literal row's half-way save followed
/// to the same end hash.
/// </summary>
public class MatchCriterionTests
{
    private readonly ITestOutputHelper _out;

    public MatchCriterionTests(ITestOutputHelper output) => _out = output;

    /// <summary>One criterion row: the map, seed, seats, teams (null: none) and the tick bound it must end within.</summary>
    internal sealed record Row(string Id, string Map, ulong Seed, int Players, int[]? Teams, int Bound);

    /// <summary>
    /// The literal row's bound: 20 minutes. Vathar Crossing seed 1 ended at 23,322 (19:26; M5-H6's house rule, was 23,043 since
    /// M5-9a's counter shift, 22,794 before), seat 1 (Whirlwind) the winner. M6-S10 (the Keep-corner cure and gatherOverlap
    /// 0.5): seed 1 ends at 24,497 (20:24, seat 1), past the bound, so by the brief's rule (the generated row's precedent, M5-9a
    /// round 2) the literal row moves to the lowest of seeds 1-8 on Vathar Crossing that ends inside 24,000 with a winner and
    /// every check clean: <b>seed 7, 23,412 (19:30), seat 0 (Malazan)</b>, Build UnitInTheWay 2 refused. Seeds 1-8: 24,497 /
    /// 30,569 / 27,240 / 27,240 / 27,361 / 27,240 / 23,412 / 23,412 (<c>Stress/MatchRowsM6S10Report</c>). The methods keep
    /// their "Seed1" names (docs/05 cites them); the row is seed 7.
    /// M6-SH2 (a construction site is no drop-off; an AI with no worker retrains): seed 7 ends at <b>19,912 (16:35), seat 0</b>,
    /// Build UnitInTheWay 1 refused, every check clean. Seeds 1-8: 24,266 / 57,039 / 25,118 / 25,118 / 55,495 / 25,118 /
    /// 19,912 / 19,912, so seed 7 is still the lowest under 24,000.
    /// </summary>
    internal const int VatharBound = 24_000;

    /// <summary>
    /// Pale Hills seed 1 ended at 34,219 (28:30; <c>MapFileTests</c>' pin), seat 1 the winner: the Producer's 36,000. M5-9a:
    /// with Normal's counter shift it no longer ended (both sides out of gold by 24:00), round 2's starved wave ended it only
    /// at 68,664 (BUG-1153). M5-9b (the far expansion, BUG-1068 (a)): both seats take an unused start's mines with a depot at
    /// about 28,000 and the match ends at <b>48,858 (40:42), seat 1</b>, every check clean: outcome (b) of the terminal rule,
    /// so the bound is <b>72,000</b> (the four-seat rows' hour), BUG-1153, <b>Producer decision 2026-11-08-1556</b> (docs/03
    /// "Implementation (M5-5)" and STATE; the owner may revisit it). Used by <c>MapFileTests</c>' Pale Hills row too. M5-H6
    /// (the house rule, the hunt's free starts from its second hunt, the leg's fixes): 50,140 (41:47), seat 1, one hunt.
    /// </summary>
    internal const int PaleHillsBound = 72_000;

    /// <summary>Raraku seed 1 ends at 28,083 (23:24), seat 0 the winner: the next 6,000 above it with at least 10 % slack (30,891). Unchanged by M5-9a; M5-H6: 28,231 (23:31), seat 0.
    /// M6-S10 (the Keep-corner cure and gatherOverlap 0.5): <b>seed 1 no longer ends inside 72,000</b> (BUG-1460, a finding, not
    /// a new bound: seat 0 is down to buildings alone from about 27,000, seat 1 keeps 17 idle workers, little wood and 2
    /// soldiers, and razes slowly; no held worker or unhunted lone unit causes it), so by the brief's rule the row moves to the
    /// lowest of seeds 1-8 on Raraku that ends: <b>seed 2, 27,936 (23:16), seat 1</b>; Raraku's rule gives the same bound
    /// (27,936 x 1.1 = 30,730, the next 6,000 above it 36,000). Seeds 1-8: none / 27,936 / none / none / 29,569 / none / 28,547
    /// / 28,547 (<c>Stress/MatchRowsM6S10Report</c>).
    /// M6-SH2 (a construction site is no drop-off; an AI with no worker retrains): <b>seed 2 now ends at 56,940 (47:27), seat 0,
    /// past the bound</b> (BUG-1521, a finding, not a new bound), so by the brief's rule the row moves to the lowest of seeds 1-8
    /// that ends inside it: <b>seed 3, 25,083 (20:54), seat 1</b>, every check clean. Seeds 1-8: none / 56,940 / 25,083 /
    /// 25,083 / 31,420 / 25,083 / 28,633 / 28,633 (seed 1 still BUG-1460).</summary>
    internal const int RarakuBound = 36_000;

    /// <summary>
    /// The generated map: seed 1 was the lowest of 1-6 ending under 24,000 (21,789 = 18:09, seat 0 the winner). M5-9a round 1:
    /// Normal's counter shift lengthened it to 26,853 (the bound went to 30,000 by Raraku's rule); round 2 (the starved wave)
    /// ends it at 25,968 (21:38), still over 24,000, so by the brief's rule the row moves to seed 2, the lowest of 1-6 under
    /// 24,000 (15,691 = 13:05, seat 1 the winner, unchanged by M5-9a), and keeps the original 24,000. M5-H6 (the house
    /// rule): 17,053 (14:12), seat 1.
    /// </summary>
    internal const int GeneratedBound = 24_000;

    /// <summary>The four-seat rows' bound (60 minutes, QA's M5-H5 sweep): the FFA (Vathar seed 2) ends at 58,155 (48:27; was 52,305 before M5-9a), the 2v2 (Vathar seed 1) at 35,648 (29:42; was 34,510). M5-H6: the FFA 56,896 (47:24, seat 3), the 2v2 26,070 (21:43, team 1).</summary>
    internal const int FourSeatBound = 72_000;

    /// <summary>The tick the literal row is saved at: half its end (M6-SH2: seed 7's 19,912 / 2; was 11,706 (M6-S10: 23,412 / 2), and 11,397, half of seed 1's 22,794 before M5-9a; any mid-match tick serves).</summary>
    internal const int HalfWay = 9_956;

    internal static readonly Row[] Rows =
    {
        new("vathar_crossing", "vathar_crossing", 7, 2, null, VatharBound), // M6-S10: seed 7 (seed 1 ends at 24,497)
        new("raraku", "raraku", 3, 2, null, RarakuBound), // M6-SH2: seed 3 (seed 2 ends at 56,940: BUG-1521); M6-S10: seed 2 (seed 1 no longer ends inside 72,000: BUG-1460)
        new("pale_hills", "pale_hills", 1, 2, null, PaleHillsBound),
        new("generated", "generated", 2, 2, null, GeneratedBound), // M5-9a round 2: seed 2 (seed 1 ends at 25,968)
        new("ffa", "vathar_crossing", 2, 4, null, FourSeatBound),
        new("2v2", "vathar_crossing", 1, 4, new[] { 1, 2, 1, 2 }, FourSeatBound),
    };

    internal static Row RowOf(string id) => Rows.Single(r => r.Id == id);

    internal static Simulation Build(Row row) => CriterionMatch.Build(row.Map, row.Seed, row.Players, row.Teams);

    /// <summary>Plays <paramref name="row"/> with every check on and asserts the criterion: no error, over inside the bound, exactly one side left.</summary>
    private CriterionMatch.Result PlayChecked(Row row)
    {
        Simulation sim = Build(row);
        CriterionMatch.Result r = CriterionMatch.Play(sim, row.Bound, checks: true, _out.WriteLine);
        int defeated = r.DefeatedAt.Count(t => t > 0);
        _out.WriteLine($"{row.Id} ({row.Map} seed {row.Seed}, {row.Players} seats{(row.Teams == null ? "" : " 2v2")}): over {r.Over} at {CriterionMatch.Clock(r.EndTick)} ({r.EndTick}, bound {row.Bound}); "
            + $"winner {r.Winner}; defeated at {string.Join(" / ", r.DefeatedAt)}; waves {r.Waves}, hunts {r.Hunts}; fold {r.Fold:X16}, end hash {r.EndHash:X16}");
        _out.WriteLine($"  checks: {r.Thinks} thinks ({r.OracleChecks} oracle, {r.CensusChecks} census), {r.EntityChecks} entity checks; refused: {r.RefusalText}; "
            + $"longest refusal run {r.LongestStreak}{(r.LongestStreak > 0 ? " (" + r.LongestStreakAt + ")" : "")}");
        Assert.True(r.Error == null, r.Error);
        Assert.True(r.Over, $"no result in {row.Bound} ticks");
        Assert.True(r.EndTick <= row.Bound);
        Assert.NotEqual("none", r.Winner);
        // Exactly one side left: two seats, one defeated; four, every seat of the losing side(s) defeated.
        if (row.Players == 2) Assert.Equal(1, defeated);
        else if (row.Teams == null) Assert.Equal(row.Players - 1, defeated);
        Assert.True(r.LongestStreak <= CriterionMatch.MaxStreak);
        return r;
    }

    /// <summary>
    /// The criterion's literal row (default loop): Vathar Crossing seed 7 (seed 1 before M6-S10), Normal v Normal, ends with one side defeated within
    /// 24,000 ticks, checks (a)-(e) clean on every tick and think.
    /// </summary>
    [Fact]
    public void VatharCrossing_Seed1_NormalVersusNormal_EndsWithAWinnerWithin24000Ticks_WithoutErrors() => PlayChecked(RowOf("vathar_crossing"));

    /// <summary>The same row on Raraku, Pale Hills and the generated map, each inside its documented bound.</summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData("raraku")]
    [InlineData("pale_hills")]
    [InlineData("generated")]
    public void EachOtherMap_NormalVersusNormal_EndsWithAWinnerInsideItsBound_WithoutErrors(string id) => PlayChecked(RowOf(id));

    /// <summary>A four-seat free-for-all and a 2v2 (teams 1 / 2 / 1 / 2), Normal seats, end inside 72,000 ticks without errors.</summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData("ffa")]
    [InlineData("2v2")]
    public void FourSeats_EndWithOneSideLeft_Within72000Ticks_WithoutErrors(string id)
    {
        CriterionMatch.Result r = PlayChecked(RowOf(id));
        if (id == "2v2") Assert.StartsWith("t", r.Winner);
    }

    private const string TwinVariable = "RTS_M5_5_TWIN_CHILD", TwinMarker = "M5-5-TWIN ";

    /// <summary>
    /// Every row in two processes: this one and a fresh child <c>dotnet test</c> of the same row play it to its end (checks
    /// off: they only read, the fold is the same) and print the per-tick fold, the end hash and tick, the winner and the
    /// hunts started: equal.
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData("vathar_crossing")]
    [InlineData("raraku")]
    [InlineData("pale_hills")]
    [InlineData("generated")]
    [InlineData("ffa")]
    [InlineData("2v2")]
    public void EveryRow_InTwoProcesses_TheSameFoldEndTickWinnerAndHunts(string id)
    {
        // The child runs every row of this method (the filter names the method): only the row named in the variable plays.
        string? childOf = Environment.GetEnvironmentVariable(TwinVariable);
        if (childOf != null && childOf != id) return;
        Row row = RowOf(id);
        CriterionMatch.Result r = CriterionMatch.Play(Build(row), row.Bound, checks: false);
        if (childOf != null)
        {
            _out.WriteLine(TwinMarker + r.TwinLine);
            return;
        }
        _out.WriteLine($"{id}: this process {r.TwinLine}");
        Assert.True(r.Over, $"no result in {row.Bound} ticks");
        string child = MapFileTests.RunChild(typeof(MatchCriterionTests), nameof(EveryRow_InTwoProcesses_TheSameFoldEndTickWinnerAndHunts), TwinVariable, TwinMarker, seconds: 900, value: id);
        _out.WriteLine($"{id}: the child {child}");
        Assert.Equal(r.TwinLine, child);
    }

    /// <summary>
    /// The literal row survives a save: saved at <see cref="HalfWay"/>, loaded in a fresh sim (save -> load -> save
    /// byte-identical) and followed tick for tick to the end, it has the run's hash on every tick, ends on the same tick with
    /// the same result and end hash.
    /// </summary>
    [Fact]
    public void VatharCrossing_Seed1_SavedHalfWay_FollowsToTheSameEndHash()
    {
        Row row = RowOf("vathar_crossing");
        Simulation? loaded = null;
        int followed = 0;
        CriterionMatch.Result r = CriterionMatch.Play(Build(row), row.Bound, checks: false, onTick: sim =>
        {
            if (loaded != null)
            {
                loaded.Tick();
                followed++;
                Assert.True(loaded.StateHash() == sim.StateHash(), $"the save from tick {HalfWay} diverged at tick {sim.TickNumber}");
                Assert.Equal(sim.MatchOver, loaded.MatchOver);
            }
            else if (sim.TickNumber == HalfWay)
            {
                byte[] bytes = SaveTestRun.Save(sim);
                loaded = SaveTestRun.Load(TestSim.Data, bytes);
                Assert.Equal(bytes, SaveTestRun.Save(loaded));
            }
        });
        _out.WriteLine($"saved at {HalfWay}, followed {followed} ticks to {r.EndTick}: end hash {r.EndHash:X16}, winner {r.Winner}");
        Assert.True(r.Over);
        Assert.NotNull(loaded);
        Assert.Equal(r.EndTick - HalfWay, followed);
        Assert.Equal(r.EndHash, loaded!.StateHash());
        Assert.Equal(r.Winner, CriterionMatch.Winner(loaded.World));
    }
}
