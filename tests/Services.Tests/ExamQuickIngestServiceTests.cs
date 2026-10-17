using System;
using System.Linq;
using Contracts;
using Services;
using Xunit;

namespace Services.Tests;

public sealed class ExamQuickIngestServiceTests
{
    private readonly ExamQuickIngestService _parser = new();

    [Fact]
    public void EmptyOrWhitespaceInput_ReturnsErrorWithLineNumber()
    {
        var result = _parser.Parse(new QuickIngestParseRequest("   "));
        Assert.False(result.Success);
        Assert.Equal(0, result.TotalQuestions);
        Assert.True(result.ErrorCount > 0);
        Assert.Contains(result.GlobalIssues, i => i.Severity == "error");
    }

    [Fact]
    public void StandardAsteriskFormat_ParsesQuestionsAndOptionsCorrectly()
    {
        var raw = """
            1. Ms. Tanaka requested that all department managers submit their quarterly budgets by _____ Friday.
            *A. next
            B. nearly
            C. soon
            D. highly
            EXPLANATION: The adjective 'next' correctly precedes a day of the week to indicate future time.

            Question 2: Visitors to the research facility must check in at the front desk.
            A) Always
            *B) Promptly
            C) Slowly
            D) Loudly
            GIẢI THÍCH: Trạng từ 'promptly' bổ nghĩa cho động từ check in.
            """;

        var result = _parser.Parse(new QuickIngestParseRequest(raw));

        Assert.True(result.Success, string.Join("; ", result.GlobalIssues.Concat(result.Questions.SelectMany(q => q.Issues)).Select(i => $"L{i.LineNumber}: {i.Message}")));
        Assert.Equal(2, result.TotalQuestions);
        Assert.Equal(2, result.ValidQuestions);
        Assert.Equal(0, result.ErrorCount);

        var q1 = result.Questions[0];
        Assert.Contains("Tanaka", q1.Stem);
        Assert.Equal(4, q1.Options.Count);
        Assert.True(q1.Options[0].IsCorrect);
        Assert.False(q1.Options[1].IsCorrect);
        Assert.Equal("A", q1.Options[0].Label);
        Assert.Equal("next", q1.Options[0].Text);
        Assert.NotNull(q1.Explanation);
        Assert.Contains("future time", q1.Explanation);

        var q2 = result.Questions[1];
        Assert.Contains("Visitors", q2.Stem);
        Assert.Equal(4, q2.Options.Count);
        Assert.False(q2.Options[0].IsCorrect);
        Assert.True(q2.Options[1].IsCorrect);
        Assert.Equal("B", q2.Options[1].Label);
        Assert.NotNull(q2.Explanation);
        Assert.Contains("Trạng từ", q2.Explanation);
    }

    [Fact]
    public void TrailingAnswerKey_SetsCorrectOption()
    {
        var raw = """
            Câu 1: The proposal was accepted _____ the committee had reviewed all feedback.
            (A) although
            (B) because
            (C) after
            (D) while
            ANSWER: C
            CATEGORY: conjunctions
            EXPLANATION: 'After' shows sequence of events.
            """;

        var result = _parser.Parse(new QuickIngestParseRequest(raw));

        Assert.True(result.Success);
        Assert.Single(result.Questions);

        var q = result.Questions[0];
        Assert.True(q.IsValid);
        Assert.Equal("conjunctions", q.CategoryCode);
        Assert.Equal("C", q.Options.Single(o => o.IsCorrect).Label);
        Assert.Equal("after", q.Options.Single(o => o.IsCorrect).Text);
        Assert.Equal("'After' shows sequence of events.", q.Explanation);
    }

    [Fact]
    public void TrailingVietnameseKey_SetsCorrectOption()
    {
        var raw = """
            1) All employees are invited to the annual gala.
            A. True
            B. False
            ĐÁP ÁN: A
            """;

        var result = _parser.Parse(new QuickIngestParseRequest(raw));

        Assert.True(result.Success);
        Assert.Single(result.Questions);
        var q = result.Questions[0];
        Assert.True(q.IsValid);
        Assert.Equal("A", q.Options.Single(o => o.IsCorrect).Label);
    }

    [Fact]
    public void MissingCorrectAnswer_FlagsErrorWithLineNumber()
    {
        var raw = """
            1. Choose the correct answer.
            A. Option 1
            B. Option 2
            C. Option 3
            """;

        var result = _parser.Parse(new QuickIngestParseRequest(raw));

        Assert.False(result.Success);
        Assert.Equal(1, result.TotalQuestions);
        Assert.Equal(0, result.ValidQuestions);
        Assert.True(result.ErrorCount > 0);

        var q = result.Questions[0];
        Assert.False(q.IsValid);
        Assert.Contains(q.Issues, i => i.Severity == "error" && i.Message.Contains("no correct answer"));
    }

    [Fact]
    public void MultipleCorrectAnswers_FlagsError()
    {
        var raw = """
            1. Choose the correct answer.
            *A. Option 1
            *B. Option 2
            C. Option 3
            """;

        var result = _parser.Parse(new QuickIngestParseRequest(raw));

        Assert.False(result.Success);
        var q = result.Questions[0];
        Assert.False(q.IsValid);
        Assert.Contains(q.Issues, i => i.Severity == "error" && i.Message.Contains("multiple correct answers"));
    }

    [Fact]
    public void FewerThanTwoOptions_FlagsError()
    {
        var raw = """
            1. Only one option provided.
            *A. Lone option
            """;

        var result = _parser.Parse(new QuickIngestParseRequest(raw));

        Assert.False(result.Success);
        var q = result.Questions[0];
        Assert.False(q.IsValid);
        Assert.Contains(q.Issues, i => i.Severity == "error" && i.Message.Contains("at least 2 options"));
    }

    [Fact]
    public void EmptyStem_FlagsError()
    {
        var raw = """
            *A. Option 1
            B. Option 2
            """;

        var result = _parser.Parse(new QuickIngestParseRequest(raw));

        // When there is no stem before options
        Assert.False(result.Success);
        var q = result.Questions[0];
        Assert.False(q.IsValid);
        Assert.Contains(q.Issues, i => i.Severity == "error" && i.Message.Contains("stem is empty"));
    }

    [Fact]
    public void JsonFallback_ParsesValidArray()
    {
        var json = """
            [
              {
                "stem": "The shipment will arrive on time _____ weather conditions worsen.",
                "difficultyLevel": 4,
                "categoryCode": "grammar_cond",
                "explanation": "Unless introduces an exception condition.",
                "options": [
                  { "label": "A", "text": "unless", "isCorrect": true, "rationale": "Correct conditional conjunction." },
                  { "label": "B", "text": "despite", "isCorrect": false },
                  { "label": "C", "text": "whereas", "isCorrect": false },
                  { "label": "D", "text": "provided", "isCorrect": false }
                ]
              }
            ]
            """;

        var result = _parser.Parse(new QuickIngestParseRequest(json));

        Assert.True(result.Success);
        Assert.Single(result.Questions);
        var q = result.Questions[0];
        Assert.True(q.IsValid);
        Assert.Equal(4, q.DifficultyLevel);
        Assert.Equal("grammar_cond", q.CategoryCode);
        Assert.Equal("unless", q.Options.Single(o => o.IsCorrect).Text);
        Assert.Equal("Correct conditional conjunction.", q.Options.Single(o => o.IsCorrect).Rationale);
    }

    [Fact]
    public void JsonFallback_MalformedJsonReportsLineError()
    {
        var badJson = """
            [
              {
                "stem": "Broken json missing closing brace"
            """;

        var result = _parser.Parse(new QuickIngestParseRequest(badJson));

        Assert.False(result.Success);
        Assert.Contains(result.GlobalIssues, i => i.Severity == "error" && i.Message.Contains("JSON"));
    }
}
