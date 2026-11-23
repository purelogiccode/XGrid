using System.Text.Json.Serialization;

namespace XGrid.Cpu.Tests;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(CpuTest))]
internal sealed partial class CpuTestJsonContext : JsonSerializerContext;
