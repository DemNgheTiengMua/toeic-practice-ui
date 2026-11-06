using System;
using System.Collections.Generic;

namespace BusinessObjects;

/// <summary>
/// #224: the outcome of a save, including the identity of the row it touched, so the caller can
/// unsave it without guessing which of the two shapes it is.
/// </summary>
public sealed record SaveQuestionResult(
    bool IsSaved,
    SavedQuestionSource? Source,
    DateTimeOffset? SavedAt,
    int? SavedQuestionId = null);
