using Contracts;
using BusinessObjects;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class ExamEndpoints
{
    private static MockExamListViewDto ToMockExamListViewDto(MockExamListView view) =>
        new MockExamListViewDto(
            ToCertificateDto(view.Certificate),
            view.CreditBalance,
            view.Exams.Select(e => new MockExamItemDto(
                e.ExamId,
                e.CertId,
                e.CertCode,
                e.Name,
                e.Description,
                e.DurationMinutes,
                e.QuestionCount,
                e.SourceName,
                e.CreditCost,
                e.ActiveAttemptId,
                e.ActiveAttemptExpiresAt,
                e.CompletedAttemptScore,
                e.CompletedAttemptBand)).ToList());

    private static MockExamInstructionsDto ToMockExamInstructionsDto(MockExamInstructionsView view) =>
        new MockExamInstructionsDto(
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
            view.Sections.Select(s => new MockExamSectionInfoDto(
                s.SectionId,
                s.SkillCode,
                s.SkillName,
                s.Modality.MapTo<ModalityDto>(),
                s.SectionCode,
                s.SectionName,
                s.ExpectedQuestionCount,
                s.QuestionCount,
                s.DurationMinutes,
                s.DisplayOrder)).ToList());

    private static StartMockExamResponse ToStartMockExamResponse(ExamAttemptInfo attempt) =>
        new StartMockExamResponse(
            attempt.AttemptId,
            attempt.ExamId,
            attempt.Status.MapTo<AttemptStatusDto>(),
            attempt.StartedAt,
            attempt.ExpiresAt,
            attempt.RemainingSeconds,
            attempt.IsAutoSubmitted);

    private static PreflightStatusDto ToPreflightStatusDto(PreflightStatusView status) =>
        new PreflightStatusDto(
            status.AttemptId,
            status.ExamId,
            status.ExamName,
            status.CertCode,
            status.CertName,
            status.DurationMinutes,
            status.RequiresPreflight,
            status.RequiresAudio,
            status.RequiresMicrophone,
            status.IsVerified,
            status.VerifiedAt,
            status.AudioUrls);

    private static SaveAttemptDraftResponse ToSaveAttemptDraftResponse(SaveAttemptDraftResult result) =>
        new SaveAttemptDraftResponse(
            result.FreeResponseId,
            result.QuestionId,
            result.SavedAt,
            result.WordCount);

    private static SaveAnswerResultDto ToSaveAnswerResultDto(SaveAttemptAnswerResult res) =>
        new SaveAnswerResultDto(
            res.QuestionId, res.SelectedOptionId, res.IsFlagged, res.RemainingSeconds, res.IsAutoSubmitted);

    private static SubmitAttemptResponse ToSubmitAttemptResponse(AttemptResultView res) =>
        new SubmitAttemptResponse(
            res.AttemptId, res.Status.MapTo<AttemptStatusDto>(), res.TotalScore, res.BandCode, res.BandName, res.SubmittedAt, res.IsAutoSubmitted);

    private static List<AttemptFinalDraft> ToFinalDrafts(SubmitAttemptRequest request) =>
        request.Drafts
            .Select(d => new AttemptFinalDraft(d.QuestionId, d.ResponseText))
            .ToList();

    private static AttemptTransitionResponse ToAttemptTransitionResponse(AttemptTransitionResult res) =>
        new AttemptTransitionResponse(
            res.AttemptId,
            res.ActiveSkillId,
            res.ActiveUnitNumber,
            res.ActiveGroupId,
            res.PhaseDeadlineAt,
            res.RemainingSeconds,
            res.Status.MapTo<AttemptStatusDto>(),
            res.Status == AttemptStatus.Graded || res.Status == AttemptStatus.Submitted);

    private static AttemptResultDto ToAttemptResultDto(AttemptResultView r) =>
        new AttemptResultDto(
            r.AttemptId,
            r.ExamId,
            r.ExamName,
            r.CertId,
            r.CertCode,
            r.CertName,
            r.Status.MapTo<AttemptStatusDto>(),
            r.StartedAt,
            r.SubmittedAt,
            r.DurationSeconds,
            r.IsAutoSubmitted,
            r.TotalScore,
            r.BandCode,
            r.BandName,
            r.SkillScores.Select(s => new AttemptSkillScoreDto(
                s.SkillId, s.SkillCode, s.SkillName, s.Modality.MapTo<ModalityDto>(), s.RawScore, s.ScaledScore, s.TotalQuestions)).ToList(),
            r.PreviousAttemptScore,
            r.ScoreDelta,
            r.FreeResponseCount,
            r.GradedFreeResponseCount,
            r.FailedFreeResponseCount);

    private static ExamHistoryDto ToExamHistoryDto(ExamHistoryView history) =>
        new ExamHistoryDto(
            ToCertificateDto(history.Certificate),
            history.TotalAttempts,
            history.BestScore,
            history.AverageScore,
            history.BestScoreBandCode,
            history.BestScoreBandName,
            history.CefrBands.Select(b => new ScoreBandItemDto(b.Code, b.Name, b.MinTotal, b.MaxTotal, b.DisplayOrder)).ToList(),
            history.Attempts.Select(a => new ExamHistoryItemDto(
                a.AttemptId,
                a.ExamId,
                a.ExamName,
                a.CertId,
                a.CertCode,
                a.StartedAt,
                a.SubmittedAt,
                a.TotalScore,
                a.BandCode,
                a.BandName,
                a.SkillScores.Select(s => new AttemptSkillScoreDto(
                    s.SkillId, s.SkillCode, s.SkillName, s.Modality.MapTo<ModalityDto>(), s.RawScore, s.ScaledScore, s.TotalQuestions)).ToList(),
                a.Status.MapTo<AttemptStatusDto>())).ToList());

    private static AttemptReviewDto ToAttemptReviewDto(AttemptReviewView review) =>
        new AttemptReviewDto(
            review.AttemptId,
            review.ExamId,
            review.ExamName,
            review.CertId,
            review.CertCode,
            review.CertName,
            review.Status.MapTo<AttemptStatusDto>(),
            review.StartedAt,
            review.SubmittedAt,
            review.DurationSeconds,
            review.TotalScore,
            review.BandCode,
            review.BandName,
            review.SkillScores.Select(s => new AttemptSkillScoreDto(
                s.SkillId, s.SkillCode, s.SkillName, s.Modality.MapTo<ModalityDto>(), s.RawScore, s.ScaledScore, s.TotalQuestions)).ToList(),
            review.TotalQuestions,
            review.AnsweredCount,
            review.CorrectCount,
            review.IncorrectCount,
            review.BlankCount,
            review.FlaggedCount,
            review.Questions.Select(q => new AttemptReviewQuestionDto(
                q.QuestionId,
                q.DisplayOrder,
                q.TotalQuestions,
                q.SectionId,
                q.SectionCode,
                q.SectionName,
                q.SkillCode,
                q.SkillName,
                q.Modality.MapTo<ModalityDto>(),
                q.Stem,
                q.ImagePath,
                q.ImageAlt,
                q.AudioPath,
                q.AudioStartMs,
                q.AudioEndMs,
                q.Transcript,
                q.Passage,
                q.Explanation,
                q.Options.Select(o => new AttemptReviewOptionDto(o.OptionId, o.Label, o.Text, o.IsCorrect, o.Rationale)).ToList(),
                q.SelectedOptionId,
                q.IsCorrect,
                q.IsFlagged)).ToList(),
            review.Navigator.Select(n => new ReviewNavigatorItemDto(
                n.QuestionId, n.DisplayOrder, n.SectionId, n.SectionCode, n.Modality.MapTo<ModalityDto>(), n.IsAnswered, n.IsFlagged, n.IsCorrect)).ToList());
}
