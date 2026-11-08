using System;
using System.Collections.Generic;

namespace Contracts;

/// <summary>
/// #220 / D19: the learner-facing vocabulary - what a sitting, a practice session, a path step or an
/// insight is called. Words here are read by a learner mid-task, so they are sentences a person would
/// say, never the enum member's own shape: <c>PendingAi</c> is "Waiting for AI", not "PendingAi".
/// </summary>
internal static class LearnerLabels
{
    public static void AddTo(IDictionary<Type, LabelFamily> families)
    {
        LabelFamily.Add<AttemptStatusDto>(families, "Sitting status not reported", [
            ("in_progress", "In progress"),
            ("submitted", "Submitted"),
            ("graded", "Graded"),
            ("abandoned", "Abandoned"),
        ]);

        LabelFamily.Add<PlacementStatusDto>(families, "Placement status not reported", [
            ("in_progress", "In progress"),
            ("done", "Done"),
            ("abandoned", "Abandoned"),
        ]);

        LabelFamily.Add<PracticeStatusDto>(families, "Practice status not reported", [
            ("in_progress", "In progress"),
            ("finished", "Finished"),
            ("abandoned", "Abandoned"),
        ]);

        LabelFamily.Add<LearnerPathStatusDto>(families, "Path status not reported", [
            ("active", "Active"),
            ("completed", "Completed"),
            ("reset", "Reset"),
        ]);

        LabelFamily.Add<LearnerPathStepStatusDto>(families, "Step status not reported", [
            ("locked", "Locked"),
            ("available", "Available"),
            ("in_progress", "In progress"),
            ("done", "Done"),
        ]);

        LabelFamily.Add<FreeResponseStatusDto>(families, "Answer status not reported", [
            ("draft", "Draft"),
            ("pending_ai", "Waiting for AI grading"),
            ("graded", "Graded"),
        ]);

        LabelFamily.Add<AiGradingStatusDto>(families, "Grading status not reported", [
            ("pending", "Waiting to be graded"),
            ("done", "Graded"),
            ("failed", "Grading failed"),
        ]);

        LabelFamily.Add<AiInsightStatusDto>(families, "Insight status not reported", [
            ("pending", "Being written"),
            ("done", "Ready"),
            ("failed", "Could not be written"),
        ]);

        LabelFamily.Add<AiInsightScopeDto>(families, "Insight scope not reported", [
            ("practice_session", "This practice session"),
            ("attempt", "This sitting"),
            ("path", "Your learning path"),
            ("dashboard", "Your dashboard"),
        ]);

        LabelFamily.Add<AiInsightTrendDto>(families, "Trend not reported", [
            ("improving", "Improving"),
            ("flat", "Holding steady"),
            ("declining", "Declining"),
        ]);

        LabelFamily.Add<RecommendationSeverityDto>(families, "Priority not reported", [
            ("info", "Tip"),
            ("minor", "Focus"),
            ("major", "Priority"),
        ]);

        LabelFamily.Add<MockProjectionOutcomeDto>(families, "Projection not reported", [
            ("Projected", "Projected level"),
            ("Uncertified", "No level for this paper"),
            ("DisplayCeiling", "At the top of the scale"),
            ("CannotJudge", "Cannot be placed on a level"),
        ]);

        LabelFamily.Add<MentorSkillStandingStatus>(families, "Standing not reported", [
            ("measured", "Measured"),
            ("not_measured", "Not measured yet"),
            ("not_examined", "Not examined by this certificate"),
        ]);

        LabelFamily.Add<FeasibilityStatusDto>(families, "Feasibility not reported", [
            ("feasible", "Reachable at this pace"),
            ("stretch", "A stretch at this pace"),
            ("unrealistic", "Out of reach at this pace"),
        ]);

        LabelFamily.Add<QuestionSourceDto>(families, "Source not reported", [
            ("all", "All questions"),
            ("wrong_before", "Answered wrong before"),
            ("unseen", "Not seen yet"),
        ]);

        LabelFamily.Add<SavedQuestionSourceDto>(families, "Source not reported", [
            ("manual", "Saved by you"),
            ("incorrect", "Wrong answer"),
        ]);
    }
}
