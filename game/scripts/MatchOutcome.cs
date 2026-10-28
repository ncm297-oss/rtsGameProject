namespace Rts.Game;

/// <summary>How the match ended for the local player (M6-V4), as the view reads the sim's defeat rule; presentation state.</summary>
public enum MatchOutcome
{
    /// <summary>No result for the local player yet.</summary>
    None,
    /// <summary>The match is over (<c>Simulation.MatchOver</c>) and the local player's side stands (M6-V16: the local player or an ally of it).</summary>
    Victory,
    /// <summary>The local player's side is out (it is defeated, <c>World.IsDefeated</c>, and so is every ally of it; M6-V16), whether or not the match is over.</summary>
    Defeat,
    /// <summary>The match is over and nobody stands (the last players fell on one tick).</summary>
    Draw,
}
