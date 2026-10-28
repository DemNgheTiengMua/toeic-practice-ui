using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record AdminQuestionOptionDto(
    int OptionId,
    int QuestionId,
    string Label,
    string Text,
    bool IsCorrect,
    string? Rationale);
