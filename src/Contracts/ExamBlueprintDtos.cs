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

/// <summary>
/// Result of generating and ingesting an exam package.
/// </summary>
public sealed record ExamGenerationResultDto(
    int ExamId,
    int CertId,
    string CertCode,
    string ExamName,
    string Status,
    string Purpose,
    int QuestionCount,
    int? BatchId = null,
    string? RawManifestJson = null);
