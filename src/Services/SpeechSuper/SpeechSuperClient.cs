using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Services;

public record SpeechSuperAssessmentResult(
    bool Success,
    string? ErrorMessage,
    int? ErrId,
    string? Transcription,
    decimal? OverallScore,
    decimal? PronunciationScore,
    decimal? FluencyScore,
    decimal? GrammarScore,
    decimal? VocabularyScore,
    decimal? RelevanceScore,
    IReadOnlyList<SpeechSuperSentence>? Sentences = null,
    string? RawResponseJson = null
);

public record SpeechSuperSentence(
    string Sentence,
    decimal? Pronunciation,
    int? Start,
    int? End
);

public interface ISpeechSuperClient
{
    Task<SpeechSuperAssessmentResult> AssessAsync(
        Stream audioStream,
        string audioType,
        string? questionPrompt = null,
        string? refText = null,
        string? userId = null,
        CancellationToken cancellationToken = default);
}

public sealed class SpeechSuperClient : ISpeechSuperClient
{
    private readonly HttpClient _httpClient;
    private readonly SpeechSuperOptions _options;
    private readonly ILogger<SpeechSuperClient>? _logger;

    public SpeechSuperClient(HttpClient httpClient, SpeechSuperOptions options, ILogger<SpeechSuperClient>? logger = null)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public static string ComputeSha1(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA1.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task<SpeechSuperAssessmentResult> AssessAsync(
        Stream audioStream,
        string audioType,
        string? questionPrompt = null,
        string? refText = null,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.AppKey) || string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            return new SpeechSuperAssessmentResult(
                Success: false,
                ErrorMessage: "SpeechSuper AppKey or SecretKey is not configured.",
                ErrId: 41030,
                Transcription: null,
                OverallScore: null,
                PronunciationScore: null,
                FluencyScore: null,
                GrammarScore: null,
                VocabularyScore: null,
                RelevanceScore: null);
        }

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var uid = string.IsNullOrWhiteSpace(userId) ? "learner" : userId;
        var connectSig = ComputeSha1($"{_options.AppKey}{timestamp}{_options.SecretKey}");
        var startSig = ComputeSha1($"{_options.AppKey}{timestamp}{uid}{_options.SecretKey}");
        var tokenId = Guid.NewGuid().ToString("N");

        var textPayload = new
        {
            connect = new
            {
                cmd = "connect",
                param = new
                {
                    sdk = new
                    {
                        version = 16777472,
                        source = 9,
                        protocol = 2
                    },
                    app = new
                    {
                        applicationId = _options.AppKey,
                        timestamp = timestamp,
                        sig = connectSig
                    }
                }
            },
            start = new
            {
                cmd = "start",
                param = new
                {
                    app = new
                    {
                        userId = uid,
                        applicationId = _options.AppKey,
                        timestamp = timestamp,
                        sig = startSig
                    },
                    audio = new
                    {
                        audioType = string.IsNullOrWhiteSpace(audioType) ? "wav" : audioType.TrimStart('.').ToLowerInvariant(),
                        channel = 1,
                        sampleBytes = 2,
                        sampleRate = 16000
                    },
                    request = new Dictionary<string, object?>
                    {
                        ["tokenId"] = tokenId,
                        ["coreType"] = _options.CoreType,
                        ["question_prompt"] = questionPrompt ?? "",
                        ["test_type"] = "ielts",
                        ["task_type"] = "ielts_part1",
                        ["refText"] = refText ?? questionPrompt ?? ""
                    }
                }
            }
        };

        var textJson = JsonSerializer.Serialize(textPayload);

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(textJson, Encoding.UTF8, "application/json"), "text");

        using var memoryStream = new MemoryStream();
        await audioStream.CopyToAsync(memoryStream, cancellationToken);
        var audioBytes = memoryStream.ToArray();

        var audioContent = new ByteArrayContent(audioBytes);
        audioContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        var sanitizedType = string.IsNullOrWhiteSpace(audioType) ? "wav" : audioType.TrimStart('.').ToLowerInvariant();
        content.Add(audioContent, "audio", $"audio.{sanitizedType}");

        var requestUrl = $"{_options.BaseUrl.TrimEnd('/')}/{_options.CoreType}";
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUrl)
        {
            Content = content
        };
        requestMessage.Headers.Add("Request-Index", "0");

        try
        {
            var response = await _httpClient.SendAsync(requestMessage, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (root.TryGetProperty("errId", out var errIdProp) && errIdProp.GetInt32() != 0)
            {
                var errId = errIdProp.GetInt32();
                var errMsg = root.TryGetProperty("error", out var errProp) ? errProp.GetString() : "SpeechSuper returned an error.";
                return new SpeechSuperAssessmentResult(
                    Success: false,
                    ErrorMessage: errMsg,
                    ErrId: errId,
                    Transcription: null,
                    OverallScore: null,
                    PronunciationScore: null,
                    FluencyScore: null,
                    GrammarScore: null,
                    VocabularyScore: null,
                    RelevanceScore: null,
                    RawResponseJson: responseBody);
            }

            if (root.TryGetProperty("result", out var resultElement))
            {
                var transcription = resultElement.TryGetProperty("transcription", out var tProp) ? tProp.GetString() : null;
                var overall = resultElement.TryGetProperty("overall", out var oProp) ? (decimal?)oProp.GetDecimal() : null;
                var pronunciation = resultElement.TryGetProperty("pronunciation", out var pProp) ? (decimal?)pProp.GetDecimal() : null;
                var fluency = resultElement.TryGetProperty("fluency_coherence", out var fProp) ? (decimal?)fProp.GetDecimal() : null;
                var grammar = resultElement.TryGetProperty("grammar", out var gProp) ? (decimal?)gProp.GetDecimal() : null;
                var vocab = resultElement.TryGetProperty("lexical_resource", out var vProp) ? (decimal?)vProp.GetDecimal() : null;
                var relevance = resultElement.TryGetProperty("relevance", out var rProp) ? (decimal?)rProp.GetDecimal() : null;

                var sentences = new List<SpeechSuperSentence>();
                if (resultElement.TryGetProperty("sentences", out var sList) && sList.ValueKind == JsonValueKind.Array)
                {
                    foreach (var s in sList.EnumerateArray())
                    {
                        var text = s.TryGetProperty("sentence", out var stProp) ? stProp.GetString() ?? "" : "";
                        var p = s.TryGetProperty("pronunciation", out var spProp) ? (decimal?)spProp.GetDecimal() : null;
                        var start = s.TryGetProperty("start", out var startProp) ? (int?)startProp.GetInt32() : null;
                        var end = s.TryGetProperty("end", out var endProp) ? (int?)endProp.GetInt32() : null;
                        sentences.Add(new SpeechSuperSentence(text, p, start, end));
                    }
                }

                return new SpeechSuperAssessmentResult(
                    Success: true,
                    ErrorMessage: null,
                    ErrId: null,
                    Transcription: transcription,
                    OverallScore: overall,
                    PronunciationScore: pronunciation,
                    FluencyScore: fluency,
                    GrammarScore: grammar,
                    VocabularyScore: vocab,
                    RelevanceScore: relevance,
                    Sentences: sentences,
                    RawResponseJson: responseBody);
            }

            return new SpeechSuperAssessmentResult(
                Success: false,
                ErrorMessage: "Invalid response format: 'result' element missing.",
                ErrId: null,
                Transcription: null,
                OverallScore: null,
                PronunciationScore: null,
                FluencyScore: null,
                GrammarScore: null,
                VocabularyScore: null,
                RelevanceScore: null,
                RawResponseJson: responseBody);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to call SpeechSuper API at {Url}", requestUrl);
            return new SpeechSuperAssessmentResult(
                Success: false,
                ErrorMessage: ex.Message,
                ErrId: null,
                Transcription: null,
                OverallScore: null,
                PronunciationScore: null,
                FluencyScore: null,
                GrammarScore: null,
                VocabularyScore: null,
                RelevanceScore: null);
        }
    }
}
