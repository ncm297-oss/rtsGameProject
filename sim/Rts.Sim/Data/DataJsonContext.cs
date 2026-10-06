using System.Text.Json.Serialization;

namespace Rts.Sim.Data;

/// <summary>Compile-time System.Text.Json metadata for the data files (no reflection at load).</summary>
/// <remarks>Unknown members are errors so a typo like <c>"cooldwon"</c> can't silently fall back to a default.</remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(DamageTableJson))]
[JsonSerializable(typeof(RulesJson))]
[JsonSerializable(typeof(FactionJson))]
[JsonSerializable(typeof(UnitFileJson))]
[JsonSerializable(typeof(ResourceFileJson))]
internal sealed partial class DataJsonContext : JsonSerializerContext
{
}
