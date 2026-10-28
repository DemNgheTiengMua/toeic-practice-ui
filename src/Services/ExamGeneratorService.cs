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
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Services;

public sealed partial class ExamGeneratorService(
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

    public async Task<SectionGenerationResultDto> GenerateSectionQuestionsAsync(
        int examId,
        int sectionId,
        int adminUserId,
        int? questionCount = null,
        CancellationToken cancellationToken = default)
    {
        var exam = await db.Exams
            .Include(e => e.Cert)
            .SingleOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken)
            ?? throw new ExamNotFoundException(examId);

        var section = await db.Sections
            .SingleOrDefaultAsync(s => s.SectionId == sectionId && s.CertId == exam.CertId, cancellationToken)
            ?? throw new AdminExamException(ApiError.Codes.Validation, $"Section with ID {sectionId} not found for certificate '{exam.Cert.Name}'.", 404);

        var existingCount = await db.Questions
            .CountAsync(q => q.ExamId == examId && q.SectionId == sectionId, cancellationToken);

        var countToGenerate = questionCount.GetValueOrDefault();
        if (countToGenerate <= 0)
        {
            countToGenerate = Math.Max(1, (section.ExpectedQuestionCount ?? 5) - existingCount);
        }

        var certCategories = await db.QuestionCategories
            .AsNoTracking()
            .Where(c => c.CertId == exam.CertId)
            .ToListAsync(cancellationToken);

        var systemPrompt = BuildSectionSystemPrompt(section);
        var userPrompt = BuildSectionUserPrompt(exam, section, countToGenerate, certCategories);

        logger?.LogInformation("Generating {Count} questions for exam {ExamId} section {SectionCode} via Claude",
            countToGenerate, exam.ExamId, section.Code);

        var rawClaudeResponse = await claudeClient.CreateMessageAsync(
            userPrompt,
            systemPrompt,
            maxTokens: 4000,
            cancellationToken);

        var json = ExtractJson(rawClaudeResponse);

        return await IngestSectionQuestionsJsonAsync(exam, section, json, cancellationToken);
    }
}
