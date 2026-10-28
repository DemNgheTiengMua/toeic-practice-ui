using BusinessObjects;
using Contracts;

namespace WebAPI.Endpoints;

public static partial class WritingEndpoints
{
    private static WritingInstructionsDto MapInstructionsDto(WritingPracticeInstructionsView view)
    {
        return new WritingInstructionsDto(
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
            view.Sections.Select(s => new WritingSectionInfoDto(
                s.SectionId,
                s.Code,
                s.Name,
                s.ExpectedQuestionCount,
                s.ResponseSeconds,
                s.DisplayOrder)).ToList());
    }

    private static WritingTakeDto MapTakeDto(WritingTakeView take)
    {
        var q = take.Question;
        return new WritingTakeDto(
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
            new WritingQuestionDto(
                q.QuestionId,
                q.DisplayOrder,
                q.Stem,
                q.MinWords,
                q.MaxWords,
                q.ResponseSeconds,
                q.Hint,
                q.SectionCode,
                q.SectionName,
                q.GroupImagePath,
                q.GroupImageAlt,
                q.ResponseText,
                q.DraftSavedAt,
                q.Status.MapTo<FreeResponseStatusDto>()),
            take.Questions.Select(nav => new WritingQuestionNavDto(
                nav.QuestionId,
                nav.DisplayOrder,
                nav.SectionCode,
                nav.SectionName,
                nav.IsAnswered,
                nav.IsCurrent)).ToList());
    }

    private static WritingGradingPendingDto MapPendingDto(WritingGradingPendingView view)
    {
        return new WritingGradingPendingDto(
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

    private static WritingReviewDto MapReviewDto(WritingReviewView review)
    {
        return new WritingReviewDto(
            review.AttemptId,
            review.ExamId,
            review.ExamName,
            review.CertCode,
            review.SubmittedAt,
            review.TotalQuestions,
            review.AnsweredQuestions,
            review.TotalScore,
            review.MaxScore,
            review.Questions.Select(q => new WritingQuestionReviewItemDto(
                q.QuestionId,
                q.DisplayOrder,
                q.SectionName,
                q.Stem,
                q.ResponseText,
                q.WordCount,
                q.TotalScore,
                q.MaxScore,
                q.Criteria.Select(c => new WritingCriterionScoreItemDto(
                    c.CriterionId,
                    c.Code,
                    c.Name,
                    c.Score,
                    c.MaxScore,
                    c.Comment)).ToList())).ToList());
    }
}
