using BusinessObjects;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class LearnerService
{
    private sealed record MissionSectionRow(
        int SectionId,
        string Code,
        string Name,
        string Modality,
        int DisplayOrder);

    private sealed record MissionCategoryRow(
        int CategoryId,
        string Code,
        string Name);

    private sealed record WeaknessCandidate(
        bool IsCategory,
        int SectionId,
        string SectionCode,
        string SectionName,
        string? CategoryCode,
        string? CategoryName,
        int Answered,
        int Correct,
        int AccuracyPercent,
        double ExactAccuracy,
        int DisplayOrder,
        int SecondaryOrder);

    private async Task<IReadOnlyList<MissionStep>> BuildMissionStepsAsync(
        int userId,
        int certId,
        CancellationToken cancellationToken)
    {
        var sections = await db.Sections
            .AsNoTracking()
            .Where(s => s.CertId == certId)
            .OrderBy(s => s.DisplayOrder)
            .ThenBy(s => s.SectionId)
            .Select(s => new MissionSectionRow(s.SectionId, s.Code, s.Name, s.Modality, s.DisplayOrder))
            .ToListAsync(cancellationToken);

        var receptiveSections = sections
            .Where(s => s.Modality is "listening" or "reading")
            .ToList();
        if (receptiveSections.Count == 0)
        {
            receptiveSections = sections;
        }

        var sectionById = sections.ToDictionary(s => s.SectionId);
        var sectionIds = sections.Select(s => s.SectionId).ToList();

        var sectionAccRows = sectionIds.Count == 0
            ? []
            : await db.vw_UserSectionAccuracyAlls
                .AsNoTracking()
                .Where(v => v.UserId == userId && sectionIds.Contains(v.SectionId) && (v.Answered ?? 0) > 0)
                .ToListAsync(cancellationToken);

        var categories = await db.QuestionCategories
            .AsNoTracking()
            .Where(c => c.CertId == certId)
            .Select(c => new MissionCategoryRow(c.CategoryId, c.Code, c.Name))
            .ToListAsync(cancellationToken);

        var categoryById = categories.ToDictionary(c => c.CategoryId);
        var categoryIds = categories.Select(c => c.CategoryId).ToList();

        var categoryAccRows = categoryIds.Count == 0
            ? []
            : await db.vw_UserCategoryAccuracies
                .AsNoTracking()
                .Where(v => v.UserId == userId
                    && v.CategoryId != null
                    && categoryIds.Contains(v.CategoryId.Value)
                    && (v.Answered ?? 0) > 0)
                .ToListAsync(cancellationToken);

        var categorySectionLinks = categoryAccRows.Count == 0
            ? []
            : await db.Questions
                .AsNoTracking()
                .Where(q => q.CategoryId != null
                    && categoryIds.Contains(q.CategoryId.Value)
                    && sectionIds.Contains(q.SectionId))
                .Select(q => new { CategoryId = q.CategoryId!.Value, q.SectionId })
                .Distinct()
                .ToListAsync(cancellationToken);

        var todayStartUtc = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var clearedSectionIdsToday = (await db.PracticeSessions
            .AsNoTracking()
            .Where(p => p.UserId == userId
                && p.Section.CertId == certId
                && p.Status == PracticeStatus.Finished.ToCode()
                && (p.FinishedAt ?? p.StartedAt) >= todayStartUtc)
            .Select(p => p.SectionId)
            .Distinct()
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var (step1, step1SectionId) = await BuildStep1Async(
            userId,
            certId,
            receptiveSections,
            sectionById,
            sectionAccRows,
            categoryById,
            categoryAccRows,
            categorySectionLinks.Select(x => (x.CategoryId, x.SectionId)).ToList(),
            clearedSectionIdsToday,
            cancellationToken);

        var step2 = await BuildStep2Async(
            userId,
            certId,
            step1SectionId,
            receptiveSections,
            sectionAccRows,
            clearedSectionIdsToday,
            cancellationToken);

        var step3 = await BuildStep3Async(userId, certId, cancellationToken);

        return [step1, step2, step3];
    }

    private async Task<(MissionStep Step, int? TargetSectionId)> BuildStep1Async(
        int userId,
        int certId,
        List<MissionSectionRow> receptiveSections,
        Dictionary<int, MissionSectionRow> sectionById,
        List<vw_UserSectionAccuracyAll> sectionAccRows,
        Dictionary<int, MissionCategoryRow> categoryById,
        List<vw_UserCategoryAccuracy> categoryAccRows,
        List<(int CategoryId, int SectionId)> categorySectionLinks,
        HashSet<int> clearedSectionIdsToday,
        CancellationToken cancellationToken)
    {
        var candidates = new List<WeaknessCandidate>();

        foreach (var row in sectionAccRows)
        {
            if (!sectionById.TryGetValue(row.SectionId, out var sec))
            {
                continue;
            }

            var answered = row.Answered ?? 0;
            var correct = row.Correct ?? 0;
            if (answered <= 0)
            {
                continue;
            }

            var pct = (int)Math.Round((double)correct / answered * 100);
            candidates.Add(new WeaknessCandidate(
                IsCategory: false,
                SectionId: sec.SectionId,
                SectionCode: sec.Code,
                SectionName: sec.Name,
                CategoryCode: null,
                CategoryName: null,
                Answered: answered,
                Correct: correct,
                AccuracyPercent: pct,
                ExactAccuracy: (double)correct / answered,
                DisplayOrder: sec.DisplayOrder,
                SecondaryOrder: sec.SectionId));
        }

        var defaultSection = receptiveSections.FirstOrDefault();
        foreach (var row in categoryAccRows)
        {
            if (row.CategoryId is null || !categoryById.TryGetValue(row.CategoryId.Value, out var cat))
            {
                continue;
            }

            var answered = row.Answered ?? 0;
            var correct = row.Correct ?? 0;
            if (answered <= 0)
            {
                continue;
            }

            var linkedSection = categorySectionLinks
                .Where(l => l.CategoryId == cat.CategoryId && sectionById.ContainsKey(l.SectionId))
                .Select(l => sectionById[l.SectionId])
                .OrderBy(s => s.DisplayOrder)
                .ThenBy(s => s.SectionId)
                .FirstOrDefault() ?? defaultSection;

            if (linkedSection is null)
            {
                continue;
            }

            var pct = (int)Math.Round((double)correct / answered * 100);
            candidates.Add(new WeaknessCandidate(
                IsCategory: true,
                SectionId: linkedSection.SectionId,
                SectionCode: linkedSection.Code,
                SectionName: linkedSection.Name,
                CategoryCode: cat.Code,
                CategoryName: cat.Name,
                Answered: answered,
                Correct: correct,
                AccuracyPercent: pct,
                ExactAccuracy: (double)correct / answered,
                DisplayOrder: linkedSection.DisplayOrder,
                SecondaryOrder: cat.CategoryId));
        }

        if (candidates.Count > 0)
        {
            var weakest = candidates
                .OrderBy(c => c.AccuracyPercent)
                .ThenBy(c => c.ExactAccuracy)
                .ThenByDescending(c => c.Answered)
                .ThenBy(c => c.DisplayOrder)
                .ThenBy(c => c.IsCategory ? 0 : 1)
                .ThenBy(c => c.SecondaryOrder)
                .First();

            var isCleared = clearedSectionIdsToday.Contains(weakest.SectionId);
            var severity = isCleared
                ? "success"
                : weakest.AccuracyPercent < 50
                    ? "danger"
                    : weakest.AccuracyPercent < 65
                        ? "warning"
                        : "info";

            var badgeLabel = isCleared
                ? "DRILL CLEARED"
                : weakest.AccuracyPercent < 50
                    ? "CRITICAL DRILL"
                    : "WEAKNESS DRILL";

            var eyebrow = weakest.IsCategory
                ? $"{weakest.SectionCode} · {weakest.CategoryCode} // ACCURACY: {weakest.AccuracyPercent}%"
                : $"{weakest.SectionCode} // ACCURACY: {weakest.AccuracyPercent}%";

            var title = weakest.IsCategory
                ? $"Drill {weakest.CategoryName} ({weakest.SectionName})"
                : $"Drill {weakest.SectionName}";

            var description = weakest.IsCategory
                ? $"Accuracy is {weakest.AccuracyPercent}% ({weakest.Correct}/{weakest.Answered}) on {weakest.CategoryName} in {weakest.SectionName}. Complete a focused practice session."
                : $"Accuracy is {weakest.AccuracyPercent}% ({weakest.Correct}/{weakest.Answered}) in {weakest.SectionName} ({weakest.SectionCode}). Complete a focused practice session.";

            return (
                new MissionStep(
                    StepNumber: 1,
                    BadgeLabel: badgeLabel,
                    Severity: severity,
                    Eyebrow: eyebrow,
                    Title: title,
                    Description: description,
                    MetaLabel: $"{weakest.Answered} ANSWERED",
                    ActionLabel: isCleared ? "Practice again" : "Start practice",
                    ActionUrl: "/practice",
                    IsCleared: isCleared),
                weakest.SectionId);
        }

        var hasCompletedPlacement = await db.PlacementTests
            .AsNoTracking()
            .AnyAsync(p => p.UserId == userId && p.CertId == certId && p.Status == PlacementStatus.Done.ToCode(), cancellationToken);

        if (!hasCompletedPlacement)
        {
            return (
                new MissionStep(
                    StepNumber: 1,
                    BadgeLabel: "CALIBRATION",
                    Severity: "info",
                    Eyebrow: "STEP 01 // DIAGNOSTIC BASELINE",
                    Title: "Take Free Placement Exam",
                    Description: "Establish your baseline band and unlock a personalised learning path for this certificate.",
                    MetaLabel: "FREE DIAGNOSTIC",
                    ActionLabel: "Take placement exam",
                    ActionUrl: "/placement",
                    IsCleared: false),
                null);
        }

        var starter = defaultSection;
        var starterCleared = starter is not null && clearedSectionIdsToday.Contains(starter.SectionId);
        return (
            new MissionStep(
                StepNumber: 1,
                BadgeLabel: "STARTER DRILL",
                Severity: starterCleared ? "success" : "info",
                Eyebrow: starter is not null ? $"{starter.Code} // STARTER SECTION" : "STEP 01 // STARTER SECTION",
                Title: starter is not null ? $"Practice {starter.Name}" : "Start Section Practice",
                Description: starter is not null
                    ? $"Placement calibrated. Begin drilling {starter.Name} ({starter.Code}) to build your accuracy profile."
                    : "Placement calibrated. Begin a listening or reading practice session to build your accuracy profile.",
                MetaLabel: "0 ANSWERED",
                ActionLabel: "Start practice",
                ActionUrl: "/practice",
                IsCleared: starterCleared),
            starter?.SectionId);
    }

    private async Task<MissionStep> BuildStep2Async(
        int userId,
        int certId,
        int? step1SectionId,
        List<MissionSectionRow> receptiveSections,
        List<vw_UserSectionAccuracyAll> sectionAccRows,
        HashSet<int> clearedSectionIdsToday,
        CancellationToken cancellationToken)
    {
        var skillPurposes = new[] { ExamPurpose.SpeakingPractice.ToCode(), ExamPurpose.WritingPractice.ToCode() };

        var latestSkillAttempt = await db.ExamAttempts
            .AsNoTracking()
            .Where(a => a.UserId == userId
                && a.CertId == certId
                && skillPurposes.Contains(a.Exam.Purpose)
                && a.Status != AttemptStatus.Abandoned.ToCode())
            .OrderByDescending(a => a.SubmittedAt ?? a.StartedAt)
            .ThenByDescending(a => a.AttemptId)
            .Select(a => new
            {
                a.AttemptId,
                a.Status,
                a.StartedAt,
                a.SubmittedAt,
                ExamName = a.Exam.Name,
                Purpose = a.Exam.Purpose,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (latestSkillAttempt is not null)
        {
            var isSpeaking = latestSkillAttempt.Purpose == ExamPurpose.SpeakingPractice.ToCode();
            var skillPrefix = isSpeaking ? "speaking" : "writing";
            var skillLabel = isSpeaking ? "Speaking" : "Writing";

            if (latestSkillAttempt.Status == AttemptStatus.InProgress.ToCode())
            {
                return new MissionStep(
                    StepNumber: 2,
                    BadgeLabel: "IN PROGRESS",
                    Severity: "warning",
                    Eyebrow: $"{skillLabel.ToUpperInvariant()} PRACTICE // IN PROGRESS",
                    Title: $"Resume {latestSkillAttempt.ExamName}",
                    Description: $"You have an unfinished {skillLabel.ToLowerInvariant()} practice attempt on {latestSkillAttempt.ExamName}. Continue where you left off.",
                    MetaLabel: "STATUS: IN PROGRESS",
                    ActionLabel: $"Continue {skillLabel.ToLowerInvariant()}",
                    ActionUrl: $"/{skillPrefix}/take/{latestSkillAttempt.AttemptId}",
                    IsCleared: false);
            }

            if (latestSkillAttempt.Status == AttemptStatus.Submitted.ToCode())
            {
                return new MissionStep(
                    StepNumber: 2,
                    BadgeLabel: "GRADING QUEUED",
                    Severity: "info",
                    Eyebrow: $"{skillLabel.ToUpperInvariant()} PRACTICE // AI GRADING",
                    Title: $"Grading {latestSkillAttempt.ExamName}",
                    Description: $"Your {skillLabel.ToLowerInvariant()} response for {latestSkillAttempt.ExamName} is queued for AI rubric grading.",
                    MetaLabel: "STATUS: GRADING QUEUED",
                    ActionLabel: "View grading status",
                    ActionUrl: $"/{skillPrefix}/pending/{latestSkillAttempt.AttemptId}",
                    IsCleared: false);
            }

            var gradedTimestamp = latestSkillAttempt.SubmittedAt ?? latestSkillAttempt.StartedAt;
            if (latestSkillAttempt.Status == AttemptStatus.Graded.ToCode()
                && gradedTimestamp >= DateTimeOffset.UtcNow.AddHours(-24))
            {
                return new MissionStep(
                    StepNumber: 2,
                    BadgeLabel: "SKILL GRADED",
                    Severity: "success",
                    Eyebrow: $"{skillLabel.ToUpperInvariant()} PRACTICE // COMPLETED",
                    Title: $"Review {latestSkillAttempt.ExamName}",
                    Description: $"Your {skillLabel.ToLowerInvariant()} practice attempt on {latestSkillAttempt.ExamName} was graded within the last 24 hours.",
                    MetaLabel: "STATUS: GRADED",
                    ActionLabel: "Review grading",
                    ActionUrl: $"/{skillPrefix}/review/{latestSkillAttempt.AttemptId}",
                    IsCleared: true);
            }
        }

        var publishedSkillExam = await db.Exams
            .AsNoTracking()
            .Where(e => e.CertId == certId
                && !e.IsDeleted
                && e.Status == ExamStatus.Published.ToCode()
                && skillPurposes.Contains(e.Purpose))
            .OrderBy(e => e.ExamId)
            .Select(e => new { e.ExamId, e.Name, e.Purpose, e.DurationMinutes })
            .FirstOrDefaultAsync(cancellationToken);

        if (publishedSkillExam is not null)
        {
            var isSpeaking = publishedSkillExam.Purpose == ExamPurpose.SpeakingPractice.ToCode();
            var skillPrefix = isSpeaking ? "speaking" : "writing";
            var skillLabel = isSpeaking ? "Speaking" : "Writing";

            return new MissionStep(
                StepNumber: 2,
                BadgeLabel: "SKILL PRACTICE",
                Severity: "info",
                Eyebrow: $"{skillLabel.ToUpperInvariant()} PRACTICE // FREE RESPONSE",
                Title: $"Start {publishedSkillExam.Name}",
                Description: $"Practice {skillLabel.ToLowerInvariant()} under timed conditions with AI rubric grading on {publishedSkillExam.Name}.",
                MetaLabel: $"DURATION: {publishedSkillExam.DurationMinutes} MIN",
                ActionLabel: $"Start {skillLabel.ToLowerInvariant()}",
                ActionUrl: $"/{skillPrefix}/{publishedSkillExam.ExamId}/instructions",
                IsCleared: false);
        }

        // Fall back to second-weakest (or next unpracticed) listening/reading section
        var accBySectionId = sectionAccRows.ToDictionary(r => r.SectionId);
        var orderedSections = receptiveSections
            .Select(s =>
            {
                accBySectionId.TryGetValue(s.SectionId, out var row);
                var answered = row?.Answered ?? 0;
                var correct = row?.Correct ?? 0;
                var pct = answered > 0 ? (int)Math.Round((double)correct / answered * 100) : 0;
                return new
                {
                    Section = s,
                    Answered = answered,
                    Correct = correct,
                    AccuracyPercent = pct,
                    ExactAccuracy = answered > 0 ? (double)correct / answered : 1.0,
                };
            })
            .OrderBy(x => x.Answered > 0 ? 0 : 1)
            .ThenBy(x => x.AccuracyPercent)
            .ThenBy(x => x.ExactAccuracy)
            .ThenByDescending(x => x.Answered)
            .ThenBy(x => x.Section.DisplayOrder)
            .ThenBy(x => x.Section.SectionId)
            .ToList();

        var excludedSectionId = step1SectionId ?? orderedSections.FirstOrDefault()?.Section.SectionId;
        var fallback = orderedSections.Count > 1
            ? orderedSections.FirstOrDefault(x => x.Section.SectionId != excludedSectionId) ?? orderedSections[0]
            : orderedSections.FirstOrDefault();

        if (fallback is null)
        {
            return new MissionStep(
                StepNumber: 2,
                BadgeLabel: "SECTION FOCUS",
                Severity: "info",
                Eyebrow: "STEP 02 // SECTION DRILL",
                Title: "Secondary Section Practice",
                Description: "Complete a listening or reading section practice session to broaden your accuracy history.",
                MetaLabel: "0 ANSWERED",
                ActionLabel: "Start practice",
                ActionUrl: "/practice",
                IsCleared: false);
        }

        var isFallbackCleared = clearedSectionIdsToday.Contains(fallback.Section.SectionId);
        var fallbackSeverity = isFallbackCleared
            ? "success"
            : fallback.Answered > 0 && fallback.AccuracyPercent < 50
                ? "danger"
                : fallback.Answered > 0 && fallback.AccuracyPercent < 65
                    ? "warning"
                    : "info";

        return new MissionStep(
            StepNumber: 2,
            BadgeLabel: "SECTION FOCUS",
            Severity: fallbackSeverity,
            Eyebrow: fallback.Answered > 0
                ? $"{fallback.Section.Code} // ACCURACY: {fallback.AccuracyPercent}%"
                : $"{fallback.Section.Code} // UNTESTED SECTION",
            Title: $"Practice {fallback.Section.Name}",
            Description: fallback.Answered > 0
                ? $"Target your secondary section ({fallback.Section.Name}, {fallback.AccuracyPercent}% accuracy across {fallback.Answered} answered)."
                : $"Expand your diagnostic coverage by drilling {fallback.Section.Name} ({fallback.Section.Code}).",
            MetaLabel: $"{fallback.Answered} ANSWERED",
            ActionLabel: "Start practice",
            ActionUrl: "/practice",
            IsCleared: isFallbackCleared);
    }

    private async Task<MissionStep> BuildStep3Async(
        int userId,
        int certId,
        CancellationToken cancellationToken)
    {
        var savedCount = await db.SavedQuestions
            .AsNoTracking()
            .CountAsync(sq => sq.UserId == userId && sq.Question.Section.CertId == certId, cancellationToken);

        if (savedCount > 0)
        {
            return new MissionStep(
                StepNumber: 3,
                BadgeLabel: "TRAP LEDGER",
                Severity: "warning",
                Eyebrow: $"STEP 03 // {savedCount} SAVED",
                Title: $"Reconcile {savedCount} Saved Questions",
                Description: $"You have {savedCount} saved or missed {(savedCount == 1 ? "question" : "questions")} waiting for review on this certificate.",
                MetaLabel: $"{savedCount} SAVED",
                ActionLabel: "Reconcile questions",
                ActionUrl: "/saved-questions",
                IsCleared: false);
        }

        return new MissionStep(
            StepNumber: 3,
            BadgeLabel: "TRAP LEDGER CLEAN",
            Severity: "success",
            Eyebrow: "STEP 03 // 0 SAVED",
            Title: "Trap Ledger Clean",
            Description: "Questions saved or missed during practice sessions appear here for targeted reconciliation.",
            MetaLabel: "0 SAVED",
            ActionLabel: "Start practice",
            ActionUrl: "/practice",
            IsCleared: true);
    }
}

