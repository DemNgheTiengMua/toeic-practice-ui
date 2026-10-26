using System;
using System.Collections.Generic;
using System.Linq;
using BusinessObjects;
using BusinessObjects.Entities;

namespace Services;

public sealed partial class AttemptEngine
{
    public WritingEvaluationResult EvaluateWriting(Question question, string? responseText) =>
        EvaluateWritingResponse(question, responseText);

    public static bool EvaluateObjectiveAnswer(
        Question question,
        IEnumerable<QuestionOption> options,
        int? selectedOptionId,
        string? selectedOptionIdsJson,
        string? responseText)
    {
        var qType = question.QuestionType?.ToLowerInvariant() ?? QuestionType.Mcq.ToCode();

        switch (qType)
        {
            case "mcq":
            case "mcq_3":
            {
                if (!selectedOptionId.HasValue) return false;
                return options.Any(o => o.OptionId == selectedOptionId.Value && o.IsCorrect);
            }

            case "multi_select":
            {
                var selectedIds = ParseOptionIds(selectedOptionIdsJson, selectedOptionId);
                var correctIds = options.Where(o => o.IsCorrect).Select(o => o.OptionId).ToHashSet();
                if (correctIds.Count == 0) return false;
                return selectedIds.SetEquals(correctIds);
            }

            case "inline_completion":
            {
                return EvaluateTextCompletion(question, options, responseText);
            }

            case "dropdown":
            case "drag_drop":
            {
                if (selectedOptionId.HasValue && options.Any(o => o.OptionId == selectedOptionId.Value && o.IsCorrect))
                {
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(responseText))
                {
                    return EvaluateTextCompletion(question, options, responseText);
                }

                return false;
            }

            default:
                return false;
        }
    }

    public static HashSet<int> ParseOptionIds(string? selectedOptionIdsJson, int? fallbackOptionId = null)
    {
        var set = new HashSet<int>();
        if (!string.IsNullOrWhiteSpace(selectedOptionIdsJson))
        {
            var trimmed = selectedOptionIdsJson.Trim();
            if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
            {
                try
                {
                    var list = System.Text.Json.JsonSerializer.Deserialize<List<int>>(trimmed);
                    if (list != null)
                    {
                        foreach (var id in list) set.Add(id);
                        return set;
                    }
                }
                catch
                {
                    // Fallback to split if invalid JSON
                }
            }

            var parts = trimmed.Trim('[', ']').Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                if (int.TryParse(p.Trim(), out var parsedId))
                {
                    set.Add(parsedId);
                }
            }
        }

        if (set.Count == 0 && fallbackOptionId.HasValue)
        {
            set.Add(fallbackOptionId.Value);
        }

        return set;
    }

    public static WritingEvaluationResult EvaluateWritingResponse(Question question, string? responseText)
    {
        var text = responseText ?? string.Empty;
        var words = text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        int wordCount = words.Length;

        bool isUnderLength = question.MinWords.HasValue && question.MinWords.Value > 0 && wordCount < question.MinWords.Value;

        var missingKeywords = new List<string>();
        bool hasRequiredKeywords = true;

        if (!string.IsNullOrWhiteSpace(question.RequiredKeywords))
        {
            var keywords = ParseKeywords(question.RequiredKeywords);
            foreach (var kw in keywords)
            {
                if (string.IsNullOrWhiteSpace(kw)) continue;
                if (!ContainsKeywordStem(text, kw))
                {
                    missingKeywords.Add(kw.Trim());
                }
            }
            hasRequiredKeywords = missingKeywords.Count == 0;
        }

        var feedbackParts = new List<string>();
        if (isUnderLength)
        {
            feedbackParts.Add($"IsUnderLength=true; WordCount={wordCount}; MinWords={question.MinWords}; Length penalty applied.");
        }
        if (!hasRequiredKeywords)
        {
            feedbackParts.Add($"MissingRequiredKeywords=[{string.Join(", ", missingKeywords)}]");
        }

        string? feedback = feedbackParts.Count > 0 ? string.Join(" | ", feedbackParts) : null;

        return new WritingEvaluationResult(
            wordCount,
            isUnderLength,
            question.MinWords,
            hasRequiredKeywords,
            missingKeywords,
            feedback);
    }

    public static bool EvaluateTextCompletion(
        Question question,
        IEnumerable<QuestionOption> options,
        string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText)) return false;

        var normalizedCandidate = System.Text.RegularExpressions.Regex.Replace(responseText.Trim(), @"\s+", " ");

        if (!question.AllowNumber && System.Text.RegularExpressions.Regex.IsMatch(normalizedCandidate, @"\d"))
        {
            return false;
        }

        var words = normalizedCandidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (question.MaxWords.HasValue && words.Length > question.MaxWords.Value)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(question.RequiredKeywords))
        {
            var keywords = ParseKeywords(question.RequiredKeywords);
            foreach (var kw in keywords)
            {
                if (string.IsNullOrWhiteSpace(kw)) continue;
                if (!ContainsKeywordStem(normalizedCandidate, kw.Trim()))
                {
                    return false;
                }
            }
        }

        var accepted = ParseAcceptedAnswers(question.AcceptedAnswersJson);
        foreach (var opt in options.Where(o => o.IsCorrect))
        {
            if (!string.IsNullOrWhiteSpace(opt.Text))
            {
                accepted.Add(opt.Text.Trim());
            }
        }

        foreach (var acc in accepted)
        {
            var normalizedAcc = System.Text.RegularExpressions.Regex.Replace(acc.Trim(), @"\s+", " ");
            if (string.Equals(normalizedCandidate, normalizedAcc, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static List<string> ParseKeywords(string requiredKeywords)
    {
        var list = new List<string>();
        var trimmed = requiredKeywords.Trim();
        if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
        {
            try
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<List<string>>(trimmed);
                if (parsed != null) return parsed.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            }
            catch { }
        }
        return trimmed.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
                      .Select(k => k.Trim())
                      .Where(k => !string.IsNullOrWhiteSpace(k))
                      .ToList();
    }

    public static bool ContainsKeywordStem(string text, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return true;
        keyword = keyword.Trim().ToLowerInvariant();
        var lowerText = text.ToLowerInvariant();

        if (lowerText.Contains(keyword)) return true;

        var stem = StripSuffixes(keyword);
        if (stem.Length >= 3 && lowerText.Contains(stem)) return true;

        var candidateWords = lowerText.Split([' ', ',', '.', '!', '?', ';', ':'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var w in candidateWords)
        {
            if (w.Contains(stem) || StripSuffixes(w) == stem) return true;
        }

        return false;
    }

    private static string StripSuffixes(string word)
    {
        if (word.EndsWith("ations") && word.Length > 7) return word[..^6];
        if (word.EndsWith("ation") && word.Length > 6) return word[..^5];
        if (word.EndsWith("tion") && word.Length > 6) return word[..^4];
        if (word.EndsWith("ment") && word.Length > 6) return word[..^4];
        if (word.EndsWith("ing") && word.Length > 5) return word[..^3];
        if (word.EndsWith("ed") && word.Length > 4) return word[..^2];
        if (word.EndsWith("es") && word.Length > 4) return word[..^2];
        if (word.EndsWith("s") && word.Length > 3) return word[..^1];
        if (word.EndsWith("e") && word.Length > 4) return word[..^1];
        return word;
    }

    public static List<string> ParseAcceptedAnswers(string? acceptedAnswersJson)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(acceptedAnswersJson)) return list;
        var trimmed = acceptedAnswersJson.Trim();
        if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
        {
            try
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<List<string>>(trimmed);
                if (parsed != null) return parsed.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            }
            catch { }
        }
        return trimmed.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries)
                      .Select(a => a.Trim())
                      .Where(a => !string.IsNullOrWhiteSpace(a))
                      .ToList();
    }
}
