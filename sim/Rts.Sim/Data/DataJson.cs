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
    public RepairJson? Repair { get; set; }
    public double? BuildingSight { get; set; }
}

internal sealed class RepairJson
{
    public double? RateFactor { get; set; }
    public double? CostFactor { get; set; }
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
    public List<string?>? Requires { get; set; }
    public List<string?>? Tags { get; set; }
    public List<string?>? Abilities { get; set; }
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
    public string? Targets { get; set; }
    public Dictionary<string, double>? BonusVs { get; set; }
}

internal sealed class CostJson
{
    public int? Gold { get; set; }
    public int? Wood { get; set; }
}

internal sealed class ResourceFileJson
{
    public List<ResourceJson?>? Resources { get; set; }
}

internal sealed class ResourceJson
{
    public string? Id { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Resource { get; set; }
    public FootprintJson? Footprint { get; set; }
}

internal sealed class ProjectileFileJson
{
    public List<ProjectileJson?>? Projectiles { get; set; }
}

internal sealed class ProjectileJson
{
    public string? Id { get; set; }
    public string? Kind { get; set; }
    public double? Speed { get; set; }
    public double? HitTolerance { get; set; }
    public double? LeadSpeed { get; set; }
}

internal sealed class FootprintJson
{
    public int? Width { get; set; }
    public int? Height { get; set; }
}

internal sealed class BuildingFileJson
{
    public List<BuildingJson?>? Buildings { get; set; }
}

internal sealed class BuildingJson
{
    public string? Id { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Slot { get; set; }
    public FootprintJson? Footprint { get; set; }
    public int? Hp { get; set; }
    public int? Armor { get; set; }
    public CostJson? Cost { get; set; }
    public double? BuildTime { get; set; }
    public double? PopProvided { get; set; }
    public bool? DropOff { get; set; }
    public List<string?>? Requires { get; set; }
    public double? Sight { get; set; }
    public AttackJson? Attack { get; set; }
    public double? Detector { get; set; }
}

internal sealed class TechFileJson
{
    public List<TechJson?>? Techs { get; set; }
}

internal sealed class TechJson
{
    public string? Id { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? ResearchedAt { get; set; }
    public CostJson? Cost { get; set; }
    public double? ResearchTime { get; set; }
    public List<string?>? Requires { get; set; }
    public RequiresAnyOfJson? RequiresAnyOf { get; set; }
    public List<TechEffectJson?>? Effects { get; set; }
}

internal sealed class RequiresAnyOfJson
{
    public int? Count { get; set; }
    public List<string?>? Of { get; set; }
}

internal sealed class TechEffectJson
{
    public string? Stat { get; set; }
    public double? Amount { get; set; }
    public AppliesToJson? AppliesTo { get; set; }
}

internal sealed class AppliesToJson
{
    public string? AttackType { get; set; }
    public List<string?>? Tags { get; set; }
    public List<string?>? Units { get; set; }
    public bool? Siege { get; set; }
}

internal sealed class StatusFileJson
{
    public List<StatusJson?>? Statuses { get; set; }
}

internal sealed class StatusJson
{
    public string? Id { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Kind { get; set; }
    public string? DamageType { get; set; }
}

internal sealed class AbilityFileJson
{
    public List<AbilityJson?>? Abilities { get; set; }
}

internal sealed class AbilityJson
{
    public string? Id { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Kind { get; set; }
    public double? Range { get; set; }
    public double? Radius { get; set; }
    public double? CastTime { get; set; }
    public double? Cooldown { get; set; }
    public double? Duration { get; set; }
    public string? Affects { get; set; }
    public bool? Autocast { get; set; }
    public List<AbilityEffectJson?>? Effects { get; set; }
}

internal sealed class AbilityEffectJson
{
    public string? Kind { get; set; }
    public string? Type { get; set; }
    public int? Amount { get; set; }
    public string? Status { get; set; }
    public double? Magnitude { get; set; }
    public double? Duration { get; set; }
    public bool? Buildings { get; set; }
    public double? FriendlyFire { get; set; }
}
