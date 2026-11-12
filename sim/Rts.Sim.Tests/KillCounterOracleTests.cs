using Rts.Sim.Combat;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-H1 (BUG-1191's scene finding): the match stats' kill and loss counters against an independent oracle. The view's
/// <c>QaM6V11Test</c> read "row 3 UnitsKilled '68' (oracle '72')" in a four-seat match once seat 2 played a third faction.
/// Here the same shape runs in the sim alone: four seats on a 128 map (seat 0 an idle human, seats 1-3 AI: Hard, Normal,
/// Hard), seat p on faction <c>p mod factions</c> as the setup's default, 21,600 ticks, on a copy with a third faction
/// appended (a Malazan copy, <see cref="FactionOrderTests.AddTeblor"/>) and on the two shipped factions. Every tick the oracle
/// reads <see cref="World.Deaths"/> and counts a death for its killer's owner when it was not an expiry, not a defeated
/// player's collapse and the killer is another player (no teams here, so every other player is an enemy: docs/03
/// "Implementation (M6-S5)", a friendly-fire death is the victim's loss and nobody's kill). The friendly-fire deaths are
/// counted apart and printed: an oracle that counts them as kills (killer owner >= 0 only) reads that many more.
/// </summary>
[Collection(SerialCollection.Name)]
public class KillCounterOracleTests(ITestOutputHelper output)
{
    private const int Ticks = 21_600;

    private static readonly Lazy<GameData> s_third = new(() =>
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        FactionOrderTests.AddTeblor(dir);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join(Environment.NewLine, r.Errors));
        return r.Data!;
    });

    private sealed class Oracle(int players)
    {
        public readonly int[] UnitKills = new int[players], UnitLosses = new int[players];
        public readonly int[] BuildingKills = new int[players], BuildingLosses = new int[players];
        public readonly int[] FriendlyFireUnits = new int[players];
        public readonly List<string> FriendlyFire = new();
        public int Dropped;
    }

    [Theory]
    [Trait("Category", "Stress")]
    [InlineData(11UL, true)]
    [InlineData(12UL, true)]
    [InlineData(13UL, true)]
    [InlineData(11UL, false)]
    [InlineData(12UL, false)]
    [InlineData(13UL, false)]
    public void TheStatsKillAndLossCounters_EqualADeathListOracle_InAFourSeatMatch(ulong seed, bool thirdFaction)
    {
        GameData data = thirdFaction ? s_third.Value : TestSim.TwoFactionData;
        int factions = data.Factions.Length;
        Assert.Equal(thirdFaction ? 3 : 2, factions);
        var seats = new PlayerSeat[4];
        for (int p = 0; p < 4; p++)
            seats[p] = new PlayerSeat(p % factions, p == 0 ? SeatController.Human : SeatController.Ai, p == 2 ? AiDifficulty.Normal : AiDifficulty.Hard);
        Simulation sim = AiScenes.Generated(seats, seed, false, out _, units: 1024, commands: 4096, data: data,
            configure: c => c with { Map = c.Map with { Width = 128, Height = 128, Forests = 12, GoldMines = 8, StartLocations = 4 } });
        World w = sim.World;
        var o = new Oracle(4);
        for (int t = 0; t < Ticks; t++)
        {
            sim.Tick();
            Observe(w, o, sim.TickNumber);
        }
        string Row(int p) => FormattableString.Invariant(
            $"seat {p} ({data.Factions[seats[p].Faction].Key}): unit kills {w.UnitKills[p]} (oracle {o.UnitKills[p]}, own-unit kills not counted {o.FriendlyFireUnits[p]}), unit losses {w.UnitLosses[p]} ({o.UnitLosses[p]}), building kills {w.BuildingKills[p]} ({o.BuildingKills[p]}), building losses {w.BuildingLosses[p]} ({o.BuildingLosses[p]})");
        for (int p = 0; p < 4; p++) output.WriteLine(Row(p));
        foreach (string f in o.FriendlyFire) output.WriteLine(f);
        Assert.Equal(0, o.Dropped);
        int fought = 0;
        for (int p = 0; p < 4; p++)
        {
            Assert.True(o.UnitKills[p] == w.UnitKills[p], Row(p));
            Assert.True(o.UnitLosses[p] == w.UnitLosses[p], Row(p));
            Assert.True(o.BuildingKills[p] == w.BuildingKills[p], Row(p));
            Assert.True(o.BuildingLosses[p] == w.BuildingLosses[p], Row(p));
            fought += o.UnitKills[p];
        }
        Assert.True(fought > 20, $"the seats hardly fought ({fought} unit kills)");
    }

    private static void Observe(World w, Oracle o, int tick)
    {
        // A full list drops an event (BUG-0452); the oracle would then miss it, so the row says so rather than guess.
        if (w.Deaths.Length >= w.Units.Capacity + w.Buildings.Capacity) o.Dropped++;
        foreach (DeathEvent d in w.Deaths)
        {
            if (d.Expired || d.Collapsed) continue;
            if (d.IsBuilding) o.BuildingLosses[d.VictimOwner]++;
            else o.UnitLosses[d.VictimOwner]++;
            if (d.KillerOwner < 0) continue;
            if (d.KillerOwner == d.VictimOwner)
            {
                if (!d.IsBuilding) o.FriendlyFireUnits[d.KillerOwner]++;
                o.FriendlyFire.Add(FormattableString.Invariant(
                    $"own kill: tick {tick}, seat {d.KillerOwner}'s {(d.IsBuilding ? w.Data.Buildings[d.VictimType].Key : w.Data.Units[d.VictimType].Key)} at ({d.Position.X:0.#}, {d.Position.Y:0.#})"));
                continue;
            }
            if (d.IsBuilding) o.BuildingKills[d.KillerOwner]++;
            else o.UnitKills[d.KillerOwner]++;
        }
    }
}
