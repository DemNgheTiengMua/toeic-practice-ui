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

public sealed partial class ExamGeneratorService
{

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
}
