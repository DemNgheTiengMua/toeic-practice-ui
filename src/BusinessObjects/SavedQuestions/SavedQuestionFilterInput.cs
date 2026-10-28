using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record SavedQuestionFilterInput(
    SavedQuestionSource? Source = null,
    int? SectionId = null,
    int? CategoryId = null);
