using System.Text.Json.Nodes;
using Rts.Sim.Combat;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA M7-H1 (session 2026-11-11-1317): the match stats' kill / loss counters against QA's own oracle, with a third faction in
/// every seat position. The third faction here is a hand-made <b>Whirlwind</b> copy (<c>mirage</c>, appended to the order of
/// the shipped data cut to its first two factions), unlike the developer's Malazan copy. Four AI seats (Hard, Normal, Hard,
/// Normal) on a 128 map, the third faction at seat <c>k</c> and Malazan / Whirlwind alternating on the others, 21,600 ticks.
/// Two independent oracles every tick: (a) <see cref="World.Deaths"/> counted by killer and victim owner with the M6-S5 rule
/// (no teams: an own-unit kill is the victim's loss and nobody's kill; expiries and collapses count neither), and (b) the
/// unit store's live slots diffed tick to tick (a slot alive before and free or re-generationed after is one unit death of its
/// owner), which must equal the death list's unit entries per victim owner (so the list dropped nothing).
/// </summary>
[Collection(SerialCollection.Name)]
public class M7H1KillCounterQaStressTests(ITestOutputHelper output)
{
    private const int Ticks = 21_600;

    private static readonly Lazy<GameData> s_mirage = new(() =>
    {
        using TestDataDir dir = TestDataDir.CopyOfShippedTwoFactions();
        AddWhirlwindCopy(dir, "mirage");
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join(Environment.NewLine, r.Errors));
        return r.Data!;
    });

    /// <summary>A Whirlwind copy as faction <paramref name="id"/>: every <c>whirlwind_</c> id renamed, the faction tech and ability renamed, appended to the order.</summary>
    internal static void AddWhirlwindCopy(TestDataDir dir, string id)
    {
        string from = dir.FullPath("factions/whirlwind"), to = dir.FullPath("factions/" + id);
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from))
        {
            string text = File.ReadAllText(file).Replace("whirlwind_", id + "_")
                .Replace("\"dryjhnas_prophecy\"", $"\"{id}_prophecy\"").Replace("\"sandstorm\"", $"\"{id}_sandstorm\"");
            if (System.IO.Path.GetFileName(file) == "faction.json")
                text = System.Text.RegularExpressions.Regex.Replace(text, "\"id\"\\s*:\\s*\"whirlwind\"", $"\"id\": \"{id}\"");
            File.WriteAllText(System.IO.Path.Combine(to, System.IO.Path.GetFileName(file)), text);
        }
        dir.EditJson("common/factions.json", root => root["order"]!.AsArray().Add(id));
    }

    public static IEnumerable<object[]> Rows()
    {
        ulong[] seeds = { 21, 22, 23, 24, 25 };
        for (int s = 0; s < seeds.Length; s++)
            for (int k = 0; k < 4; k++)
                yield return new object[] { seeds[s], k };
    }

    [Theory]
    [Trait("Category", "Stress")]
    [MemberData(nameof(Rows))]
    public void KillAndLossCounters_EqualTwoOracles_WithAThirdFactionAtEachSeat(ulong seed, int thirdAt)
    {
        GameData data = s_mirage.Value;
        Assert.Equal(3, data.Factions.Length);
        int third = data.FindFaction("mirage");
        var seats = new PlayerSeat[4];
        int next = 0;
        for (int p = 0; p < 4; p++)
            seats[p] = new PlayerSeat(p == thirdAt ? third : next++ % 2, SeatController.Ai, p % 2 == 0 ? AiDifficulty.Hard : AiDifficulty.Normal);
        Simulation sim = AiScenes.Generated(seats, seed, false, out _, units: 1024, commands: 4096, data: data,
            configure: c => c with { Map = c.Map with { Width = 128, Height = 128, Forests = 12, GoldMines = 8, StartLocations = 4 } });
        World w = sim.World;
        Entities.UnitStore u = w.Units;
        int[] uk = new int[4], ul = new int[4], bk = new int[4], bl = new int[4], ownKills = new int[4], slotDeaths = new int[4], listedUnitDeaths = new int[4];
        var wasAlive = new bool[u.Capacity];
        var wasGen = new int[u.Capacity];
        var wasOwner = new int[u.Capacity];
        int thirdKills = 0, thirdLosses = 0;
        for (int t = 0; t < Ticks; t++)
        {
            for (int i = 0; i < u.Capacity; i++)
            {
                wasAlive[i] = u.Alive[i];
                wasGen[i] = u.Generation[i];
                wasOwner[i] = u.Owner[i];
            }
            sim.Tick();
            Assert.True(w.Deaths.Length < u.Capacity + w.Buildings.Capacity, $"tick {sim.TickNumber}: the death list is full");
            foreach (DeathEvent d in w.Deaths)
            {
                if (!d.IsBuilding) listedUnitDeaths[d.VictimOwner]++;
                if (d.Expired || d.Collapsed) continue;
                if (d.IsBuilding) bl[d.VictimOwner]++; else ul[d.VictimOwner]++;
                if (d.KillerOwner < 0) continue;
                if (d.KillerOwner == d.VictimOwner)
                {
                    ownKills[d.KillerOwner]++;
                    continue;
                }
                if (d.IsBuilding) bk[d.KillerOwner]++; else uk[d.KillerOwner]++;
            }
            for (int i = 0; i < u.Capacity; i++)
                if (wasAlive[i] && (!u.Alive[i] || u.Generation[i] != wasGen[i])) slotDeaths[wasOwner[i]]++;
        }
        for (int p = 0; p < 4; p++)
        {
            string row = FormattableString.Invariant(
                $"seed {seed} third at {thirdAt}: seat {p} ({data.Factions[seats[p].Faction].Key}): unit kills {w.UnitKills[p]} (oracle {uk[p]}, own {ownKills[p]}), unit losses {w.UnitLosses[p]} ({ul[p]}), building kills {w.BuildingKills[p]} ({bk[p]}), building losses {w.BuildingLosses[p]} ({bl[p]}), slot deaths {slotDeaths[p]} (listed {listedUnitDeaths[p]})");
            output.WriteLine(row);
            Assert.True(uk[p] == w.UnitKills[p], row);
            Assert.True(ul[p] == w.UnitLosses[p], row);
            Assert.True(bk[p] == w.BuildingKills[p], row);
            Assert.True(bl[p] == w.BuildingLosses[p], row);
            Assert.True(slotDeaths[p] == listedUnitDeaths[p], row);
            Assert.True(w.Kills[p] == uk[p] + bk[p] && w.Losses[p] == ul[p] + bl[p], row + " (Kills / Losses are not the halves' sum)");
            if (p == thirdAt) (thirdKills, thirdLosses) = (uk[p] + bk[p], ul[p] + bl[p]);
        }
        Assert.True(thirdKills + thirdLosses > 0, $"the third faction's seat {thirdAt} never fought (seed {seed})");
    }
}
