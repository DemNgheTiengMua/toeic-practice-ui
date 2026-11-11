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

    /// <summary>
    /// #269: a section the paper has not assembled anything into, shaped as the Studio's empty rail -
    /// the blueprint, and no groups. The blueprint fields are the section's own, so the editor can still
    /// show what the section asks for before its first item arrives.
    /// </summary>
    private static ExamStudioSectionDto EmptyPoolSectionDto(Section sec, ExamSectionAudio? secAudio) =>
        new(
            SectionId: sec.SectionId,
            CertId: sec.CertId,
            Code: sec.Code,
            Name: sec.Name,
            Modality: EnumCodes.Parse<Modality>(sec.Modality).MapTo<ModalityDto>(),
            ExpectedQuestionCount: sec.ExpectedQuestionCount,
            DisplayOrder: sec.DisplayOrder,
            CurrentQuestionCount: 0,
            HasAudio: secAudio != null,
            MasterAudioPath: MediaUrlHelper.NormalizeMediaUrl(secAudio?.AudioPath),
            MasterAudioDurationSec: secAudio?.DurationSec,
            Groups: [],
            OptionCount: sec.OptionCount,
            ClusterSize: sec.ClusterSize,
            UnitCount: sec.UnitCount,
            HideOptionText: sec.HideOptionText,
            RequirePassage: sec.RequirePassage,
            RequireAudio: sec.RequireAudio,
            RequireImage: sec.RequireImage,
            RequiredKeywordCount: sec.RequiredKeywordCount,
            AllowedComponentTypesJson: sec.AllowedComponentTypesJson,
            LayoutType: sec.LayoutType ?? "STANDARD_VERTICAL",
            NavigationMode: sec.NavigationMode ?? "FULL_SANDBOX",
            TimerPolicyJson: sec.TimerPolicyJson,
            ToolsConfigJson: sec.ToolsConfigJson,
            SkillId: sec.SkillId,
            SkillCode: sec.Skill?.Code ?? sec.Code,
            SkillName: sec.Skill?.Name ?? sec.Name);

    /// <summary>
    /// #265: one section of a pool-authored paper, shaped as the editor's group tree.
    /// <para>
    /// <b>Why grouping is derived, not stored.</b> The editor's rail groups positions under a block that
    /// shares a passage or an audio slice. On the pool that block is the stimulus, and a position with
    /// no stimulus is its own block of one - which is exactly what a legacy <c>QuestionGroup</c> meant
    /// for a self-contained question. <c>GroupId</c> is the stimulus id, or the roster id negated for a
    /// solo position, so the two id spaces cannot collide.
    /// </para>
    /// <para>
    /// <b>Why the content comes from <see cref="SittingQuestion"/>.</b> That record is what the sitting
    /// renders: the pinned version's stem and options, the resolved audio chain, the word limits. The
    /// editor must show the same thing the learner will see, and reading it through the same seam means
    /// a binding's pinned version cannot differ between the two screens.
    /// </para>
    /// </summary>
    private ExamStudioSectionDto BuildPoolSectionDto(
        Section sec,
        ExamSectionAudio? secAudio,
        List<SittingQuestion> positions,
        IReadOnlyList<ExamStudioCriterionDto> availableCriteria,
        IReadOnlyList<SectionCriterion> sectionCriteria,
        HashSet<int> answeredPositionIds,
        IReadOnlyDictionary<int, string> levelByPositionId)
    {
        var groupDtos = new List<ExamStudioGroupDto>();
        var displayOrder = 0;

        foreach (var group in positions
            .GroupBy(p => p.GroupId ?? -p.ExamItemId)
            .OrderBy(g => g.Min(p => p.Id)))
        {
            var first = group.OrderBy(p => p.Id).First();
            var groupId = first.GroupId ?? -first.ExamItemId;

            var qDtos = new List<ExamStudioQuestionDto>();
            foreach (var position in group.OrderBy(p => p.Id))
            {
                displayOrder++;
                var examItemId = position.ExamItemId;

                qDtos.Add(new ExamStudioQuestionDto(
                    QuestionId: examItemId,
                    GroupId: groupId,
                    ExamId: position.ExamId,
                    SectionId: sec.SectionId,
                    SectionCode: sec.Code,
                    SectionName: sec.Name,
                    SkillId: sec.SkillId,
                    SkillCode: sec.Skill?.Code ?? sec.Code,
                    Stem: position.Stem,
                    QuestionType: EnumCodes.Parse<QuestionType>(position.RenderFormat).MapTo<QuestionTypeDto>(),
                    Explanation: position.Explanation,
                    // The pool's topic axis replaces the legacy category; a card shows the stem, and the
                    // topic is reachable from the item rather than from a category row.
                    CategoryId: null,
                    CategoryCode: null,
                    CategoryName: null,
                    PrepSeconds: position.PrepSeconds,
                    ResponseSeconds: position.ResponseSeconds,
                    MinWords: position.MinWords,
                    MaxWords: position.MaxWords,
                    Hint: position.Hint,
                    DisplayOrder: displayOrder,
                    UpdatedAt: null,
                    Options: position.Options
                        .Select(o => new ExamStudioQuestionOptionDto(o.OptionId, examItemId, o.Label, o.Text, o.IsCorrect, o.Rationale))
                        .ToList(),
                    Criteria: availableCriteria
                        .Where(c => c.SkillId == sec.SkillId
                            && sectionCriteria.Any(sc => sc.SectionId == sec.SectionId && sc.CriterionId == c.CriterionId))
                        .ToList(),
                    HasAnswers: answeredPositionIds.Contains(examItemId),
                    MaxSelections: position.MaxSelections,
                    AllowNumber: position.AllowNumber,
                    AcceptedAnswersJson: position.AcceptedAnswersJson,
                    RequiredKeywords: position.RequiredKeywords,
                    SampleAnswerOrImageDesc: position.ReferenceText,
                    ReferenceKind: position.ReferenceKind is null
                        ? null
                        : EnumCodes.Parse<ReferenceKindDto>(position.ReferenceKind),
                    CefrLevelCode: levelByPositionId.GetValueOrDefault(examItemId)));
            }

            groupDtos.Add(new ExamStudioGroupDto(
                GroupId: groupId,
                ExamId: first.ExamId,
                CertId: sec.CertId,
                SectionId: sec.SectionId,
                SectionCode: sec.Code,
                SectionName: sec.Name,
                Modality: EnumCodes.Parse<Modality>(sec.Modality).MapTo<ModalityDto>(),
                Passage: first.Group.Passage,
                AudioPath: MediaUrlHelper.NormalizeMediaUrl(first.Group.AudioPath),
                AudioStartMs: first.Group.AudioStartMs,
                AudioEndMs: first.Group.AudioEndMs,
                ImagePath: MediaUrlHelper.NormalizeMediaUrl(first.Group.ImagePath),
                ImageAlt: first.Group.ImageAlt,
                Transcript: first.Group.Transcript,
                PrepSeconds: first.PrepSeconds,
                ResponseSeconds: first.Group.ResponseSeconds,
                DisplayOrder: groupDtos.Count + 1,
                Questions: qDtos,
                UnitNumber: first.Group.UnitNumber ?? 1));
        }

        return new ExamStudioSectionDto(
            SectionId: sec.SectionId,
            CertId: sec.CertId,
            Code: sec.Code,
            Name: sec.Name,
            Modality: EnumCodes.Parse<Modality>(sec.Modality).MapTo<ModalityDto>(),
            ExpectedQuestionCount: sec.ExpectedQuestionCount,
            DisplayOrder: sec.DisplayOrder,
            CurrentQuestionCount: positions.Count,
            HasAudio: secAudio != null || groupDtos.Any(g => !string.IsNullOrWhiteSpace(g.AudioPath)),
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
            RequiredKeywordCount: 0,
            AllowedComponentTypesJson: sec.AllowedComponentTypesJson,
            LayoutType: sec.LayoutType ?? "STANDARD_VERTICAL",
            NavigationMode: sec.NavigationMode ?? "FULL_SANDBOX",
            TimerPolicyJson: sec.TimerPolicyJson,
            ToolsConfigJson: sec.ToolsConfigJson,
            SkillId: sec.SkillId,
            SkillCode: sec.Skill?.Code,
            SkillName: sec.Skill?.Name);
    }

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

        // #265 / #269: the Studio shows the paper, and the paper is whatever the SITTING reads. That
        // loader resolves the roster, which is now the paper's only shape - so the editor cannot
        // disagree with the exam a learner sits.
        var paper = await new AttemptEngine(db).LoadSittingQuestionsAsync(examId, cancellationToken);
        var rosterBySection = paper
            .GroupBy(p => p.SectionId)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.ExamItemId).ToList());

        // A position's authored level lives on the item, not on the sitting projection: the projection
        // exists to render, and a level is authoring metadata. Read once for the whole paper.
        var levelsByPosition = await db.ExamItems.AsNoTracking()
            .Where(i => i.ExamId == examId)
            .Join(db.Items.AsNoTracking(), i => i.ItemId, item => item.ItemId, (i, item) => new { i.ExamItemId, item.CefrLevel })
            .ToDictionaryAsync(x => x.ExamItemId, x => x.CefrLevel.Code, cancellationToken);

        var paperPositionIds = paper
            .Select(p => p.ExamItemId)
            .ToList();

        // #250: the badge reads the roster position an answer names - the only identity a paper has.
        var answeredQuestionIds = new HashSet<int>(await db.AttemptAnswers.AsNoTracking()
            .Where(a => paperPositionIds.Contains(a.ExamItemId))
            .Select(a => a.ExamItemId)
            .Concat(db.FreeResponses.AsNoTracking().Where(r => paperPositionIds.Contains(r.ExamItemId)).Select(r => r.ExamItemId))
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

        var audit = await AuditExamPublishReadinessInternal(exam, sections, sectionAudios, sectionCriteria, cancellationToken);

        var sectionDtos = new List<ExamStudioSectionDto>();
        foreach (var sec in sections)
        {
            var secAudio = sectionAudios.FirstOrDefault(a => a.SectionId == sec.SectionId);

            // #265 / #269: every section is shown from the pool. A section with no positions is shown
            // with no groups, which is what an unassembled section of the paper is.
            if (rosterBySection.TryGetValue(sec.SectionId, out var secPositions) && secPositions.Count > 0)
            {
                sectionDtos.Add(BuildPoolSectionDto(
                    sec, secAudio, secPositions, availableCriteria, sectionCriteria, answeredQuestionIds,
                    levelsByPosition));
            }
            else
            {
                sectionDtos.Add(EmptyPoolSectionDto(sec, secAudio));
            }
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
            ActualQuestionCount: paper.Count,
            FinishedAttemptCount: finishedAttemptCount,
            CreatedAt: exam.CreatedAt,
            UpdatedAt: exam.UpdatedAt,
            Sections: sectionDtos,
            AvailableCategories: availableCategories,
            AvailableCriteria: availableCriteria,
            Audit: audit);
    }
}
