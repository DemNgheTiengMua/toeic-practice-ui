using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;

namespace Services;

public class LearningPathException(string code, string message) : Exception(message)
{
    public const string PathNotFound = "path_not_found";
    public const string PathAlreadyActive = "path_already_active";
    public const string TemplateNotFound = "template_not_found";
    public const string ModuleNotFound = "module_not_found";
    public const string StepNotFound = "step_not_found";
    public const string StepLocked = "step_locked";
    public const string NoQuestionsAvailable = "no_questions_available";

    public string Code { get; } = code;
}

public interface ILearningPathService
{
    Task<LearningPathSummaryView> GetActivePathAsync(int userId, CancellationToken cancellationToken = default);

    Task<LearningPathSummaryView> GeneratePathAsync(int userId, string? targetBandCode = null, CancellationToken cancellationToken = default);

    Task<LearningPathModuleDetailView> GetModuleDetailAsync(int userId, int moduleId, CancellationToken cancellationToken = default);

    Task<StartStepResult> StartStepAsync(int userId, int stepId, CancellationToken cancellationToken = default);

    Task<bool> CompleteStepAsync(int userId, int stepId, decimal? score = null, CancellationToken cancellationToken = default);

    Task<bool> ResetActivePathAsync(int userId, CancellationToken cancellationToken = default);
}

public sealed class LearningPathService(
    CompanionDbContext db,
    ILearnerService learners) : ILearningPathService
{
    public async Task<LearningPathSummaryView> GetActivePathAsync(int userId, CancellationToken cancellationToken = default)
    {
        var activeCertId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var cert = await db.Certificates
            .AsNoTracking()
            .Where(c => c.CertId == activeCertId)
            .Select(c => new CertificateOption(c.Code, c.Name))
            .SingleAsync(cancellationToken);

        var activePath = await db.LearnerPaths
            .AsNoTracking()
            .Include(p => p.PathTemplate)
                .ThenInclude(t => t.ScoreBand)
            .Include(p => p.PathTemplate)
                .ThenInclude(t => t.ScoreBandNavigation)
            .Include(p => p.LearnerPathSteps)
                .ThenInclude(s => s.PathModuleItem)
                    .ThenInclude(pmi => pmi.PathModule)
                        .ThenInclude(m => m.Skill)
            .Where(p => p.UserId == userId && p.CertId == activeCertId && p.Status == "active")
            .FirstOrDefaultAsync(cancellationToken);

        var scoreBands = await db.ScoreBands
            .AsNoTracking()
            .Where(b => b.CertId == activeCertId)
            .OrderBy(b => b.DisplayOrder)
            .ThenBy(b => b.MinTotal)
            .ToListAsync(cancellationToken);

        if (activePath == null)
        {
            return new LearningPathSummaryView(
                HasActivePath: false,
                ActiveCert: cert,
                LearnerPathId: null,
                FromBandCode: null,
                ToBandCode: null,
                FromBandName: null,
                ToBandName: null,
                CurrentBandCode: null,
                CurrentScore: null,
                TargetBandCode: null,
                TargetScore: null,
                Status: null,
                StartedAt: null,
                IsPremium: false,
                TotalModules: 0,
                CompletedModules: 0,
                TotalEstimatedMinutes: 0,
                RemainingMinutes: 0,
                CefrBands: scoreBands.Select(b => new CefrBandInfo(b.Code, b.Name, b.MinTotal, b.MaxTotal, false, false, false)).ToList(),
                Modules: [],
                NextModuleId: null);
        }

        var latestPlacement = await db.PlacementTests
            .AsNoTracking()
            .Where(p => p.PlacementId == activePath.PlacementId)
            .FirstOrDefaultAsync(cancellationToken);

        int? currentScore = latestPlacement?.TotalScore;
        var toBand = scoreBands.FirstOrDefault(b => b.Code == activePath.PathTemplate.ToBandCode);
        int? targetScore = toBand?.MinTotal;

        var cefrBands = scoreBands.Select(b => new CefrBandInfo(
            b.Code,
            b.Name,
            b.MinTotal,
            b.MaxTotal,
            currentScore.HasValue && currentScore.Value >= b.MinTotal,
            b.Code == activePath.CurrentBandCode,
            b.Code == activePath.PathTemplate.ToBandCode)).ToList();

        bool isPremium = await db.CreditTransactions
            .AnyAsync(t => t.LearnerPathId == activePath.LearnerPathId && t.Reason == "premium_path", cancellationToken);

        var templateModules = await db.PathModules
            .AsNoTracking()
            .Include(m => m.Skill)
            .Include(m => m.PathModuleItemPathModules)
            .Where(m => m.TemplateId == activePath.TemplateId)
            .OrderBy(m => m.DisplayOrder)
            .ToListAsync(cancellationToken);

        var moduleSummaries = new List<LearningPathModuleSummary>();
        int completedModulesCount = 0;
        int totalEstimatedMins = 0;
        int remainingMins = 0;
        int? nextModuleId = null;

        foreach (var module in templateModules)
        {
            var moduleItemIds = module.PathModuleItemPathModules.Select(i => i.ItemId).ToHashSet();
            var moduleSteps = activePath.LearnerPathSteps.Where(s => moduleItemIds.Contains(s.PathModuleItemId)).ToList();

            string status = "not_started";
            if (moduleSteps.Count > 0 && moduleSteps.All(s => s.Status == "done"))
            {
                status = "done";
                completedModulesCount++;
            }
            else if (moduleSteps.Any(s => s.Status is "in_progress" or "done"))
            {
                status = "in_progress";
            }
            else if (moduleSteps.Any(s => s.Status == "available"))
            {
                status = "not_started";
            }
            else if (moduleSteps.Count > 0 && moduleSteps.All(s => s.Status == "locked"))
            {
                status = "locked";
            }

            bool isLocked = status == "locked";
            bool canContinue = !isLocked && status != "done";
            int estMinutes = module.EstimatedMinutes ?? 15;
            totalEstimatedMins += estMinutes;
            if (status != "done")
            {
                remainingMins += estMinutes;
                nextModuleId ??= module.ModuleId;
            }

            int totalQ = module.PathModuleItemPathModules.Sum(i => i.QuestionCount ?? 20);
            int completedQ = moduleSteps.Where(s => s.Status == "done").Sum(s => s.PathModuleItem?.QuestionCount ?? 20);

            var activeStep = moduleSteps.FirstOrDefault(s => s.Status == "in_progress")
                ?? moduleSteps.FirstOrDefault(s => s.Status == "available")
                ?? moduleSteps.FirstOrDefault();

            moduleSummaries.Add(new LearningPathModuleSummary(
                module.ModuleId,
                module.TemplateId,
                module.Title,
                module.GroupName,
                module.Description,
                estMinutes,
                module.DisplayOrder,
                module.Skill.Code,
                module.Skill.Name,
                EnumCodes.Parse<Modality>(module.Skill.Modality),
                status,
                isLocked,
                canContinue,
                totalQ,
                completedQ,
                module.PathModuleItemPathModules.Count,
                moduleSteps.Count(s => s.Status == "done"),
                activeStep?.StepId));
        }

        var statusEnum = EnumCodes.Parse<LearnerPathStatus>(activePath.Status);

        return new LearningPathSummaryView(
            HasActivePath: true,
            ActiveCert: cert,
            LearnerPathId: activePath.LearnerPathId,
            FromBandCode: activePath.PathTemplate.FromBandCode,
            ToBandCode: activePath.PathTemplate.ToBandCode,
            FromBandName: activePath.PathTemplate.ScoreBand?.Name ?? activePath.PathTemplate.FromBandCode,
            ToBandName: activePath.PathTemplate.ScoreBandNavigation?.Name ?? activePath.PathTemplate.ToBandCode,
            CurrentBandCode: activePath.CurrentBandCode,
            CurrentScore: currentScore,
            TargetBandCode: activePath.PathTemplate.ToBandCode,
            TargetScore: targetScore,
            Status: statusEnum,
            StartedAt: activePath.StartedAt,
            IsPremium: isPremium,
            TotalModules: templateModules.Count,
            CompletedModules: completedModulesCount,
            TotalEstimatedMinutes: totalEstimatedMins,
            RemainingMinutes: remainingMins,
            CefrBands: cefrBands,
            Modules: moduleSummaries,
            NextModuleId: nextModuleId);
    }

    public async Task<LearningPathSummaryView> GeneratePathAsync(int userId, string? targetBandCode = null, CancellationToken cancellationToken = default)
    {
        var activeCertId = await learners.GetActiveCertIdAsync(userId, cancellationToken);

        var existingActive = await db.LearnerPaths
            .FirstOrDefaultAsync(p => p.UserId == userId && p.CertId == activeCertId && p.Status == "active", cancellationToken);
        if (existingActive != null)
        {
            throw new LearningPathException(LearningPathException.PathAlreadyActive, "An active learning path already exists for this certificate.");
        }

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

        var scoreBands = await db.ScoreBands
            .AsNoTracking()
            .Where(b => b.CertId == activeCertId)
            .OrderBy(b => b.DisplayOrder)
            .ThenBy(b => b.MinTotal)
            .ToListAsync(cancellationToken);

        if (string.IsNullOrEmpty(startingBand))
        {
            startingBand = scoreBands.FirstOrDefault()?.Code ?? "A1";
        }

        string toBand = targetBandCode ?? "";
        if (string.IsNullOrEmpty(toBand))
        {
            var savedTarget = await db.TargetBands
                .AsNoTracking()
                .FirstOrDefaultAsync(tb => tb.UserId == userId && tb.CertId == activeCertId, cancellationToken);
            toBand = savedTarget?.TargetBandCode ?? "";
        }

        if (string.IsNullOrEmpty(toBand))
        {
            toBand = scoreBands.LastOrDefault()?.Code ?? "B2";
        }

        // Match template: exact (from, to) -> closest from -> any for cert
        var template = await db.PathTemplates
            .Where(t => t.CertId == activeCertId && t.FromBandCode == startingBand && t.ToBandCode == toBand)
            .FirstOrDefaultAsync(cancellationToken);

        template ??= await db.PathTemplates
            .Where(t => t.CertId == activeCertId && t.FromBandCode == startingBand)
            .FirstOrDefaultAsync(cancellationToken);

        template ??= await db.PathTemplates
            .Where(t => t.CertId == activeCertId)
            .OrderBy(t => t.TemplateId)
            .FirstOrDefaultAsync(cancellationToken);

        if (template == null)
        {
            throw new LearningPathException(LearningPathException.TemplateNotFound, "No learning path template found for this certificate.");
        }

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

        var modules = await db.PathModules
            .Include(m => m.PathModuleItemPathModules)
            .Where(m => m.TemplateId == template.TemplateId)
            .OrderBy(m => m.DisplayOrder)
            .ToListAsync(cancellationToken);

        var allItems = modules
            .SelectMany(m => m.PathModuleItemPathModules.OrderBy(i => i.DisplayOrder))
            .ToList();

        bool isFirst = true;
        foreach (var item in allItems)
        {
            db.LearnerPathSteps.Add(new LearnerPathStep
            {
                LearnerPathId = learnerPath.LearnerPathId,
                PathModuleItemId = item.ItemId,
                TemplateId = template.TemplateId,
                Status = isFirst ? "available" : "locked",
                Score = null,
                StartedAt = null,
                CompletedAt = null
            });
            isFirst = false;
        }

        await db.SaveChangesAsync(cancellationToken);

        return await GetActivePathAsync(userId, cancellationToken);
    }

    public async Task<LearningPathModuleDetailView> GetModuleDetailAsync(int userId, int moduleId, CancellationToken cancellationToken = default)
    {
        var activeCertId = await learners.GetActiveCertIdAsync(userId, cancellationToken);

        var activePath = await db.LearnerPaths
            .AsNoTracking()
            .Include(p => p.PathTemplate)
            .Where(p => p.UserId == userId && p.CertId == activeCertId && p.Status == "active")
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new LearningPathException(LearningPathException.PathNotFound, "No active learning path found.");

        var module = await db.PathModules
            .AsNoTracking()
            .Include(m => m.Skill)
            .Include(m => m.PathModuleTopics)
            .Include(m => m.PathModuleItemPathModules)
            .Where(m => m.ModuleId == moduleId && m.TemplateId == activePath.TemplateId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new LearningPathException(LearningPathException.ModuleNotFound, $"Module {moduleId} was not found.");

        var allTemplateModules = await db.PathModules
            .AsNoTracking()
            .Where(m => m.TemplateId == activePath.TemplateId)
            .OrderBy(m => m.DisplayOrder)
            .ToListAsync(cancellationToken);

        int moduleIndex = allTemplateModules.FindIndex(m => m.ModuleId == moduleId) + 1;
        int totalModules = allTemplateModules.Count;

        var itemIds = module.PathModuleItemPathModules.Select(i => i.ItemId).ToHashSet();
        var steps = await db.LearnerPathSteps
            .AsNoTracking()
            .Include(s => s.PathModuleItem)
            .Include(s => s.PracticeSessions)
            .Where(s => s.LearnerPathId == activePath.LearnerPathId && itemIds.Contains(s.PathModuleItemId))
            .OrderBy(s => s.PathModuleItem.DisplayOrder)
            .ToListAsync(cancellationToken);

        bool isPremium = await db.CreditTransactions
            .AnyAsync(t => t.LearnerPathId == activePath.LearnerPathId && t.Reason == "premium_path", cancellationToken);

        string moduleStatus = "not_started";
        if (steps.Count > 0 && steps.All(s => s.Status == "done"))
        {
            moduleStatus = "done";
        }
        else if (steps.Any(s => s.Status is "in_progress" or "done"))
        {
            moduleStatus = "in_progress";
        }
        else if (steps.Any(s => s.Status == "available"))
        {
            moduleStatus = "not_started";
        }
        else if (steps.Count > 0 && steps.All(s => s.Status == "locked"))
        {
            moduleStatus = "locked";
        }

        bool isLocked = moduleStatus == "locked";

        int totalQuestions = module.PathModuleItemPathModules.Sum(i => i.QuestionCount ?? 20);
        var sessionIds = steps.SelectMany(s => s.PracticeSessions).Select(ps => ps.SessionId).ToList();
        var practiceAnswers = sessionIds.Count > 0
            ? await db.PracticeAnswers.AsNoTracking().Where(pa => sessionIds.Contains(pa.SessionId)).ToListAsync(cancellationToken)
            : new List<PracticeAnswer>();
        int answeredCount = practiceAnswers.Count(a => a.SelectedOptionId != null);
        int correctCount = practiceAnswers.Count(a => a.IsCorrect == true);
        int? accuracyPct = answeredCount > 0 ? (int)Math.Round((double)correctCount / answeredCount * 100) : null;

        int estMinutes = module.EstimatedMinutes ?? 20;
        var allSessions = steps.SelectMany(s => s.PracticeSessions).ToList();
        int timeSpentMinutes = (int)allSessions.Sum(s =>
        {
            if (s.FinishedAt.HasValue)
            {
                return Math.Max(1, (s.FinishedAt.Value - s.StartedAt).TotalMinutes);
            }
            var elapsed = (DateTimeOffset.UtcNow - s.StartedAt).TotalMinutes;
            return Math.Clamp(elapsed, 0, estMinutes * 2);
        });
        int estRemaining = Math.Max(0, estMinutes - timeSpentMinutes);

        int cumulativeAnswered = answeredCount;
        var topics = new List<ModuleTopicInfo>();
        foreach (var t in module.PathModuleTopics.OrderBy(t => t.DisplayOrder))
        {
            string topicStatus = "not_started";
            if (moduleStatus == "done")
            {
                topicStatus = "done";
            }
            else if (moduleStatus is "not_started" or "locked")
            {
                topicStatus = "not_started";
            }
            else // in_progress
            {
                int qCount = t.QuestionCount ?? 10;
                if (cumulativeAnswered >= qCount)
                {
                    topicStatus = "done";
                    cumulativeAnswered -= qCount;
                }
                else if (cumulativeAnswered > 0)
                {
                    topicStatus = "in_progress";
                    cumulativeAnswered = 0;
                }
                else
                {
                    topicStatus = "not_started";
                }
            }
            topics.Add(new ModuleTopicInfo(t.TopicId, t.Title, t.QuestionCount, topicStatus));
        }

        var stepInfos = steps.Select(s =>
        {
            var itemType = EnumCodes.Parse<PathModuleItemType>(s.PathModuleItem.ItemType);
            var stepStatus = EnumCodes.Parse<LearnerPathStepStatus>(s.Status);
            string title = itemType switch
            {
                PathModuleItemType.PracticeSection => $"Practice: {module.Title}",
                PathModuleItemType.MockExam => "Mock Exam Assessment",
                PathModuleItemType.AiTask => "AI-Graded Task",
                PathModuleItemType.Mentor => "Mentor Session",
                _ => module.Title
            };
            return new ModuleStepInfo(
                s.StepId,
                s.PathModuleItemId,
                itemType,
                title,
                stepStatus,
                s.Score,
                s.PathModuleItem.QuestionCount,
                s.PathModuleItem.IsPremiumOnly,
                CanStart: stepStatus != LearnerPathStepStatus.Locked,
                s.StartedAt,
                s.CompletedAt,
                s.PathModuleItem.RefSectionId,
                s.PathModuleItem.RefExamId);
        }).ToList();

        var activeStep = steps.FirstOrDefault(s => s.Status == "in_progress")
            ?? steps.FirstOrDefault(s => s.Status == "available")
            ?? steps.FirstOrDefault();

        return new LearningPathModuleDetailView(
            activePath.LearnerPathId,
            module.ModuleId,
            module.Title,
            module.GroupName,
            module.Description,
            estMinutes,
            module.DisplayOrder,
            module.Skill.Code,
            module.Skill.Name,
            EnumCodes.Parse<Modality>(module.Skill.Modality),
            moduleStatus,
            isLocked,
            moduleIndex,
            totalModules,
            activePath.PathTemplate.FromBandCode,
            activePath.PathTemplate.ToBandCode,
            answeredCount,
            totalQuestions,
            accuracyPct,
            correctCount,
            answeredCount,
            timeSpentMinutes,
            estRemaining,
            isPremium,
            topics,
            stepInfos,
            activeStep?.StepId);
    }

    public async Task<StartStepResult> StartStepAsync(int userId, int stepId, CancellationToken cancellationToken = default)
    {
        var step = await db.LearnerPathSteps
            .Include(s => s.LearnerPath)
            .Include(s => s.PathModuleItem)
                .ThenInclude(pmi => pmi.PathModule)
            .FirstOrDefaultAsync(s => s.StepId == stepId, cancellationToken)
            ?? throw new LearningPathException(LearningPathException.StepNotFound, $"Step {stepId} not found.");

        if (step.LearnerPath.UserId != userId || step.LearnerPath.Status != "active")
        {
            throw new LearningPathException(LearningPathException.StepNotFound, $"Step {stepId} not found.");
        }

        if (step.Status == "locked")
        {
            throw new LearningPathException(LearningPathException.StepLocked, "This step is locked.");
        }

        if (step.Status == "available")
        {
            step.Status = "in_progress";
            step.StartedAt ??= DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        var itemType = EnumCodes.Parse<PathModuleItemType>(step.PathModuleItem.ItemType);

        switch (itemType)
        {
            case PathModuleItemType.PracticeSection:
            {
                int sectionId = step.PathModuleItem.RefSectionId
                    ?? throw new InvalidOperationException($"Practice section step {stepId} is missing RefSectionId.");

                var existingSession = await db.PracticeSessions
                    .Where(ps => ps.LearnerPathStepId == stepId && ps.UserId == userId)
                    .OrderByDescending(ps => ps.StartedAt)
                    .FirstOrDefaultAsync(cancellationToken);

                if (existingSession != null)
                {
                    string target = existingSession.Status == PracticeStatus.Finished.ToCode()
                        ? $"/practice/{existingSession.SessionId}/summary"
                        : $"/practice/{existingSession.SessionId}";
                    return new StartStepResult(stepId, itemType, target, existingSession.SessionId, null);
                }

                var section = await db.Sections.SingleAsync(s => s.SectionId == sectionId, cancellationToken);
                var questionIds = await db.Questions
                    .Where(q => q.SectionId == sectionId && q.QuestionType == "mcq" && db.QuestionOptions.Any(o => o.QuestionId == q.QuestionId))
                    .OrderBy(q => q.DisplayOrder)
                    .ThenBy(q => q.QuestionId)
                    .Select(q => q.QuestionId)
                    .ToListAsync(cancellationToken);

                if (questionIds.Count == 0)
                {
                    throw new LearningPathException(LearningPathException.NoQuestionsAvailable, $"No practice questions are available for section {sectionId}.");
                }

                int qCount = Math.Min(questionIds.Count, step.PathModuleItem.QuestionCount ?? 20);
                if (qCount == 0 && questionIds.Count > 0) qCount = questionIds.Count;

                var session = new PracticeSession
                {
                    UserId = userId,
                    SectionId = sectionId,
                    Modality = section.Modality,
                    QuestionCount = Math.Max(1, qCount),
                    Status = PracticeStatus.InProgress.ToCode(),
                    CorrectCount = 0,
                    QuestionSource = QuestionSource.All.ToCode(),
                    FeedbackMode = FeedbackMode.Immediate.ToCode(),
                    LearnerPathStepId = step.StepId,
                    StartedAt = DateTimeOffset.UtcNow
                };

                db.PracticeSessions.Add(session);
                await db.SaveChangesAsync(cancellationToken);

                for (int i = 0; i < qCount; i++)
                {
                    db.PracticeAnswers.Add(new PracticeAnswer
                    {
                        SessionId = session.SessionId,
                        QuestionId = questionIds[i],
                        SectionId = sectionId,
                        SelectedOptionId = null,
                        IsCorrect = null,
                        HintUsed = false,
                        DisplayOrder = i + 1
                    });
                }

                await db.SaveChangesAsync(cancellationToken);
                return new StartStepResult(stepId, itemType, $"/practice/{session.SessionId}", session.SessionId, null);
            }

            case PathModuleItemType.MockExam:
            {
                int examId = step.PathModuleItem.RefExamId
                    ?? throw new InvalidOperationException($"Mock exam step {stepId} is missing RefExamId.");
                return new StartStepResult(stepId, itemType, $"/exams/{examId}/instructions", null, null);
            }

            case PathModuleItemType.AiTask:
            {
                if (step.PathModuleItem.RefExamId is { } refExamId)
                {
                    var exam = await db.Exams.FindAsync([refExamId], cancellationToken);
                    if (exam?.Purpose == "speaking_practice")
                    {
                        return new StartStepResult(stepId, itemType, $"/speaking/{refExamId}/instructions", null, null);
                    }
                    if (exam?.Purpose == "writing_practice")
                    {
                        return new StartStepResult(stepId, itemType, $"/writing/{refExamId}/instructions", null, null);
                    }
                    return new StartStepResult(stepId, itemType, $"/exams/{refExamId}/instructions", null, null);
                }
                return new StartStepResult(stepId, itemType, "/practice", null, null);
            }

            case PathModuleItemType.Mentor:
            default:
                return new StartStepResult(stepId, itemType, "/mentors", null, null);
        }
    }

    public async Task<bool> CompleteStepAsync(int userId, int stepId, decimal? score = null, CancellationToken cancellationToken = default)
    {
        var step = await db.LearnerPathSteps
            .Include(s => s.LearnerPath)
            .FirstOrDefaultAsync(s => s.StepId == stepId && s.LearnerPath.UserId == userId, cancellationToken);

        if (step == null) return false;

        if (step.Status == "done") return true;

        step.Status = "done";
        step.CompletedAt = DateTimeOffset.UtcNow;
        step.Score = score ?? step.Score ?? 100m;

        var allSteps = await db.LearnerPathSteps
            .Include(s => s.PathModuleItem)
                .ThenInclude(pmi => pmi.PathModule)
            .Where(s => s.LearnerPathId == step.LearnerPathId)
            .OrderBy(s => s.PathModuleItem.PathModule.DisplayOrder)
            .ThenBy(s => s.PathModuleItem.DisplayOrder)
            .ToListAsync(cancellationToken);

        var nextLocked = allSteps.FirstOrDefault(s => s.Status == "locked");
        if (nextLocked != null)
        {
            nextLocked.Status = "available";
        }

        if (allSteps.All(s => s.Status == "done"))
        {
            step.LearnerPath.Status = "completed";
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ResetActivePathAsync(int userId, CancellationToken cancellationToken = default)
    {
        var activeCertId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var path = await db.LearnerPaths
            .FirstOrDefaultAsync(p => p.UserId == userId && p.CertId == activeCertId && p.Status == "active", cancellationToken);

        if (path != null)
        {
            path.Status = "reset";
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        return false;
    }
}
