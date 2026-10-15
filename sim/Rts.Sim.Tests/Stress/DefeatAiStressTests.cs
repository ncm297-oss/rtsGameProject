using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA (M5-4b, session 2026-10-13-0916): four AI seats (Easy, Normal, Hard, Hard) on the flat scene, combat on, the early-wave
/// data and plenty to spend, with the halls of some seats pre-damaged to 1 hp so defeats happen inside the run (M5-4e: and
/// their workers taken away at the start, a live unit keeping a side in while nobody hunts them, M5-4d). After every
/// tick: a defeated player stays defeated with no live unit and no finished unit-producing building; a <see cref="World.DefeatEvents"/> entry comes once per
/// player, on the tick its flag flips; a defeated seat's AI sends nothing and thinks no more; <see cref="Simulation.MatchOver"/>
/// matches the rule recomputed from the flags; a twin run hashes equal every 50 ticks.
/// </summary>
public class DefeatAiStressTests
{
    private readonly ITestOutputHelper _out;

    public DefeatAiStressTests(ITestOutputHelper output) => _out = output;

    private static readonly Lazy<GameData> s_early = new(() => AiScenes.EarlyWaves(2));

    [Theory]
    [InlineData(3UL, 0b1100)]
    [InlineData(7UL, 0b0011)]
    // Measured (QA 2026-10-13-0916), not rows: weak 0110, 1010, 1001 see no defeat in 6,000 ticks; a strong seat defends from
    // early to the end (one threat, never released: BUG-0670 / BUG-0671) and never waves.
    public void FourAiSeats_WithWeakHalls_DefeatsStick_TheLosersGoSilent_Deterministic(ulong seed, int weak)
    {
        var seats = new PlayerSeat[]
        {
            new(0, SeatController.Ai, AiDifficulty.Easy), new(1, SeatController.Ai, AiDifficulty.Normal),
            new(0, SeatController.Ai, AiDifficulty.Hard), new(1, SeatController.Ai, AiDifficulty.Hard),
        };
        Simulation Make()
        {
            Simulation s = AiScenes.Flat(seats, s_early.Value, units: 600, commands: 1024, seed: seed, combat: true);
            for (int p = 0; p < 4; p++) BuildMaps.SetTotals(s, p, (weak >> p & 1) != 0 ? 0 : 3000, (weak >> p & 1) != 0 ? 0 : 3000);
            for (int k = 0; k < s.World.Buildings.Capacity; k++)
                if (s.World.Buildings.Alive[k] && (weak >> s.World.Buildings.Owner[k] & 1) != 0) s.World.Buildings.SetHp(k, 1);
            for (int i = 0; i < s.World.Units.Capacity; i++)
                if (s.World.Units.Alive[i] && (weak >> s.World.Units.Owner[i] & 1) != 0) s.World.Units.Free(new Rts.Sim.Entities.EntityHandle(i, s.World.Units.Generation[i]));
            // The strong seats start with eight line units each beside their hall, toward the middle.
            for (int p = 0; p < 4; p++)
            {
                if ((weak >> p & 1) != 0) continue;
                (int hx, int hy) = AiScenes.HallAt(p);
                int line = Rts.Sim.ViewApi.StartBase.UnitOfSlot(s.World.Data, s.World.FactionOf(p), UnitSlot.Line);
                int dx = p == 0 ? 6 : p == 1 ? -4 : 0, dy = p == 2 ? 6 : p == 3 ? -4 : 0;
                for (int k = 0; k < 8; k++) CombatScenes.Place(s, p, line, s.World.NavGrid.CellCenter(hx + dx + k % 4, hy + dy + k / 4));
            }
            return s;
        }
        Simulation sim = Make(), twin = Make();
        World w = sim.World;
        var defeatedAt = new int[4];
        var sentAt = new int[4];
        var thinksAt = new int[4];
        Array.Fill(defeatedAt, -1);
        for (int t = 0; t < 6000; t++)
        {
            sim.Tick();
            twin.Tick();
            int built = 0, standing = 0;
            for (int p = 0; p < 4; p++)
            {
                AiPlayer ai = sim.AiOf(p)!;
                bool now = w.IsDefeated(p);
                int events = 0;
                foreach (DefeatEvent e in w.DefeatEvents)
                    if (e.Player == p)
                    {
                        events++;
                        Assert.Equal(sim.TickNumber - 1, e.Tick);
                    }
                if (defeatedAt[p] < 0)
                {
                    Assert.Equal(now ? 1 : 0, events);
                    if (now)
                    {
                        defeatedAt[p] = sim.TickNumber - 1;
                        sentAt[p] = ai.CommandsSent;
                        thinksAt[p] = ai.Thinks;
                    }
                }
                else
                {
                    Assert.True(now, $"seed {seed}: player {p} un-defeated at tick {t}");
                    Assert.Equal(0, events);
                    Assert.Equal(sentAt[p], ai.CommandsSent);
                    Assert.Equal(thinksAt[p], ai.Thinks);
                }
                if (now) Assert.True(w.LiveUnits(p) == 0 && w.ProductionBuildings(p) == 0, $"seed {seed}: player {p} defeated with {w.LiveUnits(p)} units, {w.ProductionBuildings(p)} production buildings");
                if (w.EverBuilt(p))
                {
                    built++;
                    if (!now) standing++;
                }
            }
            Assert.Equal(built >= 2 && standing <= 1, sim.MatchOver);
            if (t % 50 == 0) Assert.True(sim.StateHash() == twin.StateHash(), $"seed {seed}: twins differ at tick {t}");
        }
        Assert.Equal(sim.StateHash(), twin.StateHash());
        _out.WriteLine($"seed {seed}, weak {Convert.ToString(weak, 2)}: defeated at {string.Join(", ", defeatedAt)}; match over {sim.MatchOver}; " +
            $"waves {string.Join(", ", Enumerable.Range(0, 4).Select(p => sim.AiOf(p)!.Board.WavesStarted))}; defending now {string.Join(", ", Enumerable.Range(0, 4).Select(p => sim.AiOf(p)!.Board.Defending ? "y" : "n"))} since {string.Join(", ", Enumerable.Range(0, 4).Select(p => sim.AiOf(p)!.Board.DefendTick))}; defenses {string.Join(", ", Enumerable.Range(0, 4).Select(p => sim.AiOf(p)!.Board.Defenses))}");
        Assert.True(defeatedAt.Count(d => d >= 0) >= 1, $"seed {seed}: nobody was defeated: the run proves little");
    }
}
