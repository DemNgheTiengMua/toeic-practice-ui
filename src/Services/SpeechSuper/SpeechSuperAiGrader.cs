using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Services;

/// <summary>
/// Real AI grader that evaluates spoken responses via SpeechSuper's pronunciation &amp; fluency API,
/// falling back to the standard grader for written responses.
/// </summary>
/// <remarks>
/// #152: the provider's own scale decides the ratio, and the certificate decides the marks. Every
/// criterion ratio is unit-free (<c>[0..1]</c>) before it is multiplied by the rubric's
/// <see cref="GradingCriterionDetail.MaxScore"/>, so no criterion is divided by an IELTS maximum and
/// no missing measurement is replaced by a plausible-looking number.
/// </remarks>
public sealed class SpeechSuperAiGrader : IAiGrader
{
    /// <summary>
    /// #152: one retry, then the grading is Failed. A second provider call costs quota, so it only
    /// happens when the first answer carried nothing that could be graded.
    /// </summary>
    private const int AttemptsAllowed = 2;

    private readonly ISpeechSuperClient _speechSuperClient;
    private readonly IFileStore _fileStore;
    private readonly IAiGrader _writtenGrader;

    public SpeechSuperAiGrader(
        ISpeechSuperClient speechSuperClient,
        IFileStore fileStore,
        IAiGrader? writtenGrader = null)
    {
        _speechSuperClient = speechSuperClient;
        _fileStore = fileStore;
        _writtenGrader = writtenGrader ?? new FakeAiGrader();
    }

    public async Task<AiGradingResult> GradeAsync(AiGradingRequest request, CancellationToken cancellationToken = default)
    {
        // For written responses (no AudioPath), delegate to Claude AI grader
        if (string.IsNullOrEmpty(request.AudioPath))
        {
            return await _writtenGrader.GradeAsync(request, cancellationToken);
        }

        // Retrieve audio stream from file store
        using var audioInfo = await _fileStore.GetAudioAsync(request.AudioPath, cancellationToken);
        if (audioInfo is null)
        {
            return new AiGradingResult(
                Success: false,
                FailureReason: $"Audio file '{request.AudioPath}' not found in storage.",
                Model: "speechsuper",
                Scores: Array.Empty<AiCriterionScoreResult>());
        }

        var ext = Path.GetExtension(audioInfo.FileName).TrimStart('.');
        var prompt = SpeakingTask.QuestionPrompt(
            request.QuestionStem, request.ReferenceText, request.ReferenceDescribesImage);
        var refText = SpeakingTask.PronunciationReference(request.ReferenceText, request.ReferenceDescribesImage);

        SpeechSuperAssessmentResult? assessment = null;
        string? lastReason = null;

        for (var attempt = 1; attempt <= AttemptsAllowed; attempt++)
        {
            if (audioInfo.Stream.CanSeek) audioInfo.Stream.Position = 0;

            assessment = await _speechSuperClient.AssessAsync(
                audioInfo.Stream,
                ext,
                questionPrompt: prompt,
                refText: refText,
                cancellationToken: cancellationToken);

            if (!assessment.Success)
            {
                lastReason = assessment.ErrorMessage ?? "SpeechSuper assessment failed.";
                continue;
            }

            var mapped = MapCriteria(request, assessment, out lastReason);
            if (mapped.Count > 0)
            {
                var acousticSummary = BuildAcousticSummary(assessment);
                var linguisticGuide = string.IsNullOrWhiteSpace(request.Guide)
                    ? acousticSummary
                    : $"{request.Guide}\n\n{acousticSummary}";

                AiGradingResult? linguistic = null;
                if (!string.IsNullOrWhiteSpace(assessment.Transcription))
                {
                    var transcriptRequest = request with
                    {
                        AudioPath = null,
                        ResponseText = assessment.Transcription,
                        Guide = linguisticGuide
                    };
                    linguistic = await _writtenGrader.GradeAsync(transcriptRequest, cancellationToken);
                }

                var finalScores = MergeScores(mapped, linguistic);
                return new AiGradingResult(
                    Success: true,
                    FailureReason: null,
                    Model: "speechsuper-speak.eval.pro",
                    Scores: finalScores,
                    Transcript: assessment.Transcription,
                    ErrorSpans: linguistic?.Success == true ? linguistic.ErrorSpans : null,
                    PriorityFix: linguistic?.Success == true ? linguistic.PriorityFix : null,
                    ModelRewrite: linguistic?.Success == true ? linguistic.ModelRewrite : null);
            }
        }

        // Nothing gradable after the retry: no score at all, and the reason says why. The learner gets
        // the "grade again" control because the AiGradings row is Failed, not Done with zeros.
        return new AiGradingResult(
            Success: false,
            FailureReason: lastReason ?? "SpeechSuper returned no usable score.",
            Model: "speechsuper-speak.eval.pro",
            Scores: Array.Empty<AiCriterionScoreResult>(),
            Transcript: assessment?.Transcription);
    }

    /// <summary>
    /// One criterion per provider measurement, and only where a measurement exists. A criterion with
    /// no figure is dropped rather than scored zero or invented, and relevance is dropped whenever the
    /// question declares no reference for the answer to be relevant to.
    /// </summary>
    private static List<AiCriterionScoreResult> MapCriteria(
        AiGradingRequest request,
        SpeechSuperAssessmentResult assessment,
        out string? reason)
    {
        var scores = new List<AiCriterionScoreResult>();
        var missing = new List<string>();
        var outOfScale = new List<string>();

        if (string.IsNullOrWhiteSpace(assessment.Transcription))
        {
            reason = "SpeechSuper recognised no speech in the recording, so there is nothing to grade.";
            return scores;
        }

        foreach (var criterion in request.Criteria)
        {
            var code = criterion.Code.ToUpperInvariant();
            var name = criterion.Name.ToLowerInvariant();

            var (measurement, nativeMax) = Classify(code, name, assessment, request);
            if (measurement is null)
            {
                missing.Add(criterion.Name);
                continue;
            }

            if (measurement < 0m || measurement > nativeMax)
            {
                // Outside the scale the provider documents for that field: a scale mismatch, not a top
                // mark, so it is not usable - and it is named separately, because retrying a scale
                // mismatch burns quota and cannot change the answer.
                outOfScale.Add(string.Create(CultureInfo.InvariantCulture, $"{criterion.Name} ({measurement.Value:0.##} on a 0-{nativeMax:0.##} scale)"));
                continue;
            }

            var ratio = measurement.Value / nativeMax;
            var score = Math.Round(criterion.MaxScore * ratio, 1, MidpointRounding.AwayFromZero);
            scores.Add(new AiCriterionScoreResult(
                criterion.CriterionId,
                Math.Clamp(score, 0m, criterion.MaxScore),
                Comment(ratio, measurement.Value, nativeMax)));
        }

        // #152 finding 7: an out-of-scale answer and a transient fault look the same to the learner
        // (Failed, Retry) unless the reason says which it was. A scale mismatch is not retried blindly:
        // it says so, and says it will keep saying so.
        reason = scores.Count > 0
            ? null
            : outOfScale.Count > 0
                ? "SpeechSuper answered outside the scale it documents for " + string.Join(", ", outOfScale)
                  + ". Retrying will not change this until the provider or the certificate's rubric is fixed."
                : $"SpeechSuper returned no score for {string.Join(", ", missing)}.";
        return scores;
    }

    /// <summary>
    /// Which provider field answers this criterion, and the scale that field is reported on. Read off a
    /// live <c>speak.eval.pro</c> 200: every result figure — overall, pronunciation, fluency, grammar,
    /// vocabulary, relevance, topic_development — is a percentage. There is no 0–9 block on this
    /// endpoint, which is why the whole mapping is one scale now instead of a guessed band scale plus
    /// a percentage. Null means the provider did not measure it for this response.
    /// </summary>
    private static (decimal? Value, decimal NativeMax) Classify(
        string code, string name, SpeechSuperAssessmentResult a, AiGradingRequest request)
    {
        const decimal Percent = 100m;

        // The code is the certificate's own vocabulary (PRON, INT, GRAM, FLU, LR, FC, GRA, VOCAB,
        // REL, DEV); the name is only a fallback when the code says nothing. A name alone is
        // ambiguous - "Vocabulary & Grammar" would otherwise land on grammar.
        var isPronunciation = code.Contains("PRON") || (code.Length == 0 && name.Contains("pronunciation"));
        var isFluency = code.Contains("INT") || code.Contains("FLU") || code == "FC"
            || (code.Length == 0 && (name.Contains("intonation") || name.Contains("fluency") || name.Contains("stress") || name.Contains("coherence")));
        var isGrammar = code.Contains("GRAM") || code == "GRA"
            || (code.Length == 0 && name.Contains("grammar"));
        var isVocabulary = code.Contains("LR") || code.Contains("VOCAB")
            || (code.Length == 0 && (name.Contains("vocabulary") || name.Contains("lexical")));
        var isRelevance = code.Contains("REL") || code == "DEV"
            || (code.Length == 0 && (name.Contains("relevance") || name.Contains("content") || name.Contains("completeness") || name.Contains("development")));

        if (isPronunciation) return (a.PronunciationScore, Percent);
        if (isFluency) return (a.FluencyScore, Percent);
        if (isGrammar) return (a.GrammarScore, Percent);
        if (isVocabulary) return (a.VocabularyScore, Percent);

        if (isRelevance)
        {
            // No reference, no relevance: the answer has nothing to be relevant to, so the criterion
            // is left out of the result instead of scored.
            return SpeakingTask.CanScoreRelevance(request.ReferenceText)
                ? (a.RelevanceScore, Percent)
                : (null, Percent);
        }

        return (a.OverallScore, Percent);
    }

    private static string Comment(decimal ratio, decimal value, decimal nativeMax) =>
        string.Create(CultureInfo.InvariantCulture,
            $"SpeechSuper measured {value:0.##}/{nativeMax:0.##} ({ratio * 100m:0}%).");

    private static string BuildAcousticSummary(SpeechSuperAssessmentResult a)
    {
        var parts = new List<string>();
        if (a.PronunciationScore.HasValue) parts.Add($"Pronunciation: {a.PronunciationScore.Value:0.#}/100");
        if (a.FluencyScore.HasValue) parts.Add($"Fluency: {a.FluencyScore.Value:0.#}/100");
        if (a.RelevanceScore.HasValue) parts.Add($"Completeness: {a.RelevanceScore.Value:0.#}/100");
        if (a.OverallScore.HasValue) parts.Add($"Overall Acoustic: {a.OverallScore.Value:0.#}/100");
        return parts.Count == 0
            ? "SpeechSuper Acoustic Metrics: unavailable."
            : "SpeechSuper Acoustic Metrics: " + string.Join(", ", parts);
    }

    private static IReadOnlyList<AiCriterionScoreResult> MergeScores(
        IReadOnlyList<AiCriterionScoreResult> acousticScores,
        AiGradingResult? linguistic)
    {
        if (linguistic is not { Success: true, Scores.Count: > 0 })
            return acousticScores;

        var byId = linguistic.Scores.ToDictionary(s => s.CriterionId);
        return acousticScores.Select(a =>
        {
            if (byId.TryGetValue(a.CriterionId, out var ling) && !string.IsNullOrWhiteSpace(ling.Comment))
            {
                return a with { Comment = $"{a.Comment} {ling.Comment}".Trim() };
            }
            return a;
        }).ToList();
    }
}
