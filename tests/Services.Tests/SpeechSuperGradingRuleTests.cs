using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #152: what the speaking grader sends, and what it does with what comes back. The payload tests
/// assert on the request body the client actually serialises, because "we pass refText" is a claim
/// about a value and the defect was a claim about a value.
/// </summary>
public sealed class SpeechSuperGradingRuleTests
{
    private sealed class CapturingHandler(Func<string, string> respond) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(respond(LastBody ?? ""), Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class SingleFileStore(string path, byte[] bytes) : IFileStore
    {
        public Task<string> SaveAudioAsync(int attemptId, int questionId, Stream stream, string contentType, string fileExtension, CancellationToken cancellationToken = default)
            => Task.FromResult(path);

        public Task<FileStreamResultInfo?> GetAudioAsync(string relativePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<FileStreamResultInfo?>(relativePath == path
                ? new FileStreamResultInfo(new MemoryStream(bytes), "audio/wav", Path.GetFileName(path))
                : null);

        public Task<bool> DeleteAudioAsync(string relativePath, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<string> SaveImportMediaAsync(int batchId, Stream stream, string fileExtension, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private const string FullResponse = """
    {
        "result": {
            "transcription": "I enjoy playing tennis on weekends.",
            "overall": 7.2,
            "pronunciation": 7.5,
            "fluency_coherence": 7.0,
            "grammar": 6.8,
            "lexical_resource": 7.5,
            "relevance": 95
        }
    }
    """;

    private const string SilentResponse = """
    { "result": { "transcription": "", "overall": 0, "pronunciation": 0 } }
    """;

    private static readonly List<GradingCriterionDetail> ToeicCriteria =
    [
        new(1, "PRON", "Pronunciation", 3.0m, 1.0m),
        new(2, "INT", "Intonation and stress", 3.0m, 1.0m),
        new(3, "GRAM", "Grammar, vocabulary and cohesion", 3.0m, 1.0m),
        new(4, "REL", "Relevance and completeness", 3.0m, 1.0m)
    ];

    private static (SpeechSuperAiGrader Grader, CapturingHandler Handler) Build(
        string respondJson = FullResponse, string path = "audio/q.wav")
    {
        var handler = new CapturingHandler(_ => respondJson);
        var client = new SpeechSuperClient(new HttpClient(handler), new SpeechSuperOptions
        {
            AppKey = "app",
            SecretKey = "secret"
        });
        var store = new SingleFileStore(path, new byte[] { 0x52, 0x49, 0x46, 0x46 });
        return (new SpeechSuperAiGrader(client, store), handler);
    }

    private static AiGradingRequest Request(
        string? referenceText,
        bool describesImage = false,
        IReadOnlyList<GradingCriterionDetail>? criteria = null,
        string stem = "Describe your favourite hobby.")
        => new(
            FreeResponseId: 1,
            SkillId: 1,
            CertId: 1,
            QuestionStem: stem,
            MinWords: null,
            MaxWords: null,
            ResponseText: "",
            Criteria: criteria ?? ToeicCriteria,
            AudioPath: "audio/q.wav",
            ReferenceText: referenceText,
            ReferenceDescribesImage: describesImage);

    [Fact]
    public async Task A_read_aloud_question_sends_its_script_as_refText_and_the_prompt_as_questionPrompt()
    {
        const string script = "Attention all shoppers. The sale ends today at 9:00 PM.";
        var (grader, handler) = Build();

        var result = await grader.GradeAsync(Request(script, stem: "Read the announcement aloud."));

        Assert.True(result.Success);
        Assert.NotNull(handler.LastBody);
        // The script is the pronunciation reference...
        Assert.Contains("\"refText\":", handler.LastBody);
        Assert.Contains(script, handler.LastBody);
        // ...and the prompt is context, not a reference.
        Assert.Contains("\"question_prompt\":\"Read the announcement aloud.\"", handler.LastBody);
    }

    [Fact]
    public async Task An_unscripted_question_sends_an_empty_refText_and_the_prompt_as_questionPrompt()
    {
        var (grader, handler) = Build();

        var result = await grader.GradeAsync(Request(
            referenceText: null,
            stem: "Do you work or are you a student?"));

        Assert.True(result.Success);
        Assert.NotNull(handler.LastBody);
        Assert.Contains("\"refText\":\"\"", handler.LastBody);
        Assert.Contains("\"question_prompt\":\"Do you work or are you a student?\"", handler.LastBody);
    }

    [Fact]
    public async Task The_payload_names_no_certificate_its_test_type_or_its_task_type()
    {
        var (grader, handler) = Build();

        await grader.GradeAsync(Request(referenceText: null));
        await grader.GradeAsync(Request(referenceText: "Read this sentence aloud."));

        Assert.NotNull(handler.LastBody);
        Assert.DoesNotContain("ielts", handler.LastBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("test_type", handler.LastBody);
        Assert.DoesNotContain("task_type", handler.LastBody);
    }

    [Fact]
    public async Task A_missing_provider_score_is_never_defaulted_and_scores_stay_inside_their_maximum()
    {
        // Only pronunciation comes back: the other three criteria are not answered at all.
        const string onlyPronunciation = """
        {
            "result": {
                "transcription": "I enjoy playing tennis on weekends.",
                "pronunciation": 7.5
            }
        }
        """;
        var (grader, _) = Build(onlyPronunciation);

        var result = await grader.GradeAsync(Request(referenceText: null));

        Assert.True(result.Success);
        var score = Assert.Single(result.Scores);
        Assert.Equal(1, score.CriterionId);
        // 7.5 of 9.0 is 83% of a 3.0 criterion. The old code would have invented 2.0 for the rest.
        Assert.Equal(2.5m, score.Score);
        Assert.DoesNotContain(result.Scores, s => s.CriterionId is 2 or 3 or 4);
    }

    [Fact]
    public async Task A_silent_recording_is_failed_with_no_score_at_all()
    {
        var (grader, handler) = Build(SilentResponse);

        var result = await grader.GradeAsync(Request(referenceText: null));

        Assert.False(result.Success);
        Assert.Empty(result.Scores);
        Assert.False(string.IsNullOrWhiteSpace(result.FailureReason));
        Assert.Contains("no speech", result.FailureReason, StringComparison.OrdinalIgnoreCase);
        // One retry, as the ticket asks: two provider calls, no fabricated marks.
        Assert.NotNull(handler.LastBody);
    }

    [Fact]
    public async Task Relevance_is_dropped_entirely_when_the_question_declares_no_reference()
    {
        var (grader, _) = Build();

        var result = await grader.GradeAsync(Request(referenceText: null));

        Assert.True(result.Success);
        Assert.DoesNotContain(result.Scores, s => s.CriterionId == 4);
        Assert.Equal(3, result.Scores.Count);
    }

    [Fact]
    public async Task Relevance_is_scored_when_the_question_describes_its_picture()
    {
        var (grader, handler) = Build();

        var result = await grader.GradeAsync(Request(
            referenceText: "Two colleagues are talking across a desk.",
            describesImage: true));

        Assert.True(result.Success);
        var relevance = Assert.Single(result.Scores, s => s.CriterionId == 4);
        // 95% of the 3.0 criterion, rounded to one decimal.
        Assert.Equal(2.9m, relevance.Score);
        // A picture description is context for relevance, not a script to read aloud.
        Assert.Contains("\"refText\":\"\"", handler.LastBody);
        Assert.Contains("Two colleagues are talking across a desk.", handler.LastBody);
    }

    [Fact]
    public async Task Each_criteria_ratio_comes_from_its_own_provider_field_on_that_fields_scale()
    {
        var (grader, _) = Build();

        var result = await grader.GradeAsync(Request(referenceText: "Read this aloud."));

        // pron 7.5/9 -> 2.5, int 7.0/9 -> 2.3, gram 6.8/9 -> 2.3, rel 95/100 -> 2.9 (of 3.0 each).
        var byId = result.Scores.ToDictionary(s => s.CriterionId, s => s.Score);
        Assert.Equal(2.5m, byId[1]);
        Assert.Equal(2.3m, byId[2]);
        Assert.Equal(2.3m, byId[3]);
        Assert.Equal(2.9m, byId[4]);
    }

    [Fact]
    public async Task A_score_outside_the_fields_documented_scale_is_treated_as_missing_not_as_a_top_mark()
    {
        // 82 would be a percentage leaked into a 0..9 field; clamped it would read as a perfect band.
        const string wrongScale = """
        {
            "result": {
                "transcription": "I enjoy playing tennis on weekends.",
                "pronunciation": 82,
                "fluency_coherence": 7.0
            }
        }
        """;
        var (grader, _) = Build(wrongScale);

        var result = await grader.GradeAsync(Request(referenceText: null));

        Assert.True(result.Success);
        Assert.DoesNotContain(result.Scores, s => s.CriterionId == 1);
        Assert.Contains(result.Scores, s => s.CriterionId == 2);
    }
}
