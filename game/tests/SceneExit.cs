using System;
using Godot;

namespace Rts.Game.Tests;

/// <summary>
/// The test scenes' shutdown (M4-VH2, BUG-0251): free the scene, let .NET finalize the Godot wrappers it held while Godot is
/// still running, then quit.
/// </summary>
/// <remarks>
/// A bare <c>GetTree().Quit()</c> leaves the scene's C# references (the Match <c>PackedScene</c>, view meshes and materials)
/// to the .NET finalizer thread, which can dispose a RefCounted wrapper while Godot is tearing the C# language down: under
/// load that crashed after the test's PASS line ("FATAL: Condition csharp_lang ...", exit -1073741795). Here the scene node
/// (and the Match under it) is freed first; once the test object is unreachable a full collection and
/// <see cref="GC.WaitForPendingFinalizers"/> run every pending finalizer a frame before the quit.
/// </remarks>
public static class SceneExit
{
    /// <summary>Frees <paramref name="scene"/>, collects and finalizes, waits a frame, then quits with <paramref name="exitCode"/>. Call it last: the scene is gone a frame later.</summary>
    public static async void Quit(Node scene, int exitCode)
    {
        SceneTree tree = scene.GetTree();
        scene.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame); // the scene and its Match are freed by now
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        tree.Quit(exitCode);
    }
}
