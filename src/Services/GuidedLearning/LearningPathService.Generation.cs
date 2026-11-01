using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Services.Credits;

namespace Services;

public sealed partial class LearningPathService
{
    /// <summary>#154: the module a generated path's catalogue items live in, inside the
    /// certificate's own template. One per template, reused by every learner — the *steps* are what
    /// differ, and they point at different items.</summary>
    private const string CatalogueModuleTitle = "Path drills";

    /// <summary>#154: questions in one practice step. Ten is a sitting, and the step's plan length
    /// is stated in minutes beside it.</summary>
    private const int PracticeQuestionsPerStep = 10;

    public async Task<LearningPathSummaryView> GeneratePathAsync(int userId, string? targetBandCode = null, CancellationToken cancellationToken = default, bool archiveExisting = false)
    {
        var activeCertId = await learners.GetActiveCertIdAsync(userId, cancellationToken);

        var existingActive = await db.LearnerPaths
            .FirstOrDefaultAsync(p => p.UserId == userId && p.CertId == activeCertId && p.Status == "active", cancellationToken);
        if (existingActive != null)
        {
            if (archiveExisting)
            {
                existingActive.Status = "reset";
                await db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                throw new LearningPathException(LearningPathException.PathAlreadyActive, "An active learning path already exists for this certificate.");
            }
        }

        var savedTarget = await db.TargetBands
            .AsNoTracking()
            .FirstOrDefaultAsync(tb => tb.UserId == userId && tb.CertId == activeCertId, cancellationToken);

        var latestPlacement = await db.PlacementTests
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.CertId == activeCertId && p.Status == PlacementStatus.Done.ToCode())
            .OrderByDescending(p => p.TakenAt)
            .FirstOrDefaultAsync(cancellationToken);

        string? startingBand = latestPlacement?.ResultBandCode;
        if (string.IsNullOrEmpty(startingBand))
        {
            var latestMock = await db.ExamAttempts
                .AsNoTracking()
                .Where(a => a.UserId == userId && a.CertId == activeCertId && a.Status == AttemptStatus.Graded.ToCode() && a.Exam.Purpose == ExamPurpose.Mock.ToCode() && a.BandCode != null)
                .OrderByDescending(a => a.SubmittedAt ?? a.StartedAt)
                .FirstOrDefaultAsync(cancellationToken);
            startingBand = latestMock?.BandCode;
        }

        // #153: the band the goal recorded is the fallback — never a hardcoded band code that matches no
        // certificate's rows. With neither, the certificate cannot say where the learner starts.
        startingBand ??= savedTarget?.CurrentBandCode;

        var scoreBands = await db.ScoreBands
            .AsNoTracking()
            .Where(b => b.CertId == activeCertId)
            .OrderBy(b => b.DisplayOrder)
            .ThenBy(b => b.MinTotal)
            .ToListAsync(cancellationToken);

        if (string.IsNullOrEmpty(startingBand))
        {
            throw new LearningPathException(
                LearningPathException.StartingBandUnknown,
                "This certificate cannot say which band you start from. Take the free placement test first, or set your current band in your goal.");
        }

        var toBand = targetBandCode ?? "";
        if (string.IsNullOrEmpty(toBand))
        {
            toBand = savedTarget?.TargetBandCode ?? "";
        }

        if (string.IsNullOrEmpty(toBand))
        {
            throw new LearningPathException(
                LearningPathException.TargetBandMissing,
                "No target band is set for this certificate. Set your goal first — a path aimed at an invented band would not be your path.");
        }

        RefuseUnrealisticGoal(savedTarget, startingBand, toBand, scoreBands);

        // #154: the template is a container, not the plan. A path row and its steps carry its id, so a
        // row has to exist for this exact band pair — but nothing is read off its items to decide what
        // the learner does. AC7: the "no matching pair, so take any template for this certificate"
        // branch is gone; that branch silently attached learners to a plan nobody wrote for them.
        var template = await db.PathTemplates
            .FirstOrDefaultAsync(t => t.CertId == activeCertId && t.FromBandCode == startingBand && t.ToBandCode == toBand, cancellationToken);

        if (template is null)
        {
            template = new PathTemplate
            {
                CertId = activeCertId,
                FromBandCode = startingBand,
                ToBandCode = toBand,
                Name = $"{startingBand} to {toBand} learning path"
            };
            db.PathTemplates.Add(template);
            await db.SaveChangesAsync(cancellationToken);
        }

        var catalogue = await EnsureCatalogueAsync(template.TemplateId, activeCertId, userId, cancellationToken);

        if (catalogue.DrillableSectionCount == 0)
        {
            throw new LearningPathException(
                LearningPathException.NoQuestionsAvailable,
                "This certificate has no section with questions to practise yet, so a path would have no steps. Add questions first.");
        }

        var targetBand = scoreBands.FirstOrDefault(b => b.Code == toBand);
        var targetAccuracy = LearningPathPlanner.TargetAccuracyFor(
            isTopBand: targetBand is not null && targetBand.DisplayOrder >= scoreBands.Max(b => b.DisplayOrder));

        // #154: the plan is arithmetic. WeeklyStudyHours is the volume; the weakness order is what
        // makes two learners with the same band pair differ. A goal written before #153 (or by the
        // placement flow, which sets a band pair) carries no pace — the plan then uses the smallest
        // week the ticket allows rather than refusing to build anything, and says so on the screen.
        var weeklyHours = savedTarget?.WeeklyStudyHours ?? LearningPathPlanner.DefaultWeeklyHours;

        var plan = LearningPathPlanner.Plan(
            weeklyStudyHours: weeklyHours,
            weakSectionCount: catalogue.WeakSectionCount,
            lessonCount: catalogue.LessonCount,
            mockExamCount: catalogue.MockExamCount);

        var learnerPath = new LearnerPath
        {
            UserId = userId,
            CertId = activeCertId,
            TemplateId = template.TemplateId,
            CurrentBandCode = startingBand,
            PlacementId = latestPlacement?.PlacementId,
            Status = "active",
            StartedAt = DateTimeOffset.UtcNow
        };

        try
        {
            db.LearnerPaths.Add(learnerPath);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.Entry(learnerPath).State = EntityState.Detached;
            var concurrent = await db.LearnerPaths
                .FirstOrDefaultAsync(p => p.UserId == userId && p.CertId == activeCertId && p.Status == "active", cancellationToken);
            if (concurrent is not null)
            {
                throw new LearningPathException(LearningPathException.PathAlreadyActive, "An active learning path already exists for this certificate.");
            }
            throw;
        }

        var isFirst = true;
        foreach (var planned in plan)
        {
            var itemId = catalogue.ResolveItemId(planned);

            db.LearnerPathSteps.Add(new LearnerPathStep
            {
                LearnerPathId = learnerPath.LearnerPathId,
                PathModuleItemId = itemId,
                TemplateId = template.TemplateId,
                Status = isFirst ? "available" : "locked",
                Score = null,
                WeekNumber = planned.WeekNumber,
                EstimatedMinutes = planned.EstimatedMinutes,
                TargetAccuracyPct = targetAccuracy,
                ActualAccuracyPct = null,
                StartedAt = null,
                CompletedAt = null
            });
            isFirst = false;
        }

        await db.SaveChangesAsync(cancellationToken);

        return await GetActivePathAsync(userId, cancellationToken);
    }

    /// <summary>
    /// #154: the items a plan can point at, ordered by this learner's own weakness. A step has to
    /// reference a real <c>PathModuleItem</c>, so the certificate's template gets one module holding
    /// one item per drillable section, per lesson and per published mock exam — created once and
    /// reused. The items are the same for every learner; which of them a learner's steps point at is
    /// the personal part, and it is decided here from <c>vw_UserSectionAccuracyAll</c>.
    /// </summary>
    /// <param name="WeakSectionCount">How many distinct sections the plan may drill, weakest first.</param>
    /// <param name="LessonCount">Lessons available to plan.</param>
    /// <param name="MockExamCount">Published mock exams available to plan.</param>
    /// <param name="ResolveItemId">Maps a planned step to the item id it should reference.</param>
    private sealed record PathCatalogue(
        int WeakSectionCount,
        int LessonCount,
        int MockExamCount,
        int DrillableSectionCount,
        Func<PlannedPathStep, int> ResolveItemId);

    private async Task<PathCatalogue> EnsureCatalogueAsync(
        int templateId, int certId, int userId, CancellationToken cancellationToken)
    {
        var sectionRows = await db.Sections
            .AsNoTracking()
            .Where(s => s.CertId == certId)
            .OrderBy(s => s.Skill.DisplayOrder)
            .ThenBy(s => s.DisplayOrder)
            .Select(s => s.SectionId)
            .ToListAsync(cancellationToken);

        // Only a section with questions can be drilled: a step pointing at an empty section would
        // throw "no questions available" the moment the learner opened it.
        var drillableSectionIds = await db.Questions
            .AsNoTracking()
            .Where(q => q.Section.CertId == certId && q.QuestionType == QuestionType.Mcq.ToCode())
            .Select(q => q.SectionId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var drillable = sectionRows.Where(drillableSectionIds.Contains).ToList();

        // #154: the sections in *this* learner's weakness order — worst accuracy first. A learner with
        // no history has no weaknesses to rank, so the certificate's own section order stands in, and
        // the handoff's "say so rather than inventing weaknesses" holds.
        var accuracy = await db.vw_UserSectionAccuracyAlls
            .AsNoTracking()
            .Where(w => w.UserId == userId && drillable.Contains(w.SectionId))
            .ToDictionaryAsync(w => w.SectionId, w => (double)(w.Correct ?? 0) / Math.Max(1, w.Answered ?? 1), cancellationToken);

        var ranked = drillable
            .OrderBy(s => accuracy.TryGetValue(s, out var pct) ? pct : 1d)
            .ThenBy(s => drillable.IndexOf(s))
            .ToList();

        var lessons = await db.Lessons
            .AsNoTracking()
            .Where(l => l.CertId == certId)
            .OrderBy(l => l.DisplayOrder)
            .ThenBy(l => l.LessonId)
            .Select(l => l.LessonId)
            .ToListAsync(cancellationToken);

        var mockExams = await db.Exams
            .AsNoTracking()
            .Where(e => e.CertId == certId
                && e.Purpose == ExamPurpose.Mock.ToCode()
                && e.Status == ExamStatus.Published.ToCode()
                && !e.IsDeleted)
            .OrderBy(e => e.ExamId)
            .Select(e => e.ExamId)
            .ToListAsync(cancellationToken);

        if (ranked.Count == 0)
        {
            return new PathCatalogue(0, lessons.Count, mockExams.Count, 0, _ => 0);
        }

        var module = await db.PathModules
            .FirstOrDefaultAsync(m => m.TemplateId == templateId && m.Title == CatalogueModuleTitle, cancellationToken);

        if (module is null)
        {
            module = new PathModule
            {
                TemplateId = templateId,
                CertId = certId,
                SkillId = await db.Skills
                    .Where(s => s.CertId == certId)
                    .OrderBy(s => s.DisplayOrder)
                    .Select(s => s.SkillId)
                    .FirstAsync(cancellationToken),
                DisplayOrder = 900,
                Title = CatalogueModuleTitle,
                GroupName = "Drills",
                Description = "Practice, lesson and mock-exam steps the planner picks from.",
                EstimatedMinutes = 30
            };
            db.PathModules.Add(module);
            await db.SaveChangesAsync(cancellationToken);
        }

        var sectionItemIds = await EnsureSectionItemsAsync(module.ModuleId, templateId, certId, ranked, cancellationToken);
        var lessonItemIds = await EnsureLessonItemsAsync(module.ModuleId, templateId, certId, lessons, cancellationToken);
        var examItemIds = await EnsureExamItemsAsync(module.ModuleId, templateId, certId, mockExams, cancellationToken);

        var rankCount = Math.Min(LearningPathPlanner.DrilledSectionCount, sectionItemIds.Count);

        int Resolve(PlannedPathStep step)
        {
            return step.Kind switch
            {
                PathStepKind.Lesson when lessonItemIds.Count > 0
                    => lessonItemIds[Math.Min(step.WeekNumber - 1, lessonItemIds.Count - 1)],
                PathStepKind.MockExam when examItemIds.Count > 0
                    => examItemIds[Math.Min(Math.Max(0, (step.WeekNumber / 2) - 1), examItemIds.Count - 1)],
                _ => sectionItemIds[Math.Min(step.SectionRank, sectionItemIds.Count - 1)],
            };
        }

        return new PathCatalogue(rankCount, lessonItemIds.Count, examItemIds.Count, ranked.Count, Resolve);
    }

    private async Task<List<int>> EnsureSectionItemsAsync(
        int moduleId, int templateId, int certId, IReadOnlyList<int> sectionIds, CancellationToken cancellationToken)
    {
        var existing = await db.PathModuleItems
            .Where(i => i.TemplateId == templateId
                && i.ItemType == PathModuleItemType.PracticeSection.ToCode()
                && i.RefSectionId != null)
            .GroupBy(i => i.RefSectionId!.Value)
            .Select(g => new { SectionId = g.Key, ItemId = g.Min(i => i.ItemId) })
            .ToDictionaryAsync(x => x.SectionId, x => x.ItemId, cancellationToken);

        var order = 1;
        foreach (var sectionId in sectionIds)
        {
            if (existing.ContainsKey(sectionId))
            {
                order++;
                continue;
            }

            var item = new PathModuleItem
            {
                ModuleId = moduleId,
                CertId = certId,
                TemplateId = templateId,
                ItemType = PathModuleItemType.PracticeSection.ToCode(),
                RefSectionId = sectionId,
                QuestionCount = PracticeQuestionsPerStep,
                IsPremiumOnly = false,
                DisplayOrder = order++
            };

            db.PathModuleItems.Add(item);
            await db.SaveChangesAsync(cancellationToken);
            existing[sectionId] = item.ItemId;
        }

        return sectionIds.Select(id => existing[id]).ToList();
    }

    private async Task<List<int>> EnsureLessonItemsAsync(
        int moduleId, int templateId, int certId, IReadOnlyList<int> lessonIds, CancellationToken cancellationToken)
    {
        var existing = await db.PathModuleItems
            .Where(i => i.TemplateId == templateId
                && i.ItemType == PathModuleItemType.Lesson.ToCode()
                && i.RefLessonId != null)
            .GroupBy(i => i.RefLessonId!.Value)
            .Select(g => new { LessonId = g.Key, ItemId = g.Min(i => i.ItemId) })
            .ToDictionaryAsync(x => x.LessonId, x => x.ItemId, cancellationToken);

        for (var i = 0; i < lessonIds.Count; i++)
        {
            if (existing.ContainsKey(lessonIds[i]))
            {
                continue;
            }

            var item = new PathModuleItem
            {
                ModuleId = moduleId,
                CertId = certId,
                TemplateId = templateId,
                ItemType = PathModuleItemType.Lesson.ToCode(),
                RefLessonId = lessonIds[i],
                IsPremiumOnly = false,
                DisplayOrder = 100 + i
            };

            db.PathModuleItems.Add(item);
            await db.SaveChangesAsync(cancellationToken);
            existing[lessonIds[i]] = item.ItemId;
        }

        return lessonIds.Select(id => existing[id]).ToList();
    }

    private async Task<List<int>> EnsureExamItemsAsync(
        int moduleId, int templateId, int certId, IReadOnlyList<int> examIds, CancellationToken cancellationToken)
    {
        var existing = await db.PathModuleItems
            .Where(i => i.TemplateId == templateId
                && i.ItemType == PathModuleItemType.MockExam.ToCode()
                && i.RefExamId != null)
            .GroupBy(i => i.RefExamId!.Value)
            .Select(g => new { ExamId = g.Key, ItemId = g.Min(i => i.ItemId) })
            .ToDictionaryAsync(x => x.ExamId, x => x.ItemId, cancellationToken);

        for (var i = 0; i < examIds.Count; i++)
        {
            if (existing.ContainsKey(examIds[i]))
            {
                continue;
            }

            var item = new PathModuleItem
            {
                ModuleId = moduleId,
                CertId = certId,
                TemplateId = templateId,
                ItemType = PathModuleItemType.MockExam.ToCode(),
                RefExamId = examIds[i],
                IsPremiumOnly = false,
                DisplayOrder = 200 + i
            };

            db.PathModuleItems.Add(item);
            await db.SaveChangesAsync(cancellationToken);
            existing[examIds[i]] = item.ItemId;
        }

        return examIds.Select(id => existing[id]).ToList();
    }

    /// <summary>
    /// #153: an Unrealistic goal blocks path generation at the service layer, not just in the screen —
    /// a caller that skips the onboarding form still cannot get a path built on a goal the learner has
    /// no hours for. The verdict is recomputed here rather than read from the stored column, so a goal
    /// whose date has since passed is judged on today's runway. The message carries the two numbers
    /// (hours needed, hours available) because a refusal without them is indistinguishable from a bug.
    /// </summary>
    private static void RefuseUnrealisticGoal(
        TargetBand? goal,
        string startingBandCode,
        string targetBandCode,
        IReadOnlyList<ScoreBand> scoreBands)
    {
        if (goal is null)
        {
            return;
        }

        // AC4: the stored verdict is the contract. A goal an admin or a learner has already been
        // told is Unrealistic must not quietly become buildable because a band's study hours moved.
        if (goal.FeasibilityStatus is { } stored)
        {
            if (!string.Equals(stored, FeasibilityStatus.Unrealistic.ToCode(), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Both numbers, recomputed from the stored date and pace, so the refusal is never a
            // claim the learner has to take on trust.
            var weeksAvailable = goal.TargetDate is { } date
                ? Math.Max(1, (int)Math.Ceiling((date.DayNumber - DateOnly.FromDateTime(DateTime.UtcNow).DayNumber) / 7.0))
                : 0;
            var hoursAvailable = weeksAvailable * (goal.WeeklyStudyHours ?? 0m);
            var hoursNeeded = goal.RequiredTotalHours
                ?? scoreBands.FirstOrDefault(b => b.Code == targetBandCode)?.CumulativeStudyHours
                - scoreBands.FirstOrDefault(b => b.Code == startingBandCode)?.CumulativeStudyHours
                ?? 0;

            throw new LearningPathException(
                LearningPathException.GoalUnrealistic,
                $"This goal is marked unrealistic: it needs {hoursNeeded} hours of study, and you have " +
                $"{hoursAvailable:0.#} hours ({weeksAvailable} weeks at {goal.WeeklyStudyHours:0.#} h/week). " +
                "Move the exam date back, lower the target band, or raise your weekly pace — then your path can be built.");
        }

        // No stored verdict (an older row, or bands re-bandered since): judge it now rather than
        // assuming it is fine.
        if (goal.TargetDate is not { } targetDate || goal.WeeklyStudyHours is not { } weeklyHours)
        {
            return;   // no deadline: nothing to refuse
        }

        var fromHours = scoreBands.FirstOrDefault(b => b.Code == startingBandCode)?.CumulativeStudyHours;
        var toHours = scoreBands.FirstOrDefault(b => b.Code == targetBandCode)?.CumulativeStudyHours;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var feasibility = GoalFeasibilityCalculator.Evaluate(fromHours, toHours, targetDate, weeklyHours, today);
        if (feasibility?.Status != FeasibilityStatus.Unrealistic)
        {
            return;
        }

        throw new LearningPathException(
            LearningPathException.GoalUnrealistic,
            $"This goal does not fit the time you have: it needs {feasibility.RequiredHours} hours of study, " +
            $"and {feasibility.WeeksAvailable} weeks at {weeklyHours:0.#} h/week gives you {feasibility.AvailableHours:0.#} hours. " +
            "Move the exam date back, lower the target band, or raise your weekly pace — then your path can be built.");
    }
}
