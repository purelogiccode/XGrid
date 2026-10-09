using System.Collections.Generic;
using XGrid.Cpu.Testing;
using Xunit;

namespace XGrid.Cpu.Tests;

public sealed class CpuTestRunnerTests
{
    private const string PlanSampleJson = """
                                          {
                                            "name": "ADD_EAX_EBX",
                                            "registers": {
                                              "EAX": "0x00000005",
                                              "EBX": "0x00000007"
                                            },
                                            "code": "01D8",
                                            "expected": {
                                              "EAX": "0x0000000C"
                                            }
                                          }
                                          """;

    [Fact]
    public void Run_PlanSample_Passes()
    {
        CpuTest test = CpuTestLoader.Load(PlanSampleJson);

        CpuTestResult result = CpuTestRunner.Run(test);

        Assert.True(result.Passed, string.Join("; ", result.Failures));
        Assert.Equal(0xCu, result.Actual["EAX"]);
    }

    [Fact]
    public void Load_ParsesNameAndCode()
    {
        CpuTest test = CpuTestLoader.Load(PlanSampleJson);

        Assert.Equal("ADD_EAX_EBX", test.Name);
        Assert.Equal("01D8", test.Code);
        Assert.Equal(2, test.Registers.Count);
    }

    [Fact]
    public void Save_Then_Load_RoundTrips()
    {
        CpuTest original = new()
        {
            Name = "MOV_TEST",
            Code = "B878563412",
            Registers = new Dictionary<string, string>(System.StringComparer.Ordinal) { ["EAX"] = "0x00000000" },
            Expected = new Dictionary<string, string>(System.StringComparer.Ordinal) { ["EAX"] = "0x12345678" },
        };

        string json = CpuTestLoader.Save(original);
        CpuTest loaded = CpuTestLoader.Load(json);

        Assert.Equal(original.Name, loaded.Name);
        Assert.Equal(original.Code, loaded.Code);
        Assert.Equal(original.Expected["EAX"], loaded.Expected["EAX"]);
    }

    [Fact]
    public void Run_WrongExpectation_ReportsFailure()
    {
        CpuTest test = CpuTestLoader.Load("""
                                          {
                                            "name": "FAIL",
                                            "registers": { "EAX": "0x00000005", "EBX": "0x00000007" },
                                            "code": "01D8",
                                            "expected": { "EAX": "0x00000000" }
                                          }
                                          """);

        CpuTestResult result = CpuTestRunner.Run(test);

        Assert.False(result.Passed);
        Assert.NotEmpty(result.Failures);
        Assert.Equal(0xCu, result.Actual["EAX"]);
    }

    [Fact]
    public void ParseBytes_AcceptsWhitespace()
    {
        Assert.Equal(new byte[] { 0x01, 0xD8 }, CpuTestRunner.ParseBytes("01 D8"));
    }

    [Fact]
    public void ParseUInt32_AcceptsHexPrefix()
    {
        Assert.Equal(0x12345678u, CpuTestRunner.ParseUInt32("0x12345678"));
        Assert.Equal(0xCAFEu, CpuTestRunner.ParseUInt32("CAFE"));
    }
}
