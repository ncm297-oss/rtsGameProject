namespace Rts.Sim.ViewApi;

/// <summary>
/// When the game keeps the cursor inside its window (M6-V7, docs/02 "Controls and camera"): only while a match runs unpaused
/// in the focused window and the player's setting allows it, so the edge-pan band is reachable on every side even with a
/// second monitor next to the window. Everything else (a menu, the pause menu, the match-over banner, Alt-Tab) frees it.
/// </summary>
public static class CursorConfine
{
    /// <summary>
    /// True to confine the cursor: a match is running, the tree is not paused (pause menu, banner, stats, settings over the
    /// pause menu all pause it), the window has focus, and <paramref name="allowed"/> (the "Confine cursor to window" setting,
    /// off under <c>--no-confine</c> and headless).
    /// </summary>
    public static bool Wanted(bool matchRunning, bool paused, bool focused, bool allowed) => matchRunning && !paused && focused && allowed;
}
