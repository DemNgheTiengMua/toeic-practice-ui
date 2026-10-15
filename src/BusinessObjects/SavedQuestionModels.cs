using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record SavedQuestionOption(
    int OptionId,
    string Label,
    string Text,
    bool IsCorrect);

public sealed record SavedQuestionItem(
    int QuestionId,
    SavedQuestionSource Source,
    DateTimeOffset SavedAt,
    int SectionId,
    string SectionCode,
    string SectionName,
    int? CategoryId,
    string? CategoryName,
    string Stem,
    string? Passage,
    string? ImagePath,
    string? AudioPath,
    string? Hint,
    string? Explanation,
    string? Transcript,
    IReadOnlyList<SavedQuestionOption> Options);

public sealed record SavedQuestionSectionFilter(
    int SectionId,
    string Code,
    string Name,
    int Count);

public sealed record SavedQuestionCategoryFilter(
    int CategoryId,
    string Name,
    int Count);

public sealed record SavedQuestionsView(
    CertificateOption Certificate,
    int TotalSavedCount,
    int ManualSavedCount,
    int IncorrectSavedCount,
    IReadOnlyList<SavedQuestionItem> Questions,
    IReadOnlyList<SavedQuestionSectionFilter> Sections,
    IReadOnlyList<SavedQuestionCategoryFilter> Categories);

public sealed record SavedQuestionFilterInput(
    SavedQuestionSource? Source = null,
    int? SectionId = null,
    int? CategoryId = null);

public sealed record SaveQuestionResult(
    bool IsSaved,
    SavedQuestionSource? Source,
    DateTimeOffset? SavedAt);
