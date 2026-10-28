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

    private static bool IsRapidAnswerKeyBatch(string[] rawLines)
    {
        var nonBlankLines = rawLines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (nonBlankLines.Count == 0) return false;

        var matchesKeyCount = 0;
        foreach (var line in nonBlankLines)
        {
            var trimmed = line.Trim();
            if (RapidAnswerKeyLineRegex().IsMatch(trimmed))
            {
                matchesKeyCount++;
            }
            else if (TranscriptRegex().IsMatch(trimmed) || ExplanationRegex().IsMatch(trimmed))
            {
                // Supplementary lines in rapid mode
            }
            else
            {
                return false;
            }
        }

        return matchesKeyCount > 0 && matchesKeyCount >= nonBlankLines.Count / 2;
    }

    private static QuickIngestParseResultDto ParseRapidAnswerKeys(string[] rawLines, Section? section)
    {
        var parsedQuestions = new List<QuickIngestParsedQuestionDto>();
        var globalIssues = new List<QuickIngestIssueDto>();

        var optCount = section?.OptionCount ?? 4;
        var qIdx = 1;

        QuickIngestParsedQuestionDto? currentQ = null;

        for (var i = 0; i < rawLines.Length; i++)
        {
            var lineNum = i + 1;
            var line = rawLines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var rapidMatch = RapidAnswerKeyLineRegex().Match(line);
            if (rapidMatch.Success)
            {
                if (currentQ != null)
                {
                    parsedQuestions.Add(currentQ);
                }

                var qNumStr = rapidMatch.Groups[1].Success ? rapidMatch.Groups[1].Value : rapidMatch.Groups[2].Value;
                var keyLetter = rapidMatch.Groups[3].Value.ToUpperInvariant();
                var parsedNum = int.TryParse(qNumStr, out var n) ? n : qIdx;

                var options = new List<QuickIngestParsedOptionDto>();
                for (var oi = 0; oi < Math.Max(optCount, 2); oi++)
                {
                    var label = ((char)('A' + oi)).ToString();
                    options.Add(new QuickIngestParsedOptionDto(label, $"({label})", label == keyLetter));
                }

                var issues = new List<QuickIngestIssueDto>();
                var maxLetter = (char)('A' + optCount - 1);
                if (keyLetter.Length == 1 && keyLetter[0] > maxLetter)
                {
                    issues.Add(new QuickIngestIssueDto(lineNum, "error", $"Answer key '{keyLetter}' exceeds section option count ({optCount}, max option is {maxLetter}).", parsedNum));
                }

                currentQ = new QuickIngestParsedQuestionDto(
                    Index: parsedNum,
                    StartLineNumber: lineNum,
                    EndLineNumber: lineNum,
                    Stem: $"Question {parsedNum}",
                    DifficultyLevel: 3,
                    Explanation: null,
                    CategoryCode: null,
                    Options: options,
                    Issues: issues,
                    IsValid: issues.Count == 0,
                    QuestionType: optCount == 3 ? "mcq_3" : "mcq");

                qIdx = parsedNum + 1;
                continue;
            }

            var transMatch = TranscriptRegex().Match(line);
            if (transMatch.Success && currentQ != null)
            {
                var transText = transMatch.Groups[1].Value.Trim();
                currentQ = currentQ with
                {
                    Transcript = transText,
                    Stem = !string.IsNullOrWhiteSpace(transText) ? transText : currentQ.Stem
                };
                continue;
            }

            var expMatch = ExplanationRegex().Match(line);
            if (expMatch.Success && currentQ != null)
            {
                var expText = expMatch.Groups[1].Value.Trim();
                currentQ = currentQ with { Explanation = expText };
                continue;
            }
        }

        if (currentQ != null)
        {
            parsedQuestions.Add(currentQ);
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
