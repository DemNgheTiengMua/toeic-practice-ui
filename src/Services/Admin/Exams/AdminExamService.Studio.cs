using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class AdminExamService
{

    public async Task<ExamStudioDetailDto> GetExamStudioAsync(int examId, CancellationToken cancellationToken = default)
    {
        var exam = await db.Exams.AsNoTracking()
            .Include(e => e.Cert)
            .FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken);

        if (exam == null)
        {
            throw new ExamNotFoundException(examId);
        }

        var sections = await db.Sections.AsNoTracking()
            .Include(s => s.Skill)
            .Where(s => s.CertId == exam.CertId)
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(cancellationToken);

        var sectionAudios = await db.ExamSectionAudios.AsNoTracking()
            .Where(a => a.ExamId == examId)
            .ToListAsync(cancellationToken);

        var groups = await db.QuestionGroups.AsNoTracking()
            .Include(g => g.Section)
            .Include(g => g.QuestionGroups)
                .ThenInclude(q => q.QuestionOptionQuestionNavigations)
            .Include(g => g.QuestionGroups)
                .ThenInclude(q => q.Category)
            .Where(g => g.ExamId == examId)
            .OrderBy(g => g.DisplayOrder)
            // Two collection levels (questions, then options): split, or EF warns 20504 and the
            // rows multiply (see the readiness audit). Children are re-ordered in memory below.
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var allQuestionIds = groups.SelectMany(g => g.QuestionGroups.Select(q => q.QuestionId)).ToList();
        var answeredQuestionIds = new HashSet<int>(await db.AttemptAnswers.AsNoTracking()
            .Where(a => allQuestionIds.Contains(a.QuestionId))
            .Select(a => a.QuestionId)
            .Concat(db.FreeResponses.AsNoTracking().Where(r => allQuestionIds.Contains(r.QuestionId)).Select(r => r.QuestionId))
            .ToListAsync(cancellationToken));

        var availableCategories = await db.QuestionCategories.AsNoTracking()
            .Where(c => c.CertId == exam.CertId)
            .OrderBy(c => c.Code)
            .Select(c => new AdminQuestionCategoryOptionDto(c.CategoryId, c.Code, c.Name))
            .ToListAsync(cancellationToken);

        var availableCriteria = await db.GradingCriteria.AsNoTracking()
            .Where(c => c.CertId == exam.CertId)
            .OrderBy(c => c.DisplayOrder)
            .Select(c => new ExamStudioCriterionDto(c.CriterionId, c.Code, c.Name, null, c.MaxScore, c.SkillId))
            .ToListAsync(cancellationToken);

        var sectionCriteria = await db.SectionCriteria.AsNoTracking()
            .Where(sc => sections.Select(s => s.SectionId).Contains(sc.SectionId))
            .ToListAsync(cancellationToken);

        var audit = AuditExamPublishReadinessInternal(exam, sections, sectionAudios, groups, sectionCriteria);

        var sectionDtos = new List<ExamStudioSectionDto>();
        foreach (var sec in sections)
        {
            var secAudio = sectionAudios.FirstOrDefault(a => a.SectionId == sec.SectionId);
            var secGroups = groups.Where(g => g.SectionId == sec.SectionId).OrderBy(g => g.DisplayOrder).ToList();
            var secQuestions = secGroups.SelectMany(g => g.QuestionGroups).ToList();

            var groupDtos = new List<ExamStudioGroupDto>();
            foreach (var g in secGroups)
            {
                var qDtos = new List<ExamStudioQuestionDto>();
                foreach (var q in g.QuestionGroups.OrderBy(x => x.DisplayOrder))
                {
                    var opts = q.QuestionOptionQuestionNavigations
                        .OrderBy(o => o.Label)
                        .Select(o => new ExamStudioQuestionOptionDto(o.OptionId, o.QuestionId, o.Label, o.Text, o.IsCorrect, o.Rationale))
                        .ToList();

                    qDtos.Add(new ExamStudioQuestionDto(
                        QuestionId: q.QuestionId,
                        GroupId: q.GroupId,
                        ExamId: q.ExamId,
                        SectionId: q.SectionId,
                        SectionCode: sec.Code,
                        SectionName: sec.Name,
                        SkillId: q.SkillId,
                        SkillCode: sec.Skill?.Code ?? sec.Code,
                        Stem: q.Stem,
                        DifficultyLevel: q.DifficultyLevel,
                        QuestionType: EnumCodes.Parse<QuestionType>(q.QuestionType).MapTo<QuestionTypeDto>(),
                        Explanation: q.Explanation,
                        CategoryId: q.CategoryId,
                        CategoryCode: q.Category?.Code,
                        CategoryName: q.Category?.Name,
                        PrepSeconds: q.PrepSeconds,
                        ResponseSeconds: q.ResponseSeconds,
                        MinWords: q.MinWords,
                        MaxWords: q.MaxWords,
                        Hint: q.Hint,
                        DisplayOrder: q.DisplayOrder,
                        UpdatedAt: q.UpdatedAt,
                        Options: opts,
                        Criteria: availableCriteria
                            .Where(c => c.SkillId == q.SkillId
                                && sectionCriteria.Any(sc => sc.SectionId == q.SectionId && sc.CriterionId == c.CriterionId))
                            .ToList(),
                        HasAnswers: answeredQuestionIds.Contains(q.QuestionId),
                        MaxSelections: q.MaxSelections,
                        AllowNumber: q.AllowNumber,
                        AcceptedAnswersJson: q.AcceptedAnswersJson,
                        RequiredKeywords: q.RequiredKeywords));
                }

                groupDtos.Add(new ExamStudioGroupDto(
                    GroupId: g.GroupId,
                    ExamId: g.ExamId,
                    CertId: g.CertId,
                    SectionId: g.SectionId,
                    SectionCode: sec.Code,
                    SectionName: sec.Name,
                    Modality: EnumCodes.Parse<Modality>(sec.Modality).MapTo<ModalityDto>(),
                    Passage: g.Passage,
                    AudioPath: MediaUrlHelper.NormalizeMediaUrl(g.AudioPath),
                    AudioStartMs: g.AudioStartMs,
                    AudioEndMs: g.AudioEndMs,
                    ImagePath: MediaUrlHelper.NormalizeMediaUrl(g.ImagePath),
                    ImageAlt: g.ImageAlt,
                    Transcript: g.Transcript,
                    PrepSeconds: g.PrepSeconds,
                    ResponseSeconds: g.ResponseSeconds,
                    DisplayOrder: g.DisplayOrder,
                    Questions: qDtos,
                    UnitNumber: g.UnitNumber));
            }

            var hasAudio = secAudio != null || secGroups.Any(g => !string.IsNullOrWhiteSpace(g.AudioPath));
            sectionDtos.Add(new ExamStudioSectionDto(
                SectionId: sec.SectionId,
                CertId: sec.CertId,
                Code: sec.Code,
                Name: sec.Name,
                Modality: EnumCodes.Parse<Modality>(sec.Modality).MapTo<ModalityDto>(),
                ExpectedQuestionCount: sec.ExpectedQuestionCount,
                DisplayOrder: sec.DisplayOrder,
                CurrentQuestionCount: secQuestions.Count,
                HasAudio: hasAudio,
                MasterAudioPath: MediaUrlHelper.NormalizeMediaUrl(secAudio?.AudioPath),
                MasterAudioDurationSec: secAudio?.DurationSec,
                Groups: groupDtos,
                OptionCount: sec.OptionCount,
                ClusterSize: sec.ClusterSize,
                UnitCount: sec.UnitCount,
                HideOptionText: sec.HideOptionText,
                RequirePassage: sec.RequirePassage,
                RequireAudio: sec.RequireAudio,
                RequireImage: sec.RequireImage,
                RequiredKeywordCount: sec.RequiredKeywordCount,
                AllowedComponentTypesJson: sec.AllowedComponentTypesJson,
                LayoutType: sec.LayoutType,
                NavigationMode: sec.NavigationMode,
                TimerPolicyJson: sec.TimerPolicyJson,
                ToolsConfigJson: sec.ToolsConfigJson,
                SkillId: sec.SkillId,
                SkillCode: sec.Skill?.Code ?? sec.Code,
                SkillName: sec.Skill?.Name ?? sec.Name));
        }

        var finishedAttemptCount = await db.ExamAttempts.CountAsync(
            a => a.ExamId == examId && (a.Status == "submitted" || a.Status == "graded"), cancellationToken);

        return new ExamStudioDetailDto(
            ExamId: exam.ExamId,
            CertId: exam.CertId,
            CertCode: exam.Cert.Code,
            CertName: exam.Cert.Name,
            Name: exam.Name,
            Status: EnumCodes.Parse<ExamStatus>(exam.Status).MapTo<ExamStatusDto>(),
            Purpose: EnumCodes.Parse<ExamPurpose>(exam.Purpose).MapTo<ExamPurposeDto>(),
            DurationMinutes: exam.DurationMinutes,
            Description: exam.Description,
            SourceName: exam.SourceName,
            InternalNote: exam.InternalNote,
            QuestionCount: exam.QuestionCount,
            ActualQuestionCount: allQuestionIds.Count,
            FinishedAttemptCount: finishedAttemptCount,
            CreatedAt: exam.CreatedAt,
            UpdatedAt: exam.UpdatedAt,
            Sections: sectionDtos,
            AvailableCategories: availableCategories,
            AvailableCriteria: availableCriteria,
            Audit: audit);
    }
}
