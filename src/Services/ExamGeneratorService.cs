using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Services;

public sealed class ExamGeneratorService(
    CompanionDbContext db,
    IQuestionImportService importService,
    IClaudeClient claudeClient,
    ILogger<ExamGeneratorService>? logger = null) : IExamGeneratorService
{
    private static readonly HashSet<string> ValidStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        ExamStatus.Draft.ToCode(),
        ExamStatus.Published.ToCode(),
        ExamStatus.Hidden.ToCode()
    };

    private static readonly HashSet<string> ValidPurposes = new(StringComparer.OrdinalIgnoreCase)
    {
        ExamPurpose.Mock.ToCode(),
        ExamPurpose.Placement.ToCode(),
        ExamPurpose.SpeakingPractice.ToCode(),
        ExamPurpose.WritingPractice.ToCode()
    };

    public async Task<ExamGenerationResultDto> GenerateAndIngestExamAsync(
        ExamBlueprintDto blueprint,
        int adminUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(blueprint);

        if (string.IsNullOrWhiteSpace(blueprint.CertCode))
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Certificate code is required.", 400);
        }

        var cert = await db.Certificates
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Code == blueprint.CertCode.Trim(), cancellationToken)
            ?? throw new AdminExamException(ApiError.Codes.CertificateUnavailable, $"Certificate with code '{blueprint.CertCode}' not found.", 404);

        var status = string.IsNullOrWhiteSpace(blueprint.Status) ? ExamStatus.Draft.ToCode() : blueprint.Status.Trim().ToLowerInvariant();
        if (!ValidStatuses.Contains(status))
        {
            throw new AdminExamException(ApiError.Codes.Validation, $"Invalid exam status '{blueprint.Status}'. Allowed: draft, published, hidden.", 400);
        }

        var purpose = string.IsNullOrWhiteSpace(blueprint.Purpose) ? ExamPurpose.Mock.ToCode() : blueprint.Purpose.Trim().ToLowerInvariant();
        if (!ValidPurposes.Contains(purpose))
        {
            throw new AdminExamException(ApiError.Codes.Validation, $"Invalid exam purpose '{blueprint.Purpose}'. Allowed: mock, placement, speaking_practice, writing_practice.", 400);
        }

        // Validate Certificate Isolation: sections must strictly belong to the certificate
        var certSections = await db.Sections
            .AsNoTracking()
            .Where(s => s.CertId == cert.CertId)
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(cancellationToken);

        if (blueprint.Sections is not { Count: > 0 })
        {
            var defaultSection = certSections.FirstOrDefault()
                ?? throw new AdminExamException(ApiError.Codes.Validation, $"No sections configured for certificate '{cert.Code}'.", 400);
            blueprint = blueprint with { Sections = [new ExamBlueprintSectionDto(defaultSection.Code, 2)] };
        }

        var certCategories = await db.QuestionCategories
            .AsNoTracking()
            .Where(c => c.CertId == cert.CertId)
            .ToListAsync(cancellationToken);

        foreach (var sec in blueprint.Sections)
        {
            if (string.IsNullOrWhiteSpace(sec.SectionCode))
            {
                throw new AdminExamException(ApiError.Codes.Validation, "Section code is required for each blueprint section.", 400);
            }

            var section = certSections.SingleOrDefault(s => s.Code.Equals(sec.SectionCode.Trim(), StringComparison.OrdinalIgnoreCase));
            if (section is null)
            {
                throw new AdminExamException(ApiError.Codes.Validation,
                    $"Section '{sec.SectionCode}' does not exist for certificate '{cert.Code}'. Cross-certificate leakage prevented.", 400);
            }

            if (!string.IsNullOrWhiteSpace(sec.CategoryCode))
            {
                var cat = certCategories.SingleOrDefault(c => c.Code.Equals(sec.CategoryCode.Trim(), StringComparison.OrdinalIgnoreCase));
                if (cat is null)
                {
                    throw new AdminExamException(ApiError.Codes.Validation,
                        $"Category '{sec.CategoryCode}' does not exist for certificate '{cert.Code}'.", 400);
                }
            }

            if (sec.QuestionCount <= 0)
            {
                throw new AdminExamException(ApiError.Codes.Validation,
                    $"Section '{sec.SectionCode}' question count must be greater than 0.", 400);
            }
        }

        // Build prompt and query Claude Sonnet 3.7
        var systemPrompt = BuildSystemPrompt();
        var userPrompt = BuildUserPrompt(blueprint, cert, certSections, certCategories);

        logger?.LogInformation("Generating exam package via Claude for cert {CertCode}", cert.Code);

        var rawClaudeResponse = await claudeClient.CreateMessageAsync(
            userPrompt,
            systemPrompt,
            maxTokens: 4000,
            cancellationToken);

        var manifestJson = ExtractJson(rawClaudeResponse);

        return await IngestManifestAsync(cert.CertId, manifestJson, adminUserId, status, cancellationToken);
    }

    public async Task<ExamGenerationResultDto> IngestManifestAsync(
        int certId,
        string manifestJson,
        int adminUserId,
        string? targetStatus = null,
        CancellationToken cancellationToken = default)
    {
        var cert = await db.Certificates
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.CertId == certId, cancellationToken)
            ?? throw new AdminExamException(ApiError.Codes.CertificateUnavailable, $"Certificate {certId} not found.", 404);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(manifestJson));
        var sourceName = $"generative-{cert.Code}-{DateTime.UtcNow:yyyyMMddHHmmss}.json";

        var importResult = await importService.ImportAsync(adminUserId, certId, sourceName, stream, cancellationToken);

        if (importResult.Status != ImportBatchStatusDto.Done || !importResult.ExamId.HasValue)
        {
            var logs = await db.ImportLogs
                .AsNoTracking()
                .Where(l => l.BatchId == importResult.BatchId && l.Level == "error")
                .ToListAsync(cancellationToken);

            var errorDetails = logs.Count > 0
                ? string.Join("; ", logs.Select(l => l.RowNo.HasValue ? $"Q{l.RowNo}: {l.Message}" : l.Message))
                : "Schema or constraint validation failed.";

            throw new AdminExamException(ApiError.Codes.Validation, $"Ingestion failed validation: {errorDetails}", 400);
        }

        var examId = importResult.ExamId.Value;
        var exam = await db.Exams.SingleAsync(e => e.ExamId == examId, cancellationToken);

        if (!string.IsNullOrWhiteSpace(targetStatus))
        {
            var normalizedStatus = targetStatus.Trim().ToLowerInvariant();
            if (normalizedStatus == ExamStatus.Published.ToCode() || normalizedStatus == ExamStatus.Hidden.ToCode())
            {
                exam.Status = normalizedStatus;
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        return new ExamGenerationResultDto(
            ExamId: exam.ExamId,
            CertId: cert.CertId,
            CertCode: cert.Code,
            ExamName: exam.Name,
            Status: exam.Status,
            Purpose: exam.Purpose,
            QuestionCount: importResult.QuestionCount,
            BatchId: importResult.BatchId,
            RawManifestJson: manifestJson);
    }

    private static string BuildSystemPrompt()
    {
        return """
            You are an expert exam author and psychometrician specializing in standardized English language tests (TOEIC, IELTS, VSTEP).
            Your task is to generate complete, pedagogically rigorous exam packages conforming strictly to the package manifest specification (version 1.0).

            Output format requirements:
            - Respond strictly with valid JSON conforming to the manifest format.
            - Wrap your JSON inside a ```json ... ``` code block.
            - Manifest schema:
              {
                "version": "1.0",
                "exam": {
                  "title": "Exam Title",
                  "purpose": "mock",
                  "durationMinutes": 60,
                  "sourceName": "Claude Sonnet 3.7",
                  "description": "Short description"
                },
                "sections": [
                  {
                    "sectionCode": "P5",
                    "groups": [
                      {
                        "displayOrder": 1,
                        "passage": null,
                        "audioFile": null,
                        "transcript": null,
                        "questions": [
                          {
                            "displayOrder": 1,
                            "stem": "Question stem text...",
                            "difficultyLevel": 3,
                            "questionType": "mcq",
                            "categoryCode": "grammar_prep",
                            "explanation": "Pedagogical explanation of the correct choice and why distractors are wrong.",
                            "hint": "Brief pedagogical hint without giving away the answer.",
                            "options": [
                              { "label": "A", "text": "correct option", "isCorrect": true, "rationale": "Why this is correct." },
                              { "label": "B", "text": "distractor option", "isCorrect": false, "rationale": "Trap rationale." },
                              { "label": "C", "text": "distractor option", "isCorrect": false, "rationale": "Trap rationale." },
                              { "label": "D", "text": "distractor option", "isCorrect": false, "rationale": "Trap rationale." }
                            ]
                          }
                        ]
                      }
                    ]
                  }
                ]
              }

            Strict validation constraints:
            1. Every MCQ question must have at least 2 options (standard: 4 options with labels A, B, C, D).
            2. Exactly ONE option must have "isCorrect": true per MCQ question.
            3. Option labels must be unique within each question (A, B, C, D).
            4. Each option must have non-empty "text" and a non-empty "rationale".
            5. Question stems must be complete and non-empty. Difficulty level must be an integer between 1 and 5.
            6. For reading comprehension sections (e.g. P6, P7, PAS, R4), the group must supply a non-empty "passage".
            7. For listening sections (e.g. P1, P2, P3, P4, REC, L3), the group must supply an audio script in "transcript" with explicit dialogue speaker turns (e.g. 'Speaker 1: ...\nSpeaker 2: ...' or announcer prompt) and environmental staging cues.
            8. Category codes must match the valid category codes provided.
            """;
    }

    private static string BuildUserPrompt(
        ExamBlueprintDto blueprint,
        Certificate cert,
        List<Section> certSections,
        List<QuestionCategory> certCategories)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Generate a complete standardized exam package for certificate: {cert.Name} (Code: {cert.Code}).");
        sb.AppendLine($"Title: {(!string.IsNullOrWhiteSpace(blueprint.Title) ? blueprint.Title : $"{cert.Code} Standardized Practice Form")}");
        sb.AppendLine($"Exam Purpose: {blueprint.Purpose ?? "mock"}");
        if (!string.IsNullOrWhiteSpace(blueprint.Skill))
        {
            sb.AppendLine($"Target Skill: {blueprint.Skill}");
        }
        sb.AppendLine($"Target CEFR Band: {blueprint.TargetBand ?? "B2"}");
        if (blueprint.TargetBandDistribution is { Count: > 0 })
        {
            var dist = string.Join(", ", blueprint.TargetBandDistribution.Select(kv => $"{kv.Key}: {(int)Math.Round(kv.Value * 100, MidpointRounding.AwayFromZero)}%"));
            sb.AppendLine($"Target CEFR Band Distribution: {dist}");
        }
        sb.AppendLine($"Default Difficulty Level: {blueprint.DifficultyLevel ?? 3} (1=easiest to 5=hardest)");
        if (blueprint.DurationMinutes.HasValue && blueprint.DurationMinutes.Value > 0)
        {
            sb.AppendLine($"Duration: {blueprint.DurationMinutes.Value} minutes");
        }

        sb.AppendLine();
        sb.AppendLine("Sections to generate:");
        foreach (var sec in blueprint.Sections!)
        {
            var matchSec = certSections.First(s => s.Code.Equals(sec.SectionCode.Trim(), StringComparison.OrdinalIgnoreCase));
            sb.AppendLine($"- Section Code: '{matchSec.Code}' ({matchSec.Name}, Modality: {matchSec.Modality})");
            sb.AppendLine($"  Questions to author: {sec.QuestionCount}");
            if (sec.DifficultyLevel.HasValue)
            {
                sb.AppendLine($"  Target Difficulty: {sec.DifficultyLevel.Value}");
            }
            if (!string.IsNullOrWhiteSpace(sec.CategoryCode))
            {
                sb.AppendLine($"  Target Category: '{sec.CategoryCode}'");
            }
            if (sec.Topics is { Count: > 0 })
            {
                sb.AppendLine($"  Topics: {string.Join(", ", sec.Topics)}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Valid category codes for this certificate:");
        foreach (var cat in certCategories.Take(15))
        {
            sb.AppendLine($"- {cat.Code}: {cat.Name}");
        }

        sb.AppendLine();
        sb.AppendLine("Generate the manifest JSON now. Output ONLY valid JSON inside ```json ... ```.");
        return sb.ToString();
    }

    private static string ExtractJson(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            var end = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (end > 7) return trimmed[7..end].Trim();
        }
        if (trimmed.StartsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            var end = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (end > 3) return trimmed[3..end].Trim();
        }
        var first = trimmed.IndexOf('{');
        var last = trimmed.LastIndexOf('}');
        if (first >= 0 && last > first) return trimmed[first..(last + 1)];
        return trimmed;
    }
}
