using System;
using System.Collections.Generic;

namespace Contracts;

/// <summary>
/// Blueprint for generative exam authoring via Claude Sonnet 3.7 (Issue #88).
/// </summary>
public sealed record ExamBlueprintDto(
    string CertCode,
    string? Title = null,
    string? Purpose = "mock",
    string? Status = "draft",
    int? DurationMinutes = null,
    string? Skill = null,
    string? TargetBand = null,
    IReadOnlyDictionary<string, double>? TargetBandDistribution = null,
    int? DifficultyLevel = null,
    string? Description = null,
    IReadOnlyList<ExamBlueprintSectionDto>? Sections = null);
