using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record SavedQuestionsViewDto(
    CertificateOptionDto Certificate,
    int TotalSavedCount,
    int ManualSavedCount,
    int IncorrectSavedCount,
    IReadOnlyList<SavedQuestionItemDto> Questions,
    IReadOnlyList<SavedQuestionSectionFilterDto> Sections,
    IReadOnlyList<SavedQuestionCategoryFilterDto> Categories);
