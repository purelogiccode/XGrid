using System.Text.Json.Serialization;

namespace XGrid.Diagnostics;

/// <summary>The JSON body accepted by the centralized bug-report endpoint.</summary>
public sealed record BugReportRequest
{
    /// <summary>Gets the report message, including environment and error sections.</summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    /// <summary>Gets the application name.</summary>
    [JsonPropertyName("applicationName")]
    public string? ApplicationName { get; init; }

    /// <summary>Gets the application version.</summary>
    [JsonPropertyName("version")]
    public string? Version { get; init; }

    /// <summary>Gets a short environment description.</summary>
    [JsonPropertyName("environment")]
    public string? Environment { get; init; }

    /// <summary>Gets the exception text, when there is one.</summary>
    [JsonPropertyName("stackTrace")]
    public string? StackTrace { get; init; }
}
