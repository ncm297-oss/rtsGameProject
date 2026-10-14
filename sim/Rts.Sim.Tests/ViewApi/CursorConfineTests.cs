using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M6-V7: when the shell confines the cursor to the window (<see cref="CursorConfine"/>).</summary>
public class CursorConfineTests
{
    [Fact]
    public void Confined_OnlyWhileAMatchRunsUnpausedFocusedAndAllowed()
    {
        Assert.True(CursorConfine.Wanted(matchRunning: true, paused: false, focused: true, allowed: true));
    }

    [Theory]
    [InlineData(false, false, true, true)] // menu screens: no match
    [InlineData(true, true, true, true)] // pause menu, banner, stats, settings over the pause menu
    [InlineData(true, false, false, true)] // Alt-Tab: the window lost focus
    [InlineData(true, false, true, false)] // the setting off, --no-confine, headless
    public void EachConditionFalse_FreesTheCursor(bool matchRunning, bool paused, bool focused, bool allowed)
    {
        Assert.False(CursorConfine.Wanted(matchRunning, paused, focused, allowed));
    }

    [Fact]
    public void EveryCombination_IsConfinedExactlyOnce()
    {
        int confined = 0;
        for (int bits = 0; bits < 16; bits++)
            if (CursorConfine.Wanted((bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0, (bits & 8) != 0)) confined++;
        Assert.Equal(1, confined);
    }
}
