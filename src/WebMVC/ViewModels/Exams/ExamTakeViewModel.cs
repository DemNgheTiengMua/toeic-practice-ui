using System.Text.Json;
using Contracts;

namespace WebMVC.Models;

/// <summary>
/// #158: the real sitting view model, plus the two things only an admin preview may see — the
/// answer key and the exam's status. Every other property the sitting uses is unchanged, so the
/// admin approves the same markup a learner gets.
/// </summary>
public sealed record ExamTakeViewModel(
    AttemptTakeDto Take,
    string? ErrorMessage = null,
    ExamPreviewTakeDto? Preview = null)
{
    public bool HasErrorMessage { get; } = !string.IsNullOrEmpty(ErrorMessage);

    public ExamTakePresentation Display { get; } = ExamTakePresentation.Create(Take, Preview is not null);

    public bool IsPreview { get; } = Preview is not null;

    public int ExamId { get; } = Preview?.Take.ExamId ?? Take.ExamId;

    /// <summary>"DRAFT" or "PUBLISHED" — the placard that tells an admin why nobody else can see it.</summary>
    public string ExamStatus { get; } = (Preview?.ExamStatus ?? string.Empty).ToUpperInvariant();

    public int DurationMinutes { get; } = Preview?.DurationMinutes ?? 0;

    public IReadOnlyList<ExamPreviewSectionDto> Sections { get; } = Preview?.Sections ?? [];

    /// <summary>Section jumps, ready for the preview bar's select (no LINQ in the view).</summary>
    public IReadOnlyList<PreviewSectionChoice> SectionChoices { get; } = Preview is null
        ? []
        : Preview.Sections
            .Select(section => new PreviewSectionChoice(
                section.SectionId,
                section.FirstDisplayOrder,
                $"{section.SectionCode} · {section.SectionName} (Q{section.FirstDisplayOrder}–{section.LastDisplayOrder})"))
            .ToArray();

    /// <summary>questionId → answer key, as the JSON island the preview script reads.</summary>
    public string PreviewAnswersJson { get; } = Preview is null
        ? "{}"
        : JsonSerializer.Serialize(
            Preview.Answers.ToDictionary(
                answer => answer.QuestionId,
                answer => new { correctOptionIds = answer.CorrectOptionIds, explanation = answer.Explanation }),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
}

public sealed record PreviewSectionChoice(int SectionId, int DisplayOrder, string Label);
