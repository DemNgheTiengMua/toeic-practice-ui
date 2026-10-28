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

    public static QuickIngestParseResultDto ValidateAgainstSectionBlueprint(QuickIngestParseResultDto result, Section section)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(section);

        var updatedQuestions = new List<QuickIngestParsedQuestionDto>();
        HashSet<string>? allowedTypes = null;

        if (!string.IsNullOrWhiteSpace(section.AllowedComponentTypesJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(section.AllowedComponentTypesJson);
                if (parsed != null && parsed.Count > 0)
                {
                    allowedTypes = new HashSet<string>(parsed, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch
            {
                // Fallback if parsing fails
            }
        }

        foreach (var q in result.Questions)
        {
            var issues = new List<QuickIngestIssueDto>(q.Issues);
            var qType = q.QuestionType.ToLowerInvariant();

            // 1. Allowed Component Types
            if (allowedTypes != null && allowedTypes.Count > 0)
            {
                var isAllowed = allowedTypes.Contains(qType);
                if (!isAllowed)
                {
                    if (qType == "mcq" && allowedTypes.Contains("mcq_3") && section.OptionCount == 3)
                    {
                        isAllowed = true;
                    }
                    else if (qType == "mcq_3" && allowedTypes.Contains("mcq"))
                    {
                        isAllowed = true;
                    }
                }

                if (!isAllowed)
                {
                    issues.Add(new QuickIngestIssueDto(
                        q.StartLineNumber,
                        "error",
                        $"Question component type '{q.QuestionType}' is not allowed in section '{section.Name}' (Allowed: {string.Join(", ", allowedTypes)}).",
                        q.Index));
                }
            }

            // 2. OptionCount
            if (q.Options.Count > 0)
            {
                var maxAllowedChar = (char)('A' + section.OptionCount - 1);
                foreach (var opt in q.Options)
                {
                    if (opt.Label.Length == 1 && opt.Label[0] > maxAllowedChar)
                    {
                        issues.Add(new QuickIngestIssueDto(
                            q.StartLineNumber,
                            "error",
                            $"Option '{opt.Label}' exceeds section allowed OptionCount ({section.OptionCount}, max allowed label is '{maxAllowedChar}').",
                            q.Index));
                    }
                }

                if ((qType == "mcq" || qType == "mcq_3") && q.Options.Count > section.OptionCount)
                {
                    issues.Add(new QuickIngestIssueDto(
                        q.StartLineNumber,
                        "error",
                        $"Question has {q.Options.Count} options, but section allows maximum of {section.OptionCount} options.",
                        q.Index));
                }
            }

            // 3. RequiredKeywordCount
            if (section.RequiredKeywordCount > 0)
            {
                if (string.IsNullOrWhiteSpace(q.RequiredKeywords))
                {
                    issues.Add(new QuickIngestIssueDto(
                        q.StartLineNumber,
                        "error",
                        $"Section requires {section.RequiredKeywordCount} keyword(s), but none were provided (use 'KEYWORDS: word1, word2').",
                        q.Index));
                }
                else
                {
                    var actualCount = q.RequiredKeywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
                    if (actualCount != section.RequiredKeywordCount)
                    {
                        issues.Add(new QuickIngestIssueDto(
                            q.StartLineNumber,
                            "error",
                            $"Section requires {section.RequiredKeywordCount} keyword(s), but found {actualCount} keyword(s).",
                            q.Index));
                    }
                }
            }

            // 4. RequirePassage
            if (section.RequirePassage && string.IsNullOrWhiteSpace(q.Passage))
            {
                issues.Add(new QuickIngestIssueDto(
                    q.StartLineNumber,
                    "error",
                    $"Section requires a reading passage, but no passage was provided.",
                    q.Index));
            }

            var isValid = issues.All(i => i.Severity != "error");
            updatedQuestions.Add(q with { Issues = issues, IsValid = isValid });
        }

        var totalErrors = updatedQuestions.Sum(q => q.Issues.Count(i => i.Severity == "error")) + result.GlobalIssues.Count(i => i.Severity == "error");

        return result with
        {
            Success = updatedQuestions.Count > 0 && totalErrors == 0,
            TotalQuestions = updatedQuestions.Count,
            ValidQuestions = updatedQuestions.Count(q => q.IsValid),
            ErrorCount = totalErrors,
            Questions = updatedQuestions
        };
    }
}
