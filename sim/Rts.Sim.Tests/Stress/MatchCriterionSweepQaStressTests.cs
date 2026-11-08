using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA M5-5 (session 2026-11-08-0726), focus 1 and 3: the criterion row's shape (two Normal seats, Malazan v Whirlwind,
/// starts 0 and 1, every check (a)-(e) of <see cref="CriterionMatch"/> on) on each of the four maps x seeds 1-6, to 72,000
/// ticks, with four extra oracles for what (a)-(e) cannot see, measured on every tick:
/// <list type="bullet">
/// <item>a unit stuck: <see cref="UnitState.Moving"/> without leaving a 2 m circle for <see cref="StuckTicks"/>;</item>
/// <item>a silent seat: an undefeated AI seat with a live worker that sends no command for <see cref="SilentTicks"/> (a seat
/// without a worker may have nothing left to order; its clock restarts);</item>
/// <item>still resources: an undefeated seat whose stock and gathered totals do not move (measured and printed only: a
/// losing seat with no worker left is still by design);</item>
/// <item>a wave that never leaves: a wave (or hunt) active <see cref="WaveLeaveTicks"/> whose army's centroid never got
/// <see cref="WaveLeaveMeters"/> from where it stood when the wave began.</item>
/// </list>
/// Each row prints one table line. A row that does not end is a note (BUG-1068, BUG-1116), not a failure; an error from
/// (a)-(e) or an extra oracle past its limit fails the row.
/// </summary>
[Trait("Category", "Stress")]
public class MatchCriterionSweepQaStressTests
{
    internal const int Bound = 72_000;
    internal const int StuckTicks = 2_400, SilentTicks = 4_800, WaveLeaveTicks = 1_200;
    internal const float WaveLeaveMeters = 15f;

    private readonly ITestOutputHelper _out;

    public MatchCriterionSweepQaStressTests(ITestOutputHelper output) => _out = output;

    public static TheoryData<string, ulong> Rows()
    {
        var rows = new TheoryData<string, ulong>();
        foreach (string map in new[] { "vathar_crossing", "raraku", "pale_hills", "generated" })
            for (ulong s = 1; s <= 6; s++) rows.Add(map, s);
        return rows;
    }

    /// <summary>
    /// Rows a known bug fails, with the bug: measured here, printed, and not asserted (the row asserts it still fails, so a fix
    /// is noticed and the entry removed). Every other row must pass every oracle.
    /// </summary>
    internal static readonly Dictionary<(string Map, ulong Seed), string> Known = new()
    {
        // The rebalance re-sends a Gather to a gone mine every think: check (e)'s streak fires at tick 18,421.
        [("generated", 4)] = "BUG-1151",
        // A losing seat whose last worker gathers only gold, wood under the cost of anything, sends nothing for 4+ minutes.
        [("raraku", 1)] = "BUG-1152",
        [("raraku", 2)] = "BUG-1152",
    };

    /// <summary>The extra oracles' state over one match.</summary>
    internal sealed class Extra
    {
        private readonly int _n;
        private readonly int[] _lastSent, _lastResChange, _workers;
        private readonly long[] _resKey;
        private readonly Vector2[] _anchor;
        private readonly int[] _anchorTick, _anchorGen;
        private readonly bool[] _reported;
        private readonly bool[] _waving;
        private readonly Vector2[] _waveFrom;
        private readonly int[] _waveStart;
        private readonly float[] _waveMax;
        private readonly bool[] _waveHunt;
        public readonly int[] MaxSilent, MaxStill;
        public readonly string[] SilentAt, StillAt;
        public int Stuck, WorstStuck;
        public string WorstStuckAt = "";
        public readonly List<string> StuckLines = new();
        public int WavesSeen, WavesStayed;
        public readonly List<string> StayedLines = new();
        public int LongestWave;
        private bool _chained;

        public Extra(Simulation sim)
        {
            _n = sim.World.Config.PlayerCount;
            int cap = sim.World.Units.Capacity;
            _lastSent = new int[_n];
            _lastResChange = new int[_n];
            _workers = new int[_n];
            _resKey = new long[_n];
            MaxSilent = new int[_n];
            MaxStill = new int[_n];
            SilentAt = new string[_n];
            StillAt = new string[_n];
            _anchor = new Vector2[cap];
            _anchorTick = new int[cap];
            _anchorGen = new int[cap];
            _reported = new bool[cap];
            _waving = new bool[_n];
            _waveFrom = new Vector2[_n];
            _waveStart = new int[_n];
            _waveMax = new float[_n];
            _waveHunt = new bool[_n];
            for (int p = 0; p < _n; p++) SilentAt[p] = StillAt[p] = "";
        }

        /// <summary>After each tick; on the first, chains onto the AI seats' sent hook that <see cref="CriterionMatch.Play"/> set.</summary>
        public void OnTick(Simulation sim)
        {
            World w = sim.World;
            int now = sim.TickNumber;
            if (!_chained)
            {
                _chained = true;
                for (int p = 0; p < _n; p++)
                {
                    AiPlayer? ai = sim.AiOf(p);
                    if (ai == null) continue;
                    Action<Command>? inner = ai.SentForTests;
                    int player = p;
                    ai.SentForTests = c =>
                    {
                        inner?.Invoke(c);
                        _lastSent[player] = c.Tick;
                    };
                }
            }
            Array.Clear(_workers);
            for (int i = 0; i < w.Units.Capacity; i++)
                if (w.Units.Alive[i] && EconomySystem.IsWorker(w, i)) _workers[w.Units.Owner[i]]++;
            for (int p = 0; p < _n; p++)
            {
                if (w.IsDefeated(p)) continue;
                if (_workers[p] == 0) _lastSent[p] = now; // nothing to order: the clock restarts
                int silent = now - _lastSent[p];
                if (silent > MaxSilent[p])
                {
                    MaxSilent[p] = silent;
                    SilentAt[p] = $"p{p} silent {_lastSent[p]}..{now}";
                }
                long key = ((long)w.Gold[p] << 40) ^ ((long)w.Wood[p] << 20) ^ ((long)w.GatheredGold[p] * 31 + w.GatheredWood[p]);
                if (key != _resKey[p])
                {
                    _resKey[p] = key;
                    _lastResChange[p] = now;
                }
                int still = now - _lastResChange[p];
                if (still > MaxStill[p])
                {
                    MaxStill[p] = still;
                    StillAt[p] = $"p{p} still {_lastResChange[p]}..{now} (gold {w.Gold[p]} wood {w.Wood[p]})";
                }
            }

            UnitStore u = w.Units;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                Vector2 at = u.Position[i];
                if (u.Generation[i] != _anchorGen[i] || u.State[i] != UnitState.Moving || Vector2.DistanceSquared(at, _anchor[i]) >= 4f)
                {
                    _anchorGen[i] = u.Generation[i];
                    _anchor[i] = at;
                    _anchorTick[i] = now;
                    _reported[i] = false;
                    continue;
                }
                int still = now - _anchorTick[i];
                if (still > WorstStuck)
                {
                    WorstStuck = still;
                    WorstStuckAt = $"unit {i} (p{u.Owner[i]} type {w.Data.Units[u.TypeId[i]].Key}) at {at} goal {u.Goal[i]} since {_anchorTick[i]}";
                }
                if (still >= StuckTicks && !_reported[i])
                {
                    _reported[i] = true;
                    Stuck++;
                    if (StuckLines.Count < 8)
                        StuckLines.Add($"tick {now}: unit {i} (p{u.Owner[i]} {w.Data.Units[u.TypeId[i]].Key}) Moving in 2 m since {_anchorTick[i]} at {at}, goal {u.Goal[i]}, target {u.Target[i]}");
                }
            }

            for (int p = 0; p < _n; p++)
            {
                AiPlayer? ai = sim.AiOf(p);
                if (ai == null) continue;
                bool active = ai.Board.WaveActive && !w.IsDefeated(p);
                if (active && !_waving[p])
                {
                    _waving[p] = true;
                    _waveStart[p] = now;
                    _waveFrom[p] = ArmyCentroid(w, p, out _);
                    _waveMax[p] = 0f;
                    _waveHunt[p] = ai.Board.Hunting;
                    WavesSeen++;
                }
                else if (active)
                {
                    Vector2 c = ArmyCentroid(w, p, out int count);
                    if (count > 0) _waveMax[p] = Math.Max(_waveMax[p], Vector2.Distance(c, _waveFrom[p]));
                }
                if (!active && _waving[p]) EndWave(p, now);
            }
        }

        private void EndWave(int p, int now)
        {
            _waving[p] = false;
            int length = now - _waveStart[p];
            LongestWave = Math.Max(LongestWave, length);
            if (length >= WaveLeaveTicks && _waveMax[p] < WaveLeaveMeters)
            {
                WavesStayed++;
                StayedLines.Add($"p{p} {(_waveHunt[p] ? "hunt" : "wave")} {_waveStart[p]}..{now}: centroid moved at most {_waveMax[p]:F1} m");
            }
        }

        public void Finish(Simulation sim)
        {
            for (int p = 0; p < _n; p++) if (_waving[p]) EndWave(p, sim.TickNumber);
        }

        private static Vector2 ArmyCentroid(World w, int p, out int count)
        {
            UnitStore u = w.Units;
            Vector2 sum = Vector2.Zero;
            count = 0;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i] || u.Owner[i] != p || EconomySystem.IsWorker(w, i)) continue;
                sum += u.Position[i];
                count++;
            }
            return count == 0 ? Vector2.Zero : sum / count;
        }
    }

    /// <summary>One row of the sweep: checks (a)-(e) clean; the extra oracles printed and held to their limits.</summary>
    [Theory]
    [MemberData(nameof(Rows))]
    public void NormalVersusNormal_SeedSweep_ChecksAndExtraOracles(string map, ulong seed)
    {
        Simulation sim = CriterionMatch.Build(map, seed);
        var extra = new Extra(sim);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        CriterionMatch.Result r = CriterionMatch.Play(sim, Bound, checks: true, onTick: extra.OnTick);
        extra.Finish(sim);
        sw.Stop();
        _out.WriteLine($"SWEEP | {map} | {seed} | {(r.Over ? "ends" : "NO END")} | {r.EndTick} ({CriterionMatch.Clock(r.EndTick)}) | {r.Winner} | waves {r.Waves} hunts {r.Hunts} | refused {r.RefusalText} | streak {r.LongestStreak} "
            + $"| silent max {string.Join("/", extra.MaxSilent)} | still max {string.Join("/", extra.MaxStill)} | stuck {extra.Stuck} (worst {extra.WorstStuck}) | waves stayed {extra.WavesStayed}/{extra.WavesSeen}, longest {extra.LongestWave} "
            + $"| {r.EndHash:X16} | {sw.Elapsed.TotalSeconds:F0} s | error {r.Error ?? "none"}");
        foreach (string s in extra.SilentAt) _out.WriteLine("  " + s);
        foreach (string s in extra.StillAt) _out.WriteLine("  " + s);
        _out.WriteLine("  worst stuck: " + extra.WorstStuckAt);
        foreach (string s in extra.StuckLines) _out.WriteLine("  " + s);
        foreach (string s in extra.StayedLines) _out.WriteLine("  " + s);
        string? failure = r.Error
            ?? (extra.MaxSilent.Max() >= SilentTicks ? $"a seat with a worker sent nothing for {extra.MaxSilent.Max()} ticks: {string.Join("; ", extra.SilentAt)}" : null)
            ?? (extra.Stuck > 0 ? $"{extra.Stuck} units stuck Moving: {string.Join("; ", extra.StuckLines)}" : null)
            ?? (extra.WavesStayed > 0 ? string.Join("; ", extra.StayedLines) : null);
        if (Known.TryGetValue((map, seed), out string? bug))
        {
            _out.WriteLine($"  known ({bug}): {failure ?? "no longer fails: remove the entry"}");
            Assert.True(failure != null, $"{map} seed {seed} no longer fails ({bug} fixed?): remove it from Known");
            return;
        }
        Assert.True(failure == null, failure);
    }
}
