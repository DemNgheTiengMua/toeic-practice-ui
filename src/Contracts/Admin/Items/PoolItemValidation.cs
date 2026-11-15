using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Contracts;

internal static class PoolItemValidation
{
    public static IEnumerable<ValidationResult> ValidateItemFields(
        string? stem,
        string? optionA,
        string? optionB,
        string? optionC,
        string? optionD,
        string? keyLabel,
        string? explanation,
        int? evidenceStart,
        int? evidenceEnd,
        string? evidenceUnit)
    {
        if (string.IsNullOrWhiteSpace(stem))
        {
            yield return new ValidationResult("Question stem is required.", ["Stem"]);
        }

        if (string.IsNullOrWhiteSpace(explanation))
        {
            yield return new ValidationResult("Explanation is required.", ["Explanation"]);
        }

        var options = new[]
        {
            ("OptionA", optionA),
            ("OptionB", optionB),
            ("OptionC", optionC),
            ("OptionD", optionD),
        };
        var filledCount = options.Count(o => !string.IsNullOrWhiteSpace(o.Item2));
        var hasKey = !string.IsNullOrWhiteSpace(keyLabel);

        if (filledCount > 0 || hasKey)
        {
            foreach (var (member, value) in options)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    yield return new ValidationResult("All four options (A, B, C, D) are required for objective items.", [member]);
                }
            }

            var normalizedKey = keyLabel?.Trim().ToUpperInvariant();
            if (normalizedKey is not ("A" or "B" or "C" or "D"))
            {
                yield return new ValidationResult("Correct option key must be one of A, B, C, or D.", ["KeyLabel"]);
            }
        }

        var hasUnit = !string.IsNullOrWhiteSpace(evidenceUnit);
        if ((evidenceStart is null) != (evidenceEnd is null) || hasUnit != (evidenceStart is not null))
        {
            yield return new ValidationResult(
                "Evidence span requires start, end, and unit ('char' or 'sec') together.",
                ["EvidenceStart"]);
        }
        else if (evidenceStart is int start && evidenceEnd is int end && (start < 0 || end <= start))
        {
            yield return new ValidationResult(
                "Evidence span end must be greater than start.",
                ["EvidenceEnd"]);
        }
    }
}
