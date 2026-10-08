using System.Collections.Immutable;

namespace Rts.Sim.Data;

/// <summary>A unit's attack (docs/02 "Combat / Stats"), with times in ticks.</summary>
public sealed class AttackDef
{
    internal AttackDef()
    {
    }

    /// <summary>Base damage per hit.</summary>
    public int Value { get; init; }
    /// <summary>Damage type id into <see cref="DamageTable"/>.</summary>
    public int DamageType { get; init; }
    /// <summary>Ticks between attacks.</summary>
    public int CooldownTicks { get; init; }
    /// <summary>Ticks from attack start to the damage point.</summary>
    public int WindupTicks { get; init; }
    /// <summary>Range in meters, edge to edge (melee is 0.5).</summary>
    public float Range { get; init; }
    /// <summary>Minimum range in meters; 0 when none.</summary>
    public float MinRange { get; init; }
    /// <summary>Splash radius in meters; 0 when none.</summary>
    public float Splash { get; init; }
    /// <summary>True when splash also hits allied and own units (docs/02 "Splash and friendly fire").</summary>
    public bool FriendlyFire { get; init; }
    /// <summary>Projectile id as written (<c>attack.projectile</c>); null for an attack that hits at the wind-up point (melee, the ram).</summary>
    public string? Projectile { get; init; }
    /// <summary><see cref="Projectile"/> resolved to a <see cref="GameData.Projectiles"/> id at load (M4-2b); -1 for none (and in hand-built data).</summary>
    public int ProjectileTypeId { get; init; } = -1;
    /// <summary>What the attack may target (<c>attack.targets</c>, M4-2a): units, buildings or both (the default).</summary>
    public AttackTargets Targets { get; init; }
    /// <summary>Damage multiplier per armor class id (1 where the data gives none).</summary>
    public required ImmutableArray<float> BonusVs { get; init; }
}
