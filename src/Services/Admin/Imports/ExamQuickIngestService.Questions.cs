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

    private static QuickIngestParsedQuestionDto ParseSingleBlock(
        QuestionBlock block,
        int questionIndex,
        string? clusterPassage,
        string? clusterTranscript,
        int groupIndex,
        Section? section)
    {
        var issues = new List<QuickIngestIssueDto>();
        var stemLines = new List<string>();
        var options = new List<QuickIngestParsedOptionDto>();
        var explanationLines = new List<string>();
        string? categoryCode = null;
        string? explicitType = null;
        string? requiredKeywords = null;
        string? hint = null;
        int? maxWords = null;
        int? minWords = null;
        int? maxSelections = null;
        int? prepSeconds = null;
        int? responseSeconds = null;
        var acceptedAnswers = new List<string>();

        var trailingAnswerKeys = new List<string>();
        var trailingKeyLineNumber = 0;
        var inExplanation = false;

        for (var i = 0; i < block.Lines.Count; i++)
        {
            var lineObj = block.Lines[i];
            var line = lineObj.Text.Trim();
            var lineNum = lineObj.LineNumber;

            if (string.IsNullOrWhiteSpace(line)) continue;

            var catMatch = CategoryRegex().Match(line);
            if (catMatch.Success)
            {
                categoryCode = catMatch.Groups[1].Value.Trim();
                inExplanation = false;
                continue;
            }

            var typeMatch = QuestionTypeRegex().Match(line);
            if (typeMatch.Success)
            {
                explicitType = typeMatch.Groups[1].Value.Trim().ToLowerInvariant();
                inExplanation = false;
                continue;
            }

            var kwMatch = KeywordsRegex().Match(line);
            if (kwMatch.Success)
            {
                requiredKeywords = kwMatch.Groups[1].Value.Trim();
                inExplanation = false;
                continue;
            }

            var mwMatch = MaxWordsRegex().Match(line);
            if (mwMatch.Success && int.TryParse(mwMatch.Groups[1].Value, out var mw))
            {
                maxWords = mw;
                inExplanation = false;
                continue;
            }

            var minwMatch = MinWordsRegex().Match(line);
            if (minwMatch.Success && int.TryParse(minwMatch.Groups[1].Value, out var minw))
            {
                minWords = minw;
                inExplanation = false;
                continue;
            }

            var msMatch = MaxSelectionsRegex().Match(line);
            if (msMatch.Success && int.TryParse(msMatch.Groups[1].Value, out var ms))
            {
                maxSelections = ms;
                inExplanation = false;
                continue;
            }

            var prepMatch = PrepRegex().Match(line);
            if (prepMatch.Success && int.TryParse(prepMatch.Groups[1].Value, out var pSec))
            {
                prepSeconds = pSec;
                inExplanation = false;
                continue;
            }

            var respMatch = ResponseRegex().Match(line);
            if (respMatch.Success && int.TryParse(respMatch.Groups[1].Value, out var rSec))
            {
                responseSeconds = rSec;
                inExplanation = false;
                continue;
            }

            var hintMatch = HintRegex().Match(line);
            if (hintMatch.Success)
            {
                hint = hintMatch.Groups[1].Value.Trim();
                inExplanation = false;
                continue;
            }

            var ansMatch = AnswerKeyRegex().Match(line);
            if (ansMatch.Success)
            {
                var ansRaw = ansMatch.Groups[1].Value.Trim();
                trailingKeyLineNumber = lineNum;
                inExplanation = false;

                // Check if inline completion answers: "answer 1 | answer 2"
                if (ansRaw.Contains('|') || (ansRaw.Length > 2 && !ansRaw.Contains(',')))
                {
                    var parts = ansRaw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    acceptedAnswers.AddRange(parts);
                }
                else
                {
                    // Single or comma-separated keys: "A, C" or "B"
                    var keys = ansRaw.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var k in keys)
                    {
                        var clean = k.Trim('(', ')', '.', ':').ToUpperInvariant();
                        if (clean.Length == 1 && clean[0] is >= 'A' and <= 'Z')
                        {
                            trailingAnswerKeys.Add(clean);
                        }
                    }
                }
                continue;
            }

            var expMatch = ExplanationRegex().Match(line);
            if (expMatch.Success)
            {
                inExplanation = true;
                var expText = expMatch.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(expText)) explanationLines.Add(expText);
                continue;
            }

            if (inExplanation)
            {
                explanationLines.Add(line);
                continue;
            }

            var hasLeadingAsterisk = line.StartsWith('*') || line.StartsWith("（*") || line.StartsWith("(*");
            var optMatch = OptionLineRegex().Match(line);
            if (optMatch.Success && !line.StartsWith("Question", StringComparison.OrdinalIgnoreCase) && !line.StartsWith("Câu", StringComparison.OrdinalIgnoreCase))
            {
                var label = (optMatch.Groups[1].Success ? optMatch.Groups[1].Value : optMatch.Groups[2].Value).ToUpperInvariant();
                var remainder = optMatch.Groups[3].Value.Trim();

                var isCorrect = false;
                if (hasLeadingAsterisk || line.StartsWith($"*{label}", StringComparison.OrdinalIgnoreCase) || line.StartsWith($"*({label})", StringComparison.OrdinalIgnoreCase))
                {
                    isCorrect = true;
                }

                if (remainder.StartsWith('*'))
                {
                    isCorrect = true;
                    remainder = remainder.TrimStart('*', ' ');
                }

                if (remainder.EndsWith('*'))
                {
                    isCorrect = true;
                    remainder = remainder.TrimEnd('*', ' ');
                }

                string? rationale = null;
                var commentIdx = remainder.IndexOf("//", StringComparison.Ordinal);
                if (commentIdx >= 0)
                {
                    rationale = remainder[(commentIdx + 2)..].Trim();
                    remainder = remainder[..commentIdx].Trim();
                }

                options.Add(new QuickIngestParsedOptionDto(label, remainder, isCorrect, rationale));
                continue;
            }

            if (options.Count == 0)
            {
                var pMatch = PromptHeaderRegex().Match(line);
                if (pMatch.Success)
                {
                    var stemPart = pMatch.Groups["stem"].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(stemPart)) stemLines.Add(stemPart);
                    continue;
                }

                var qMatch = QuestionHeaderRegex().Match(line);
                if (qMatch.Success)
                {
                    var stemPart = qMatch.Groups["stem"].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(stemPart)) stemLines.Add(stemPart);
                }
                else
                {
                    stemLines.Add(line);
                }
            }
            else
            {
                var last = options[^1];
                var combinedText = last.Text + " " + line;
                options[^1] = new QuickIngestParsedOptionDto(last.Label, combinedText, last.IsCorrect, last.Rationale);
            }
        }

        // Apply trailing answer keys to options
        if (trailingAnswerKeys.Count > 0)
        {
            for (var o = 0; o < options.Count; o++)
            {
                var opt = options[o];
                var matched = trailingAnswerKeys.Contains(opt.Label, StringComparer.OrdinalIgnoreCase);
                if (matched)
                {
                    options[o] = new QuickIngestParsedOptionDto(opt.Label, opt.Text, true, opt.Rationale);
                }
                else if (options.Count(x => x.IsCorrect) == 0 && trailingAnswerKeys.Count == 1)
                {
                    options[o] = new QuickIngestParsedOptionDto(opt.Label, opt.Text, false, opt.Rationale);
                }
            }

            foreach (var key in trailingAnswerKeys)
            {
                if (!options.Any(o => string.Equals(o.Label, key, StringComparison.OrdinalIgnoreCase)))
                {
                    issues.Add(new QuickIngestIssueDto(trailingKeyLineNumber, "error", $"Answer key '{key}' does not match any parsed option (available: {string.Join(", ", options.Select(x => x.Label))}).", questionIndex));
                }
            }
        }

        var stem = string.Join(" ", stemLines).Trim();
        var explanation = explanationLines.Count > 0 ? string.Join("\n", explanationLines).Trim() : null;

        // Determine question type
        var questionType = explicitType ?? DetermineQuestionType(options, trailingAnswerKeys, acceptedAnswers, requiredKeywords, maxSelections, section);

        var acceptedJson = acceptedAnswers.Count > 0 ? JsonSerializer.Serialize(acceptedAnswers) : null;

        // Validation based on QuestionType
        if (string.IsNullOrWhiteSpace(stem))
        {
            issues.Add(new QuickIngestIssueDto(block.StartLineNumber, "error", "Question stem is empty.", questionIndex));
        }

        if (questionType == "free_response")
        {
            // Free response: no options required
        }
        else if (questionType == "inline_completion")
        {
            if (acceptedAnswers.Count == 0 && string.IsNullOrWhiteSpace(acceptedJson))
            {
                issues.Add(new QuickIngestIssueDto(block.StartLineNumber, "error", "Inline completion requires accepted answer variants (ANSWER: option1 | option2).", questionIndex));
            }
        }
        else if (questionType == "multi_select")
        {
            if (options.Count < 2)
            {
                issues.Add(new QuickIngestIssueDto(block.StartLineNumber, "error", $"Multi-select question must have at least 2 options (found {options.Count}).", questionIndex));
            }
            var correctCount = options.Count(o => o.IsCorrect);
            if (correctCount == 0)
            {
                issues.Add(new QuickIngestIssueDto(block.StartLineNumber, "error", "Multi-select question has no correct options marked (*A. or ANSWER: A, C).", questionIndex));
            }
            if (!maxSelections.HasValue && correctCount > 0)
            {
                maxSelections = correctCount;
            }
        }
        else // mcq or mcq_3
        {
            if (options.Count < 2)
            {
                issues.Add(new QuickIngestIssueDto(block.StartLineNumber, "error", $"Question must have at least 2 options (found {options.Count}).", questionIndex));
            }

            var correctCount = options.Count(o => o.IsCorrect);
            if (options.Count >= 2 && correctCount == 0)
            {
                issues.Add(new QuickIngestIssueDto(block.StartLineNumber, "error", "Question has no correct answer marked (*A. or ANSWER: A).", questionIndex));
            }
            else if (correctCount > 1)
            {
                issues.Add(new QuickIngestIssueDto(block.StartLineNumber, "error", $"Question has multiple correct answers marked ({correctCount} marked correct). Exactly one is required.", questionIndex));
            }
        }

        var duplicateLabels = options.GroupBy(o => o.Label).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicateLabels.Count > 0)
        {
            issues.Add(new QuickIngestIssueDto(block.StartLineNumber, "error", $"Duplicate option label(s) found: {string.Join(", ", duplicateLabels)}.", questionIndex));
        }

        var isValid = issues.All(i => i.Severity != "error");

        return new QuickIngestParsedQuestionDto(
            Index: questionIndex,
            StartLineNumber: block.StartLineNumber,
            EndLineNumber: block.EndLineNumber,
            Stem: stem,
            DifficultyLevel: 3,
            Explanation: explanation,
            CategoryCode: categoryCode,
            Options: options,
            Issues: issues,
            IsValid: isValid,
            QuestionType: questionType,
            Passage: clusterPassage,
            Transcript: clusterTranscript,
            MaxWords: maxWords,
            MaxSelections: maxSelections,
            AcceptedAnswersJson: acceptedJson,
            RequiredKeywords: requiredKeywords,
            Hint: hint,
            GroupNumber: groupIndex,
            PrepSeconds: prepSeconds,
            ResponseSeconds: responseSeconds,
            MinWords: minWords);
    }

    private static string DetermineQuestionType(
        List<QuickIngestParsedOptionDto> options,
        List<string> trailingKeys,
        List<string> acceptedAnswers,
        string? requiredKeywords,
        int? maxSelections,
        Section? section)
    {
        if (!string.IsNullOrWhiteSpace(requiredKeywords)) return "free_response";
        if (acceptedAnswers.Count > 0) return "inline_completion";
        if (trailingKeys.Count > 1 || maxSelections.HasValue) return "multi_select";

        if (section != null)
        {
            if (section.OptionCount == 3) return "mcq_3";
            if (!string.IsNullOrWhiteSpace(section.AllowedComponentTypesJson))
            {
                if (section.AllowedComponentTypesJson.Contains("inline_completion") && options.Count == 0) return "inline_completion";
                if (section.AllowedComponentTypesJson.Contains("free_response") && options.Count == 0) return "free_response";
            }
        }

        return "mcq";
    }
}
