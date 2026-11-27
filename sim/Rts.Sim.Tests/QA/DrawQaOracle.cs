using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M6-S13; M6-SH3: the exact "can still train"): an independent reading of the draw rule (docs/02 "Game flow" 4, docs/01 2026-11-25-1437 PLAN row) written
/// from the design, not from <c>World.Draw.cs</c>: every quantity is scanned from the stores each time (no counter), so a
/// counter the stores' paths forget to move shows up as a disagreement. <see cref="Step"/> follows the clock one tick.
/// </summary>
internal sealed class DrawQaOracle
{
    private readonly Simulation _sim;
    private readonly List<long> _liveBefore = new();

    /// <summary>The clock this oracle expects (0 to the wait).</summary>
    public int Clock { get; private set; }

    /// <summary>Whether this oracle expects the match drawn.</summary>
    public bool Drawn { get; private set; }

    /// <summary>The clock's restarts from a non-zero value this oracle saw, split by cause.</summary>
    public int RestartsOnDeath, RestartsOnDefeat, RestartsUnspent;

    public DrawQaOracle(Simulation sim)
    {
        _sim = sim;
        World w = sim.World;
        Clock = w.DrawClock;
        Drawn = w.MatchDrawn;
    }

    /// <summary>
    /// The design's "can still train", every term scanned from the stores. M6-SH3: the exact rule of docs/01 2026-11-27 (second
    /// row) replaced M6-S13's gold-only reading; the scan is <see cref="Draw.DrawReachScan"/> (no counter is read).
    /// </summary>
    public bool CanTrain(int p) => Draw.DrawReachScan.NewCan(_sim.World, p);

    /// <summary>The design's "spent", given whether the match has a result (a win, not the draw) now.</summary>
    public bool Spent(bool hasWinResult)
    {
        World w = _sim.World;
        if (hasWinResult) return false;
        int first = -1;
        bool two = false;
        for (int p = 0; p < w.Config.PlayerCount; p++)
        {
            if (!w.EverBuilt(p) || w.IsDefeated(p)) continue;
            if (first < 0) first = p;
            else if (!w.AreAllied(first, p)) two = true;
        }
        if (!two) return false;
        for (int p = 0; p < w.Config.PlayerCount; p++)
            if (!w.IsDefeated(p) && CanTrain(p)) return false;
        return true;
    }

    /// <summary>Call before <see cref="Simulation.Tick"/>: remembers the live non-summoned units.</summary>
    public void BeforeTick()
    {
        UnitStore u = _sim.World.Units;
        _liveBefore.Clear();
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && !u.IsSummoned(i)) _liveBefore.Add(((long)u.Generation[i] << 32) | (uint)i);
    }

    /// <summary>Call after the tick: moves the expected clock as the design says and returns a failure text, or null when the sim agrees.</summary>
    public string? AfterTick()
    {
        World w = _sim.World;
        UnitStore u = w.Units;
        bool died = false;
        foreach (long key in _liveBefore)
        {
            int i = (int)(uint)key, gen = (int)(key >> 32);
            if (!u.IsAlive(new EntityHandle(i, gen))) { died = true; break; }
        }
        bool defeated = w.DefeatEvents.Length > 0;
        if (!Drawn && w.DrawWaitTicks > 0)
        {
            bool winResult = w.MatchOver && !w.MatchDrawn;
            bool spent = Spent(winResult);
            if (died || defeated || !spent)
            {
                if (Clock > 0)
                {
                    if (died) RestartsOnDeath++;
                    else if (defeated) RestartsOnDefeat++;
                    else RestartsUnspent++;
                }
                Clock = 0;
            }
            else if (++Clock >= w.DrawWaitTicks) Drawn = true;
        }
        if (w.DrawClock != Clock || w.MatchDrawn != Drawn)
            return $"tick {w.TickNumber - 1}: sim clock {w.DrawClock} drawn {w.MatchDrawn}, oracle clock {Clock} drawn {Drawn} (died {died}, defeated {defeated})";
        if (Drawn && !w.MatchOver) return $"tick {w.TickNumber - 1}: drawn but not over";
        for (int p = 0; p < w.Config.PlayerCount; p++)
            if (w.CanStillTrain(p) != CanTrain(p))
                return $"tick {w.TickNumber - 1}: CanStillTrain({p}) sim {w.CanStillTrain(p)}, oracle {CanTrain(p)}";
        return null;
    }
}
