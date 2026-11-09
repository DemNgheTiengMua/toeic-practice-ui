using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// E2.1 (#151): what the writing grader is given, what it accepts back, and what it refuses. No
/// database: the subject is the prompt and the parser, and a fake client is the only way to read the
/// exact text the model would receive.
/// </summary>
public sealed class WritingGradingPromptTests
{
    private const string Response = "Dog run fast.";

    private static GradingCriterionDetail Criterion(
        int id, string code, decimal maxScore = 9m, decimal weight = 1m, string? rubric = null) =>
        new(id, code, $"{code} name", maxScore, weight, rubric ?? $"Band 9: excellent {code}. Band 5: partial {code}. Band 1: no {code}.");

    private static AiGradingRequest Request(IReadOnlyList<GradingCriterionDetail> criteria, string response = Response) =>
        new(
            FreeResponseId: 1,
            SkillId: 2,
            CertId: 3,
            QuestionStem: "Discuss both views and give your own opinion.",
            MinWords: 250,
            MaxWords: 350,
            ResponseText: response,
            Criteria: criteria,
            RequiredKeywords: null);

    [Fact]
    public async Task The_prompt_carries_each_criterions_band_descriptors()
    {
        var client = new ScriptedClaudeClient(_ => FullAnswer(1, 2));
        var grader = new ClaudeAiGrader(client);

        var result = await grader.GradeAsync(Request([Criterion(1, "TR"), Criterion(2, "CC")]));

        Assert.True(result.Success);
        Assert.Contains("Band 9: excellent TR. Band 5: partial TR.", client.LastPrompt);
        Assert.Contains("Band 9: excellent CC. Band 5: partial CC.", client.LastPrompt);
    }

    [Fact]
    public async Task A_criterion_without_a_rubric_is_left_out_of_the_prompt_and_never_scored()
    {
        // #147 made the description authorable; an empty one is an authoring gap, not a licence for the
        // model to invent a standard for that criterion.
        var client = new ScriptedClaudeClient(_ => """{ "scores": [{ "criterionId": 1, "score": 6, "comment": "ok" }] }""");
        var grader = new ClaudeAiGrader(client);

        var result = await grader.GradeAsync(Request([
            Criterion(1, "TR"),
            new GradingCriterionDetail(2, "CC", "Coherence", 9m, 1m, RubricDescription: "   ")
        ]));

        Assert.True(result.Success);
        Assert.DoesNotContain("ID: 2", client.LastPrompt);
        Assert.Single(result.Scores);
        Assert.Equal(1, result.Scores[0].CriterionId);
    }

    [Fact]
    public async Task A_rubric_with_no_descriptions_at_all_fails_instead_of_being_invented()
    {
        var client = new ScriptedClaudeClient(_ => FullAnswer(1));
        var grader = new ClaudeAiGrader(client);

        var result = await grader.GradeAsync(Request([
            new GradingCriterionDetail(1, "TR", "Task Response", 9m, 1m)
        ]));

        Assert.False(result.Success);
        Assert.Empty(result.Scores);
        Assert.Contains("rubric description", result.FailureReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(client.Prompts);
    }

    [Fact]
    public async Task The_checklist_is_computed_from_the_text_before_any_score_is_asked_for()
    {
        var client = new ScriptedClaudeClient(_ => FullAnswer(1));
        var grader = new ClaudeAiGrader(client);
        var request = Request([Criterion(1, "TR")], response: "dog run fast") with
        {
            RequiredKeywords = "universities, skills"
        };

        await grader.GradeAsync(request);

        Assert.Contains("wordCount: 3", client.LastPrompt);
        Assert.Contains("meetsLength: false (minimum 250, maximum 350)", client.LastPrompt);
        Assert.Contains("usesAllKeywords: false (missing: universities, skills)", client.LastPrompt);
    }

    [Fact]
    public async Task An_omitted_criterion_is_retried_once_and_then_fails_with_a_reason()
    {
        // The rule the ticket exists for: an omission must never become MaxScore x 0.7 with a
        // "Standard performance." note.
        var client = new ScriptedClaudeClient(_ => """{ "scores": [{ "criterionId": 1, "score": 6, "comment": "ok" }] }""");
        var grader = new ClaudeAiGrader(client);

        var result = await grader.GradeAsync(Request([Criterion(1, "TR"), Criterion(2, "CC")]));

        Assert.False(result.Success);
        Assert.Empty(result.Scores);
        Assert.Contains("CC (2)", result.FailureReason);
        Assert.Equal(2, client.Prompts.Count);
    }

    [Fact]
    public async Task The_retry_recovers_when_the_second_answer_is_complete()
    {
        var answers = new Queue<string>([
            """{ "scores": [{ "criterionId": 1, "score": 6, "comment": "ok" }] }""",
            FullAnswer(1, 2)
        ]);
        var client = new ScriptedClaudeClient(_ => answers.Dequeue());
        var grader = new ClaudeAiGrader(client);

        var result = await grader.GradeAsync(Request([Criterion(1, "TR"), Criterion(2, "CC")]));

        Assert.True(result.Success);
        Assert.Equal(2, result.Scores.Count);
        Assert.Equal(2, client.Prompts.Count);
    }

    [Fact]
    public async Task Every_reported_score_is_kept_so_no_criterion_is_ever_filled_in()
    {
        var client = new ScriptedClaudeClient(_ => FullAnswer(1, 2));
        var grader = new ClaudeAiGrader(client);

        var result = await grader.GradeAsync(Request([Criterion(1, "TR"), Criterion(2, "CC")]));

        Assert.Equal([1, 2], result.Scores.Select(s => s.CriterionId));
        Assert.All(result.Scores, s => Assert.NotNull(s.Comment));
    }

    [Fact]
    public async Task A_score_for_a_criterion_that_was_not_asked_about_is_ignored()
    {
        var client = new ScriptedClaudeClient(_ => FullAnswer(1, 2, 99));
        var grader = new ClaudeAiGrader(client);

        var result = await grader.GradeAsync(Request([Criterion(1, "TR"), Criterion(2, "CC")]));

        Assert.True(result.Success);
        Assert.Equal([1, 2], result.Scores.Select(s => s.CriterionId));
    }

    [Fact]
    public async Task An_error_span_that_does_not_quote_the_answer_is_dropped()
    {
        var client = new ScriptedClaudeClient(_ => """
        {
          "checklist": { "addressesPrompt": true },
          "scores": [{ "criterionId": 1, "score": 4, "comment": "ok" }],
          "errorSpans": [
            { "originalSnippet": "Dog run fast", "correctedSnippet": "Dogs run fast.", "category": "grammar", "explanationVi": "Chủ ngữ số nhiều." },
            { "originalSnippet": "a sentence that is not in the answer", "correctedSnippet": "x", "category": "grammar", "explanationVi": "không có" }
          ],
          "priorityFix": "Add a verb agreement check.",
          "modelRewrite": "Dogs run fast."
        }
        """);
        var grader = new ClaudeAiGrader(client);

        var result = await grader.GradeAsync(Request([Criterion(1, "TR")]));

        Assert.True(result.Success);
        var span = Assert.Single(result.ErrorSpans!);
        Assert.Equal("Dog run fast", span.OriginalSnippet);
        Assert.Equal("Dogs run fast.", span.CorrectedSnippet);
        Assert.Equal("Add a verb agreement check.", result.PriorityFix);
        Assert.Equal("Dogs run fast.", result.ModelRewrite);
    }

    [Fact]
    public async Task At_most_five_error_spans_are_kept()
    {
        var spans = string.Join(",", Enumerable.Range(0, 8).Select(i =>
            $$"""{ "originalSnippet": "answer {{i}}", "correctedSnippet": "fixed {{i}}", "category": "grammar", "explanationVi": "v" }"""));
        var client = new ScriptedClaudeClient(_ => $$"""
        {
          "scores": [{ "criterionId": 1, "score": 4, "comment": "ok" }],
          "errorSpans": [{{spans}}],
          "priorityFix": "p",
          "modelRewrite": "r"
        }
        """);
        var grader = new ClaudeAiGrader(client);

        var result = await grader.GradeAsync(Request(
            [Criterion(1, "TR")],
            response: "answer 0 answer 1 answer 2 answer 3 answer 4 answer 5 answer 6 answer 7"));

        Assert.Equal(5, result.ErrorSpans!.Count);
    }

    [Fact]
    public async Task An_empty_answer_fails_without_spending_a_request()
    {
        var client = new ScriptedClaudeClient(_ => FullAnswer(1));
        var grader = new ClaudeAiGrader(client);

        var result = await grader.GradeAsync(Request([Criterion(1, "TR")], response: "   "));

        Assert.False(result.Success);
        Assert.Empty(result.Scores);
        Assert.Empty(client.Prompts);
        Assert.Contains("No answer", result.FailureReason);
    }

    [Fact]
    public async Task A_refusal_to_score_an_off_task_answer_is_reported_not_scored()
    {
        // What the live model does with "dog run fast": addressesPrompt false. It answered once with
        // scores [] and once with four marks anyway, so the checklist - not the score array - decides.
        var client = new ScriptedClaudeClient(_ => """
        { "checklist": { "addressesPrompt": false },
          "scores": [{ "criterionId": 1, "score": 4, "comment": "fluent" }],
          "errorSpans": [{ "originalSnippet": "Dog run fast", "correctedSnippet": "Dogs run fast.", "category": "grammar", "explanationVi": "v" }],
          "priorityFix": "The response does not address the task.", "modelRewrite": "x" }
        """);
        var grader = new ClaudeAiGrader(client);

        var result = await grader.GradeAsync(Request([Criterion(1, "TR")]));

        Assert.False(result.Success);
        Assert.Empty(result.Scores);
        Assert.Null(result.PriorityFix);
        Assert.Null(result.ModelRewrite);
        Assert.Contains("does not answer the task", result.FailureReason);
        Assert.Equal(2, client.Prompts.Count);
    }

    [Fact]
    public async Task The_prompt_lists_each_band_with_its_gates_so_the_grader_scores_against_them()
    {
        // The schema can prove seven rows exist. It cannot prove the grader reads them, and a grid that
        // is stored but never sent is exactly the "grades nothing" defect. This asserts the text the
        // model would receive.
        var client = new ScriptedClaudeClient(_ => FullAnswer(1));
        var grader = new ClaudeAiGrader(client);

        var grid = Enumerable.Range(0, 7)
            .Select(b => new GradingCriterionBandDescriptor(
                (byte)b,
                $"Band {b}: speech rate at least {60 + b * 20} wpm.",
                $"Error-free T-units at least {10 + b * 12}%."))
            .ToList();

        var result = await grader.GradeAsync(Request([
            new GradingCriterionDetail(1, "W_TR", "Task Response", 6m, 1m, "Prose notes.", grid)
        ]));

        Assert.True(result.Success);
        Assert.Contains("Award the highest band whose gates ALL hold", client.LastPrompt);
        for (var b = 0; b <= 6; b++)
        {
            Assert.Contains($"Band {b} - Band {b}: speech rate at least", client.LastPrompt);
            Assert.Contains($"gates: Error-free T-units at least {10 + b * 12}%", client.LastPrompt);
        }
        // The prose is kept alongside the grid, not replaced by it.
        Assert.Contains("Rubric notes: Prose notes.", client.LastPrompt);
    }

    [Fact]
    public async Task The_prompt_carries_each_anchor_at_its_band_so_the_model_compares_against_written_answers()
    {
        // #193: D0.6 mechanism 5. With no anchor in the prompt the model has only a ladder of adjectives
        // to interpolate over, which is the compression the mechanism exists to stop.
        var client = new ScriptedClaudeClient(_ => FullAnswer(1));
        var grader = new ClaudeAiGrader(client);

        var grid = Enumerable.Range(0, 7)
            .Select(b => new GradingCriterionBandDescriptor(
                (byte)b, $"Band {b}: speech rate at least {60 + b * 20} wpm.", $"Gates {b}."))
            .ToList();

        var anchors = new[]
        {
            new GradingCriterionBandAnchor(2, "The band two answer, written by hand."),
            new GradingCriterionBandAnchor(3, "The band three answer, written by hand."),
            new GradingCriterionBandAnchor(4, "The band four answer, written by hand."),
        };

        var result = await grader.GradeAsync(Request([
            new GradingCriterionDetail(1, "W_TR", "Task Response", 6m, 1m, "Prose notes.", grid, anchors)
        ]));

        Assert.True(result.Success);
        foreach (var anchor in anchors)
        {
            Assert.Contains($"Band {anchor.Band} anchor:", client.LastPrompt);
            Assert.Contains(anchor.ResponseText, client.LastPrompt);
        }
    }

    [Fact]
    public async Task A_rubric_with_no_anchors_sends_a_prompt_identical_to_todays()
    {
        // The guard direction: every rubric authored before #193 has no anchors, and the prompt must not
        // grow a section that says so. Nothing is added for an absent anchor.
        var client = new ScriptedClaudeClient(_ => FullAnswer(1));
        var grader = new ClaudeAiGrader(client);

        var result = await grader.GradeAsync(Request([Criterion(1, "W_TR")]));

        Assert.True(result.Success);
        Assert.DoesNotContain("anchor", client.LastPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_grid_short_of_seven_bands_is_not_graded_even_though_it_has_seven_lines_of_text()
    {
        // The input that would pass a "does it have text" check: six rows of real thresholds plus a
        // prose description. The prose would happily be scored; the missing band would be inferred.
        var client = new ScriptedClaudeClient(_ => FullAnswer(1));
        var grader = new ClaudeAiGrader(client);

        var grid = Enumerable.Range(0, 6)
            .Select(b => new GradingCriterionBandDescriptor(
                (byte)b, $"Band {b}: speech rate at least {60 + b * 20} wpm.", $"Gates {b}."))
            .ToList();

        var result = await grader.GradeAsync(Request([
            new GradingCriterionDetail(1, "W_TR", "Task Response", 6m, 1m, "Prose that looks complete.", grid)
        ]));

        Assert.False(result.Success);
        Assert.Contains("W_TR", result.FailureReason!, StringComparison.Ordinal);
        Assert.Empty(result.Scores);
        Assert.Empty(client.Prompts);
    }

    private static string FullAnswer(params int[] criterionIds) => $$"""
    {
      "checklist": { "addressesPrompt": true },
      "scores": [{{string.Join(",", criterionIds.Select(id => $$"""{ "criterionId": {{id}}, "score": 6, "comment": "noted" }"""))}}],
      "errorSpans": [],
      "priorityFix": "",
      "modelRewrite": ""
    }
    """;

    private sealed class ScriptedClaudeClient(Func<string, string> respond) : IClaudeClient
    {
        public List<string> Prompts { get; } = [];

        public string LastPrompt => Prompts[^1];

        public Task<string> CreateMessageAsync(string userPrompt, string? systemPrompt = null, int maxTokens = 4000, string? model = null, CancellationToken cancellationToken = default)
        {
            Prompts.Add(userPrompt);
            return Task.FromResult(respond(userPrompt));
        }
    }
}
