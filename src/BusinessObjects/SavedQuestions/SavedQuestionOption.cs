using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record SavedQuestionOption(
    int OptionId,
    string Label,
    string Text,
    bool IsCorrect);
