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
            .GroupBy(p => p.GroupId ?? -(p.ExamItemId ?? p.Id))
            .OrderBy(g => g.Min(p => p.Id)))
        {
            var first = group.OrderBy(p => p.Id).First();
            var groupId = first.GroupId ?? -(first.ExamItemId ?? first.Id);

            var qDtos = new List<ExamStudioQuestionDto>();
            foreach (var position in group.OrderBy(p => p.Id))
            {
                displayOrder++;
                var examItemId = position.ExamItemId ?? position.Id;

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

        // #265: the Studio shows the paper, and the paper is whatever the SITTING reads. That loader
        // already resolves both shapes - the roster for a content-model paper, the legacy rows for a
        // paper authored before it - so the editor cannot disagree with the exam a learner sits.
        //
        // Only a ROSTER position is edited through the pool path: a legacy paper's positions carry a
        // QuestionId and the group tree below is their editor. Filtering here is what keeps the two
        // edit surfaces from both claiming the same section.
        var paper = await new AttemptEngine(db).LoadSittingQuestionsAsync(examId, cancellationToken);
        var rosterBySection = paper
            .Where(p => p.ExamItemId.HasValue)
            .GroupBy(p => p.SectionId)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.ExamItemId).ToList());

        var isPoolPaper = rosterBySection.Count > 0;

        // A position's authored level lives on the item, not on the sitting projection: the projection
        // exists to render, and a level is authoring metadata. Read once for the whole paper.
        var levelsByPosition = isPoolPaper
            ? await db.ExamItems.AsNoTracking()
                .Where(i => i.ExamId == examId)
                .Join(db.Items.AsNoTracking(), i => i.ItemId, item => item.ItemId, (i, item) => new { i.ExamItemId, item.CefrLevel })
                .ToDictionaryAsync(x => x.ExamItemId, x => x.CefrLevel.Code, cancellationToken)
            : new Dictionary<int, string>();

        var allQuestionIds = groups.SelectMany(g => g.QuestionGroups.Select(q => q.QuestionId)).ToList();
        var paperPositionIds = paper
            .Where(p => p.ExamItemId.HasValue)
            .Select(p => p.ExamItemId!.Value)
            .ToList();

        // #250: a pool answer names its roster position and has a NULL QuestionId, so the badge must
        // consult both shapes or a pool paper's cards all read "unanswered".
        var answeredQuestionIds = new HashSet<int>(await db.AttemptAnswers.AsNoTracking()
            .Where(a => a.QuestionId != null && allQuestionIds.Contains(a.QuestionId.Value))
            .Select(a => a.QuestionId!.Value)
            .Concat(db.FreeResponses.AsNoTracking().Where(r => r.QuestionId != null && allQuestionIds.Contains(r.QuestionId.Value)).Select(r => r.QuestionId!.Value))
            .Concat(db.AttemptAnswers.AsNoTracking().Where(a => a.ExamItemId != null && paperPositionIds.Contains(a.ExamItemId.Value)).Select(a => a.ExamItemId!.Value))
            .Concat(db.FreeResponses.AsNoTracking().Where(r => r.ExamItemId != null && paperPositionIds.Contains(r.ExamItemId.Value)).Select(r => r.ExamItemId!.Value))
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

        var audit = await AuditExamPublishReadinessInternal(exam, sections, sectionAudios, groups, sectionCriteria, cancellationToken);

        var sectionDtos = new List<ExamStudioSectionDto>();
        foreach (var sec in sections)
        {
            var secAudio = sectionAudios.FirstOrDefault(a => a.SectionId == sec.SectionId);

            // #265: a section whose positions are on the roster is shown from the pool; every other
            // section keeps the group tree. Per section rather than per paper, because a transition
            // paper can hold both and a screen that showed nothing for one of them would be a
            // regression an admin cannot work around.
            if (rosterBySection.TryGetValue(sec.SectionId, out var secPositions) && secPositions.Count > 0)
            {
                sectionDtos.Add(BuildPoolSectionDto(
                    sec, secAudio, secPositions, availableCriteria, sectionCriteria, answeredQuestionIds,
                    levelsByPosition));
                continue;
            }

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
                        RequiredKeywords: q.RequiredKeywords,
                        SampleAnswerOrImageDesc: q.SampleAnswerOrImageDesc,
                        ReferenceKind: q.ReferenceKind is null
                            ? null
                            : EnumCodes.Parse<ReferenceKindDto>(q.ReferenceKind),
                        CefrLevelCode: q.CefrLevel?.Code));
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
            ActualQuestionCount: isPoolPaper ? paper.Count(p => p.ExamItemId.HasValue) : allQuestionIds.Count,
            FinishedAttemptCount: finishedAttemptCount,
            CreatedAt: exam.CreatedAt,
            UpdatedAt: exam.UpdatedAt,
            Sections: sectionDtos,
            AvailableCategories: availableCategories,
            AvailableCriteria: availableCriteria,
            Audit: audit);
    }
}
