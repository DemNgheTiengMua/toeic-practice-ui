using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record SavedQuestionOptionDto(
    int OptionId,
    string Label,
    string Text,
    bool IsCorrect);
