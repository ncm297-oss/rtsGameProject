namespace Rts.Sim.Data;

/// <summary>When an <c>ai.json</c> build-order step becomes due (M5-1), in <see cref="DataLimits.AiStepTriggerIds"/> order.</summary>
public enum AiStepTrigger
{
    /// <summary><c>at.time</c>: the match time, in ticks.</summary>
    Time,
    /// <summary><c>at.workers</c>: the AI has at least this many workers.</summary>
    Workers,
    /// <summary><c>at.pop</c>: the AI's population, in half-pop, is at least this.</summary>
    Pop,
}
