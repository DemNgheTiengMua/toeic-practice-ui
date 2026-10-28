using System;
using System.Collections.Generic;

namespace Contracts;

// --- Studio State DTOs ---

public sealed record ExamStudioQuestionOptionDto(
    int OptionId,
    int QuestionId,
    string Label,
    string Text,
    bool IsCorrect,
    string? Rationale);
