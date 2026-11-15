using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M6-S9 (session 2026-11-14-2156): per-tick invariant watch for gather-overlap scenes. After every tick: positions finite,
/// no unit on blocked ground, the largest step any unit took (against its speed), the closest gather pair (share of its radii
/// sum, against the scaled pack limit), the closest non-gather pair, BUG-0146's shape (a worker on the node leg out of
/// <see cref="EconomyConstants.Reach"/> of its node with a clear line to it, within 0.5 m of one spot), and the longest a
/// worker on its loop went without a trip while its node lived (a new node or no node restarts its clock).
/// </summary>
internal sealed class GatherOverlapQaWatch
{
    private readonly Simulation _sim;
    private readonly float _limitShare;
    private readonly Vector2[] _before;
    private readonly int[] _cargo;
    private readonly int[] _clock;
    private readonly EntityHandle[] _node;
    private readonly int[] _wedgeRun;
    private readonly Vector2[] _anchor;
    private readonly bool _floorOn;
    private readonly bool[] _armed;
    private readonly int[] _insideSince;

    public GatherOverlapQaWatch(Simulation sim)
    {
        _sim = sim;
        World w = sim.World;
        UnitStore u = w.Units;
        float overlap = w.Data.Rules.GatherOverlap;
        _floorOn = overlap > 0f;
        _limitShare = MovementConstants.ShoveSpacing * (1f - overlap);
        _before = new Vector2[u.Capacity];
        _cargo = new int[u.Capacity];
        _clock = new int[u.Capacity];
        _node = new EntityHandle[u.Capacity];
        _wedgeRun = new int[u.Capacity];
        _anchor = new Vector2[u.Capacity];
        _armed = new bool[u.Capacity * u.Capacity];
        _insideSince = new int[u.Capacity * u.Capacity];
        Snapshot();
        for (int i = 0; i < u.Capacity; i++)
        {
            _clock[i] = sim.TickNumber;
            _node[i] = u.GatherNode[i];
            _cargo[i] = u.Cargo[i];
            _anchor[i] = u.Position[i];
        }
    }

    /// <summary>Largest (step length - speed) seen for any live unit, m.</summary>
    public float MaxStepExcess { get; private set; } = float.NegativeInfinity;
    public string MaxStepAt { get; private set; } = "";

    /// <summary>Smallest centre distance / radii sum over gather pairs.</summary>
    public float ClosestGatherShare { get; private set; } = float.PositiveInfinity;

    /// <summary>Smallest centre distance / radii sum over live pairs that are not gather pairs (any owner).</summary>
    public float ClosestOtherShare { get; private set; } = float.PositiveInfinity;

    /// <summary>Smallest share over pairs of different owners.</summary>
    public float ClosestEnemyShare { get; private set; } = float.PositiveInfinity;

    /// <summary>Longest BUG-0146 node-leg wedge (ticks) and where.</summary>
    public int LongestWedge { get; private set; }
    public string WedgeAt { get; private set; } = "";

    /// <summary>Longest ticks a worker on its loop went without a delivery while its node lived.</summary>
    public int LongestNoTrip { get; private set; }
    public string NoTripAt { get; private set; } = "";

    public int Trips { get; private set; }

    /// <summary>Longest a gather pair that never stood at its limit (spawned overlapped) stayed inside it, ticks.</summary>
    public int LongestUnarmedInside { get; private set; }

    /// <summary>First failure of a hard invariant (finite, blocked ground, floor), or null.</summary>
    public string? Failure { get; private set; }

    /// <summary>Restarts unit i's trip clock (a new order).</summary>
    public void Restart(int i) => _clock[i] = _sim.TickNumber;

    private void Snapshot() => Array.Copy(_sim.World.Units.Position, _before, _before.Length);

    /// <summary>Ticks once and checks.</summary>
    public void Tick()
    {
        Snapshot();
        _sim.Tick();
        Check();
    }

    public void Run(int ticks)
    {
        for (int t = 0; t < ticks; t++) Tick();
    }

    private void Check()
    {
        World w = _sim.World;
        UnitStore u = w.Units;
        int now = _sim.TickNumber;
        int blocked = MoveScenario.FirstUnitOnBlockedGround(w);
        if (blocked >= 0) Failure ??= $"unit {blocked} on blocked ground at tick {now} ({u.Position[blocked]})";
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i])
            {
                _clock[i] = now;
                continue;
            }
            Vector2 p = u.Position[i];
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y)) Failure ??= $"unit {i} not finite at tick {now}";
            float step = Vector2.Distance(_before[i], p) - u.Speed[i];
            if (step > MaxStepExcess)
            {
                MaxStepExcess = step;
                MaxStepAt = $"unit {i} ({u.State[i]}, gather {u.GatherNode[i].Generation != 0}) at tick {now}";
            }
            for (int j = i + 1; j < u.Capacity; j++)
            {
                if (!u.Alive[j]) continue;
                float sum = u.Radius[i] + u.Radius[j];
                float d = Vector2.Distance(p, u.Position[j]);
                if (d > 3f) continue;
                float share = d / sum;
                if (MovementSystem.IsGatherPair(u, i, j))
                {
                    // The floor is armed for a pair once it stood at or outside the limit: a pair spawned overlapped
                    // (spawns don't separate) is the spawn's, measured separately (how long it stays inside).
                    int key = i * u.Capacity + j;
                    if (share >= _limitShare - 1e-4f)
                    {
                        _armed[key] = true;
                        _insideSince[key] = 0;
                    }
                    else if (!_armed[key])
                    {
                        if (_insideSince[key] == 0) _insideSince[key] = now;
                        LongestUnarmedInside = Math.Max(LongestUnarmedInside, now - _insideSince[key] + 1);
                    }
                    if (_armed[key] && share < ClosestGatherShare) ClosestGatherShare = share;
                    if (_floorOn && _armed[key] && share < _limitShare - 1e-4f) Failure ??= $"gather pair {i}/{j} at {share:F4} of its sum at tick {now}, past the scaled pack limit {_limitShare:F3}";
                }
                else
                {
                    if (share < ClosestOtherShare) ClosestOtherShare = share;
                    if (u.Owner[i] != u.Owner[j] && share < ClosestEnemyShare) ClosestEnemyShare = share;
                }
            }
            // Trips and the no-trip clock.
            if (_cargo[i] > 0 && u.Cargo[i] == 0)
            {
                Trips++;
                _clock[i] = now;
            }
            _cargo[i] = u.Cargo[i];
            EntityHandle node = u.GatherNode[i];
            if (node != _node[i] || node.Generation == 0 || !w.Resources.IsAlive(node)) _clock[i] = now;
            _node[i] = node;
            int since = now - _clock[i];
            if (since > LongestNoTrip)
            {
                LongestNoTrip = since;
                NoTripAt = $"unit {i} ({u.State[i]}, at {p}, cargo {u.Cargo[i]}, stuck {u.StuckTicks[i]}) at tick {now}";
            }
            // BUG-0146's shape on the node leg.
            bool nodeLeg = EconomySystem.OnLoop(u, i) && w.Resources.IsAlive(node) && u.Cargo[i] < w.Data.CarryOf(u.TypeId[i]);
            if (!nodeLeg || InReach(w, i) || SomeoneBetween(w, i) || Vector2.Distance(p, _anchor[i]) > 0.5f)
            {
                _anchor[i] = p;
                _wedgeRun[i] = 0;
                continue;
            }
            if (++_wedgeRun[i] > LongestWedge)
            {
                LongestWedge = _wedgeRun[i];
                WedgeAt = $"unit {i} at {p} ({u.State[i]}) to tick {now}";
            }
        }
    }

    public string Line =>
        $"step excess {MaxStepExcess:F4} m ({MaxStepAt}); closest gather pair {ClosestGatherShare:F3} (limit {_limitShare:F3}; a spawn overlap stayed inside {LongestUnarmedInside} ticks); closest other {ClosestOtherShare:F3}; closest enemy {ClosestEnemyShare:F3}; " +
        $"longest node-leg wedge {LongestWedge} ({WedgeAt}); longest without a trip {LongestNoTrip} ({NoTripAt}); trips {Trips}";

    private static bool InReach(World w, int i)
    {
        int n = w.Units.GatherNode[i].Index;
        ResourceDef def = w.Data.Resources[w.Resources.TypeId[n]];
        return GatherMaps.DistanceToFootprint(w.NavGrid, w.Units.Position[i], w.Resources.Cell[n], def.FootprintWidth, def.FootprintHeight) <= EconomyConstants.Reach;
    }

    // GatherPocketStressTests.SomeoneBetween, copied (private there).
    private static bool SomeoneBetween(World w, int i)
    {
        UnitStore u = w.Units;
        NavGrid g = w.NavGrid;
        int n = u.GatherNode[i].Index;
        ResourceDef def = w.Data.Resources[w.Resources.TypeId[n]];
        int cell = w.Resources.Cell[n];
        const float cs = MapConstants.CellSize;
        float x0 = cell % g.Width * cs, y0 = cell / g.Width * cs;
        Vector2 min = new(x0, y0), max = new(x0 + def.FootprintWidth * cs, y0 + def.FootprintHeight * cs);
        Vector2 p = u.Position[i], q = Vector2.Clamp(p, min, max);
        float mine = Vector2.Distance(p, q);
        Vector2 d = q - p;
        float len2 = d.LengthSquared();
        for (int j = 0; j < u.Capacity; j++)
        {
            if (j == i || !u.Alive[j]) continue;
            Vector2 pj = u.Position[j];
            float s = len2 > 0f ? Math.Clamp(Vector2.Dot(pj - p, d) / len2, 0f, 1f) : 0f;
            float r = u.Radius[i] + u.Radius[j];
            if (Vector2.DistanceSquared(pj, p + d * s) > r * r) continue;
            if (Vector2.Distance(pj, Vector2.Clamp(pj, min, max)) < mine) return true;
        }
        int steps = (int)(mine / 0.25f);
        for (int k = 1; k < steps; k++)
        {
            Vector2 at = p + d * (k / (float)steps);
            if (!g.WorldToCell(at, out int cx, out int cy)) continue;
            bool own = at.X >= min.X && at.X <= max.X && at.Y >= min.Y && at.Y <= max.Y;
            if (!own && !g.IsPassable(cx, cy)) return true;
        }
        return false;
    }
}
