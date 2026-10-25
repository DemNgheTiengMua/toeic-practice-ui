using BusinessObjects;
using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static class PlacementEndpoints
{
    public static void MapPlacementEndpoints(this IEndpointRouteBuilder app)
    {
        var learner = app.MapGroup("/api/learner").RequireAuthorization(Roles.Learner);

        // Placement Test Catalog
        learner.MapGet("/placement", async (
            HttpContext http,
            IPlacementService placementService,
            CancellationToken cancellationToken) =>
        {
            var view = await placementService.GetPlacementCatalogAsync(CurrentUserId(http), cancellationToken);
            var dto = new PlacementCatalogViewDto(
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

            return Results.Ok(dto);
        });

        // Start Placement Test (Free - 0 credits)
        learner.MapPost("/placement/start", async Task<IResult> (
            [FromBody] StartPlacementTestRequest request,
            HttpContext http,
            IPlacementService placementService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var info = await placementService.StartPlacementTestAsync(CurrentUserId(http), request.ExamId, cancellationToken);
                return Results.Ok(new PlacementTestInfoDto(
                    info.PlacementId,
                    info.AttemptId,
                    info.ExamId,
                    info.Status.MapTo<PlacementStatusDto>(),
                    info.StartedAt,
                    info.ExpiresAt,
                    info.RemainingSeconds,
                    info.IsAutoSubmitted));
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.ExamNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Placement exam not found", ApiError.Codes.ExamNotFound, ex.Message);
            }
        });

        // Get Placement Take
        learner.MapGet("/placement/take/{placementId:int}", async Task<IResult> (
            int placementId,
            [FromQuery] int? q,
            HttpContext http,
            IPlacementService placementService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var take = await placementService.GetPlacementTakeAsync(CurrentUserId(http), placementId, q, cancellationToken);
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
                    take.CurrentQuestion.SkillDisplayOrder);

                var navigator = take.Navigator.Select(n => new NavigatorItemDto(
                    n.QuestionId,
                    n.DisplayOrder,
                    n.SectionId,
                    n.SectionCode,
                    n.Modality.MapTo<ModalityDto>(),
                    n.IsAnswered,
                    n.IsFlagged,
                    n.IsCurrent)).ToList();

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
                    q.SkillDisplayOrder)).ToList();

                var phaseDtos = take.Phases?.Select(p => new AttemptPhaseDto(
                    p.SkillId,
                    p.SkillCode,
                    p.SkillName,
                    p.SkillDisplayOrder,
                    p.StartOffsetSeconds,
                    p.DurationSeconds)).ToList();

                return Results.Ok(new PlacementTakeViewDto(
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
                    phaseDtos));
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.PlacementNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Placement test not found", ApiError.Codes.PlacementNotFound, ex.Message);
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.PlacementNotActive)
            {
                return Problem(StatusCodes.Status400BadRequest, "Placement test not active", ApiError.Codes.PlacementNotActive, ex.Message);
            }
        });

        // Save Placement Answer
        learner.MapPost("/placement/take/{placementId:int}/answers/{questionId:int}", async Task<IResult> (
            int placementId,
            int questionId,
            [FromBody] SaveAnswerRequest request,
            HttpContext http,
            IPlacementService placementService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await placementService.SaveAnswerAsync(
                    CurrentUserId(http),
                    placementId,
                    questionId,
                    request.SelectedOptionId,
                    request.IsFlagged,
                    cancellationToken,
                    request.ResponseText,
                    request.SelectedOptionIdsJson);

                return Results.Ok(new SaveAnswerResultDto(
                    result.QuestionId,
                    result.SelectedOptionId,
                    result.IsFlagged,
                    result.RemainingSeconds,
                    result.IsAutoSubmitted));
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.PlacementNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Placement test not found", ApiError.Codes.PlacementNotFound, ex.Message);
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.PlacementNotActive)
            {
                return Problem(StatusCodes.Status400BadRequest, "Placement test not active", ApiError.Codes.PlacementNotActive, ex.Message);
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.PlacementExpired)
            {
                return Problem(StatusCodes.Status410Gone, "Placement test expired", ApiError.Codes.PlacementExpired, ex.Message);
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.QuestionNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Question not found", ApiError.Codes.QuestionNotFound, ex.Message);
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.InvalidOption)
            {
                return Problem(StatusCodes.Status400BadRequest, "Invalid option", ApiError.Codes.Validation, ex.Message);
            }
        });

                // Save Placement Draft
        learner.MapPost("/placement/take/{placementId:int}/draft", async Task<IResult> (
            int placementId,
            [FromBody] SavePlacementDraftRequestDto request,
            HttpContext http,
            IPlacementService placementService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await placementService.SaveDraftAsync(
                    CurrentUserId(http),
                    placementId,
                    request.QuestionId,
                    request.ResponseText,
                    cancellationToken);

                return Results.Ok(new SavePlacementDraftResponseDto(
                    result.FreeResponseId,
                    result.QuestionId,
                    result.SavedAt,
                    result.WordCount));
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.PlacementNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Placement test not found", ApiError.Codes.PlacementNotFound, ex.Message);
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.PlacementNotActive)
            {
                return Problem(StatusCodes.Status400BadRequest, "Placement test not active", ApiError.Codes.PlacementNotActive, ex.Message);
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.PlacementExpired)
            {
                return Problem(StatusCodes.Status410Gone, "Placement test expired", ApiError.Codes.PlacementExpired, ex.Message);
            }
        });

        // Placement Grading Status
        learner.MapGet("/placement/result/{placementId:int}/status", async Task<IResult> (
            int placementId,
            HttpContext http,
            IPlacementService placementService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var status = await placementService.GetGradingStatusAsync(CurrentUserId(http), placementId, cancellationToken);
                return Results.Ok(new PlacementGradingStatusDto(
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
                    status.StageDescriptor));
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.PlacementNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Placement test not found", ApiError.Codes.PlacementNotFound, ex.Message);
            }
        });


        // Submit Placement Test
        learner.MapPost("/placement/take/{placementId:int}/submit", async Task<IResult> (
            int placementId,
            [FromBody] SubmitPlacementTestRequest? request,
            HttpContext http,
            IPlacementService placementService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                bool isAutoSubmit = request?.IsAutoSubmit ?? false;
                var result = await placementService.SubmitPlacementTestAsync(CurrentUserId(http), placementId, isAutoSubmit, cancellationToken);
                return Results.Ok(ToPlacementResultDto(result));
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.PlacementNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Placement test not found", ApiError.Codes.PlacementNotFound, ex.Message);
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.PlacementNotActive)
            {
                return Problem(StatusCodes.Status400BadRequest, "Placement test not active", ApiError.Codes.PlacementNotActive, ex.Message);
            }
        });

        // Get Placement Result
        learner.MapGet("/placement/result/{placementId:int}", async Task<IResult> (
            int placementId,
            HttpContext http,
            IPlacementService placementService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await placementService.GetPlacementResultAsync(CurrentUserId(http), placementId, cancellationToken);
                return Results.Ok(ToPlacementResultDto(result));
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.PlacementNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Placement test not found", ApiError.Codes.PlacementNotFound, ex.Message);
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.PlacementNotFinished)
            {
                return Problem(StatusCodes.Status400BadRequest, "Placement test not finished", ApiError.Codes.PlacementNotFinished, ex.Message);
            }
        });

        // Target Band: Get Available & Current Target Bands
        learner.MapGet("/target-band", async (
            HttpContext http,
            IPlacementService placementService,
            CancellationToken cancellationToken) =>
        {
            var view = await placementService.GetTargetBandViewAsync(CurrentUserId(http), cancellationToken);
            var dto = new TargetBandViewDto(
                ToCertificateDto(view.ActiveCert),
                view.CurrentBandCode,
                view.CurrentTotalScore,
                view.CurrentBandName,
                view.TargetBandCode,
                view.CefrBands.Select(b => new CefrBandInfoDto(b.Code, b.Name, b.MinTotal, b.MaxTotal, b.IsReached, b.IsCurrent, b.IsTarget)).ToList(),
                view.AvailableBands.Select(b => new TargetBandOptionDto(b.Code, b.Name, b.MinTotal, b.MaxTotal, b.Description, b.IsCurrent, b.IsTarget)).ToList());

            return Results.Ok(dto);
        });

        // Target Band: Save Target Band
        learner.MapPost("/target-band", async Task<IResult> (
            [FromBody] SetTargetBandRequest request,
            HttpContext http,
            IPlacementService placementService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await placementService.SetTargetBandAsync(CurrentUserId(http), request.TargetBandCode, cancellationToken);
                return Results.Ok(new SetTargetBandResultDto(result.UserId, result.CertId, result.TargetBandCode, result.SetAt));
            }
            catch (PlacementException ex) when (ex.Code == PlacementException.TargetBandInvalid)
            {
                return Problem(StatusCodes.Status400BadRequest, "Invalid target band", ApiError.Codes.TargetBandInvalid, ex.Message);
            }
        });
    }

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
