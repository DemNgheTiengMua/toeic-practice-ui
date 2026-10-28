using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class ExamQuickIngestService
{

    private static QuickIngestParseResultDto ParseJson(string jsonText)
    {
        using var doc = JsonDocument.Parse(jsonText);
        var root = doc.RootElement;

        JsonElement questionsElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            questionsElement = root;
        }
        else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("questions", out var qProp) && qProp.ValueKind == JsonValueKind.Array)
        {
            questionsElement = qProp;
        }
        else
        {
            return new QuickIngestParseResultDto(
                Success: false,
                TotalQuestions: 0,
                ValidQuestions: 0,
                ErrorCount: 1,
                Questions: [],
                GlobalIssues: [new QuickIngestIssueDto(1, "error", "JSON must be an array of questions or an object with a 'questions' array.")]);
        }

        var parsedQuestions = new List<QuickIngestParsedQuestionDto>();
        var globalIssues = new List<QuickIngestIssueDto>();
        var qIdx = 1;

        foreach (var item in questionsElement.EnumerateArray())
        {
            var issues = new List<QuickIngestIssueDto>();

            var stem = item.TryGetProperty("stem", out var sProp) ? sProp.GetString() ?? string.Empty : string.Empty;
            var difficulty = item.TryGetProperty("difficultyLevel", out var dProp) && dProp.TryGetInt32(out var diff) ? diff : 3;
            var explanation = item.TryGetProperty("explanation", out var eProp) ? eProp.GetString() : null;
            var categoryCode = item.TryGetProperty("categoryCode", out var cProp) ? cProp.GetString() : null;
            var questionType = item.TryGetProperty("questionType", out var qtProp) ? qtProp.GetString()?.ToLowerInvariant() ?? "mcq" : "mcq";
            var passage = item.TryGetProperty("passage", out var pasProp) ? pasProp.GetString() : null;
            var transcript = item.TryGetProperty("transcript", out var trProp) ? trProp.GetString() : null;
            var requiredKeywords = item.TryGetProperty("requiredKeywords", out var rkProp) ? rkProp.GetString() : null;
            var hint = item.TryGetProperty("hint", out var hProp) ? hProp.GetString() : null;
            var maxWords = item.TryGetProperty("maxWords", out var mwProp) && mwProp.TryGetInt32(out var mwVal) ? (int?)mwVal : null;
            var minWords = item.TryGetProperty("minWords", out var minwProp) && minwProp.TryGetInt32(out var minwVal) ? (int?)minwVal : null;
            var maxSelections = item.TryGetProperty("maxSelections", out var msProp) && msProp.TryGetInt32(out var msVal) ? (int?)msVal : null;
            var prepSec = item.TryGetProperty("prepSeconds", out var psProp) && psProp.TryGetInt32(out var psVal) ? (int?)psVal : null;
            var respSec = item.TryGetProperty("responseSeconds", out var rsProp) && rsProp.TryGetInt32(out var rsVal) ? (int?)rsVal : null;

            string? acceptedJson = null;
            if (item.TryGetProperty("acceptedAnswers", out var aaProp))
            {
                if (aaProp.ValueKind == JsonValueKind.Array)
                {
                    var aaList = aaProp.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                    acceptedJson = JsonSerializer.Serialize(aaList);
                }
                else if (aaProp.ValueKind == JsonValueKind.String)
                {
                    acceptedJson = aaProp.GetString();
                }
            }

            var options = new List<QuickIngestParsedOptionDto>();
            if (item.TryGetProperty("options", out var optProp) && optProp.ValueKind == JsonValueKind.Array)
            {
                var optIdx = 0;
                foreach (var opt in optProp.EnumerateArray())
                {
                    var label = opt.TryGetProperty("label", out var lProp) ? lProp.GetString() ?? ((char)('A' + optIdx)).ToString() : ((char)('A' + optIdx)).ToString();
                    var text = opt.TryGetProperty("text", out var tProp) ? tProp.GetString() ?? string.Empty : string.Empty;
                    var isCorrect = opt.TryGetProperty("isCorrect", out var icProp) && icProp.GetBoolean();
                    var rationale = opt.TryGetProperty("rationale", out var rProp) ? rProp.GetString() : null;

                    options.Add(new QuickIngestParsedOptionDto(label, text, isCorrect, rationale));
                    optIdx++;
                }
            }

            if (string.IsNullOrWhiteSpace(stem))
            {
                issues.Add(new QuickIngestIssueDto(qIdx, "error", "Question stem is empty.", qIdx));
            }

            if (questionType == "free_response")
            {
                // Free response: options not required
            }
            else if (questionType == "inline_completion")
            {
                if (string.IsNullOrWhiteSpace(acceptedJson))
                {
                    issues.Add(new QuickIngestIssueDto(qIdx, "error", "Inline completion requires 'acceptedAnswers' array or string.", qIdx));
                }
            }
            else if (questionType == "multi_select")
            {
                if (options.Count < 2)
                {
                    issues.Add(new QuickIngestIssueDto(qIdx, "error", $"Multi-select question must have at least 2 options (found {options.Count}).", qIdx));
                }
                var correctCount = options.Count(o => o.IsCorrect);
                if (correctCount == 0)
                {
                    issues.Add(new QuickIngestIssueDto(qIdx, "error", "Multi-select question has no correct answer marked (isCorrect: true).", qIdx));
                }
            }
            else // mcq or mcq_3
            {
                if (options.Count < 2)
                {
                    issues.Add(new QuickIngestIssueDto(qIdx, "error", $"Question must have at least 2 options (found {options.Count}).", qIdx));
                }

                var correctCount = options.Count(o => o.IsCorrect);
                if (options.Count >= 2 && correctCount == 0)
                {
                    issues.Add(new QuickIngestIssueDto(qIdx, "error", "Question has no correct answer marked (isCorrect: true).", qIdx));
                }
                else if (correctCount > 1)
                {
                    issues.Add(new QuickIngestIssueDto(qIdx, "error", $"Question has multiple correct answers marked ({correctCount} marked correct). Exactly one is required.", qIdx));
                }
            }

            var isValid = issues.All(i => i.Severity != "error");

            parsedQuestions.Add(new QuickIngestParsedQuestionDto(
                Index: qIdx,
                StartLineNumber: qIdx,
                EndLineNumber: qIdx,
                Stem: stem,
                DifficultyLevel: difficulty,
                Explanation: explanation,
                CategoryCode: categoryCode,
                Options: options,
                Issues: issues,
                IsValid: isValid,
                QuestionType: questionType,
                Passage: passage,
                Transcript: transcript,
                MaxWords: maxWords,
                MinWords: minWords,
                MaxSelections: maxSelections,
                AcceptedAnswersJson: acceptedJson,
                RequiredKeywords: requiredKeywords,
                Hint: hint,
                PrepSeconds: prepSec,
                ResponseSeconds: respSec));

            qIdx++;
        }

        var totalErrors = parsedQuestions.Sum(q => q.Issues.Count(i => i.Severity == "error"));

        return new QuickIngestParseResultDto(
            Success: parsedQuestions.Count > 0 && totalErrors == 0,
            TotalQuestions: parsedQuestions.Count,
            ValidQuestions: parsedQuestions.Count(q => q.IsValid),
            ErrorCount: totalErrors,
            Questions: parsedQuestions,
            GlobalIssues: globalIssues);
    }
}
