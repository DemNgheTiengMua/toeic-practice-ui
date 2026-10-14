using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record SavedQuestionOptionDto(
    int OptionId,
    string Label,
    string Text,
    bool IsCorrect);

public sealed record SavedQuestionItemDto(
    int QuestionId,
    string Source,
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
    IReadOnlyList<SavedQuestionOptionDto> Options);

public sealed record SavedQuestionSectionFilterDto(
    int SectionId,
    string Code,
    string Name,
    int Count);

public sealed record SavedQuestionCategoryFilterDto(
    int CategoryId,
    string Name,
    int Count);

public sealed record SavedQuestionsViewDto(
    CertificateOptionDto Certificate,
    int TotalSavedCount,
    int ManualSavedCount,
    int IncorrectSavedCount,
    IReadOnlyList<SavedQuestionItemDto> Questions,
    IReadOnlyList<SavedQuestionSectionFilterDto> Sections,
    IReadOnlyList<SavedQuestionCategoryFilterDto> Categories);

public sealed record SaveQuestionRequest(string? Source = "manual");

public sealed record SaveQuestionResultDto(
    bool IsSaved,
    string? Source,
    DateTimeOffset? SavedAt);
