namespace Rts.Sim.Ai;

/// <summary>What an AI command still waiting to be judged was (M5-2, <see cref="PendingOrder"/>).</summary>
internal enum PendingKind : byte
{
    /// <summary>Nothing pending.</summary>
    None,
    /// <summary>A <c>Build</c> (judged by an own building of its type at its anchor).</summary>
    Build,
    /// <summary>A <c>Train</c> (judged by its building's queue).</summary>
    Train,
    /// <summary>A <c>Research</c> (judged by the tech researched or queued).</summary>
    Research,
}
