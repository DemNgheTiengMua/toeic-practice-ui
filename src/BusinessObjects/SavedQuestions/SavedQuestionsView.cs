using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record SavedQuestionsView(
    CertificateOption Certificate,
    int TotalSavedCount,
    int ManualSavedCount,
    int IncorrectSavedCount,
    IReadOnlyList<SavedQuestionItem> Questions,
    IReadOnlyList<SavedQuestionSectionFilter> Sections,
    IReadOnlyList<SavedQuestionCategoryFilter> Categories);
