using BusinessObjects;
using Contracts;

namespace WebAPI.Endpoints;

public static partial class SpeakingEndpoints
{
    private static SpeakingInstructionsDto MapInstructionsDto(SpeakingPracticeInstructionsView view)
    {
        return new SpeakingInstructionsDto(
            view.ExamId,
            view.CertId,
            view.CertCode,
            view.ExamName,
            view.Description,
            view.DurationMinutes,
            view.QuestionCount,
            view.CreditCost,
            view.CreditBalance,
            view.ActiveAttemptId,
            view.Sections.Select(s => new SpeakingSectionInfoDto(
                s.SectionId,
                s.Code,
                s.Name,
                s.ExpectedQuestionCount,
                s.PrepSeconds,
                s.ResponseSeconds,
                s.DisplayOrder)).ToList());
    }

    private static SpeakingTakeDto MapTakeDto(SpeakingTakeView take)
    {
        var q = take.Question;
        return new SpeakingTakeDto(
            take.AttemptId,
            take.ExamId,
            take.ExamName,
            take.CertId,
            take.CertCode,
            take.Status.MapTo<AttemptStatusDto>(),
            take.StartedAt,
            take.ExpiresAt,
            take.RemainingSeconds,
            take.CurrentQuestionId,
            take.CurrentQuestionIndex,
            take.TotalQuestions,
            take.SavedCount,
            new SpeakingQuestionDto(
                q.QuestionId,
                q.DisplayOrder,
                q.Stem,
                q.PrepSeconds,
                q.ResponseSeconds,
                q.Hint,
                q.SectionCode,
                q.SectionName,
                q.GroupImagePath,
                q.GroupImageAlt,
                q.GroupAudioPath,
                q.AudioPath,
                q.DurationMs,
                q.ResponseText,
                q.DraftSavedAt,
                q.Status.MapTo<FreeResponseStatusDto>()),
            take.Questions.Select(nav => new SpeakingQuestionNavDto(
                nav.QuestionId,
                nav.DisplayOrder,
                nav.SectionCode,
                nav.SectionName,
                nav.IsAnswered,
                nav.IsCurrent)).ToList());
    }

    private static SpeakingGradingPendingDto MapPendingDto(SpeakingGradingPendingView view)
    {
        return new SpeakingGradingPendingDto(
            view.AttemptId,
            view.ExamId,
            view.ExamName,
            view.CertCode,
            view.AttemptStatus.MapTo<AttemptStatusDto>(),
            view.TotalResponses,
            view.GradedResponses,
            view.FailedResponses,
            view.PendingResponses,
            view.CanRetry,
            view.FailureReason,
            view.Items.Select(item => new FreeResponseGradingStatusDto(
                item.FreeResponseId,
                item.QuestionId,
                item.DisplayOrder,
                item.Status.MapTo<AiGradingStatusDto>(),
                item.AttemptCount,
                item.FailureReason)).ToList());
    }

    private static SpeakingReviewDto MapReviewDto(SpeakingReviewView review)
    {
        SpeakingQuestionReviewItemDto? lowestDto = null;
        if (review.LowestScoredQuestion != null)
        {
            var l = review.LowestScoredQuestion;
            lowestDto = new SpeakingQuestionReviewItemDto(
                l.QuestionId,
                l.DisplayOrder,
                l.SectionName,
                l.Stem,
                l.AudioPath,
                l.DurationMs,
                l.Transcript,
                l.TotalScore,
                l.MaxScore,
                l.Criteria.Select(c => new SpeakingCriterionScoreItemDto(
                    c.CriterionId,
                    c.Code,
                    c.Name,
                    c.Score,
                    c.MaxScore,
                    c.Comment)).ToList());
        }

        return new SpeakingReviewDto(
            review.AttemptId,
            review.ExamId,
            review.ExamName,
            review.CertCode,
            review.SubmittedAt,
            review.TotalQuestions,
            review.AnsweredQuestions,
            review.TotalScore,
            review.MaxScore,
            review.ScaledScore,
            review.Questions.Select(q => new SpeakingQuestionReviewItemDto(
                q.QuestionId,
                q.DisplayOrder,
                q.SectionName,
                q.Stem,
                q.AudioPath,
                q.DurationMs,
                q.Transcript,
                q.TotalScore,
                q.MaxScore,
                q.Criteria.Select(c => new SpeakingCriterionScoreItemDto(
                    c.CriterionId,
                    c.Code,
                    c.Name,
                    c.Score,
                    c.MaxScore,
                    c.Comment)).ToList())).ToList(),
            lowestDto);
    }
}
