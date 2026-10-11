namespace Rts.Sim;

/// <summary>Who plays a seat (M5-2, <see cref="PlayerSeat"/>): a person through the input, or the sim's AI.</summary>
public enum SeatController
{
    /// <summary>Commands come from outside the sim (the player's input, a test, a replay).</summary>
    Human,
    /// <summary>The sim runs an <c>AiPlayer</c> for the seat, which sends its own commands through the command door.</summary>
    Ai,
}
