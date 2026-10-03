using System.Collections.Generic;

namespace Rts.Sim.Data;

// Raw JSON shapes, one class per object kind in game/data/. Every member is nullable so the loader,
// not the parser, reports a missing field (with its path) and keeps going. Only DataLoader sees these.

internal sealed class DamageTableJson
{
    public List<NamedJson?>? ArmorClasses { get; set; }
    public List<DamageTypeJson?>? DamageTypes { get; set; }
}

internal sealed class NamedJson
{
    public string? Id { get; set; }
    public string? DisplayName { get; set; }
}

internal sealed class DamageTypeJson
{
    public string? Id { get; set; }
    public string? DisplayName { get; set; }
    public bool? IgnoresArmor { get; set; }
    public Dictionary<string, double>? Multipliers { get; set; }
}

internal sealed class RulesJson
{
    public int? StartingGold { get; set; }
    public int? StartingWood { get; set; }
    public int? StartingWorkers { get; set; }
    public int? PopCap { get; set; }
    public int? WorkerCarry { get; set; }
    public RateJson? GatherRate { get; set; }
    public MinesJson? StartMines { get; set; }
    public MinesJson? ExpansionMines { get; set; }
    public int? TreeWood { get; set; }
    public double? NodeSearchRadius { get; set; }
}

internal sealed class RateJson
{
    public double? Gold { get; set; }
    public double? Wood { get; set; }
}

internal sealed class MinesJson
{
    public int? Count { get; set; }
    public int? Gold { get; set; }
}

internal sealed class FactionJson
{
    public string? Id { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public TextJson? Bonus { get; set; }
    public ResourceNamesJson? Resources { get; set; }
    public PaletteJson? Palette { get; set; }
}

internal sealed class TextJson
{
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
}

internal sealed class ResourceNamesJson
{
    public TextJson? Gold { get; set; }
    public TextJson? Wood { get; set; }
}

internal sealed class PaletteJson
{
    public string? Primary { get; set; }
    public string? Secondary { get; set; }
    public string? Accent { get; set; }
}

internal sealed class UnitFileJson
{
    public List<UnitJson?>? Units { get; set; }
}

internal sealed class UnitJson
{
    public string? Id { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Slot { get; set; }
    public string? Model { get; set; }
    public int? Hp { get; set; }
    public int? Armor { get; set; }
    public string? ArmorClass { get; set; }
    public AttackJson? Attack { get; set; }
    public double? Speed { get; set; }
    public double? Sight { get; set; }
    public double? Radius { get; set; }
    public CostJson? Cost { get; set; }
    public double? Pop { get; set; }
    public double? TrainTime { get; set; }
    public string? TrainedAt { get; set; }
    public List<string>? Requires { get; set; }
    public List<string>? Tags { get; set; }
}

internal sealed class AttackJson
{
    public int? Value { get; set; }
    public string? Type { get; set; }
    public double? Cooldown { get; set; }
    public double? Range { get; set; }
    public double? MinRange { get; set; }
    public double? Windup { get; set; }
    public double? Splash { get; set; }
    public bool? FriendlyFire { get; set; }
    public string? Projectile { get; set; }
    public Dictionary<string, double>? BonusVs { get; set; }
}

internal sealed class CostJson
{
    public int? Gold { get; set; }
    public int? Wood { get; set; }
}
