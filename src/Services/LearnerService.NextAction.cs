using BusinessObjects;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class LearnerService
{
    /// <summary>#159: the hero's one action. The precedence is the order a learner would agree with:
    /// finish something already open, then unblock everything else with the free diagnostic, then
    /// commit to a goal, then drill the weakest topic, then open a skill the certificate has.
    /// <para>
    /// This decides <em>what</em> to do next, never <em>how</em>: which ten questions a drill contains
    /// is #155's <c>SmartPracticePlanner</c>, and whether practice has diverged from the placement band
    /// is #156's <c>CalibrationService</c>. The hero links to them; it does not recompute them.
    /// </para>
    /// </summary>
    private async Task<NextAction> BuildNextActionAsync(
        int userId,
        int certId,
        CertificateOption cert,
        bool isNew,
        IReadOnlyList<SkillCard> skills,
        GoalStanding goal,
        RecentAttempt? unfinishedAttempt,
        RecentPractice? unfinishedPractice,
        CancellationToken cancellationToken)
    {
        if (unfinishedAttempt is not null)
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
                $"{prefix}/take/{unfinishedAttempt.AttemptId}",
                "warning",
                "IN PROGRESS");
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
                "IN PROGRESS");
        }

        var hasPlacement = await db.PlacementTests
            .AsNoTracking()
            .AnyAsync(p => p.UserId == userId
                && p.CertId == certId
                && p.Status == PlacementStatus.Done.ToCode(), cancellationToken);

        if (!hasPlacement)
        {
            return new NextAction(
                "START HERE",
                $"Find your {cert.Code} level",
                isNew
                    ? $"A free placement test sets your starting band, then your practice and your learning path are chosen from it. No credit is spent."
                    : $"You have not placed on {cert.Code} yet. A free placement test sets the band everything else is measured from.",
                "Take the free placement test",
                "/placement",
                "info",
                "FREE");
        }

        if (goal.TargetBandCode is null)
        {
            return new NextAction(
                "SET THE TARGET",
                "Choose the band you are working towards",
                "Goals are what turn practice into a plan: with a target band and an exam date, the path shows how far there is to go.",
                "Set your target band",
                "/placement/target-band",
                "info",
                goal.CurrentBandCode is null ? null : $"NOW {goal.CurrentBandCode}");
        }

        var weakest = skills
            .SelectMany(skill => skill.WeakestCategories.Select(category => (Skill: skill, Category: category)))
            .OrderBy(x => x.Category.AccuracyPercent)
            .ThenByDescending(x => x.Category.Answered)
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
                "1 CREDIT");
        }

        return new NextAction(
            "NEXT UP",
            "Practise a section",
            $"Ten questions chosen from the topics you have not mastered yet on {cert.Code}. Free, and no credit is deducted.",
            "Start practice",
            "/practice",
            "info",
            "FREE");
    }
}
