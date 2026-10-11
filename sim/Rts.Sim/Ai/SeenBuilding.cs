using Rts.Sim.Entities;

namespace Rts.Sim.Ai;

/// <summary>
/// A live building as a <see cref="PlayerView"/> answers it (M5-1): one of the player's own, or an enemy's the player sees
/// now. A remembered enemy building the player does not see now is a ghost (<see cref="PlayerView.NextGhost"/>) instead.
/// </summary>
/// <param name="Handle">The building's slot and generation.</param>
/// <param name="Owner">The owning player.</param>
/// <param name="TypeId">Its building type (<c>GameData.Buildings</c>).</param>
/// <param name="Cell">Its footprint's anchor cell (lowest x, y; <c>y * width + x</c>).</param>
/// <param name="Hp">Its hit points now.</param>
/// <param name="UnderConstruction">True for a construction site.</param>
public readonly record struct SeenBuilding(EntityHandle Handle, int Owner, int TypeId, int Cell, int Hp, bool UnderConstruction);
