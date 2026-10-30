using BusinessObjects;
using Contracts;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class PlacementEndpoints
{
    private static PlacementCatalogViewDto ToPlacementCatalogViewDto(PlacementCatalogView view) =>
        new PlacementCatalogViewDto(
            ToCertificateDto(view.ActiveCert),
            view.CurrentBandCode,
            view.CurrentBandName,
            view.CurrentTotalScore,
            view.Exams.Select(e => new PlacementCatalogItemDto(
                e.ExamId,
                e.CertId,
                e.CertCode,
                e.Name,
                e.Description,
                e.DurationMinutes,
                e.QuestionCount,
                e.IsRecommended,
                e.ActivePlacementId)).ToList(),
            view.ActivePlacementId,
            view.ActiveAttemptId);

    private static PlacementTestInfoDto ToPlacementTestInfoDto(PlacementTestInfo info) =>
        new PlacementTestInfoDto(
            info.PlacementId,
            info.AttemptId,
            info.ExamId,
            info.Status.MapTo<PlacementStatusDto>(),
            info.StartedAt,
            info.ExpiresAt,
            info.RemainingSeconds,
            info.IsAutoSubmitted);

    private static PlacementTakeViewDto ToPlacementTakeViewDto(PlacementTakeView take)
    {
        var currentQ = new AttemptQuestionDto(
            take.CurrentQuestion.QuestionId,
            take.CurrentQuestion.DisplayOrder,
            take.CurrentQuestion.TotalQuestions,
            take.CurrentQuestion.SectionId,
            take.CurrentQuestion.SectionCode,
            take.CurrentQuestion.SectionName,
            take.CurrentQuestion.SkillCode,
            take.CurrentQuestion.SkillName,
            take.CurrentQuestion.Modality.MapTo<ModalityDto>(),
            take.CurrentQuestion.Stem,
            take.CurrentQuestion.ImagePath,
            take.CurrentQuestion.ImageAlt,
            take.CurrentQuestion.AudioPath,
            take.CurrentQuestion.AudioStartMs,
            take.CurrentQuestion.AudioEndMs,
            take.CurrentQuestion.Transcript,
            take.CurrentQuestion.Passage,
            take.CurrentQuestion.Options.Select(o => new AttemptOptionDto(o.OptionId, o.Label, o.Text)).ToList(),
            take.CurrentQuestion.SelectedOptionId,
            take.CurrentQuestion.IsFlagged,
            take.CurrentQuestion.QuestionType,
            take.CurrentQuestion.MinWords,
            take.CurrentQuestion.MaxWords,
            take.CurrentQuestion.PrepSeconds,
            take.CurrentQuestion.ResponseSeconds,
            take.CurrentQuestion.ResponseText,
            take.CurrentQuestion.ResponseAudioPath,
            take.CurrentQuestion.ResponseDurationMs,
            take.CurrentQuestion.DraftSavedAt,
            take.CurrentQuestion.SkillDurationMinutes,
            take.CurrentQuestion.SkillDisplayOrder,
            NavigationMode: take.CurrentQuestion.NavigationMode,
            ToolsConfigJson: take.CurrentQuestion.ToolsConfigJson,
            UnitNumber: take.CurrentQuestion.UnitNumber,
            GroupId: take.CurrentQuestion.GroupId);

        var navigator = take.Navigator.Select(n => new NavigatorItemDto(
            n.QuestionId,
            n.DisplayOrder,
            n.SectionId,
            n.SectionCode,
            n.Modality.MapTo<ModalityDto>(),
            n.IsAnswered,
            n.IsFlagged,
            n.IsCurrent,
            n.IsLocked)).ToList();

        var allQDtos = take.Questions?.Select(q => new AttemptQuestionDto(
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
            q.Options.Select(o => new AttemptOptionDto(o.OptionId, o.Label, o.Text)).ToList(),
            q.SelectedOptionId,
            q.IsFlagged,
            q.QuestionType,
            q.MinWords,
            q.MaxWords,
            q.PrepSeconds,
            q.ResponseSeconds,
            q.ResponseText,
            q.ResponseAudioPath,
            q.ResponseDurationMs,
            q.DraftSavedAt,
            q.SkillDurationMinutes,
            q.SkillDisplayOrder,
            // The sitting finds where a locked part ends from each question's mode and group.
            NavigationMode: q.NavigationMode,
            ToolsConfigJson: q.ToolsConfigJson,
            UnitNumber: q.UnitNumber,
            GroupId: q.GroupId)).ToList();

        var phaseDtos = take.Phases?.Select(p => new AttemptPhaseDto(
            p.SkillId,
            p.SkillCode,
            p.SkillName,
            p.SkillDisplayOrder,
            p.StartOffsetSeconds,
            p.DurationSeconds)).ToList();

        return new PlacementTakeViewDto(
            take.PlacementId,
            take.AttemptId,
            take.ExamId,
            take.ExamName,
            take.CertCode,
            take.Status.MapTo<PlacementStatusDto>(),
            take.StartedAt,
            take.ExpiresAt,
            take.RemainingSeconds,
            take.TotalQuestions,
            take.AnsweredCount,
            take.FlaggedCount,
            currentQ,
            navigator,
            take.IsAutoSubmitted,
            allQDtos,
            phaseDtos);
    }

    private static SaveAnswerResultDto ToSaveAnswerResultDto(SaveAttemptAnswerResult result) =>
        new SaveAnswerResultDto(
            result.QuestionId,
            result.SelectedOptionId,
            result.IsFlagged,
            result.RemainingSeconds,
            result.IsAutoSubmitted);

    private static SavePlacementDraftResponseDto ToSavePlacementDraftResponseDto(SaveAttemptDraftResult result) =>
        new SavePlacementDraftResponseDto(
            result.FreeResponseId,
            result.QuestionId,
            result.SavedAt,
            result.WordCount);

    private static PlacementGradingStatusDto ToPlacementGradingStatusDto(PlacementGradingStatusView status) =>
        new PlacementGradingStatusDto(
            status.PlacementId,
            status.AttemptId,
            status.IsGraded,
            status.FreeResponseCount,
            status.GradedFreeResponseCount,
            status.FailedFreeResponseCount,
            status.TotalScore,
            status.BandCode,
            status.BandName,
            status.SkillScores.Select(s => new PlacementSkillScoreInfoDto(
                s.SkillId,
                s.SkillCode,
                s.SkillName,
                s.RawScore,
                s.TotalQuestions,
                s.ScaledScore,
                s.BandCode)).ToList(),
            status.StageDescriptor);

    private static TargetBandViewDto ToTargetBandViewDto(TargetBandView view) =>
        new TargetBandViewDto(
            ToCertificateDto(view.ActiveCert),
            view.CurrentBandCode,
            view.CurrentTotalScore,
            view.CurrentBandName,
            view.TargetBandCode,
            view.CefrBands.Select(b => new CefrBandInfoDto(b.Code, b.Name, b.MinTotal, b.MaxTotal, b.IsReached, b.IsCurrent, b.IsTarget)).ToList(),
            view.AvailableBands.Select(b => new TargetBandOptionDto(b.Code, b.Name, b.MinTotal, b.MaxTotal, b.Description, b.IsCurrent, b.IsTarget)).ToList());

    private static SetTargetBandResultDto ToSetTargetBandResultDto(SaveTargetBandResult result) =>
        new SetTargetBandResultDto(result.UserId, result.CertId, result.TargetBandCode, result.SetAt);

    private static PlacementResultViewDto ToPlacementResultDto(PlacementResultView result) =>
        new(
            result.PlacementId,
            result.AttemptId,
            result.CertId,
            result.CertCode,
            result.ExamName,
            result.TakenAt,
            result.DurationMinutes,
            result.TotalQuestions,
            result.TotalScore,
            result.ResultBandCode,
            result.ResultBandName,
            result.CefrBands.Select(b => new CefrBandInfoDto(b.Code, b.Name, b.MinTotal, b.MaxTotal, b.IsReached, b.IsCurrent, b.IsTarget)).ToList(),
            result.SkillScores.Select(s => new PlacementSkillScoreInfoDto(s.SkillId, s.SkillCode, s.SkillName, s.RawScore, s.TotalQuestions, s.ScaledScore, s.BandCode)).ToList(),
            result.WeakerSkillName,
            result.BandNote,
            result.IsAutoSubmitted,
            result.FreeResponseCount,
            result.GradedFreeResponseCount,
            result.FailedFreeResponseCount,
            result.IsGraded);
}
