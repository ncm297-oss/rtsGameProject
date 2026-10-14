namespace Rts.Game;

/// <summary>How the match ended for the local player (M6-V4), as the view reads the sim's defeat rule; presentation state.</summary>
public enum MatchOutcome
{
    /// <summary>No result for the local player yet.</summary>
    None,
    /// <summary>The match is over (<c>Simulation.MatchOver</c>) and the local player stands.</summary>
    Victory,
    /// <summary>The local player is defeated (<c>World.IsDefeated</c>), whether or not the match is over.</summary>
    Defeat,
    /// <summary>The match is over and nobody stands (the last players fell on one tick).</summary>
    Draw,
}
