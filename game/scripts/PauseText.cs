namespace Rts.Game;

/// <summary>A pause menu text from <c>ui.json</c> <c>pause</c> (M6-V1); the key is the member's snake_case name (<see cref="UiText.Key{T}"/>).</summary>
public enum PauseText
{
    /// <summary>The heading while the match is paused.</summary>
    Title,
    /// <summary>The button that unpauses.</summary>
    Resume,
    /// <summary>The button that ends the match and goes back to the main menu.</summary>
    QuitToMenu,
}
