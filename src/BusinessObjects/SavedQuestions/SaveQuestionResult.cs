using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record SaveQuestionResult(
    bool IsSaved,
    SavedQuestionSource? Source,
    DateTimeOffset? SavedAt);
