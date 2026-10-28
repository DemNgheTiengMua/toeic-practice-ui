using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record AdminQuestionOptionInput(
    int? OptionId,
    string Label,
    string Text,
    bool IsCorrect,
    string? Rationale);
