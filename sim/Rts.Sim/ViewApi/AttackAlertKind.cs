namespace Rts.Sim.ViewApi;

/// <summary>What an <see cref="AttackAlerts"/> alert is about: picks the HUD's text.</summary>
public enum AttackAlertKind
{
    /// <summary>One of the player's buildings was hit ("Your base is under attack").</summary>
    Base,

    /// <summary>One of the player's units was hit ("Your units are under attack").</summary>
    Units,
}
