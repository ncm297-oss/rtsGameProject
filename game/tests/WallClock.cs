using System.Threading.Tasks;
using Godot;

namespace Rts.Game.Tests;

/// <summary>Scene-test waits on the wall clock, the clock <see cref="Sfx"/>'s rate limit reads (BUG-0220).</summary>
/// <remarks>
/// A scene-tree timer counts frame deltas, which can run ahead of the wall clock (a delta clamped after a stalled frame
/// under CPU load, or <c>Engine.TimeScale</c>), so a sound played right after one could still fall inside the 50 ms gap
/// and be dropped. These waits loop frames until the wall clock has moved far enough.
/// </remarks>
public static class WallClock
{
    /// <summary>Waits at least one process frame and until at least <paramref name="ms"/> milliseconds of wall-clock time have passed.</summary>
    public static async Task Wait(Node node, double ms)
    {
        ulong start = Time.GetTicksUsec(), need = (ulong)(ms * 1000.0);
        SceneTree tree = node.GetTree();
        do await node.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        while (Time.GetTicksUsec() - start < need);
    }
}
