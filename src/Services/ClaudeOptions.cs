namespace Services;

/// <summary>
/// Configuration options for the Claude AI provider via HH-Tech proxy gateway.
/// </summary>
public sealed class ClaudeOptions
{
    public const string SectionName = "Claude";

    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://hhtechapi.net";
    public string Model { get; set; } = "claude-sonnet-3.7";
}
