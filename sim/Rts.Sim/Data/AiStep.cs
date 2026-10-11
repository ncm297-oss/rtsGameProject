namespace Rts.Sim.Data;

/// <summary>One step of a faction's <c>ai.json</c> <c>buildOrder</c> (M5-1), resolved at load.</summary>
public readonly record struct AiStep
{
    /// <summary>What makes the step due.</summary>
    public AiStepTrigger Trigger { get; init; }

    /// <summary>The trigger's value: ticks for <see cref="AiStepTrigger.Time"/>, a worker count, or half-pop for <see cref="AiStepTrigger.Pop"/>.</summary>
    public int At { get; init; }

    /// <summary>What the step does.</summary>
    public AiStepAction Action { get; init; }

    /// <summary>The building, unit or tech type id the action names; -1 for <see cref="AiStepAction.Expand"/>.</summary>
    public int Target { get; init; }

    /// <summary>The string id of <see cref="Target"/> as written; empty for <see cref="AiStepAction.Expand"/>.</summary>
    public string TargetKey { get; init; }

    /// <summary>How many the step asks for (1 when the file gives none; always 1 for a research).</summary>
    public int Count { get; init; }
}
