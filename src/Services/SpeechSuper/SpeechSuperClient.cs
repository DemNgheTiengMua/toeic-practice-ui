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

    /// <summary>
    /// A numeric provider field, or null when it is absent. Read through <see cref="JsonElement.TryGetDecimal"/>
    /// so an integer-valued field (39) and a fractional one (7.5) both work — <c>ValueKind</c>-blind
    /// access throws on the other shape.
    /// </summary>
    private static decimal? ReadNumber(JsonElement element, string name) =>
        element.TryGetProperty(name, out var prop)
        && prop.ValueKind == JsonValueKind.Number
        && prop.TryGetDecimal(out var value)
            ? value
            : null;

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
                        // #152: the prompt travels as topic context only. It is never the reference.
                        ["question_prompt"] = questionPrompt ?? "",
                        // #152: empty when the question declares no script, so the provider judges free
                        // speech instead of comparing the learner to words nobody was asked to read.
                        // test_type/task_type are not sent: they named IELTS for every certificate.
                        ["refText"] = refText ?? "",
                        ["model"] = "non_native"
                    }
                }
            }
        };

        var textJson = JsonSerializer.Serialize(textPayload);

        if (audioStream.CanSeek) audioStream.Position = 0;
        using var memoryStream = new MemoryStream();
        await audioStream.CopyToAsync(memoryStream, cancellationToken);
        var audioBytes = memoryStream.ToArray();

        var sanitizedType = string.IsNullOrWhiteSpace(audioType) ? "wav" : audioType.TrimStart('.').ToLowerInvariant();
        var boundary = $"----WebKitFormBoundary{Guid.NewGuid():N}";

        using var postStream = new MemoryStream();
        var textHeader = Encoding.UTF8.GetBytes($"--{boundary}\r\nContent-Disposition: form-data; name=\"text\"\r\nContent-Type: application/json\r\n\r\n");
        await postStream.WriteAsync(textHeader, cancellationToken);
        var textData = Encoding.UTF8.GetBytes(textJson);
        await postStream.WriteAsync(textData, cancellationToken);
        await postStream.WriteAsync(Encoding.UTF8.GetBytes("\r\n"), cancellationToken);

        var audioHeader = Encoding.UTF8.GetBytes($"--{boundary}\r\nContent-Disposition: form-data; name=\"audio\"; filename=\"audio.{sanitizedType}\"\r\nContent-Type: application/octet-stream\r\n\r\n");
        await postStream.WriteAsync(audioHeader, cancellationToken);
        await postStream.WriteAsync(audioBytes, cancellationToken);
        await postStream.WriteAsync(Encoding.UTF8.GetBytes("\r\n"), cancellationToken);

        var endBoundary = Encoding.UTF8.GetBytes($"--{boundary}--\r\n");
        await postStream.WriteAsync(endBoundary, cancellationToken);

        var requestUrl = $"{_options.BaseUrl.TrimEnd('/')}/{_options.CoreType}";
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        var byteContent = new ByteArrayContent(postStream.ToArray());
        byteContent.Headers.TryAddWithoutValidation("Content-Type", $"multipart/form-data; boundary={boundary}");
        requestMessage.Content = byteContent;
        requestMessage.Headers.Add("Request-Index", "0");

        _logger?.LogInformation("Calling SpeechSuper {Url} with audio size {Size} bytes ({Type})", requestUrl, audioBytes.Length, sanitizedType);

        try
        {
            var response = await _httpClient.SendAsync(requestMessage, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            _logger?.LogInformation("SpeechSuper HTTP status {StatusCode}, body length {Len}", response.StatusCode, responseBody.Length);

            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogError("SpeechSuper API returned HTTP {StatusCode}: {Body}", response.StatusCode, responseBody);
                try
                {
                    using var errDoc = JsonDocument.Parse(responseBody);
                    var errRoot = errDoc.RootElement;
                    if (errRoot.TryGetProperty("errId", out var pErrId))
                    {
                        var parsedErrId = pErrId.GetInt32();
                        var parsedErrMsg = errRoot.TryGetProperty("error", out var pErr) ? pErr.GetString() : responseBody;
                        return new SpeechSuperAssessmentResult(
                            Success: false,
                            ErrorMessage: parsedErrMsg,
                            ErrId: parsedErrId,
                            Transcription: null,
                            OverallScore: null,
                            PronunciationScore: null,
                            FluencyScore: null,
                            GrammarScore: null,
                            VocabularyScore: null,
                            RelevanceScore: null,
                            RawResponseJson: responseBody);
                    }
                }
                catch { }

                return new SpeechSuperAssessmentResult(
                    Success: false,
                    ErrorMessage: $"SpeechSuper returned HTTP {(int)response.StatusCode}: {responseBody}",
                    ErrId: (int)response.StatusCode,
                    Transcription: null,
                    OverallScore: null,
                    PronunciationScore: null,
                    FluencyScore: null,
                    GrammarScore: null,
                    VocabularyScore: null,
                    RelevanceScore: null);
            }

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
                // #152: the field names here are the ones `speak.eval.pro` actually returns, read off a
                // live 200. `fluency_coherence` and `lexical_resource` are not in the body at all — the
                // real names are `fluency` and `vocabulary` — and a field that never arrives was being
                // treated as "not measured", which silently dropped those criteria from every grading.
                var overall = ReadNumber(resultElement, "overall");
                var pronunciation = ReadNumber(resultElement, "pronunciation");
                var fluency = ReadNumber(resultElement, "fluency");
                var grammar = ReadNumber(resultElement, "grammar");
                var vocab = ReadNumber(resultElement, "vocabulary");
                var relevance = ReadNumber(resultElement, "relevance");

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
