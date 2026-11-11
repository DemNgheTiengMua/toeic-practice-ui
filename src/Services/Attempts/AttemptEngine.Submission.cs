using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class AttemptEngine
{
    public async Task<AttemptGradingResult> SubmitAttemptAsync(
        int attemptId,
        int userId,
        bool isAutoSubmit,
        CancellationToken cancellationToken = default,
        IReadOnlyList<AttemptFinalDraft>? finalDrafts = null)
    {
        var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            // #247.4: SUBMIT IS A RACE, and the status check below is not enough to stop it. Two callers
            // - a learner double-clicking and an auto-submit firing, or a retried request - both read
            // 'in_progress', both score, and both insert AttemptSkillScores. The second insert dies on
            // PK_AttemptSkillScores and the caller gets a 500 for a paper that was in fact submitted.
            // Measured, not theorised: the concurrent test failed with "Cannot insert duplicate key ...
            // The duplicate key value is (1, 1)".
            //
            // The lock is taken on the attempt ROW, so the second caller blocks here and then reads the
            // state the first one committed - which is exactly what the early-return branch expects.
            // READPAST is deliberately NOT used: a caller that skipped a locked row would fall through
            // to scoring, which is the defect.
            await db.Database.ExecuteSqlRawAsync(
                $"SELECT 1 FROM ExamAttempts WITH (UPDLOCK, HOLDLOCK) WHERE AttemptId = {attemptId}",
                cancellationToken);

            var attempt = await db.ExamAttempts
                .Include(a => a.Exam)
                .Include(a => a.Cert)
                .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.UserId == userId, cancellationToken)
                ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

            if (attempt.Status == AttemptStatus.Graded.ToCode()
                || attempt.Status == AttemptStatus.Submitted.ToCode())
            {
                var existingScores = await db.AttemptSkillScores
                    .Where(s => s.AttemptId == attemptId)
                    .ToListAsync(cancellationToken);
                var existingAnswers = await db.AttemptAnswers
                    .Where(a => a.AttemptId == attemptId)
                    .ToListAsync(cancellationToken);

                if (tx is not null) await tx.RollbackAsync(cancellationToken);

                return new AttemptGradingResult(
                    attempt,
                    attempt.TotalScore ?? 0,
                    attempt.BandCode,
                    existingScores,
                    existingAnswers,
                    attempt.SubmittedAt ?? DateTimeOffset.UtcNow,
                    attempt.IsAutoSubmitted);
            }

            if (attempt.Status == AttemptStatus.Abandoned.ToCode())
            {
                throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "This attempt was abandoned.");
            }
            var now = DateTimeOffset.UtcNow;
            if (isAutoSubmit || now >= attempt.ExpiresAt)
            {
                attempt.IsAutoSubmitted = true;
            }

            var freeResponses = attempt.Exam.Purpose == ExamPurpose.Mock.ToCode() || attempt.Exam.Purpose == ExamPurpose.Placement.ToCode()
                ? await db.FreeResponses.Where(fr => fr.AttemptId == attemptId).ToListAsync(cancellationToken)
                : [];
            if (finalDrafts is { Count: > 0 })
            {
                // #269: a draft's `QuestionId` is the POSITION the client was handed - a question id on a
                // legacy paper, an ExamItemId on a pool one. The wire field keeps its name (the client did
                // not change); `Address` says what the value actually means.
                int Address(AttemptFinalDraft draft) => draft.QuestionId;

                if (finalDrafts.Select(Address).Distinct().Count() != finalDrafts.Count)
                {
                    throw new AttemptEngineException(
                        AttemptEngineException.QuestionNotFound,
                        "A final response draft was submitted more than once.");
                }

                foreach (var finalDraft in finalDrafts)
                {
                    // #269: the draft names a POSITION, which is a QuestionId on a legacy paper and an
                    // ExamItemId on a pool one. Matching on QuestionId alone missed every pool draft, so
                    // the create branch below inserted a second row for a position CreateAttemptAsync had
                    // already seeded - and UX_FreeResponses_ExamItem refused it with a duplicate key.
                    var response = freeResponses.SingleOrDefault(item => item.QuestionId == Address(finalDraft)
                        || item.ExamItemId == Address(finalDraft));
                    if (response is null)
                    {
                        // #250: the position comes from the paper, so a pool-only productive task
                        // resolves too. A response row names whichever identity its paper uses, and
                        // CK_FreeResp_HasShape guarantees at least one is set.
                        var question = await FindSittingQuestionAsync(attempt, Address(finalDraft), cancellationToken);

                        if (question.RenderFormat != QuestionType.FreeResponse.ToCode())
                        {
                            throw new AttemptEngineException(
                                AttemptEngineException.QuestionNotFound,
                                $"Question {finalDraft.QuestionId} is not a free-response task.");
                        }

                        response = new FreeResponse
                        {
                            AttemptId = attemptId,
                            QuestionId = question.LegacyQuestion is null ? null : question.Id,
                            ExamItemId = question.ExamItemId,
                            ExamId = attempt.ExamId,
                            SkillId = question.Section.SkillId,
                            QuestionType = QuestionType.FreeResponse.ToCode(),
                            ResponseText = finalDraft.ResponseText ?? string.Empty,
                            Status = FreeResponseStatus.Draft.ToCode(),
                            DraftSavedAt = now,
                            SubmittedAt = null
                        };
                        freeResponses.Add(response);
                        db.FreeResponses.Add(response);
                    }
                    else
                    {
                        response.ResponseText = finalDraft.ResponseText ?? string.Empty;
                        response.DraftSavedAt = now;
                    }
                }
            }

            // #217 / PLC-19: a productive task the learner never touched is settled immediately rather
            // than queued. The engine pre-creates a draft row per free-response question when the
            // sitting opens, so without this a blank paper would queue an AI grading for every writing
            // task and the sitting would sit at `grading` forever instead of recording the honest fact:
            // nothing was attempted.
            //
            // The row stays, because PLC-35's "one graded task of two" divides by the tasks the paper
            // DELIVERED, and deleting a blank one would silently turn an estimate into a full
            // measurement. It is settled as `graded` with no AiGrading row: settled, so the attempt can
            // finish, and unqueued, so no grader is spent on an empty answer.
            var untouchedDrafts = freeResponses
                .Where(fr => fr.Status == FreeResponseStatus.Draft.ToCode()
                    && string.IsNullOrWhiteSpace(fr.ResponseText)
                    && string.IsNullOrWhiteSpace(fr.AudioPath))
                .ToList();
            foreach (var untouched in untouchedDrafts)
            {
                untouched.Status = FreeResponseStatus.Graded.ToCode();
                untouched.SubmittedAt = now;
            }

            foreach (var response in freeResponses.Where(fr => fr.Status == FreeResponseStatus.Draft.ToCode()))
            {
                response.Status = FreeResponseStatus.PendingAi.ToCode();
                response.SubmittedAt = now;
            }

            if (freeResponses.Count > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            var hasPendingMockResponses = freeResponses.Count > 0
                && freeResponses.Any(fr => fr.Status != FreeResponseStatus.Graded.ToCode());
            attempt.SubmittedAt = now;
            attempt.Status = hasPendingMockResponses
                ? AttemptStatus.Submitted.ToCode()
                : AttemptStatus.Graded.ToCode();

            var answers = await db.AttemptAnswers
                .Where(a => a.AttemptId == attemptId)
                .Include(a => a.Question).ThenInclude(q => q.Section).ThenInclude(s => s.Skill)
                .ToListAsync(cancellationToken);
            var isPlacement = attempt.Exam.Purpose == ExamPurpose.Placement.ToCode();
            var questionIds = answers.Select(answer => answer.QuestionId).Distinct().ToList();
            var optionsByQuestion = (await db.QuestionOptions
                .Where(option => questionIds.Contains(option.QuestionId))
                .ToListAsync(cancellationToken))
                .ToLookup(option => option.QuestionId);

            // #186: a pinned answer is scored against the version it pinned, not the question row.
            var pinnedVersions = await LoadPinnedVersionsAsync(answers, cancellationToken);

            // #250: an answer on a pool-authored paper has no `Question` navigation - it names a roster
            // position instead. Scoring therefore cannot read `a.Question.QuestionType`; it asks the
            // paper, which is the one place that knows the render format and the section of a position.
            //
            // Measured before this: submitting a pool paper threw NullReferenceException at
            // `a.Question.QuestionType`, because the navigation is legitimately null when
            // CK_AttemptAnswers_HasShape chose the ExamItemId branch.
            var paperQuestions = await LoadSittingQuestionsAsync(attempt.ExamId, cancellationToken);
            var positionById = paperQuestions.ToDictionary(q => q.Id);
            var positionByExamItemId = paperQuestions
                .Where(q => q.ExamItemId.HasValue)
                .ToDictionary(q => q.ExamItemId!.Value);

            SittingQuestion? PositionOf(AttemptAnswer a) => a.ExamItemId is int examItemId
                ? positionByExamItemId.GetValueOrDefault(examItemId)
                : a.QuestionId is int questionId ? positionById.GetValueOrDefault(questionId) : null;

            bool IsProductiveAnswer(AttemptAnswer a) =>
                PositionOf(a)?.RenderFormat == QuestionType.FreeResponse.ToCode();

            // Score each objective answer (#217 / PLC-18). An unanswered item scores as incorrect by
            // NOT being correct - the skill's raw score counts only `IsCorrect == true` over a
            // denominator of every item the section delivered - but its IsCorrect stays NULL.
            //
            // Writing `false` here was the defect the pool accuracy views read: vw_UserSectionAccuracyAll
            // counts `COUNT(IsCorrect)`, so a blank marked incorrect was counted as an ANSWERED item and
            // inflated the denominator. #253's rule is that answered is "carries content", not "has a
            // grade"; leaving the blank unjudged is what makes the two agree.
            foreach (var ans in answers.Where(a => !IsProductiveAnswer(a)))
            {
                ans.IsCorrect = IsAnswered(ans) ? EvaluateAnswer(ans, pinnedVersions, optionsByQuestion) : null;
            }

            // Clean any existing skill scores for idempotent resubmits
            var existingSkillScores = await db.AttemptSkillScores
                .Where(s => s.AttemptId == attemptId)
                .ToListAsync(cancellationToken);
            if (existingSkillScores.Count > 0)
            {
                db.AttemptSkillScores.RemoveRange(existingSkillScores);
            }

            var skillGroups = answers
                .Where(a => !IsProductiveAnswer(a))
                .GroupBy(a => PositionOf(a)!.Section.Skill)
                // #217 / PLC-19: on a PLACEMENT, a skill the learner never touched is not part of the
                // result. It used to be scored as a zero row, and that zero was summed into the total -
                // the "not-measured contributing 0" the ticket forbids. Excluding it here is what makes
                // "excluded from the overall" true by construction rather than by a screen remembering
                // to skip a row. A skill where the learner answered something and got it all wrong is
                // still here, scored zero: that is a real measurement at the floor.
                //
                // A mock exam keeps every skill, because a blank paper there is a legitimate zero - the
                // learner sat it and chose not to answer. Placement's "not measured" is a result
                // concept (PLC-19); a mock has none.
                .Where(group => !isPlacement || group.Any(IsAnswered))
                .ToList();
            var newSkillScores = new List<AttemptSkillScore>();

            foreach (var group in skillGroups)
            {
                var skill = group.Key;
                var sectionGroups = group
                    .GroupBy(a => PositionOf(a)!.Section)
                    .ToList();
                int rawScore;
                int scaledScore;

                bool hasVaryingWeights = sectionGroups.Any(sg => sg.Key.ScoreWeight != sectionGroups[0].Key.ScoreWeight);
                if (hasVaryingWeights)
                {
                    decimal totalWeight = sectionGroups.Sum(sg => sg.Key.ScoreWeight > 0 ? sg.Key.ScoreWeight : 1.0m);
                    decimal weightedSectionRaw = sectionGroups.Sum(sg =>
                        (decimal)sg.Count(a => a.IsCorrect == true) * (sg.Key.ScoreWeight > 0 ? sg.Key.ScoreWeight : 1.0m)) / totalWeight;

                    rawScore = (int)Math.Round(weightedSectionRaw, MidpointRounding.AwayFromZero);
                    scaledScore = await ComputeScaledScoreAsync(
                        attempt.CertId, skill.SkillId, attempt.ExamId, weightedSectionRaw, group.Count(), cancellationToken);
                }
                else
                {
                    rawScore = group.Count(a => a.IsCorrect == true);
                    scaledScore = await ComputeScaledScoreAsync(
                        attempt.CertId, skill.SkillId, attempt.ExamId, rawScore, group.Count(), cancellationToken);
                }

                newSkillScores.Add(new AttemptSkillScore
                {
                    AttemptId = attempt.AttemptId,
                    SkillId = skill.SkillId,
                    CertId = attempt.CertId,
                    RawScore = rawScore,
                    ScaledScore = scaledScore
                });
            }

            db.AttemptSkillScores.AddRange(newSkillScores);

            var cert = await db.Certificates
                .Where(c => c.CertId == attempt.CertId)
                .Select(c => new { c.Code })
                .SingleAsync(cancellationToken);
            // #227 / #257: the rule is the one the edition froze, not today's certificate column.
            var aggregationRule = await LoadAggregationRuleAsync(attempt.ExamId, attempt.CertId, cancellationToken);
            // #217 / PLC-19: a placement where nothing could be measured has NO result. Writing the
            // aggregate of an empty score set would store `0`, and a 0 is a figure the learner reads as
            // "you scored zero" - the fabricated result the ticket forbids. The attempt and its
            // placement carry no total and no band; the placement's own writer moves it to `abandoned`
            // with the reason recorded.
            var measuredNothing = isPlacement && newSkillScores.Count == 0;
            var totalScore = measuredNothing ? (int?)null : AggregateTotalScore(aggregationRule, newSkillScores);
            attempt.TotalScore = totalScore;
            // #191: the derived fact on this table is TotalScore + BandCode, and this is its one
            // writer. The version moves forward on every scoring pass, so a stale out-of-order
            // callback (a duplicate submit racing this one) cannot overwrite the result it is behind.
            attempt.WrittenByService = DerivedFactWriters.AttemptScoreAndBand;
            attempt.ProjectionVersion++;
            db.Entry(attempt).Property(a => a.WrittenByService).IsModified = true;
            string? matchingBandCode = null;
            if (!hasPendingMockResponses && totalScore is int total)
            {
                matchingBandCode = await FindBandCodeAsync(attempt.CertId, total, cancellationToken);
                attempt.BandCode = matchingBandCode;
            }
            else
            {
                attempt.BandCode = null;
            }

            // #212 / #191: the placement snapshot is derived from the attempt, and this is the only
            // place that may write it. The rows are written BEFORE the placement's own completion is
            // decided, because the completion guard reads them: a done placement with nothing measured
            // is refused, and a productive task still in the AI queue leaves it in progress.
            //
            // The remaining drafts are not yet graded, so calling the writer here would report
            // `grading`. It is called on both paths - here for the objective half, and again from
            // FinalizeAiGradedAttemptAsync when the productive half settles - and it is idempotent, so
            // the second call rewrites only the rows that changed.
            var linkedPlacements = await db.PlacementTests
                .Where(p => p.AttemptId == attempt.AttemptId && p.Status == PlacementStatus.InProgress.ToCode())
                .Select(p => p.PlacementId)
                .ToListAsync(cancellationToken);

            // #217 / PLC-19: only a task that is actually QUEUED keeps the attempt `submitted`. The old
            // test was "does the paper carry any free response at all", which a settled blank task now
            // satisfies - and that left a paper with nothing to grade stuck at `submitted` forever.
            var hasFreeResponses = freeResponses.Any(fr => fr.Status != FreeResponseStatus.Graded.ToCode());
            attempt.Status = hasFreeResponses
                ? AttemptStatus.Submitted.ToCode()
                : AttemptStatus.Graded.ToCode();
            attempt.SubmittedAt = now;
            attempt.TotalScore = totalScore;
            attempt.BandCode = hasPendingMockResponses ? null : matchingBandCode;

            if (freeResponses.Count > 0)
            {
                var responseIds = freeResponses.Select(r => r.FreeResponseId).ToList();
                var existingGradingIds = await db.AiGradings
                    .Where(g => responseIds.Contains(g.FreeResponseId))
                    .Select(g => g.FreeResponseId)
                    .ToListAsync(cancellationToken);

                // Only a task that is waiting for the grader gets a grading row. A settled blank task
                // has no answer to score, and queueing it would spend a grader call on nothing and hold
                // the sitting open (see SettleFreeResponseAsync in AiGradingProcessor for the mirror
                // rule on the failure path).
                foreach (var response in freeResponses.Where(r =>
                    r.Status == FreeResponseStatus.PendingAi.ToCode()
                    && !existingGradingIds.Contains(r.FreeResponseId)))
                {
                    db.AiGradings.Add(new AiGrading
                    {
                        FreeResponseId = response.FreeResponseId,
                        SkillId = response.SkillId,
                        Status = AiGradingStatus.Pending.ToCode(),
                        AttemptCount = 1,
                        RequestedAt = now
                    });
                }
            }

            await db.SaveChangesAsync(cancellationToken);

            // #212: the per-skill rows and the placement's own completion, from what was just saved.
            // A productive task still in the queue reads as `grading` and leaves the placement in
            // progress; the objective half is measured and visible immediately (PLC-34).
            foreach (var placementId in linkedPlacements)
            {
                await PlacementResults.WriteAsync(placementId, cancellationToken);
            }

            // #154: a finished mock exam closes the path step that planned it. This is the writer that
            // did not exist — sitting the exam left the step available forever. Inside the same
            // transaction as the grading, so a step is never ticked for an attempt that failed to save.
            if (!hasPendingMockResponses
                && attempt.Exam.Purpose == ExamPurpose.Mock.ToCode()
                && attempt.BandCode is not null)
            {
                await PathStepCompletion.CloseAsync(
                    db,
                    attempt.UserId,
                    PathStepCompletion.Activity.MockSection,
                    attempt.ExamId,
                    score: attempt.TotalScore,
                    accuracyPct: null,
                    cancellationToken);
            }

            if (tx is not null) await tx.CommitAsync(cancellationToken);

            return new AttemptGradingResult(
                attempt,
                // A placement that measured nothing has no total; the caller's own view decides what to
                // show, and `0` here is only the DTO's non-nullable shape, never a stored figure.
                totalScore ?? 0,
                attempt.BandCode,
                newSkillScores,
                answers,
                now,
                attempt.IsAutoSubmitted);
        }
        finally
        {
            if (tx is not null) await tx.DisposeAsync();
        }
    }

    public async Task FinalizeAiGradedAttemptAsync(int attemptId, CancellationToken cancellationToken = default)
    {
        var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var attempt = await db.ExamAttempts
                .Include(a => a.Exam)
                .SingleOrDefaultAsync(a => a.AttemptId == attemptId, cancellationToken)
                ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

            if (attempt.Status == AttemptStatus.Graded.ToCode()) return;
            if ((attempt.Exam.Purpose != ExamPurpose.Mock.ToCode() && attempt.Exam.Purpose != ExamPurpose.Placement.ToCode())
                || attempt.Status != AttemptStatus.Submitted.ToCode())
            {
                throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "Only a submitted mock or placement attempt can be finalized after AI grading.");
            }

            var freeResponses = await db.FreeResponses
                .Where(fr => fr.AttemptId == attemptId)
                .Include(fr => fr.AiGradingFreeResponse).ThenInclude(g => g!.AiGradingScores)
                .ToListAsync(cancellationToken);

            // #217 / PLC-35: a task is SETTLED when its response is `graded` and its grading row, if any,
            // reached a terminal state. A blank task settled at submission carries no grading row at all
            // (there was nothing to score), so "has no row" must count as settled - requiring a `done`
            // row sent `Finalize` back for a sitting whose only unfinished work was a task nobody wrote.
            if (freeResponses.Count == 0
                || freeResponses.Any(fr => fr.Status != FreeResponseStatus.Graded.ToCode()
                    || fr.AiGradingFreeResponse?.Status is not (null or "done" or "failed")))
            {
                throw new AttemptEngineException(AttemptEngineException.AttemptNotFinished, "All free responses must finish AI grading before the result can be finalized.");
            }

            var answers = await db.AttemptAnswers
                .Where(a => a.AttemptId == attemptId)
                .Include(a => a.Question).ThenInclude(q => q.QuestionOptionQuestionNavigations)
                .ToListAsync(cancellationToken);

            // #269: a pool position has no question row, so the section, the skill and the length rule
            // come from the roster. The legacy branch keeps its question rows for one more release;
            // the readers below ask the paper, which knows both shapes.
            var paperQuestions = await LoadSittingQuestionsAsync(attempt.ExamId, cancellationToken);
            var positionByQuestionId = paperQuestions
                .Where(q => !q.ExamItemId.HasValue)
                .ToDictionary(q => q.Id);
            var positionByExamItemId = paperQuestions
                .Where(q => q.ExamItemId.HasValue)
                .ToDictionary(q => q.ExamItemId!.Value);

            SittingQuestion? PositionOfQuestion(int? examItemId, int? questionId) =>
                examItemId is int itemKey
                    ? positionByExamItemId.GetValueOrDefault(itemKey)
                    : questionId is int questionKey ? positionByQuestionId.GetValueOrDefault(questionKey) : null;

            SittingQuestion PositionOfResponse(FreeResponse fr) =>
                PositionOfQuestion(fr.ExamItemId, fr.QuestionId)
                ?? throw new AttemptEngineException(
                    AttemptEngineException.QuestionNotFound,
                    $"Free response {fr.FreeResponseId} names no position on attempt {attemptId}'s paper.");

            SittingQuestion? PositionOfAnswer(AttemptAnswer a) => PositionOfQuestion(a.ExamItemId, a.QuestionId);

            // #191: clearing the score is a write of the same derived fact, so it declares the same
            // writer. This runs before the rescore below and is what makes a resubmit start from a
            // known state rather than from the previous total.
            //
            // `Property(...).IsModified` is needed because EF omits a column whose value did not
            // change: on a second pass the stamp already holds this name, so without it the UPDATE
            // would carry no declaration and the trigger would - correctly - refuse it.
            attempt.TotalScore = null;
            attempt.WrittenByService = DerivedFactWriters.AttemptScoreAndBand;
            attempt.ProjectionVersion++;
            db.Entry(attempt).Property(a => a.WrittenByService).IsModified = true;
            await db.SaveChangesAsync(cancellationToken);

            var existingScores = await db.AttemptSkillScores
                .Where(s => s.AttemptId == attemptId)
                .ToListAsync(cancellationToken);
            db.AttemptSkillScores.RemoveRange(existingScores);

            foreach (var response in freeResponses)
            {
                var eval = EvaluateWritingResponse(
                    PositionOfResponse(response).MinWords,
                    PositionOfResponse(response).RequiredKeywords,
                    response.ResponseText);
                if (eval.IsUnderLength && response.AiGradingFreeResponse != null)
                {
                    foreach (var s in response.AiGradingFreeResponse.AiGradingScores)
                    {
                        if (string.IsNullOrWhiteSpace(s.Comment))
                        {
                            s.Comment = "IsUnderLength=true; Length penalty applied.";
                        }
                        else if (!s.Comment.Contains("IsUnderLength"))
                        {
                            s.Comment = $"{s.Comment} [IsUnderLength=true]";
                        }
                    }

                    var hasExistingRec = await db.Recommendations
                        .AnyAsync(r => r.GradingId == response.AiGradingFreeResponse.GradingId && r.Text.Contains("IsUnderLength"), cancellationToken);
                    if (!hasExistingRec)
                    {
                        db.Recommendations.Add(new Recommendation
                        {
                            UserId = attempt.UserId,
                            CertId = attempt.CertId,
                            AttemptId = attempt.AttemptId,
                            GradingId = response.AiGradingFreeResponse.GradingId,
                            SkillId = response.SkillId,
                            Severity = "minor",
                            Text = $"IsUnderLength=true: Bài viết có {eval.WordCount} từ, dưới mức tối thiểu {eval.MinWords} từ.",
                            CreatedAt = DateTimeOffset.UtcNow
                        });
                    }
                }
            }

            var fractionByResponse = new Dictionary<int, decimal>();
            foreach (var response in freeResponses)
            {
                var position = PositionOfResponse(response);
                var sectionCriteria = await db.SectionCriteria
                    .Where(sc => sc.SectionId == position.SectionId)
                    .Include(sc => sc.GradingCriterion)
                    .ToListAsync(cancellationToken);

                var maxScore = sectionCriteria.Count > 0
                    ? sectionCriteria.Sum(sc => sc.MaxScore ?? sc.GradingCriterion.MaxScore)
                    : await db.GradingCriteria
                        .Where(c => c.SkillId == response.SkillId)
                        .SumAsync(c => (decimal?)c.MaxScore, cancellationToken) ?? 0m;

                var fraction = maxScore > 0m
                    ? Math.Clamp((response.AiGradingFreeResponse!.OverallScaled ?? 0m) / maxScore, 0m, 1m)
                    : 0m;
                fractionByResponse[response.FreeResponseId] = fraction;
            }

            // #186: the same pinned-version rule as the other scoring pass, so an attempt finalized
            // after AI grading cannot score against a question row the learner never saw.
            var pinnedVersions = await LoadPinnedVersionsAsync(answers, cancellationToken);
            foreach (var answer in answers.Where(a =>
                PositionOfAnswer(a)?.RenderFormat != QuestionType.FreeResponse.ToCode()))
            {
                // #217 / PLC-18: a blank stays unjudged, exactly as in the objective pass above. It is
                // still incorrect for the skill's raw score (only `IsCorrect == true` counts, over every
                // delivered item) but it does not read as an ANSWERED row to the accuracy views.
                // #269: a pool answer has no Question row, so the legacy options lookup is empty for it.
                // EvaluateAnswer reads the pinned version for such an answer and never touches this
                // lookup, but building it dereferenced the null navigation and threw
                // NullReferenceException - so finalising ANY attempt containing a pool answer failed.
                var legacyOptions = answer.Question is { } question
                    ? question.QuestionOptionQuestionNavigations.ToLookup(o => o.QuestionId)
                    : ((IEnumerable<BusinessObjects.Entities.QuestionOption>)[]).ToLookup(o => o.QuestionId);

                answer.IsCorrect = IsAnswered(answer)
                    ? EvaluateAnswer(answer, pinnedVersions, legacyOptions)
                    : null;
            }

            var skills = answers.Select(a => PositionOfAnswer(a)!.Section.Skill)
                .Concat(freeResponses.Select(fr => PositionOfResponse(fr).Section.Skill))
                .DistinctBy(s => s.SkillId)
                .ToList();

            var isPlacementAttempt = attempt.Exam.Purpose == ExamPurpose.Placement.ToCode();
            var newSkillScores = new List<AttemptSkillScore>();
            foreach (var skill in skills)
            {
                var objectiveAnswers = answers
                    .Where(a => PositionOfAnswer(a)!.Section.SkillId == skill.SkillId
                        && PositionOfAnswer(a)!.RenderFormat != QuestionType.FreeResponse.ToCode())
                    .ToList();
                var freeResponsesInSkill = freeResponses.Where(fr => fr.SkillId == skill.SkillId).ToList();
                var totalQuestionsInSkill = objectiveAnswers.Count + freeResponsesInSkill.Count;
                if (totalQuestionsInSkill == 0) continue;

                var hasScales = await db.ScoreScales.AnyAsync(s => s.CertId == attempt.CertId && s.SkillId == skill.SkillId, cancellationToken);
                if (objectiveAnswers.Count == 0 && !hasScales && freeResponsesInSkill.Count == 0) continue;

                // #217 / PLC-35: a productive task whose grading FAILED contributes nothing at all -
                // neither a zero in the numerator nor a slot in the denominator. Counting it as a zero
                // would make a failed task read as "scored badly", which is the fabricated result the
                // ticket forbids, and it would drag the skill's level below the one task that did grade.
                // What remains is the measurement "estimated from one task" (D7).
                var gradedInSkill = freeResponsesInSkill
                    .Where(fr => fr.AiGradingFreeResponse?.Status == AiGradingStatus.Done.ToCode())
                    .ToList();

                // A placement skill with no answered objective item and no graded task measured nothing
                // and must not be given a score row: the writer would then derive a `measured` status
                // from a row that says zero, which is the not-measured-as-zero fabrication PLC-19
                // forbids. A mock keeps the zero, because there a blank paper is a legitimate score.
                var objectiveAttempted = objectiveAnswers.Any(IsAnswered);
                if (isPlacementAttempt && !objectiveAttempted && gradedInSkill.Count == 0
                    && freeResponsesInSkill.Count > 0)
                {
                    continue;
                }

                int scaledScore;
                decimal rawScore;

                if (freeResponsesInSkill.Count > 0 && objectiveAnswers.Count == 0)
                {
                    // Pure free response skill (e.g. IELTS Writing Task 1 + Task 2). Only the tasks that
                    // actually graded are averaged; a failed task is absent, not a zero (PLC-35).
                    if (gradedInSkill.Count == 0) continue;

                    var gradedSections = gradedInSkill
                        .Select(fr => PositionOfResponse(fr).Section)
                        .DistinctBy(s => s.SectionId)
                        .ToList();
                    decimal totalWeight = gradedSections.Sum(s => s.ScoreWeight > 0 ? s.ScoreWeight : 1.0m);
                    decimal weightedSectionSum = 0m;

                    foreach (var fr in gradedInSkill)
                    {
                        var section = PositionOfResponse(fr).Section;
                        var secWeight = section.ScoreWeight > 0 ? section.ScoreWeight : 1.0m;
                        var frScore = fr.AiGradingFreeResponse?.OverallScaled ?? 0m;
                        weightedSectionSum += frScore * secWeight;
                    }

                    decimal weightedScore = totalWeight > 0 ? weightedSectionSum / totalWeight : 0m;
                    rawScore = weightedScore;

                    // #257: the rounding of a productive skill's scaled score is the aggregation rule
                    // the attempt is scored under, so it takes the same resolution as the total. A live
                    // read here mixed two rules inside one attempt: an edition frozen to avg_half_step
                    // over a certificate since moved to sum stored a whole-rounded skill score beneath a
                    // half-stepped total.
                    var certRule = await LoadAggregationRuleAsync(
                        attempt.ExamId, attempt.CertId, cancellationToken);

                    if (string.Equals(certRule, "avg_half_step", StringComparison.OrdinalIgnoreCase))
                    {
                        scaledScore = RoundHalfStep((double)weightedScore);
                    }
                    else
                    {
                        scaledScore = (int)Math.Round(weightedScore, MidpointRounding.AwayFromZero);
                    }
                }
                else
                {
                    // The denominator is every item the skill DELIVERED - which is what makes an
                    // unanswered item count as incorrect without being counted as answered anywhere.
                    // A failed productive task is absent from BOTH sides (PLC-35): a zero would drag
                    // the level down to a mark the grader never gave.
                    rawScore = objectiveAnswers.Count(a => a.IsCorrect == true)
                        + gradedInSkill.Sum(fr => fractionByResponse.GetValueOrDefault(fr.FreeResponseId));
                    scaledScore = await ComputeScaledScoreAsync(
                        attempt.CertId,
                        skill.SkillId,
                        attempt.ExamId,
                        rawScore,
                        totalQuestionsInSkill,
                        cancellationToken);
                }

                newSkillScores.Add(new AttemptSkillScore
                {
                    AttemptId = attempt.AttemptId,
                    SkillId = skill.SkillId,
                    CertId = attempt.CertId,
                    RawScore = (int)Math.Round(rawScore, MidpointRounding.AwayFromZero),
                    ScaledScore = scaledScore
                });
            }

            db.AttemptSkillScores.AddRange(newSkillScores);
            await db.SaveChangesAsync(cancellationToken);

            var cert = await db.Certificates
                .Where(c => c.CertId == attempt.CertId)
                .Select(c => new { c.Code })
                .SingleAsync(cancellationToken);
            // #227 / #257: the same rule the edition froze, as in the objective pass above.
            var productiveRule = await LoadAggregationRuleAsync(attempt.ExamId, attempt.CertId, cancellationToken);
            // #217 / PLC-19: same rule as the objective pass - a placement where nothing could be
            // measured has no result, so it carries no total and no band.
            var measuredNothing = isPlacementAttempt && newSkillScores.Count == 0;
            var totalScore = measuredNothing ? (int?)null : AggregateTotalScore(productiveRule, newSkillScores);
            attempt.TotalScore = totalScore;
            attempt.BandCode = totalScore is int total
                ? await FindBandCodeAsync(attempt.CertId, total, cancellationToken)
                : null;
            attempt.WrittenByService = DerivedFactWriters.AttemptScoreAndBand;
            attempt.ProjectionVersion++;
            db.Entry(attempt).Property(a => a.WrittenByService).IsModified = true;
            attempt.Status = AttemptStatus.Graded.ToCode();

            // #191 / D14 / #212: the placement snapshot is derived from the attempt, and this is the
            // only place that may write it. Before #191 the AI-grading path stopped at the attempt and
            // left the placement on the earlier null-band value, while `AiGradingProcessor` patched
            // the snapshot from the outside - a second writer on the row the confirmed defect is
            // about. When a placement carries a free response, this is the moment its result exists.
            //
            // The per-skill rows are derived from the attempt's own scoring and the AI gradings that
            // just settled, so this call is what turns a `grading` row into `measured`.
            var linkedPlacements = attempt.Exam.Purpose == ExamPurpose.Placement.ToCode()
                ? await db.PlacementTests
                    .Where(p => p.AttemptId == attempt.AttemptId)
                    .Select(p => p.PlacementId)
                    .ToListAsync(cancellationToken)
                : [];

            await db.SaveChangesAsync(cancellationToken);

            foreach (var placementId in linkedPlacements)
            {
                await PlacementResults.WriteAsync(placementId, cancellationToken);
            }

            // #154: this is the second way a mock reaches `graded` (a mock with free responses is
            // finalized here after AI grading). Both writers close the step; leaving this one out
            // would keep the gap open for exactly the exams that take longest to finish.
            if (attempt.Exam.Purpose == ExamPurpose.Mock.ToCode())
            {
                await PathStepCompletion.CloseAsync(
                    db,
                    attempt.UserId,
                    PathStepCompletion.Activity.MockSection,
                    attempt.ExamId,
                    score: attempt.TotalScore,
                    accuracyPct: null,
                    cancellationToken);
            }

            if (tx is not null) await tx.CommitAsync(cancellationToken);
        }
        finally
        {
            if (tx is not null) await tx.DisposeAsync();
        }
    }
}
