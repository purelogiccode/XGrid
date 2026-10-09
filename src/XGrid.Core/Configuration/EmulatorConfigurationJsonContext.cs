using System.Text.Json.Serialization;

namespace XGrid.Core.Configuration;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(EmulatorConfiguration))]
internal sealed partial class EmulatorConfigurationJsonContext : JsonSerializerContext
{
}
