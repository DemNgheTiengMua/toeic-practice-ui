using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Services;

/// <summary>
/// Real AI grader that evaluates spoken responses via SpeechSuper's pronunciation & fluency API,
/// falling back to the standard grader for written responses.
/// </summary>
public sealed class SpeechSuperAiGrader : IAiGrader
{
    private readonly ISpeechSuperClient _speechSuperClient;
    private readonly IFileStore _fileStore;
    private readonly IAiGrader _writtenGrader;

    public SpeechSuperAiGrader(
        ISpeechSuperClient speechSuperClient,
        IFileStore fileStore,
        ClaudeAiGrader? writtenGrader = null)
    {
        _speechSuperClient = speechSuperClient;
        _fileStore = fileStore;
        _writtenGrader = (IAiGrader?)writtenGrader ?? new FakeAiGrader();
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
        var assessment = await _speechSuperClient.AssessAsync(
            audioInfo.Stream,
            ext,
            questionPrompt: request.QuestionStem,
            refText: request.QuestionStem,
            cancellationToken: cancellationToken);

        if (!assessment.Success)
        {
            return new AiGradingResult(
                Success: false,
                FailureReason: assessment.ErrorMessage ?? "SpeechSuper assessment failed.",
                Model: "speechsuper",
                Scores: Array.Empty<AiCriterionScoreResult>());
        }

        // Map criteria scores
        var scores = new List<AiCriterionScoreResult>();
        foreach (var criterion in request.Criteria)
        {
            var code = criterion.Code.ToUpperInvariant();
            var name = criterion.Name.ToLowerInvariant();
            decimal ratio;
            string comment;

            if (code.Contains("PRON") || name.Contains("pronunciation"))
            {
                var pron = assessment.PronunciationScore ?? assessment.OverallScore ?? 6.0m;
                ratio = Math.Clamp(pron / 9.0m, 0m, 1m);
                comment = $"SpeechSuper assessed pronunciation at {pron:F1}/9.0.";
            }
            else if (code.Contains("INT") || name.Contains("intonation") || name.Contains("fluency") || name.Contains("stress"))
            {
                var fluency = assessment.FluencyScore ?? assessment.OverallScore ?? 6.0m;
                ratio = Math.Clamp(fluency / 9.0m, 0m, 1m);
                comment = $"SpeechSuper assessed fluency and coherence at {fluency:F1}/9.0.";
            }
            else if (code.Contains("GRAM") || name.Contains("grammar"))
            {
                var grammar = assessment.GrammarScore ?? 6.0m;
                ratio = Math.Clamp(grammar / 9.0m, 0m, 1m);
                comment = $"SpeechSuper evaluated grammar at {grammar:F1}/9.0.";
            }
            else if (code.Contains("LR") || name.Contains("vocabulary") || name.Contains("lexical"))
            {
                var vocab = assessment.VocabularyScore ?? 6.0m;
                ratio = Math.Clamp(vocab / 9.0m, 0m, 1m);
                comment = $"SpeechSuper assessed lexical resource at {vocab:F1}/9.0.";
            }
            else if (code.Contains("REL") || name.Contains("relevance") || name.Contains("content"))
            {
                var rel = assessment.RelevanceScore ?? 80m;
                ratio = Math.Clamp(rel / 100m, 0m, 1m);
                comment = $"Response relevance scored at {rel:F0}%.";
            }
            else
            {
                var overall = assessment.OverallScore ?? 6.0m;
                ratio = Math.Clamp(overall / 9.0m, 0m, 1m);
                comment = $"Overall speaking proficiency evaluated at {overall:F1}/9.0.";
            }

            var score = Math.Round(criterion.MaxScore * ratio, 1, MidpointRounding.AwayFromZero);
            scores.Add(new AiCriterionScoreResult(criterion.CriterionId, Math.Clamp(score, 0m, criterion.MaxScore), comment));
        }

        return new AiGradingResult(
            Success: true,
            FailureReason: null,
            Model: "speechsuper-speak.eval.pro",
            Scores: scores,
            Transcript: assessment.Transcription);
    }
}
