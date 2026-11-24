using System;
using System.Text.Json;
using Xunit;

namespace XGrid.Diagnostics.Tests;

public sealed class BugReportTextTests
{
    [Fact]
    public void CreateRequest_ContainsEveryRequiredSection()
    {
        var options = new DiagnosticsOptions
        {
            ApplicationName = "XGrid.Tests",
            Version = "1.2.3",
        };

        BugReportRequest request = BugReportText.CreateRequest(
            "The renderer failed",
            new InvalidOperationException("boom"),
            options);

        Assert.Contains("=== Environment Details ===", request.Message, StringComparison.Ordinal);
        Assert.Contains("Date:", request.Message, StringComparison.Ordinal);
        Assert.Contains("Application Name: XGrid.Tests", request.Message, StringComparison.Ordinal);
        Assert.Contains("Application Version: 1.2.3", request.Message, StringComparison.Ordinal);
        Assert.Contains("OS Version:", request.Message, StringComparison.Ordinal);
        Assert.Contains("Architecture:", request.Message, StringComparison.Ordinal);
        Assert.Contains("Bitness:", request.Message, StringComparison.Ordinal);
        Assert.Contains("Windows, Linux or MacOsX Version:", request.Message, StringComparison.Ordinal);
        Assert.Contains("Processor Count:", request.Message, StringComparison.Ordinal);
        Assert.Contains("Base Directory:", request.Message, StringComparison.Ordinal);
        Assert.Contains("Temp Path:", request.Message, StringComparison.Ordinal);
        Assert.Contains("=== Error Details ===", request.Message, StringComparison.Ordinal);
        Assert.Contains("Error message: The renderer failed", request.Message, StringComparison.Ordinal);
        Assert.Contains("=== Exception Details ===", request.Message, StringComparison.Ordinal);
        Assert.Contains("Type: System.InvalidOperationException", request.Message, StringComparison.Ordinal);
        Assert.Contains("Message: boom", request.Message, StringComparison.Ordinal);
        Assert.Contains("Source:", request.Message, StringComparison.Ordinal);
        Assert.Contains("StackTrace:", request.Message, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", request.StackTrace, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateRequest_WithoutExceptionStillWritesTheExceptionSection()
    {
        var options = new DiagnosticsOptions { Version = "1.0.0" };

        BugReportRequest request = BugReportText.CreateRequest("warning", null, options);

        Assert.Contains("=== Exception Details ===", request.Message, StringComparison.Ordinal);
        Assert.Contains("Type: (none)", request.Message, StringComparison.Ordinal);
        Assert.Null(request.StackTrace);
    }

    [Fact]
    public void CreateRequest_TruncatesToServiceLimits()
    {
        var options = new DiagnosticsOptions { Version = "1.0.0" };
        string message = new('x', BugReportText.MaxMessageLength * 2);

        BugReportRequest request = BugReportText.CreateRequest(message, null, options);

        Assert.InRange(request.Message.Length, 1, BugReportText.MaxMessageLength);
        Assert.Contains("...", request.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateRequest_LongErrorStillKeepsTheExceptionSection()
    {
        var options = new DiagnosticsOptions { Version = "1.0.0" };
        string message = new('x', BugReportText.MaxMessageLength * 4);

        BugReportRequest request = BugReportText.CreateRequest(
            message,
            new InvalidOperationException("still here"),
            options);

        Assert.Contains("=== Environment Details ===", request.Message, StringComparison.Ordinal);
        Assert.Contains("=== Error Details ===", request.Message, StringComparison.Ordinal);
        Assert.Contains("=== Exception Details ===", request.Message, StringComparison.Ordinal);
        Assert.Contains("still here", request.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Request_SerializesWithTheServiceFieldNames()
    {
        var request = new BugReportRequest
        {
            Message = "message",
            ApplicationName = "XGrid",
            Version = "1.0.0",
            Environment = "Windows",
            StackTrace = "stack",
        };

        string json = JsonSerializer.Serialize(request);

        Assert.Contains("\"message\":\"message\"", json, StringComparison.Ordinal);
        Assert.Contains("\"applicationName\":\"XGrid\"", json, StringComparison.Ordinal);
        Assert.Contains("\"version\":\"1.0.0\"", json, StringComparison.Ordinal);
        Assert.Contains("\"environment\":\"Windows\"", json, StringComparison.Ordinal);
        Assert.Contains("\"stackTrace\":\"stack\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_IncludesTypeMessageAndStack()
    {
        string text = BugReportText.Describe(new InvalidOperationException("bad"));

        Assert.Contains("System.InvalidOperationException", text, StringComparison.Ordinal);
        Assert.Contains("bad", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => BugReportText.Describe(null!));
    }

    [Fact]
    public void CreateRequest_RejectsMissingOptions()
    {
        Assert.Throws<ArgumentNullException>(
            () => BugReportText.CreateRequest("message", null, null!));
    }
}
