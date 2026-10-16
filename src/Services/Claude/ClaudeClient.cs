using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Services;

public interface IClaudeClient
{
    Task<string> CreateMessageAsync(
        string userPrompt,
        string? systemPrompt = null,
        int maxTokens = 1500,
        CancellationToken cancellationToken = default);
}

public sealed class ClaudeClient : IClaudeClient
{
    private readonly HttpClient _httpClient;
    private readonly ClaudeOptions _options;
    private readonly ILogger<ClaudeClient>? _logger;

    public ClaudeClient(HttpClient httpClient, ClaudeOptions options, ILogger<ClaudeClient>? logger = null)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<string> CreateMessageAsync(
        string userPrompt,
        string? systemPrompt = null,
        int maxTokens = 4000,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = (_options.BaseUrl?.TrimEnd('/') ?? "https://hhtechapi.net");
        var endpoint = $"{baseUrl}/v1/messages";

        var payload = new
        {
            model = string.IsNullOrWhiteSpace(_options.Model) ? "claude-sonnet-3.7" : _options.Model,
            max_tokens = maxTokens,
            system = systemPrompt,
            messages = new[]
            {
                new { role = "user", content = userPrompt }
            }
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add("x-api-key", _options.ApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger?.LogInformation("Sending request to Claude endpoint {Endpoint} with model {Model}", endpoint, payload.model);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger?.LogError("Claude API error {StatusCode}: {ResponseBody}", response.StatusCode, responseBody);
            throw new InvalidOperationException($"Claude API returned {response.StatusCode}: {responseBody}");
        }

        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        // Anthropic format: content array of blocks
        if (root.TryGetProperty("content", out var contentArray) && contentArray.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var block in contentArray.EnumerateArray())
            {
                if (block.TryGetProperty("text", out var textProp))
                {
                    sb.Append(textProp.GetString());
                }
            }
            if (sb.Length > 0)
            {
                return sb.ToString();
            }

            _logger?.LogWarning("Claude returned content array without text blocks. Stop reason: {StopReason}",
                root.TryGetProperty("stop_reason", out var sr) ? sr.GetString() : "unknown");
        }

        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
        {
            var msg = choices[0].GetProperty("message");
            if (msg.TryGetProperty("content", out var content))
            {
                return content.GetString() ?? string.Empty;
            }
        }

        return responseBody;
    }
}
