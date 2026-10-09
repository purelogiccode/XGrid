using System.Text.Json.Serialization;

namespace XGrid.Cpu.Testing;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(CpuTest))]
internal sealed partial class CpuTestJsonContext : JsonSerializerContext;
