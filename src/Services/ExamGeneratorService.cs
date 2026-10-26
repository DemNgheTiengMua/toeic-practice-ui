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

    private async Task<SectionGenerationResultDto> IngestSectionQuestionsJsonAsync(
        Exam exam,
        Section section,
        string json,
        CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        JsonElement groupsElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("groups", out var gProp) && gProp.ValueKind == JsonValueKind.Array)
        {
            groupsElement = gProp;
        }
        else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("sections", out var sProp) && sProp.ValueKind == JsonValueKind.Array)
        {
            var firstSec = sProp.EnumerateArray().FirstOrDefault();
            if (firstSec.TryGetProperty("groups", out var secGProp))
            {
                groupsElement = secGProp;
            }
            else
            {
                throw new AdminExamException(ApiError.Codes.Validation, "Invalid AI response JSON: missing 'groups' array.", 400);
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            groupsElement = root;
        }
        else
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Invalid AI response JSON: expected array or object with 'groups'.", 400);
        }

        var categories = await db.QuestionCategories
            .Where(c => c.CertId == exam.CertId)
            .ToDictionaryAsync(c => c.Code, c => c.CategoryId, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var maxGroupOrder = await db.QuestionGroups
            .Where(g => g.ExamId == exam.ExamId && g.SectionId == section.SectionId)
            .Select(g => (int?)g.DisplayOrder)
            .MaxAsync(cancellationToken) ?? 0;

        var maxQuestionOrder = await db.Questions
            .Where(q => q.ExamId == exam.ExamId && q.SectionId == section.SectionId)
            .Select(q => (int?)q.DisplayOrder)
            .MaxAsync(cancellationToken) ?? 0;

        var currentDisplayOrder = Math.Max(maxGroupOrder, maxQuestionOrder) + 1;

        var groupsCreated = 0;
        var questionsCreated = 0;

        foreach (var groupEl in groupsElement.EnumerateArray())
        {
            string? passage = groupEl.TryGetProperty("passage", out var pProp) && pProp.ValueKind == JsonValueKind.String ? pProp.GetString() : null;
            string? transcript = groupEl.TryGetProperty("transcript", out var tProp) && tProp.ValueKind == JsonValueKind.String ? tProp.GetString() : null;

            var group = new QuestionGroup
            {
                ExamId = exam.ExamId,
                CertId = exam.CertId,
                SectionId = section.SectionId,
                DisplayOrder = currentDisplayOrder,
                Passage = passage,
                Transcript = transcript,
                UnitNumber = 1
            };

            db.QuestionGroups.Add(group);
            await db.SaveChangesAsync(cancellationToken);
            groupsCreated++;

            if (groupEl.TryGetProperty("questions", out var questionsEl) && questionsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var qEl in questionsEl.EnumerateArray())
                {
                    var stem = qEl.TryGetProperty("stem", out var stmProp) ? stmProp.GetString() ?? string.Empty : string.Empty;
                    var difficulty = qEl.TryGetProperty("difficultyLevel", out var diffProp) && diffProp.TryGetInt32(out var diffVal) ? diffVal : 3;
                    var explanation = qEl.TryGetProperty("explanation", out var expProp) ? expProp.GetString() : null;
                    var categoryCode = qEl.TryGetProperty("categoryCode", out var catProp) ? catProp.GetString() : null;
                    var qType = qEl.TryGetProperty("questionType", out var qtProp) ? qtProp.GetString()?.ToLowerInvariant() ?? "mcq" : "mcq";
                    var hint = qEl.TryGetProperty("hint", out var hProp) ? hProp.GetString() : null;
                    var maxWords = qEl.TryGetProperty("maxWords", out var mwProp) && mwProp.TryGetInt32(out var mw) ? (int?)mw : null;
                    var minWords = qEl.TryGetProperty("minWords", out var minwProp) && minwProp.TryGetInt32(out var minw) ? (int?)minw : null;
                    var maxSelections = qEl.TryGetProperty("maxSelections", out var msProp) && msProp.TryGetInt32(out var ms) ? (int?)ms : null;
                    var prepSec = qEl.TryGetProperty("prepSeconds", out var psProp) && psProp.TryGetInt32(out var ps) ? (int?)ps : null;
                    var respSec = qEl.TryGetProperty("responseSeconds", out var rsProp) && rsProp.TryGetInt32(out var rs) ? (int?)rs : null;
                    var requiredKeywords = qEl.TryGetProperty("requiredKeywords", out var rkProp) ? rkProp.GetString() : null;

                    string? acceptedAnswersJson = null;
                    if (qEl.TryGetProperty("acceptedAnswers", out var aaProp))
                    {
                        if (aaProp.ValueKind == JsonValueKind.Array)
                        {
                            var list = aaProp.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                            acceptedAnswersJson = JsonSerializer.Serialize(list);
                        }
                        else if (aaProp.ValueKind == JsonValueKind.String)
                        {
                            acceptedAnswersJson = aaProp.GetString();
                        }
                    }

                    int? categoryId = null;
                    if (!string.IsNullOrWhiteSpace(categoryCode) && categories.TryGetValue(categoryCode, out var cid))
                    {
                        categoryId = cid;
                    }

                    if (section.OptionCount == 3 && qType == "mcq")
                    {
                        qType = "mcq_3";
                    }

                    var question = new Question
                    {
                        ExamId = exam.ExamId,
                        SectionId = section.SectionId,
                        SkillId = section.SkillId,
                        GroupId = group.GroupId,
                        Stem = !string.IsNullOrWhiteSpace(stem) ? stem.Trim() : $"Generated Question {questionsCreated + 1}",
                        DifficultyLevel = difficulty is >= 1 and <= 5 ? difficulty : 3,
                        QuestionType = qType,
                        Explanation = explanation?.Trim(),
                        CategoryId = categoryId,
                        PrepSeconds = prepSec,
                        ResponseSeconds = respSec,
                        MinWords = minWords,
                        MaxWords = maxWords,
                        MaxSelections = maxSelections,
                        AllowNumber = true,
                        AcceptedAnswersJson = acceptedAnswersJson,
                        RequiredKeywords = requiredKeywords?.Trim(),
                        Hint = hint?.Trim(),
                        DisplayOrder = currentDisplayOrder
                    };

                    db.Questions.Add(question);
                    await db.SaveChangesAsync(cancellationToken);
                    questionsCreated++;

                    if (qEl.TryGetProperty("options", out var optionsEl) && optionsEl.ValueKind == JsonValueKind.Array)
                    {
                        var optIdx = 0;
                        foreach (var optEl in optionsEl.EnumerateArray())
                        {
                            if (optIdx >= section.OptionCount && (qType == "mcq" || qType == "mcq_3"))
                            {
                                break;
                            }

                            var label = optEl.TryGetProperty("label", out var lProp) ? lProp.GetString() ?? ((char)('A' + optIdx)).ToString() : ((char)('A' + optIdx)).ToString();
                            var optText = optEl.TryGetProperty("text", out var otProp) ? otProp.GetString() ?? string.Empty : string.Empty;
                            var isCorrect = optEl.TryGetProperty("isCorrect", out var icProp) && icProp.GetBoolean();
                            var rationale = optEl.TryGetProperty("rationale", out var rProp) ? rProp.GetString() : null;

                            db.QuestionOptions.Add(new QuestionOption
                            {
                                QuestionId = question.QuestionId,
                                QuestionType = question.QuestionType,
                                Label = label.Trim().ToUpperInvariant(),
                                Text = optText.Trim(),
                                IsCorrect = isCorrect,
                                Rationale = rationale?.Trim()
                            });
                            optIdx++;
                        }
                    }

                    currentDisplayOrder++;
                }
            }
        }

        exam.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return new SectionGenerationResultDto(
            ExamId: exam.ExamId,
            SectionId: section.SectionId,
            QuestionsGenerated: questionsCreated,
            GroupsGenerated: groupsCreated,
            Message: $"Successfully generated {questionsCreated} questions in {groupsCreated} groups for section '{section.Name}'.");
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
            1. Respect OptionCount for each section: if OptionCount=3, author exactly 3 options (A, B, C); if OptionCount=4, author 4 options (A, B, C, D).
            2. For MCQ questions, exactly ONE option must have "isCorrect": true.
            3. For multi_select questions, author at least 2 correct options and include "maxSelections".
            4. For inline_completion questions, provide "acceptedAnswers" array of strings and "maxWords".
            5. For free_response questions with RequiredKeywordCount > 0, provide "requiredKeywords" with the exact count of comma-separated words.
            6. Each option must have non-empty "text" and a non-empty "rationale".
            7. Question stems must be complete and non-empty. Difficulty level must be an integer between 1 and 5.
            8. For reading comprehension sections requiring passage, the group must supply a non-empty "passage".
            9. For listening sections, the group must supply an audio script in "transcript" with explicit dialogue speaker turns.
            10. Category codes must match the valid category codes provided.
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
        sb.AppendLine("Sections to generate (respect each section's blueprint):");
        foreach (var sec in blueprint.Sections!)
        {
            var matchSec = certSections.First(s => s.Code.Equals(sec.SectionCode.Trim(), StringComparison.OrdinalIgnoreCase));
            sb.AppendLine($"- Section Code: '{matchSec.Code}' ({matchSec.Name}, Modality: {matchSec.Modality})");
            sb.AppendLine($"  Questions to author: {sec.QuestionCount}");
            sb.AppendLine($"  OptionCount: {matchSec.OptionCount}");
            sb.AppendLine($"  ClusterSize: {matchSec.ClusterSize}");
            sb.AppendLine($"  RequirePassage: {matchSec.RequirePassage}");
            if (!string.IsNullOrWhiteSpace(matchSec.AllowedComponentTypesJson))
            {
                sb.AppendLine($"  Allowed Component Types: {matchSec.AllowedComponentTypesJson}");
            }
            if (matchSec.RequiredKeywordCount > 0)
            {
                sb.AppendLine($"  Required Keyword Count: {matchSec.RequiredKeywordCount}");
            }
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

    private static string BuildSectionSystemPrompt(Section section)
    {
        return $$"""
            You are an expert exam author specializing in standardized tests.
            Your task is to generate questions for section '{{section.Code}}' ({{section.Name}}, Modality: {{section.Modality}}).

            Blueprint constraints:
            - OptionCount: {{section.OptionCount}} (author exactly {{section.OptionCount}} options for MCQ questions: {{(section.OptionCount == 3 ? "A, B, C" : "A, B, C, D")}}).
            - ClusterSize: {{section.ClusterSize}}
            - RequirePassage: {{section.RequirePassage}}
            - RequiredKeywordCount: {{section.RequiredKeywordCount}}
            - AllowedComponentTypes: {{section.AllowedComponentTypesJson ?? "[\"mcq\"]"}}

            Output JSON schema:
            ```json
            {
              "groups": [
                {
                  "passage": "Reading passage if required, else null",
                  "transcript": "Audio dialogue or cue if required, else null",
                  "questions": [
                    {
                      "stem": "Question stem text...",
                      "difficultyLevel": 3,
                      "questionType": "{{(section.OptionCount == 3 ? "mcq_3" : "mcq")}}",
                      "categoryCode": "valid_category_code",
                      "explanation": "Pedagogical explanation...",
                      "hint": "Optional hint...",
                      "maxWords": 2,
                      "maxSelections": 2,
                      "acceptedAnswers": ["accepted answer 1", "accepted answer 2"],
                      "requiredKeywords": "word1, word2",
                      "prepSeconds": 15,
                      "responseSeconds": 60,
                      "minWords": 150,
                      "options": [
                        { "label": "A", "text": "Option text", "isCorrect": true, "rationale": "Rationale" },
                        { "label": "B", "text": "Option text", "isCorrect": false, "rationale": "Rationale" }
                      ]
                    }
                  ]
                }
              ]
            }
            ```

            Respond strictly with valid JSON inside a ```json ... ``` code block.
            """;
    }

    private static string BuildSectionUserPrompt(
        Exam exam,
        Section section,
        int countToGenerate,
        List<QuestionCategory> categories)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Generate {countToGenerate} pedagogically rigorous questions for section '{section.Code}' ({section.Name}) in exam '{exam.Name}'.");
        sb.AppendLine($"Certificate: {exam.Cert.Name} ({exam.Cert.Code})");
        sb.AppendLine($"Modality: {section.Modality}");
        sb.AppendLine($"Target question count: {countToGenerate}");
        sb.AppendLine($"Option count: {section.OptionCount}");
        sb.AppendLine($"Cluster size: {section.ClusterSize}");
        sb.AppendLine($"Require passage: {section.RequirePassage}");
        if (section.RequiredKeywordCount > 0)
        {
            sb.AppendLine($"Mandatory keywords per question: exactly {section.RequiredKeywordCount} comma-separated words in 'requiredKeywords'.");
        }
        if (!string.IsNullOrWhiteSpace(section.AllowedComponentTypesJson))
        {
            sb.AppendLine($"Allowed component types: {section.AllowedComponentTypesJson}");
        }

        if (categories.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Valid category codes:");
            foreach (var cat in categories.Take(12))
            {
                sb.AppendLine($"- {cat.Code}: {cat.Name}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Generate the section questions JSON now inside ```json ... ```.");
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
