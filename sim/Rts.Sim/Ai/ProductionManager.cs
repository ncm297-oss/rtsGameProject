using Rts.Sim.Data;

namespace Rts.Sim.Ai;

/// <summary>
/// The AI's army production (M5-2, docs/03 "Implementation (M5-2)"): when the build order sent nothing this think, it trains
/// the current age's most under-weighted composition unit (fewest own live + queued per unit of weight; composition order on
/// ties) among those a finished own building with an empty queue may train now, if what is left this think pays for it;
/// else it waits (it doesn't spend on a cheaper unit instead).
/// </summary>
internal static class ProductionManager
{
    /// <summary>One Train at most a think (see the type's summary).</summary>
    internal static void Run(AiPlayer ai)
    {
        if (ai.ExecutorSent) return;
        int age = ai.View.Age;
        var compositions = ai.Def.Compositions;
        if (compositions.Length == 0) return;
        AiComposition mix = compositions[System.Math.Clamp(age, 1, compositions.Length) - 1];
        int pick = -1;
        float pickShare = float.MaxValue;
        for (int k = 0; k < mix.Units.Length; k++)
        {
            int type = mix.Units[k];
            float share = (ai.Live[type] + ai.Queued[type]) / mix.Weights[k];
            if (share >= pickShare || !CanTrainSomewhere(ai, type)) continue;
            pick = type;
            pickShare = share;
        }
        if (pick < 0) return;
        // Not judged: the next think simply looks at the queues again.
        ai.TryTrain(pick, emptyQueueOnly: true, ref ai.Board.ProductionPending);
    }

    /// <summary>Whether a finished own building with an empty queue and no AI command waiting on it may train <paramref name="type"/> now (its cost aside).</summary>
    private static bool CanTrainSomewhere(AiPlayer ai, int type)
    {
        int at = ai.Data.Units[type].TrainedAtTypeId;
        for (int k = 0; k < ai.OwnBuildingCount; k++)
        {
            int slot = ai.OwnBuildingSlots[k];
            if (ai.OwnBuildingSites[k] || ai.OwnBuildingTypes[k] != at || ai.Board.BuildingBusyUntil[slot] >= ai.Now || ai.View.QueueCount(slot) > 0) continue;
            if (ai.View.CanTrain(slot, type, out Economy.TrainError why) || why == Economy.TrainError.CannotAfford) return true;
        }
        return false;
    }
}
