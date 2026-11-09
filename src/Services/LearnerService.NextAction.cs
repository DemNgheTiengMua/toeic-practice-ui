using BusinessObjects;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class LearnerService
{
    /// <summary>
    /// #159 / #241 / LRN-14: the hero's one action, chosen by the fixed cascade.
    /// <para>
    /// <b>The order is the decision.</b> <see cref="NextActionCascade.Order"/> names six steps and this
    /// method evaluates them in exactly that sequence, returning the first whose condition holds. It
    /// does not "collect candidates and pick the best": that lets whichever list happens to be longer
    /// decide, which agrees with the cascade most of the time and disagrees after a seed change.
    /// Every return names its <see cref="NextAction.CascadeStep"/>, so a test can prove a
    /// higher-priority condition existed and lost.
    /// </para>
    /// <para>
    /// This decides <em>what</em> to do next, never <em>how</em>: which ten questions a drill contains
    /// is #155's <c>SmartPracticePlanner</c>, and whether practice has diverged from the placement band
    /// is #156's <c>CalibrationService</c>. The hero links to them; it does not recompute them.
    /// </para>
    /// </summary>
    /// <param name="standing">#211's stored vector. A skill absent from it is unmeasured, which is
    /// step 2's condition - never a reason to render A1.</param>
    /// <param name="ceilingReached">The day's objective ceiling, read from
    /// <c>vw_DailyPracticeVolume</c> by the caller (#235 / D12). Passed in rather than re-read so one
    /// dashboard load reads the view once.</param>
    private async Task<NextAction> BuildNextActionAsync(
        int userId,
        int certId,
        CertificateOption cert,
        bool isNew,
        IReadOnlyList<SkillCard> skills,
        GoalStanding goal,
        LearnerStanding? standing,
        bool ceilingReached,
        RecentAttempt? unfinishedAttempt,
        RecentPractice? unfinishedPractice,
        CancellationToken cancellationToken)
    {
        // ---- Step 1: resume something already open (LRN-14's "resume an in-progress placement") ----
        //
        // The placement is checked first inside this step so the spec's own case wins when a placement
        // and a mock are both open. Both outrank everything below for one reason: the learner has
        // already spent the time, and finishing costs nothing.
        if (unfinishedAttempt is not null)
        {
            // A placement is reached by /placement/take/{placementId}; every other purpose by the
            // attempt id. Using the attempt id for a placement 404'd the "Resume" button (#159 review).
            var id = unfinishedAttempt.Purpose == ExamPurpose.Placement
                ? unfinishedAttempt.PlacementId
                : unfinishedAttempt.AttemptId;

            if (id is not null)
            {
                var prefix = unfinishedAttempt.Purpose switch
                {
                    ExamPurpose.Placement => "/placement",
                    ExamPurpose.SpeakingPractice => "/speaking",
                    ExamPurpose.WritingPractice => "/writing",
                    _ => "/exams",
                };
                var label = unfinishedAttempt.Purpose switch
                {
                    ExamPurpose.Mock => "mock exam",
                    ExamPurpose.Placement => "placement test",
                    ExamPurpose.SpeakingPractice => "speaking practice",
                    _ => "writing practice",
                };
                return new NextAction(
                    "PICK UP WHERE YOU LEFT OFF",
                    $"Finish {unfinishedAttempt.ExamName}",
                    $"An unfinished {label} on {cert.Code} is still open. Its answers are kept, so continuing costs nothing.",
                    "Resume",
                    $"{prefix}/take/{id}",
                    "warning",
                    NextActionCascade.ResumeInProgress,
                    "IN PROGRESS");
            }
        }

        if (unfinishedPractice is not null)
        {
            return new NextAction(
                "PICK UP WHERE YOU LEFT OFF",
                $"Finish {unfinishedPractice.SectionName}",
                $"{unfinishedPractice.CorrectCount} of {unfinishedPractice.QuestionCount} questions answered. Finishing updates your accuracy and your weak points.",
                "Resume practice",
                $"/practice/{unfinishedPractice.SessionId}",
                "warning",
                NextActionCascade.ResumeInProgress,
                "IN PROGRESS");
        }

        // ---- Step 2: retry an unmeasured skill ----
        //
        // LRN-13 and LRN-14 are the same rule from two directions: a skill nothing has measured is a
        // task for the learner, and when every skill is unmeasured that task is the placement test.
        // The condition reads standing (#211), never a placement row, because a mock measures skills
        // without ever creating one.
        var unmeasured = skills
            .Where(s => s.Standing is not { IsMeasured: true })
            .Select(s => s.Name)
            .ToList();

        if (unmeasured.Count > 0)
        {
            var noneAtAll = standing is null || standing.HasNoMeasurement;
            var sitting = await PlacementMinutesAsync(certId, cancellationToken);
            return new NextAction(
                noneAtAll ? "START HERE" : "NOT MEASURED YET",
                noneAtAll ? $"Find your {cert.Code} level" : $"Measure {unmeasured[0]}",
                noneAtAll
                    ? "A free placement test sets your starting band, then your practice and your learning path are chosen from it. "
                        + $"No credit is spent. It takes {sitting}."
                    : $"{string.Join(", ", unmeasured)} {(unmeasured.Count == 1 ? "has" : "have")} no measurement on {cert.Code} yet, "
                        + "so the level there is unknown rather than low. A placement test measures it. No credit is spent.",
                noneAtAll ? "Take the free placement test" : "Measure the missing skill",
                "/placement",
                "info",
                NextActionCascade.RetryUnmeasuredSkill,
                "FREE");
        }

        // ---- Step 3: the week's next available step ----
        //
        // With no target band there is no plan to step through, so the step that creates one is what
        // "next step" means here. That is one step of the cascade, not a seventh.
        if (!goal.HasTarget)
        {
            return new NextAction(
                "SET THE TARGET",
                "Choose the band you are working towards",
                "Goals are what turn practice into a plan: with a target band and an exam date, the path shows how far there is to go.",
                "Set your target band",
                "/placement/target-band",
                "info",
                NextActionCascade.NextStep,
                goal.CurrentBandCode is null ? null : $"NOW {goal.CurrentBandCode}");
        }

        var nextStep = await NextOpenStepAsync(userId, certId, cancellationToken);
        if (nextStep is not null)
        {
            return new NextAction(
                nextStep.IsInProgress ? "PICK UP YOUR PLAN" : "NEXT ON YOUR PATH",
                nextStep.Title,
                $"{nextStep.SkillName} · week {nextStep.WeekNumber} · {nextStep.Minutes} minutes. "
                    + "It is the first step of this week that is still open, so it is the one that keeps the plan moving.",
                nextStep.IsInProgress ? "Continue this step" : "Open this step",
                $"/learning-path/steps/{nextStep.StepId}",
                nextStep.IsInProgress ? "warning" : "info",
                NextActionCascade.NextStep,
                $"WEEK {nextStep.WeekNumber}");
        }

        // ---- Step 4: the recommended free drill ----
        //
        // A capped day fails this step's CONDITION rather than its position: a drill offered after the
        // ceiling would be a recommendation that cannot be started. Step 5 is where a capped day goes.
        if (!ceilingReached)
        {
            var weakest = skills
                .SelectMany(skill => skill.WeakestCategories.Select(category => (Skill: skill, Category: category)))
                .OrderBy(x => x.Category.AccuracyPercent)
                .ThenByDescending(x => x.Category.Answered)
                .ThenBy(x => x.Skill.Code, StringComparer.Ordinal)
                .ThenBy(x => x.Category.Code, StringComparer.Ordinal)
                .FirstOrDefault();

            if (weakest != default)
            {
                return new NextAction(
                    "WEAKEST TOPIC",
                    $"Drill {weakest.Category.Name}",
                    $"{weakest.Category.AccuracyPercent}% accuracy over {weakest.Category.Answered} answers in {weakest.Skill.Name}. Ten questions are chosen for you, drawn from what is due for revision.",
                    "Start practice",
                    "/practice",
                    weakest.Category.AccuracyPercent < 50 ? "danger" : "warning",
                    NextActionCascade.FreeDrill,
                    $"{weakest.Skill.Code} · {weakest.Category.AccuracyPercent}%");
            }

            // A certificate whose skills include speaking or writing has a whole flow the typed drill
            // cannot reach (#150). Offering it here is the difference between the feature existing and
            // the learner finding it.
            var skillExam = await db.Exams
                .AsNoTracking()
                .Where(e => e.CertId == certId
                    && !e.IsDeleted
                    && e.Status == ExamStatus.Published.ToCode()
                    && (e.Purpose == ExamPurpose.SpeakingPractice.ToCode()
                        || e.Purpose == ExamPurpose.WritingPractice.ToCode()))
                .OrderBy(e => e.ExamId)
                .Select(e => new { e.ExamId, e.Name, e.Purpose })
                .FirstOrDefaultAsync(cancellationToken);

            if (skillExam is not null)
            {
                var isSpeaking = skillExam.Purpose == ExamPurpose.SpeakingPractice.ToCode();
                return new NextAction(
                    isSpeaking ? "SPEAKING PRACTICE" : "WRITING PRACTICE",
                    $"Try {skillExam.Name}",
                    isSpeaking
                        ? "Answer by voice, recorded and graded against the certificate's speaking rubric. One credit for the AI grading."
                        : "Write an answer and have it graded against the certificate's writing rubric, with the errors located for you. One credit for the AI grading.",
                    isSpeaking ? "Practise speaking" : "Practise writing",
                    $"/{(isSpeaking ? "speaking" : "writing")}/{skillExam.ExamId}/instructions",
                    "info",
                    NextActionCascade.FreeDrill,
                    "1 CREDIT");
            }

            return new NextAction(
                "NEXT UP",
                "Practise a section",
                $"Ten questions chosen from the topics you have not mastered yet on {cert.Code}. Free, and no credit is deducted.",
                "Start practice",
                "/practice",
                "info",
                NextActionCascade.FreeDrill,
                "FREE");
        }

        // ---- Step 5: the day's ceiling state ----
        //
        // Not a refusal: the day is done, and the honest next action is to look at what it produced.
        return new NextAction(
            "TODAY'S PRACTICE IS FULL",
            "Review what you answered today",
            $"You have reached {FreeDrillState.DailyObjectiveCeiling} questions today, which is as much as spaced practice can usefully add. "
                + "Reviewing your weakest topics now is what makes tomorrow's questions stick.",
            "Review your weak topics",
            "/weakness-analysis",
            "info",
            NextActionCascade.DailyCeiling,
            "DAILY LIMIT");
    }

    /// <summary>
    /// Step 6 / LRN-14: nothing above applied, so the plan is finished. Kept as its own method because
    /// it is the one action the cascade only reaches when every other condition is false, and a caller
    /// that never sees it fire should still be able to read it.
    /// </summary>
    internal static NextAction Completed(string certCode) =>
        new(
            "PLAN COMPLETE",
            "Set the next target",
            $"Every step on your {certCode} plan is done. A new target band and exam date build the next plan from where you are now.",
            "Set your next target",
            "/placement/target-band",
            "success",
            NextActionCascade.Completion);

    /// <summary>How long the certificate's placement sitting is, from the exam row. A stored number,
    /// not an estimate: LRN-13 requires the sentence to say how long it takes, and inventing "about an
    /// hour" for a 45-minute paper is the same class of fabrication as a prorated hour.</summary>
    private async Task<string> PlacementMinutesAsync(int certId, CancellationToken cancellationToken)
    {
        var minutes = await db.Exams
            .AsNoTracking()
            .Where(e => e.CertId == certId
                && !e.IsDeleted
                && e.Status == ExamStatus.Published.ToCode()
                && e.Purpose == ExamPurpose.Placement.ToCode())
            .OrderBy(e => e.ExamId)
            .Select(e => (int?)e.DurationMinutes)
            .FirstOrDefaultAsync(cancellationToken);

        return minutes is { } value ? $"about {value} minutes" : "one sitting";
    }

    /// <summary>The first open step of the active path, in the plan's own order. Null when the
    /// certificate has no active path, or when every step on it is done.</summary>
    private async Task<OpenPathStep?> NextOpenStepAsync(
        int userId, int certId, CancellationToken cancellationToken)
    {
        var pathId = await db.LearnerPaths
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.CertId == certId && p.Status == LearnerPathStatus.Active.ToCode())
            .Select(p => (int?)p.LearnerPathId)
            .FirstOrDefaultAsync(cancellationToken);

        if (pathId is null)
        {
            return null;
        }

        return await db.LearnerPathSteps
            .AsNoTracking()
            .Where(s => s.LearnerPathId == pathId
                && s.Status != LearnerPathStepStatus.Done.ToCode()
                && s.Status != LearnerPathStepStatus.Locked.ToCode())
            .OrderBy(s => s.WeekNumber ?? int.MaxValue)
            .ThenBy(s => s.WeekSequence)
            .ThenBy(s => s.StepId)
            .Select(s => new OpenPathStep(
                s.StepId,
                s.Topic.Name,
                s.Skill.Name,
                s.WeekNumber ?? 0,
                s.EstimatedMinutes ?? 0,
                s.Status == LearnerPathStepStatus.InProgress.ToCode()))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private sealed record OpenPathStep(
        int StepId, string Title, string SkillName, int WeekNumber, int Minutes, bool IsInProgress);
}
