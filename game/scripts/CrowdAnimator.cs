using System;
using Godot;

namespace Rts.Game;

/// <summary>Advances the unit models' animation players by hand (M6-VH7, BUG-1321): the figures nearest the camera every frame, the rest staggered at a lower rate.</summary>
/// <remarks>
/// Every model's <see cref="AnimationPlayer"/> is in manual mode (<see cref="UnitModels.Instantiate"/>), so Godot never
/// advances it on its own. Each frame <see cref="Advance"/> takes the shown, active players, finds the
/// <see cref="FullRateCount"/> nearest the eye (the 3D camera), and advances those by the frame's delta; every other one is
/// advanced on one frame in <see cref="StaggerFrames"/> (the frame where <c>(frame + slot) % StaggerFrames == 0</c>, so the
/// crowd's work is spread evenly over the frames), by all the time it has waited since its last advance, so its clip runs
/// at the true speed in coarser steps. Without an eye every shown player advances every frame (the pre-VH7 behaviour).
/// A view-only schedule: it reads the slots' drawn positions and holds no gameplay state; the numbers are view constants,
/// not data. Allocation-free once sized (the sort is on preallocated arrays of primitives).
/// </remarks>
public sealed class CrowdAnimator
{
    /// <summary>How many figures nearest the camera animate every frame.</summary>
    public const int FullRateCount = 48;

    /// <summary>Every other figure animates once in this many frames (20 Hz at 60 FPS).</summary>
    public const int StaggerFrames = 3;

    private readonly double[] _waiting;
    private readonly long[] _advancedAt;
    private readonly int[] _order;
    private readonly float[] _distance;
    private readonly bool[] _fullRate;

    /// <summary>Frames <see cref="Advance"/> has run so far.</summary>
    public long Frame { get; private set; }

    /// <summary>Players advanced in the last frame.</summary>
    public int AdvancedLastFrame { get; private set; }

    /// <summary>Players that were eligible (shown and active) in the last frame.</summary>
    public int EligibleLastFrame { get; private set; }

    /// <summary>Sizes the schedule for <paramref name="slots"/> unit slots.</summary>
    public CrowdAnimator(int slots)
    {
        _waiting = new double[slots];
        _advancedAt = new long[slots];
        Array.Fill(_advancedAt, -1L);
        _order = new int[slots];
        _distance = new float[slots];
        _fullRate = new bool[slots];
    }

    /// <summary>The <see cref="Frame"/> slot <paramref name="slot"/>'s player was last advanced in, or -1.</summary>
    public long LastAdvanced(int slot) => (uint)slot < (uint)_advancedAt.Length ? _advancedAt[slot] : -1;

    /// <summary>True when slot <paramref name="slot"/> was among the nearest, advanced at the full rate, in the last frame.</summary>
    public bool WasFullRate(int slot) => (uint)slot < (uint)_fullRate.Length && _fullRate[slot];

    /// <summary>Forgets slot <paramref name="slot"/>'s waiting time (it was hidden or got a new player).</summary>
    public void Reset(int slot)
    {
        if ((uint)slot >= (uint)_waiting.Length) return;
        _waiting[slot] = 0;
        _fullRate[slot] = false;
    }

    /// <summary>One frame of the schedule: advances the shown players of <paramref name="anim"/> nearest <paramref name="eye"/> by <paramref name="delta"/> and the rest staggered.</summary>
    /// <param name="anim">Per slot its player (null: a capsule).</param>
    /// <param name="shown">Per slot whether its view is drawn.</param>
    /// <param name="views">Per slot its view node, whose global position is the figure's.</param>
    /// <param name="eye">The camera's global position, or null to advance every shown player every frame.</param>
    /// <param name="delta">View seconds since the last frame.</param>
    public void Advance(AnimationPlayer?[] anim, bool[] shown, MeshInstance3D?[] views, Vector3? eye, double delta)
    {
        long frame = Frame++;
        int n = Math.Min(Math.Min(anim.Length, shown.Length), Math.Min(views.Length, _waiting.Length));
        int count = 0;
        for (int i = 0; i < n; i++)
        {
            _fullRate[i] = false;
            AnimationPlayer? a = anim[i];
            if (a == null || !shown[i] || !a.Active)
            {
                _waiting[i] = 0;
                continue;
            }
            _waiting[i] += delta;
            _order[count] = i;
            _distance[count] = eye is Vector3 e ? views[i]!.GlobalPosition.DistanceSquaredTo(e) : 0f;
            count++;
        }
        EligibleLastFrame = count;
        // Nearest first: only the order of the first FullRateCount matters, but a sort of a few hundred keys is cheap.
        if (eye != null && count > FullRateCount) Array.Sort(_distance, _order, 0, count);
        int advanced = 0;
        for (int k = 0; k < count; k++)
        {
            int slot = _order[k];
            bool full = eye == null || k < FullRateCount;
            if (!full && (frame + slot) % StaggerFrames != 0) continue;
            _fullRate[slot] = full;
            anim[slot]!.Advance(_waiting[slot]);
            _waiting[slot] = 0;
            _advancedAt[slot] = frame;
            advanced++;
        }
        AdvancedLastFrame = advanced;
    }
}
