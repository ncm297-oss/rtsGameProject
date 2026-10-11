using System;
using Rts.Sim.Entities;

namespace Rts.Sim.Ai;

/// <summary>
/// One AI's bookkeeping (M5-2, docs/03 "Implementation (M5-2)"): build-order step progress (when due, how many queued,
/// done, the command waiting to be judged), the house it is placing, retry marks (anchors a Build was refused at), and per
/// unit and building slot when its last AI command applies (so a think doesn't order it twice before the first applied) and
/// which resource a worker was sent to. What the AI believes about the enemy is M5-3's. Not sim state: not hashed, never
/// read by a sim rule; allocated once, sized from the match.
/// </summary>
internal sealed class Blackboard
{
    /// <summary>Retry marks kept: the oldest is forgotten past this many.</summary>
    internal const int RetryMarks = 16;

    private readonly int[] _badType = new int[RetryMarks];
    private readonly int[] _badCell = new int[RetryMarks];
    private int _badNext;

    /// <summary>Sizes the bookkeeping for <paramref name="steps"/> build-order steps and the match's unit and building slots.</summary>
    internal Blackboard(int steps, int unitSlots, int buildingSlots, int resourceSlots)
    {
        StepDueTick = new int[steps];
        Array.Fill(StepDueTick, -1);
        StepQueued = new int[steps];
        StepDone = new bool[steps];
        StepPending = new PendingOrder[steps];
        UnitOrderedUntil = new int[unitSlots];
        Array.Fill(UnitOrderedUntil, -1);
        UnitGeneration = new int[unitSlots];
        GatherKind = new byte[unitSlots];
        GatherNode = new EntityHandle[unitSlots];
        NodeDropped = new int[resourceSlots];
        BuildingBusyUntil = new int[buildingSlots];
        Array.Fill(BuildingBusyUntil, -1);
        Array.Fill(_badCell, -1);
    }

    /// <summary>Per step, the tick it became due (its trigger first reached); -1 before.</summary>
    internal int[] StepDueTick { get; }

    /// <summary>Per step, the commands it had accepted (a train step's count so far).</summary>
    internal int[] StepQueued { get; }

    /// <summary>Per step, whether it is complete.</summary>
    internal bool[] StepDone { get; }

    /// <summary>Per step, its command waiting to be judged.</summary>
    internal PendingOrder[] StepPending { get; }

    /// <summary>The economy manager's house Build waiting to be judged.</summary>
    internal PendingOrder HousePending;

    /// <summary>The production manager's last Train (not judged: its building is busy until it applies, then the queue shows it).</summary>
    internal PendingOrder ProductionPending;

    /// <summary>Per unit slot, the tick the AI's last command to it applies (-1: none); with <see cref="UnitGeneration"/>, the unit it was for.</summary>
    internal int[] UnitOrderedUntil { get; }

    /// <summary>Per unit slot, the generation of the unit <see cref="UnitOrderedUntil"/> and <see cref="GatherKind"/> are about.</summary>
    internal int[] UnitGeneration { get; }

    /// <summary>Per unit slot, the resource the AI last sent the worker to: 0 none, 1 gold, 2 wood.</summary>
    internal byte[] GatherKind { get; }

    /// <summary>Per unit slot, the node the AI last sent the worker to gather at (default: none).</summary>
    internal EntityHandle[] GatherNode { get; }

    /// <summary>
    /// Per resource slot, the generation of the node there a Gather was dropped at (0: none; BUG-0570): the worker was still
    /// Idle after the order applied and the player doesn't see the node, so it is gone; the economy manager sends no more
    /// workers there. What a human sees too: the worker didn't move.
    /// </summary>
    internal int[] NodeDropped { get; }

    /// <summary>Per building slot, the tick the AI's last command to it applies (-1: none): until then its queue doesn't show it.</summary>
    internal int[] BuildingBusyUntil { get; }

    /// <summary>Remembers that a Build of <paramref name="type"/> at <paramref name="cell"/> was refused, so the site search skips it.</summary>
    internal void MarkBad(int type, int cell)
    {
        _badType[_badNext] = type;
        _badCell[_badNext] = cell;
        _badNext = (_badNext + 1) % RetryMarks;
    }

    /// <summary>Whether a Build of <paramref name="type"/> at <paramref name="cell"/> carries a retry mark.</summary>
    internal bool IsBad(int type, int cell)
    {
        for (int k = 0; k < RetryMarks; k++)
            if (_badCell[k] == cell && _badType[k] == type) return true;
        return false;
    }
}
