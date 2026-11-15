using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Contracts;

/// <summary>
/// Canonical bounds for open-ended learner submissions (writing essays and speaking audio)
/// shared across Contracts DTOs, Domain Services, and WebAPI endpoints.
/// </summary>
public static class FreeResponseBounds
{
    public const int MinWritingWords = 10;
    public const int MaxWritingWords = 1_000;
    public const int MaxWritingCharacters = 5_000;

    public const int MaxSpeakingDurationSeconds = 180;
    public const int MaxSpeakingDurationMs = MaxSpeakingDurationSeconds * 1_000;
    public const long MaxSpeakingAudioBytes = 10 * 1024 * 1024; // 10 MB

    public static string? ValidateWritingUpperBounds(string? responseText)
    {
        if (string.IsNullOrEmpty(responseText))
        {
            return null;
        }

        if (responseText.Length > MaxWritingCharacters)
        {
            return $"Response cannot exceed {MaxWritingCharacters:N0} characters.";
        }

        if (WordCount.Count(responseText) > MaxWritingWords)
        {
            return $"Response cannot exceed {MaxWritingWords:N0} words.";
        }

        return null;
    }

    public static string? ValidateWritingSubmission(string? responseText)
    {
        var upperError = ValidateWritingUpperBounds(responseText);
        if (upperError is not null)
        {
            return upperError;
        }

        var words = WordCount.Count(responseText);
        if (words < MinWritingWords)
        {
            return $"Response must be at least {MinWritingWords} words.";
        }

        return null;
    }

    public static IEnumerable<ValidationResult> ValidateDraftRequest(string? responseText)
    {
        var error = ValidateWritingUpperBounds(responseText);
        if (error is not null)
        {
            yield return new ValidationResult(error, ["response"]);
        }
    }
}
