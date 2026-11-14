using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Services;
using Xunit;

namespace Services.Tests;

public sealed class SpeechSuperTests
{
    private sealed class MockHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request);
    }

    private sealed class InMemoryFileStore(Dictionary<string, byte[]> files) : IFileStore
    {
        public Task<string> SaveAudioAsync(int attemptId, int questionId, Stream stream, string contentType, string fileExtension, CancellationToken cancellationToken = default)
        {
            var path = $"audio/{attemptId}/{questionId}.{fileExtension}";
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            files[path] = ms.ToArray();
            return Task.FromResult(path);
        }

        public Task<FileStreamResultInfo?> GetAudioAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            if (files.TryGetValue(relativePath, out var bytes))
            {
                var stream = new MemoryStream(bytes);
                return Task.FromResult<FileStreamResultInfo?>(new FileStreamResultInfo(stream, "audio/wav", Path.GetFileName(relativePath)));
            }
            return Task.FromResult<FileStreamResultInfo?>(null);
        }

        public Task<bool> DeleteAudioAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(files.Remove(relativePath));
        }

        public Task<string> SaveImportMediaAsync(int batchId, Stream stream, string fileExtension, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    [Fact]
    public void ComputeSha1_calculates_correct_lowercase_hex_hash()
    {
        // echo -n "test" | sha1sum -> a94a8fe5ccb19ba61c4c0873d391e987982fbbd3
        var hash = SpeechSuperClient.ComputeSha1("test");
        Assert.Equal("a94a8fe5ccb19ba61c4c0873d391e987982fbbd3", hash);
    }

    [Fact]
    public async Task AssessAsync_returns_error_when_keys_missing()
    {
        var options = new SpeechSuperOptions { AppKey = "", SecretKey = "" };
        using var client = new HttpClient();
        var speechSuper = new SpeechSuperClient(client, options);

        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var result = await speechSuper.AssessAsync(stream, "wav");

        Assert.False(result.Success);
        Assert.Equal(41030, result.ErrId);
        Assert.Contains("not configured", result.ErrorMessage);
    }

    [Fact]
    public async Task AssessAsync_parses_successful_result_correctly()
    {
        // The field names and scale of a live speak.eval.pro 200: every figure is a percentage.
        const string mockResponse = """
        {
            "result": {
                "transcription": "I want to be in art because I am a creative person.",
                "overall": 65,
                "pronunciation": 60,
                "fluency": 70,
                "grammar": 65,
                "vocabulary": 70,
                "relevance": 90,
                "sentences": [
                    {
                        "sentence": "I want to be in art because I am a creative person.",
                        "pronunciation": 60,
                        "start": 71,
                        "end": 708
                    }
                ]
            }
        }
        """;

        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal("https://api.speechsuper.com/speak.eval.pro", req.RequestUri?.ToString());
            Assert.True(req.Headers.Contains("Request-Index"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(mockResponse, Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(handler);
        var options = new SpeechSuperOptions
        {
            AppKey = "test-app-key",
            SecretKey = "test-secret-key",
            BaseUrl = "https://api.speechsuper.com",
            CoreType = "speak.eval.pro"
        };
        var speechSuper = new SpeechSuperClient(httpClient, options);

        using var audioStream = new MemoryStream(new byte[] { 0x52, 0x49, 0x46, 0x46 }); // mock RIFF header
        var result = await speechSuper.AssessAsync(audioStream, "wav", questionPrompt: "Describe your job");

        Assert.True(result.Success);
        Assert.Equal("I want to be in art because I am a creative person.", result.Transcription);
        Assert.Equal(65m, result.OverallScore);
        Assert.Equal(60m, result.PronunciationScore);
        Assert.Equal(70m, result.FluencyScore);
        Assert.Equal(65m, result.GrammarScore);
        Assert.Equal(70m, result.VocabularyScore);
        Assert.Equal(90m, result.RelevanceScore);
        Assert.NotNull(result.Sentences);
        Assert.Single(result.Sentences);
    }

    [Fact]
    public async Task AssessAsync_parses_api_error_response_correctly()
    {
        const string mockError = """
        {
            "errId": 41030,
            "error": "appKey not found"
        }
        """;

        var handler = new MockHttpMessageHandler(_ =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(mockError, Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(handler);
        var options = new SpeechSuperOptions
        {
            AppKey = "invalid-key",
            SecretKey = "test-secret"
        };
        var speechSuper = new SpeechSuperClient(httpClient, options);

        using var audioStream = new MemoryStream(new byte[] { 1, 2, 3 });
        var result = await speechSuper.AssessAsync(audioStream, "wav");

        Assert.False(result.Success);
        Assert.Equal(41030, result.ErrId);
        Assert.Equal("appKey not found", result.ErrorMessage);
    }

    [Fact]
    public async Task SpeechSuperAiGrader_delegates_writing_requests_to_fallback()
    {
        var options = new SpeechSuperOptions();
        using var httpClient = new HttpClient();
        var client = new SpeechSuperClient(httpClient, options);
        var fileStore = new InMemoryFileStore(new Dictionary<string, byte[]>());
        var grader = new SpeechSuperAiGrader(client, fileStore);

        var criteria = new List<GradingCriterionDetail>
        {
            new(1, "GRAMMAR", "Grammar & Accuracy", 5.0m, 1.0m)
        };

        var request = new AiGradingRequest(
            FreeResponseId: 10,
            SkillId: 2,
            CertId: 1,
            QuestionStem: "Write an email requesting info",
            MinWords: 50,
            MaxWords: 100,
            ResponseText: "Dear Sir, I am writing to inquire about the schedule.",
            Criteria: criteria,
            AudioPath: null);

        var result = await grader.GradeAsync(request);

        Assert.True(result.Success);
        Assert.Equal(FakeAiGrader.DefaultModel, result.Model);
        Assert.Single(result.Scores);
    }

    [Fact]
    public async Task SpeechSuperAiGrader_grades_speaking_request_via_speechsuper()
    {
        const string mockResponse = """
        {
            "result": {
                "transcription": "I enjoy playing tennis on weekends.",
                "overall": 78,
                "pronunciation": 83,
                "fluency": 78,
                "grammar": 76,
                "vocabulary": 83,
                "relevance": 95
            }
        }
        """;

        var handler = new MockHttpMessageHandler(_ =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(mockResponse, Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(handler);
        var options = new SpeechSuperOptions
        {
            AppKey = "valid-key",
            SecretKey = "valid-secret"
        };
        var speechClient = new SpeechSuperClient(httpClient, options);

        const string audioRelPath = "speaking/audio-101.wav";
        var fileStore = new InMemoryFileStore(new Dictionary<string, byte[]>
        {
            [audioRelPath] = new byte[] { 0x52, 0x49, 0x46, 0x46, 0x24, 0x00 }
        });

        var grader = new SpeechSuperAiGrader(speechClient, fileStore);

        var criteria = new List<GradingCriterionDetail>
        {
            new(101, "PRON", "Pronunciation", 5.0m, 1.0m),
            new(102, "FLUENCY", "Fluency & Coherence", 5.0m, 1.0m),
            new(103, "GRAMMAR", "Grammar", 5.0m, 1.0m),
            new(104, "RELEVANCE", "Relevance", 5.0m, 1.0m)
        };

        var request = new AiGradingRequest(
            FreeResponseId: 20,
            SkillId: 3,
            CertId: 1,
            QuestionStem: "Describe your favorite hobby.",
            MinWords: null,
            MaxWords: null,
            ResponseText: "",
            Criteria: criteria,
            AudioPath: audioRelPath);

        var result = await grader.GradeAsync(request);

        Assert.True(result.Success);
        Assert.Equal("speechsuper-speak.eval.pro", result.Model);
        Assert.Equal("I enjoy playing tennis on weekends.", result.Transcript);
        // #152: the request declares no reference, so relevance has nothing to be judged against and
        // is left out rather than scored. The remaining three criteria are scored.
        Assert.Equal(3, result.Scores.Count);
        Assert.DoesNotContain(result.Scores, s => s.CriterionId == 104);

        // Verify criteria scaling (scores should be clamped and bounded)
        foreach (var score in result.Scores)
        {
            Assert.True(score.Score > 0 && score.Score <= 5.0m);
            Assert.NotNull(score.Comment);
        }
    }

    [Fact]
    public async Task SpeechSuperAiGrader_returns_failure_when_audio_file_missing()
    {
        var options = new SpeechSuperOptions { AppKey = "k", SecretKey = "s" };
        using var httpClient = new HttpClient();
        var speechClient = new SpeechSuperClient(httpClient, options);
        var fileStore = new InMemoryFileStore(new Dictionary<string, byte[]>());
        var grader = new SpeechSuperAiGrader(speechClient, fileStore);

        var request = new AiGradingRequest(
            FreeResponseId: 21,
            SkillId: 3,
            CertId: 1,
            QuestionStem: "Test question",
            MinWords: null,
            MaxWords: null,
            ResponseText: "",
            Criteria: new List<GradingCriterionDetail>(),
            AudioPath: "non-existent-audio.wav");

        var result = await grader.GradeAsync(request);

        Assert.False(result.Success);
        Assert.Contains("not found", result.FailureReason);
        Assert.Equal("speechsuper", result.Model);
    }

    private sealed class StubLinguisticGrader(AiGradingResult result) : IAiGrader
    {
        public AiGradingRequest? LastRequest { get; private set; }

        public Task<AiGradingResult> GradeAsync(AiGradingRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(result);
        }
    }

    [Fact]
    public async Task SpeechSuperAiGrader_combines_acoustic_scores_and_transcription_with_linguistic_grader()
    {
        const string mockResponse = """
        {
            "result": {
                "transcription": "I enjoys playing tennis on weekends.",
                "overall": 78,
                "pronunciation": 83,
                "fluency": 78,
                "grammar": 70,
                "vocabulary": 80,
                "relevance": 90
            }
        }
        """;

        var handler = new MockHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(mockResponse, Encoding.UTF8, "application/json")
        }));

        using var httpClient = new HttpClient(handler);
        var speechClient = new SpeechSuperClient(httpClient, new SpeechSuperOptions { AppKey = "k", SecretKey = "s" });
        const string audioRelPath = "speaking/audio-combine.wav";
        var fileStore = new InMemoryFileStore(new Dictionary<string, byte[]>
        {
            [audioRelPath] = new byte[] { 0x52, 0x49, 0x46, 0x46, 0x24, 0x00 }
        });

        var stubLinguistic = new StubLinguisticGrader(new AiGradingResult(
            Success: true,
            FailureReason: null,
            Model: "claude-sonnet-3.7",
            Scores:
            [
                new AiCriterionScoreResult(101, 4.0m, "Clear pronunciation."),
                new AiCriterionScoreResult(102, 3.8m, "Smooth pacing."),
                new AiCriterionScoreResult(103, 3.5m, "Subject-verb agreement slip."),
                new AiCriterionScoreResult(104, 4.5m, "Directly answers the prompt.")
            ],
            ErrorSpans:
            [
                new BusinessObjects.AiErrorSpan("I enjoys", "I enjoy", "grammar", "Động từ 'enjoy' đi với chủ ngữ 'I' không chia số ít.")
            ],
            PriorityFix: "Fix subject-verb agreement with first-person subjects.",
            ModelRewrite: "I really enjoy playing tennis on weekends because it keeps me active."));

        var grader = new SpeechSuperAiGrader(speechClient, fileStore, stubLinguistic);

        var criteria = new List<GradingCriterionDetail>
        {
            new(101, "PRON", "Pronunciation", 5.0m, 1.0m),
            new(102, "FLUENCY", "Fluency & Coherence", 5.0m, 1.0m),
            new(103, "GRAMMAR", "Grammar", 5.0m, 1.0m),
            new(104, "COMPLETENESS", "Completeness", 5.0m, 1.0m)
        };

        var request = new AiGradingRequest(
            FreeResponseId: 22,
            SkillId: 3,
            CertId: 1,
            QuestionStem: "Describe your favorite hobby.",
            MinWords: null,
            MaxWords: null,
            ResponseText: "",
            Criteria: criteria,
            AudioPath: audioRelPath);

        var result = await grader.GradeAsync(request);

        Assert.True(result.Success);
        Assert.NotNull(stubLinguistic.LastRequest);
        Assert.Equal("I enjoys playing tennis on weekends.", stubLinguistic.LastRequest!.ResponseText);
        Assert.Null(stubLinguistic.LastRequest.AudioPath);
        Assert.Contains("Pronunciation: 83", stubLinguistic.LastRequest.Guide);
        Assert.Equal("Fix subject-verb agreement with first-person subjects.", result.PriorityFix);
        Assert.Equal("I really enjoy playing tennis on weekends because it keeps me active.", result.ModelRewrite);
        Assert.NotNull(result.ErrorSpans);
        Assert.Single(result.ErrorSpans!);
        Assert.Equal("I enjoys", result.ErrorSpans![0].OriginalSnippet);
        Assert.Equal(4, result.Scores.Count);
    }

    [Fact]
    public async Task FakeAiGrader_produces_priorityFix_modelRewrite_and_errorSpans_referencing_submission()
    {
        var grader = new FakeAiGrader();
        var submission = "In my opinion, remote work is very good for people because they can save time and work from home.";
        var request = new AiGradingRequest(
            FreeResponseId: 30,
            SkillId: 2,
            CertId: 2,
            QuestionStem: "Discuss the advantages of remote work.",
            MinWords: 10,
            MaxWords: 200,
            ResponseText: submission,
            Criteria: [new GradingCriterionDetail(1, "TR", "Task Response", 9m, 1m)],
            TargetBandCode: "7.0");

        var result = await grader.GradeAsync(request);

        Assert.True(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.PriorityFix));
        Assert.False(string.IsNullOrWhiteSpace(result.ModelRewrite));
        Assert.NotNull(result.ErrorSpans);
        Assert.NotEmpty(result.ErrorSpans!);
        Assert.Contains(result.ErrorSpans!, span => submission.Contains(span.OriginalSnippet, StringComparison.Ordinal));
    }
}
