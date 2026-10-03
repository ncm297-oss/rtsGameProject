namespace Rts.Sim.Entities;

/// <summary>Reference to a store slot; stale once the slot is freed, because the slot's generation moves on.</summary>
public readonly record struct EntityHandle(int Index, int Generation);
