using System;
using System.Collections.Generic;

namespace Contracts;

/// <summary>
/// Section specification within an exam generation blueprint.
/// </summary>
public sealed record ExamBlueprintSectionDto(
    string SectionCode,
    int QuestionCount,
    int? DifficultyLevel = null,
    string? CategoryCode = null,
    IReadOnlyList<string>? Topics = null);
