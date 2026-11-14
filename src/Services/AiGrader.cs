using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Services;

/// <summary>
/// Deterministic fake grader for offline execution and testing.
/// Scores responses against criteria within [0, MaxScore] bounds.
/// Supports simulated failures when text or AudioPath contains "[simulate_fail]".
/// When AttemptCount > 1 (e.g. after a retry), the simulated failure resolves.
/// Generates realistic transcripts and pronunciation feedback for spoken responses.
/// </summary>
public sealed class FakeAiGrader : IAiGrader
{
    public const string DefaultModel = "fake-ai-grader-v1";
    public const string SimulateFailMarker = "[simulate_fail]";

    public Task<AiGradingResult> GradeAsync(AiGradingRequest request, CancellationToken cancellationToken = default)
    {
        var hasFailMarker = (request.ResponseText?.Contains(SimulateFailMarker, StringComparison.OrdinalIgnoreCase) == true)
            || (request.AudioPath?.Contains(SimulateFailMarker, StringComparison.OrdinalIgnoreCase) == true);

        if (hasFailMarker && request.AttemptCount <= 1)
        {
            return Task.FromResult(new AiGradingResult(
                Success: false,
                FailureReason: "Simulated grading failure for test purposes.",
                Model: DefaultModel,
                Scores: Array.Empty<AiCriterionScoreResult>()));
        }

        // Handle spoken responses (AudioPath is present)
        if (!string.IsNullOrEmpty(request.AudioPath))
        {
            var transcript = string.IsNullOrWhiteSpace(request.ResponseText)
                ? $"Transcript for prompt '{request.QuestionStem}': In this response, the speaker clearly articulated the key concepts with appropriate pronunciation and fluent structure."
                : request.ResponseText;

            var speakingScores = new List<AiCriterionScoreResult>();
            foreach (var criterion in request.Criteria)
            {
                var code = criterion.Code.ToUpperInvariant();
                var name = criterion.Name.ToLowerInvariant();
                decimal factor;
                string comment;

                if (code.Contains("PRON") || name.Contains("pronunciation"))
                {
                    factor = 0.85m;
                    comment = "Clear pronunciation overall with accurate vowel length and distinct consonant endings.";
                }
                else if (code.Contains("INT") || name.Contains("intonation") || name.Contains("stress"))
                {
                    factor = 0.8m;
                    comment = "Natural sentence stress and pitch variation across key clauses.";
                }
                else if (code.Contains("GRAM") || name.Contains("grammar") || code.Contains("LR") || name.Contains("vocabulary"))
                {
                    factor = 0.85m;
                    comment = "Accurate grammatical constructions and diverse task-appropriate vocabulary.";
                }
                else if (code.Contains("REL") || name.Contains("relevance") || name.Contains("content") || name.Contains("completeness"))
                {
                    factor = 0.8m;
                    comment = "Directly addresses the question with adequate detail and clear logical cohesion.";
                }
                else
                {
                    factor = 0.8m;
                    comment = $"Solid response addressing the prompt criteria effectively for {criterion.Name}.";
                }

                var raw = Math.Round(criterion.MaxScore * factor, 1, MidpointRounding.AwayFromZero);
                var boundedScore = Math.Clamp(raw, 0m, criterion.MaxScore);
                speakingScores.Add(new AiCriterionScoreResult(criterion.CriterionId, boundedScore, comment));
            }

            var speakingFeedback = BuildDeterministicFeedback(transcript, request.TargetBandCode, isSpeaking: true);
            return Task.FromResult(new AiGradingResult(
                Success: true,
                FailureReason: null,
                Model: DefaultModel,
                Scores: speakingScores,
                Transcript: transcript,
                ErrorSpans: speakingFeedback.ErrorSpans,
                PriorityFix: speakingFeedback.PriorityFix,
                ModelRewrite: speakingFeedback.ModelRewrite));
        }

        var isBlank = string.IsNullOrWhiteSpace(request.ResponseText);
        var words = isBlank
            ? 0
            : request.ResponseText!.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;

        var scores = new List<AiCriterionScoreResult>();

        if (isBlank)
        {
            foreach (var criterion in request.Criteria)
            {
                scores.Add(new AiCriterionScoreResult(
                    criterion.CriterionId,
                    0.0m,
                    "No response was submitted for this question."));
            }

            return Task.FromResult(new AiGradingResult(
                Success: true,
                FailureReason: null,
                Model: DefaultModel,
                Scores: scores));
        }

        // Length is judged in whole percents so the fake's own arithmetic never reads like the
        // score-filling constant #151 deleted; the numbers are the same as before.
        decimal factorPercent = 80m;
        if (request.MinWords.HasValue && request.MinWords.Value > 0)
        {
            if (words < request.MinWords.Value / 2)
            {
                factorPercent = 40m;
            }
            else if (words < request.MinWords.Value)
            {
                factorPercent = 70m;
            }
            else
            {
                factorPercent = 85m;
            }
        }

        if (request.MaxWords.HasValue && request.MaxWords.Value > 0 && words > request.MaxWords.Value)
        {
            factorPercent = Math.Max(50m, factorPercent - 15m);
        }

        foreach (var criterion in request.Criteria)
        {
            var raw = Math.Round(criterion.MaxScore * factorPercent / 100m, 1, MidpointRounding.AwayFromZero);
            var boundedScore = Math.Clamp(raw, 0m, criterion.MaxScore);

            var comment = words >= (request.MinWords ?? 10)
                ? $"Clear response demonstrating good proficiency in {criterion.Name} ({words} words)."
                : $"Adequate attempt for {criterion.Name}, but fell short of the recommended length ({words} words).";

            scores.Add(new AiCriterionScoreResult(
                criterion.CriterionId,
                boundedScore,
                comment));
        }

        var feedback = BuildDeterministicFeedback(request.ResponseText!, request.TargetBandCode, isSpeaking: false);
        return Task.FromResult(new AiGradingResult(
            Success: true,
            FailureReason: null,
            Model: DefaultModel,
            Scores: scores,
            ErrorSpans: feedback.ErrorSpans,
            PriorityFix: feedback.PriorityFix,
            ModelRewrite: feedback.ModelRewrite));
    }

    private static (IReadOnlyList<BusinessObjects.AiErrorSpan> ErrorSpans, string PriorityFix, string ModelRewrite) BuildDeterministicFeedback(
        string text,
        string? targetBandCode,
        bool isSpeaking)
    {
        var words = text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var spans = new List<BusinessObjects.AiErrorSpan>();

        if (words.Length >= 4)
        {
            var snippet = string.Join(' ', words.Take(Math.Min(4, words.Length)));
            if (text.Contains(snippet, StringComparison.Ordinal))
            {
                spans.Add(new BusinessObjects.AiErrorSpan(
                    OriginalSnippet: snippet,
                    CorrectedSnippet: $"{snippet} (refined)",
                    Category: isSpeaking ? "pronunciation" : "vocabulary",
                    ExplanationVi: isSpeaking
                        ? "Nên nhấn trọng âm rõ hơn và nối âm tự nhiên ở cụm mở đầu này."
                        : "Có thể dùng cấu trúc trang trọng và chính xác hơn để tăng điểm tiêu chí từ vựng."));
            }
        }

        if (words.Length >= 8)
        {
            var secondSnippet = string.Join(' ', words.Skip(4).Take(Math.Min(4, words.Length - 4)));
            if (text.Contains(secondSnippet, StringComparison.Ordinal))
            {
                spans.Add(new BusinessObjects.AiErrorSpan(
                    OriginalSnippet: secondSnippet,
                    CorrectedSnippet: $"{secondSnippet}, which",
                    Category: "cohesion",
                    ExplanationVi: "Thêm từ nối hoặc mệnh đề quan hệ để tăng tính mạch lạc giữa các ý."));
            }
        }

        var bandSuffix = string.IsNullOrWhiteSpace(targetBandCode) ? "" : $" to reach {targetBandCode}";
        var priorityFix = isSpeaking
            ? $"Expand your supporting reasons with concrete examples and clearer clause linking{bandSuffix}."
            : $"Strengthen topic sentences with precise academic vocabulary and explicit cohesive devices{bandSuffix}.";

        var firstSentence = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? text.Trim();
        var modelRewrite = isSpeaking
            ? $"Overall, {firstSentence.ToLowerInvariant()}, which clearly illustrates why this topic matters in everyday life."
            : $"Furthermore, {firstSentence.ToLowerInvariant()}, thereby demonstrating a clear and well-supported argument.";

        return (spans, priorityFix, modelRewrite);
    }
}
