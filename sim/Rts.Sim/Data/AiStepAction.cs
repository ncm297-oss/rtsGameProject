namespace Rts.Sim.Data;

/// <summary>What an <c>ai.json</c> build-order step does (M5-1, its <c>do</c>), in <see cref="DataLimits.AiStepActionIds"/> order.</summary>
public enum AiStepAction
{
    /// <summary>Place a building of the faction (<see cref="AiStep.Target"/> is a building type id).</summary>
    Build,
    /// <summary>Train a unit of the faction (<see cref="AiStep.Target"/> is a unit type id).</summary>
    Train,
    /// <summary>Research a common or own-faction tech (<see cref="AiStep.Target"/> is a tech id).</summary>
    Research,
    /// <summary>Take an expansion (no target).</summary>
    Expand,
}
