using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Services;

public sealed partial class QuestionImportService
{

    private static PlannedQuestion? ValidateQuestion(
        ImportManifestQuestion question,
        string where,
        int row,
        int questionIndex,
        Section? section,
        Dictionary<string, int> categories,
        ILookup<int, string> criteria,
        ImportReport report)
    {
        var errorsBefore = report.ErrorCount;
        void Fail(string message) => report.Error(row, $"{where}: {message}");

        if (string.IsNullOrWhiteSpace(question.Stem))
        {
            Fail("stem is required.");
        }

        if (question.DifficultyLevel is not (>= 1 and <= 5))
        {
            Fail("difficultyLevel must be between 1 and 5.");
        }

        if (question.Hint?.Length > 1000)
        {
            Fail("hint cannot exceed 1000 characters.");
        }

        int? categoryId = null;
        if (!string.IsNullOrWhiteSpace(question.CategoryCode))
        {
            if (categories.TryGetValue(question.CategoryCode.Trim(), out var id))
            {
                categoryId = id;
            }
            else
            {
                Fail($"Unknown category '{question.CategoryCode}' for this certificate.");
            }
        }

        if (!EnumCodes.TryParse<QuestionType>(question.QuestionType?.Trim(), out var type))
        {
            Fail($"questionType must be mcq, mcq_3, multi_select, inline_completion, dropdown, drag_drop, or free_response, not '{question.QuestionType}'.");
            return null;
        }

        // Section blueprint AllowedComponentTypesJson validation
        if (section is not null && !string.IsNullOrWhiteSpace(section.AllowedComponentTypesJson))
        {
            List<string>? allowed = null;
            try
            {
                allowed = JsonSerializer.Deserialize<List<string>>(section.AllowedComponentTypesJson);
            }
            catch
            {
            }

            if (allowed != null && allowed.Count > 0 && !allowed.Contains(type.ToCode(), StringComparer.OrdinalIgnoreCase))
            {
                Fail($"Question type '{type.ToCode()}' is not allowed in section '{section.Code}' (allowed: {section.AllowedComponentTypesJson}).");
            }
        }

        // Modality checks
        if (type == QuestionType.FreeResponse)
        {
            if (section is not null && section.Modality is not ("speaking" or "writing"))
            {
                Fail($"section {section.Code} is {section.Modality}, which takes objective questions only.");
            }
        }
        else
        {
            if (section is not null && section.Modality is not ("listening" or "reading"))
            {
                Fail($"section {section.Code} is {section.Modality}, which takes free_response questions only.");
            }
        }

        // Question-type specific validation
        switch (type)
        {
            case QuestionType.Mcq:
                ValidateMcq(question, section, Fail);
                break;
            case QuestionType.Mcq_3:
                ValidateMcq3(question, section, Fail);
                break;
            case QuestionType.MultiSelect:
                ValidateMultiSelect(question, section, Fail);
                break;
            case QuestionType.InlineCompletion:
                ValidateInlineCompletion(question, section, Fail);
                break;
            case QuestionType.Dropdown:
            case QuestionType.DragDrop:
                ValidateDropdownOrDragDrop(question, section, type, Fail);
                break;
            case QuestionType.FreeResponse:
                ValidateFreeResponse(question, section, criteria, Fail);
                break;
        }

        string? acceptedAnswersJson = null;
        if (question.AcceptedAnswers != null && question.AcceptedAnswers.Count > 0)
        {
            acceptedAnswersJson = JsonSerializer.Serialize(question.AcceptedAnswers);
        }
        else if (!string.IsNullOrWhiteSpace(question.AcceptedAnswersJson))
        {
            acceptedAnswersJson = question.AcceptedAnswersJson.Trim();
        }

        string? requiredKeywords = null;
        if (question.RequiredKeywords != null && question.RequiredKeywords.Count > 0)
        {
            requiredKeywords = string.Join(", ", question.RequiredKeywords);
        }

        return report.ErrorCount > errorsBefore
            ? null
            : new PlannedQuestion(question, type, categoryId, question.DisplayOrder ?? questionIndex, acceptedAnswersJson, requiredKeywords);
    }

    private static void ValidateMcq(ImportManifestQuestion question, Section? section, Action<string> fail)
    {
        // CK_Questions_Timing / CK_Questions_Words
        if (question.PrepSeconds is not null || question.ResponseSeconds is not null || question.MinWords is not null || question.MaxWords is not null)
        {
            fail("mcq questions cannot set prepSeconds, responseSeconds, minWords or maxWords.");
        }

        if (question.Criteria is { Count: > 0 })
        {
            fail("mcq questions cannot have criteria.");
        }

        var options = question.Options ?? [];
        if (options.Count < 2)
        {
            fail("mcq questions need at least 2 options.");
            return;
        }

        if (section != null && section.OptionCount > 0 && options.Count != section.OptionCount)
        {
            fail($"Section '{section.Code}' expects {section.OptionCount} options for mcq questions, but found {options.Count}.");
        }

        // UX_Options_OneCorrect
        if (options.Count(o => o.IsCorrect) != 1)
        {
            fail("mcq questions need exactly one correct option.");
        }

        ValidateOptionEntries(options, fail);
    }

    private static void ValidateMcq3(ImportManifestQuestion question, Section? section, Action<string> fail)
    {
        if (question.PrepSeconds is not null || question.ResponseSeconds is not null || question.MinWords is not null || question.MaxWords is not null)
        {
            fail("mcq_3 questions cannot set prepSeconds, responseSeconds, minWords or maxWords.");
        }

        if (question.Criteria is { Count: > 0 })
        {
            fail("mcq_3 questions cannot have criteria.");
        }

        var options = question.Options ?? [];
        if (options.Count != 3)
        {
            fail("mcq_3 questions must have exactly 3 options.");
            return;
        }

        if (options.Count(o => o.IsCorrect) != 1)
        {
            fail("mcq_3 questions need exactly one correct option.");
        }

        ValidateOptionEntries(options, fail);
    }

    private static void ValidateMultiSelect(ImportManifestQuestion question, Section? section, Action<string> fail)
    {
        if (question.PrepSeconds is not null || question.ResponseSeconds is not null || question.MinWords is not null || question.MaxWords is not null)
        {
            fail("multi_select questions cannot set prepSeconds, responseSeconds, minWords or maxWords.");
        }

        if (question.Criteria is { Count: > 0 })
        {
            fail("multi_select questions cannot have criteria.");
        }

        var options = question.Options ?? [];
        if (options.Count < 2)
        {
            fail("multi_select questions need at least 2 options.");
            return;
        }

        var correctCount = options.Count(o => o.IsCorrect);
        if (correctCount < 1)
        {
            fail("multi_select questions must have at least one correct option.");
        }

        if (!question.MaxSelections.HasValue || question.MaxSelections.Value < 2)
        {
            fail("multi_select questions must specify maxSelections of at least 2.");
        }
        else if (question.MaxSelections.Value > options.Count)
        {
            fail("maxSelections cannot exceed the number of available options.");
        }
        else if (correctCount > question.MaxSelections.Value)
        {
            fail("multi_select questions cannot have more correct options than maxSelections.");
        }

        ValidateOptionEntries(options, fail);
    }

    private static void ValidateInlineCompletion(ImportManifestQuestion question, Section? section, Action<string> fail)
    {
        if (question.PrepSeconds is not null || question.ResponseSeconds is not null)
        {
            fail("inline_completion questions cannot set prepSeconds or responseSeconds.");
        }

        if (question.Criteria is { Count: > 0 })
        {
            fail("inline_completion questions cannot have criteria.");
        }

        var accepted = question.AcceptedAnswers ?? (string.IsNullOrWhiteSpace(question.AcceptedAnswersJson) ? [] : AttemptEngine.ParseAcceptedAnswers(question.AcceptedAnswersJson));
        if (accepted.Count == 0 && (question.Options == null || !question.Options.Any(o => o.IsCorrect)))
        {
            fail("inline_completion questions must specify acceptedAnswers.");
        }

        if (question.MinWords is <= 0)
        {
            fail("minWords must be greater than 0.");
        }

        if (question.MaxWords is <= 0)
        {
            fail("maxWords must be greater than 0.");
        }

        if (question.MaxWords is { } max && question.MinWords is { } min && max < min)
        {
            fail("maxWords must be at least minWords.");
        }

        if (question.Options is { Count: > 0 })
        {
            ValidateOptionEntries(question.Options, fail);
        }
    }

    private static void ValidateDropdownOrDragDrop(ImportManifestQuestion question, Section? section, QuestionType type, Action<string> fail)
    {
        if (question.PrepSeconds is not null || question.ResponseSeconds is not null || question.MinWords is not null || question.MaxWords is not null)
        {
            fail($"{type.ToCode()} questions cannot set prepSeconds, responseSeconds, minWords or maxWords.");
        }

        if (question.Criteria is { Count: > 0 })
        {
            fail($"{type.ToCode()} questions cannot have criteria.");
        }

        var options = question.Options ?? [];
        if (options.Count < 2)
        {
            fail($"{type.ToCode()} questions need at least 2 options.");
            return;
        }

        if (options.Count(o => o.IsCorrect) < 1)
        {
            fail($"{type.ToCode()} questions must have at least one correct option.");
        }

        ValidateOptionEntries(options, fail);
    }

    private static void ValidateFreeResponse(ImportManifestQuestion question, Section? section, ILookup<int, string> criteria, Action<string> fail)
    {
        if (question.Options is { Count: > 0 })
        {
            fail("free_response questions cannot have options.");
        }

        // CK_Questions_Timing
        if (question.PrepSeconds is < 0)
        {
            fail("prepSeconds cannot be negative.");
        }

        if (question.ResponseSeconds is <= 0)
        {
            fail("responseSeconds must be greater than 0.");
        }

        // CK_Questions_Words
        if (question.MinWords is <= 0)
        {
            fail("minWords must be greater than 0.");
        }

        if (question.MaxWords is { } max && max < (question.MinWords ?? 1))
        {
            fail("maxWords must be at least minWords.");
        }

        // Required keywords validation against certificate blueprint
        if (section is not null && section.RequiredKeywordCount > 0)
        {
            var keywords = question.RequiredKeywords ?? (string.IsNullOrWhiteSpace(question.AcceptedAnswersJson) ? [] : AttemptEngine.ParseKeywords(question.AcceptedAnswersJson));
            if (keywords.Count < section.RequiredKeywordCount)
            {
                fail($"Section '{section.Code}' requires at least {section.RequiredKeywordCount} required keywords for free_response questions, but found {keywords.Count}.");
            }
        }

        // The rubric is the skill's GradingCriteria (ADR-0004); a manifest may only name existing ones.
        if (section is not null)
        {
            var known = criteria[section.SkillId].ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var code in (question.Criteria ?? []).Select(c => c.Code?.Trim()))
            {
                if (string.IsNullOrEmpty(code))
                {
                    fail("every criterion needs a code.");
                }
                else if (!known.Contains(code))
                {
                    fail($"Unknown grading criterion '{code}' for {section.Code}. Known criteria: {string.Join(", ", known.Order())}.");
                }
            }
        }
    }

    private static void ValidateOptionEntries(IReadOnlyList<ImportManifestOption> options, Action<string> fail)
    {
        var labels = options.Select(o => o.Label?.Trim().ToUpperInvariant()).ToList();
        if (labels.Any(string.IsNullOrEmpty))
        {
            fail("every option needs a label.");
        }

        foreach (var duplicate in labels.Where(l => !string.IsNullOrEmpty(l)).GroupBy(l => l).Where(g => g.Count() > 1))
        {
            fail($"Option label '{duplicate.Key}' is used twice.");
        }

        if (labels.Any(l => l?.Length > 4))
        {
            fail("option labels cannot be longer than 4 characters.");
        }

        if (options.Any(o => string.IsNullOrWhiteSpace(o.Text)))
        {
            fail("every option needs text.");
        }
    }
}
