using Contracts;
using BusinessObjects;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static class ExamEndpoints
{
    public static void MapExamEndpoints(this IEndpointRouteBuilder app)
    {
        var learner = app.MapGroup("/api/learner").RequireAuthorization(Roles.Learner);

        // ---- Mock Exams (issue #15) ----
        learner.MapGet("/exams", async (
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            var view = await mockExamService.GetMockExamsAsync(CurrentUserId(http), cancellationToken);
            return Results.Ok(new MockExamListViewDto(
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
                    e.CompletedAttemptBand)).ToList()));
        });

        learner.MapGet("/exams/{examId:int}/instructions", async Task<IResult> (
            int examId,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var view = await mockExamService.GetExamInstructionsAsync(CurrentUserId(http), examId, cancellationToken);
                return Results.Ok(new MockExamInstructionsDto(
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
                        s.DisplayOrder)).ToList()));
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.ExamNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Exam not found", ApiError.Codes.ExamNotFound, ex.Message);
            }
        });

        learner.MapPost("/exams/{examId:int}/start", async Task<IResult> (
            int examId,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var attempt = await mockExamService.StartAttemptAsync(CurrentUserId(http), examId, cancellationToken);
                return Results.Ok(new StartMockExamResponse(
                    attempt.AttemptId,
                    attempt.ExamId,
                    attempt.Status.MapTo<AttemptStatusDto>(),
                    attempt.StartedAt,
                    attempt.ExpiresAt,
                    attempt.RemainingSeconds,
                    attempt.IsAutoSubmitted));
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.InsufficientCredits)
            {
                return Problem(StatusCodes.Status402PaymentRequired, "Insufficient credits", ApiError.Codes.InsufficientCredits, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.ExamNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Exam not found", ApiError.Codes.ExamNotFound, ex.Message);
            }
        });

        learner.MapGet("/attempts/{attemptId:int}", async Task<IResult> (
            int attemptId,
            [FromQuery] int? q,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var take = await mockExamService.GetAttemptTakeAsync(CurrentUserId(http), attemptId, q, cancellationToken);
                var qInfo = take.CurrentQuestion;
                var currentQDto = new AttemptQuestionDto(
                    qInfo.QuestionId,
                    qInfo.DisplayOrder,
                    qInfo.TotalQuestions,
                    qInfo.SectionId,
                    qInfo.SectionCode,
                    qInfo.SectionName,
                    qInfo.SkillCode,
                    qInfo.SkillName,
                    qInfo.Modality.MapTo<ModalityDto>(),
                    qInfo.Stem,
                    qInfo.ImagePath,
                    qInfo.ImageAlt,
                    qInfo.AudioPath,
                    qInfo.AudioStartMs,
                    qInfo.AudioEndMs,
                    qInfo.Transcript,
                    qInfo.Passage,
                    qInfo.Options.Select(o => new AttemptOptionDto(o.OptionId, o.Label, o.Text)).ToList(),
                    qInfo.SelectedOptionId,
                    qInfo.IsFlagged,
                    qInfo.QuestionType,
                    qInfo.MinWords,
                    qInfo.MaxWords,
                    qInfo.PrepSeconds,
                    qInfo.ResponseSeconds,
                    qInfo.ResponseText,
                    qInfo.ResponseAudioPath,
                    qInfo.ResponseDurationMs,
                    qInfo.DraftSavedAt,
                    qInfo.SkillDurationMinutes,
                    qInfo.SkillDisplayOrder);

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

                return Results.Ok(new AttemptTakeDto(
                    take.AttemptId,
                    take.ExamId,
                    take.ExamName,
                    take.CertCode,
                    take.Status.MapTo<AttemptStatusDto>(),
                    take.StartedAt,
                    take.ExpiresAt,
                    take.RemainingSeconds,
                    take.TotalQuestions,
                    take.AnsweredCount,
                    take.FlaggedCount,
                    currentQDto,
                    take.Navigator.Select(n => new NavigatorItemDto(
                        n.QuestionId, n.DisplayOrder, n.SectionId, n.SectionCode, n.Modality.MapTo<ModalityDto>(), n.IsAnswered, n.IsFlagged, n.IsCurrent, n.IsLocked)).ToList(),
                    take.IsAutoSubmitted,
                    allQDtos,
                    take.Phases?.Select(phase => new AttemptPhaseDto(
                        phase.SkillId,
                        phase.SkillCode,
                        phase.SkillName,
                        phase.SkillDisplayOrder,
                        phase.StartOffsetSeconds,
                        phase.DurationSeconds)).ToList(),
                    take.ActiveSkillId,
                    take.ActiveUnitNumber,
                    take.ActiveGroupId,
                    take.PhaseDeadlineAt,
                    take.IsSkillLocked,
                    take.AuthorizationCode,
                    take.LastAudioOffsetMs,
                    take.CandidateNotesJson));
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotActive)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.PreflightRequired)
            {
                return Problem(StatusCodes.Status400BadRequest, "Preflight verification required", ApiError.Codes.PreflightRequired, ex.Message);
            }
        });

                learner.MapGet("/attempts/{attemptId:int}/preflight", async Task<IResult> (
            int attemptId,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var status = await mockExamService.GetPreflightStatusAsync(CurrentUserId(http), attemptId, cancellationToken);
                return Results.Ok(new PreflightStatusDto(
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
                    status.AudioUrls));
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotActive)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
        });

        learner.MapPost("/attempts/{attemptId:int}/preflight/verify", async Task<IResult> (
            int attemptId,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var status = await mockExamService.VerifyPreflightAsync(CurrentUserId(http), attemptId, cancellationToken);
                return Results.Ok(new PreflightStatusDto(
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
                    status.AudioUrls));
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotActive)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
        });

        learner.MapPost("/attempts/{attemptId:int}/draft", async Task<IResult> (
            int attemptId,
            SaveAttemptDraftRequest request,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await mockExamService.SaveDraftAsync(
                    CurrentUserId(http),
                    attemptId,
                    request.QuestionId,
                    request.ResponseText,
                    cancellationToken);
                return Results.Ok(new SaveAttemptDraftResponse(
                    result.FreeResponseId,
                    result.QuestionId,
                    result.SavedAt,
                    result.WordCount));
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.QuestionNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Draft not found", ApiError.Codes.QuestionNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptExpired)
            {
                return Problem(StatusCodes.Status400BadRequest, "Exam deadline expired", ApiError.Codes.AttemptExpired, ex.Message);
            }
            catch (MockExamException ex) when (
                ex.Code == MockExamException.AttemptNotActive ||
                ex.Code == AttemptEngineException.WrongPhase)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.PreflightRequired)
            {
                return Problem(StatusCodes.Status400BadRequest, "Preflight verification required", ApiError.Codes.PreflightRequired, ex.Message);
            }
        });

        learner.MapPost("/attempts/{attemptId:int}/answers/{questionId:int}", async Task<IResult> (
            int attemptId,
            int questionId,
            SaveAnswerRequest request,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var res = await mockExamService.SaveAnswerAsync(
                    CurrentUserId(http),
                    attemptId,
                    questionId,
                    request.SelectedOptionId,
                    request.IsFlagged,
                    cancellationToken,
                    request.ResponseText);
                return Results.Ok(new SaveAnswerResultDto(
                    res.QuestionId, res.SelectedOptionId, res.IsFlagged, res.RemainingSeconds, res.IsAutoSubmitted));
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptExpired)
            {
                return Problem(StatusCodes.Status400BadRequest, "Exam deadline expired", ApiError.Codes.AttemptExpired, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.QuestionNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Question not found", ApiError.Codes.QuestionNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.InvalidOption)
            {
                return Problem(StatusCodes.Status400BadRequest, "Invalid option", ApiError.Codes.PracticeInvalidOption, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotActive)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == AttemptEngineException.WrongPhase)
            {
                return Problem(StatusCodes.Status400BadRequest, "Question not available yet", ApiError.Codes.AttemptNotActive, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.PreflightRequired)
            {
                return Problem(StatusCodes.Status400BadRequest, "Preflight verification required", ApiError.Codes.PreflightRequired, ex.Message);
            }
        });

        learner.MapPost("/attempts/{attemptId:int}/submit", async Task<IResult> (
            int attemptId,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var res = await mockExamService.SubmitAttemptAsync(CurrentUserId(http), attemptId, isAutoSubmit: false, cancellationToken);
                return Results.Ok(new SubmitAttemptResponse(
                    res.AttemptId, res.Status.MapTo<AttemptStatusDto>(), res.TotalScore, res.BandCode, res.BandName, res.SubmittedAt, res.IsAutoSubmitted));
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotActive)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
        });

        learner.MapPost("/attempts/{attemptId:int}/submit-with-drafts", async Task<IResult> (
            int attemptId,
            [FromBody] SubmitAttemptRequest request,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var finalDrafts = request.Drafts
                    .Select(d => new AttemptFinalDraft(d.QuestionId, d.ResponseText))
                    .ToList();
                var res = await mockExamService.SubmitAttemptAsync(
                    CurrentUserId(http),
                    attemptId,
                    isAutoSubmit: false,
                    cancellationToken,
                    finalDrafts);
                return Results.Ok(new SubmitAttemptResponse(
                    res.AttemptId, res.Status.MapTo<AttemptStatusDto>(), res.TotalScore, res.BandCode, res.BandName, res.SubmittedAt, res.IsAutoSubmitted));
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotActive)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.QuestionNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Question not found", ApiError.Codes.QuestionNotFound, ex.Message);
            }
        });

        learner.MapPost("/attempts/{attemptId:int}/cancel", async Task<IResult> (
            int attemptId,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await mockExamService.CancelAttemptAsync(CurrentUserId(http), attemptId, cancellationToken);
                return Results.NoContent();
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotActive)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
        });

        learner.MapPost("/attempts/{attemptId:int}/transition/skill", async Task<IResult> (
            int attemptId,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var res = await mockExamService.TransitionToNextSkillAsync(CurrentUserId(http), attemptId, cancellationToken);
                return Results.Ok(new AttemptTransitionResponse(
                    res.AttemptId,
                    res.ActiveSkillId,
                    res.ActiveUnitNumber,
                    res.ActiveGroupId,
                    res.PhaseDeadlineAt,
                    res.RemainingSeconds,
                    res.Status.MapTo<AttemptStatusDto>(),
                    res.Status == AttemptStatus.Graded || res.Status == AttemptStatus.Submitted));
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotActive)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
            catch (MockExamException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Transition failed", ApiError.Codes.Validation, ex.Message);
            }
        });

        learner.MapPost("/attempts/{attemptId:int}/transition/unit", async Task<IResult> (
            int attemptId,
            [FromBody] TransitionUnitRequest request,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var res = await mockExamService.TransitionUnitAsync(CurrentUserId(http), attemptId, request.TargetUnitNumber, cancellationToken);
                return Results.Ok(new AttemptTransitionResponse(
                    res.AttemptId,
                    res.ActiveSkillId,
                    res.ActiveUnitNumber,
                    res.ActiveGroupId,
                    res.PhaseDeadlineAt,
                    res.RemainingSeconds,
                    res.Status.MapTo<AttemptStatusDto>(),
                    res.Status == AttemptStatus.Graded || res.Status == AttemptStatus.Submitted));
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotActive)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
            catch (MockExamException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Transition failed", ApiError.Codes.Validation, ex.Message);
            }
        });

        learner.MapPost("/attempts/{attemptId:int}/transition/group", async Task<IResult> (
            int attemptId,
            [FromBody] TransitionGroupRequest request,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var res = await mockExamService.TransitionGroupAsync(CurrentUserId(http), attemptId, request.TargetGroupId, cancellationToken);
                return Results.Ok(new AttemptTransitionResponse(
                    res.AttemptId,
                    res.ActiveSkillId,
                    res.ActiveUnitNumber,
                    res.ActiveGroupId,
                    res.PhaseDeadlineAt,
                    res.RemainingSeconds,
                    res.Status.MapTo<AttemptStatusDto>(),
                    res.Status == AttemptStatus.Graded || res.Status == AttemptStatus.Submitted));
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotActive)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.QuestionNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Question group not found", ApiError.Codes.QuestionNotFound, ex.Message);
            }
            catch (MockExamException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Transition failed", ApiError.Codes.Validation, ex.Message);
            }
        });

        learner.MapPost("/attempts/{attemptId:int}/audio-review/start", async Task<IResult> (
            int attemptId,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var res = await mockExamService.CompleteAudioAndStartReviewAsync(CurrentUserId(http), attemptId, cancellationToken);
                return Results.Ok(new AttemptTransitionResponse(
                    res.AttemptId,
                    res.ActiveSkillId,
                    res.ActiveUnitNumber,
                    res.ActiveGroupId,
                    res.PhaseDeadlineAt,
                    res.RemainingSeconds,
                    res.Status.MapTo<AttemptStatusDto>(),
                    res.Status == AttemptStatus.Graded || res.Status == AttemptStatus.Submitted));
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotActive)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
            catch (MockExamException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Audio review transition failed", ApiError.Codes.Validation, ex.Message);
            }
        });

        learner.MapPost("/attempts/{attemptId:int}/sync", async Task<IResult> (
            int attemptId,
            [FromBody] SittingSyncRequestDto requestDto,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken ct) =>
        {
            var userId = CurrentUserId(http);
            try
            {
                var domainRequest = new SittingSyncRequest(
                    requestDto.Answers?.Select(a => new BatchAnswerItem(
                        a.QuestionId,
                        a.SelectedOptionId,
                        a.IsFlagged,
                        a.ResponseText,
                        a.SelectedOptionIdsJson)).ToList(),
                    requestDto.LastAudioOffsetMs,
                    requestDto.CandidateNotesJson,
                    requestDto.LastQuestionId);

                var result = await mockExamService.SaveSittingSyncAsync(userId, attemptId, domainRequest, ct);

                return Results.Ok(new SittingSyncDto(
                    result.AttemptId,
                    result.RemainingSeconds,
                    result.IsAutoSubmitted,
                    result.ActiveSkillId,
                    result.ActiveUnitNumber,
                    result.ActiveGroupId,
                    result.PhaseDeadlineAt,
                    result.LastAudioOffsetMs,
                    result.AnswersSavedCount));
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotActive)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptExpired)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt expired", ApiError.Codes.AttemptExpired, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == AttemptEngineException.WrongPhase)
            {
                return Problem(StatusCodes.Status400BadRequest, "Wrong phase", "wrong_phase", ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.PreflightRequired)
            {
                return Problem(StatusCodes.Status400BadRequest, "Preflight verification required", ApiError.Codes.PreflightRequired, ex.Message);
            }
        });

        learner.MapPost("/attempts/resume", async Task<IResult> (
            [FromBody] ResumeAttemptRequest request,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken ct) =>
        {
            var userId = CurrentUserId(http);
            try
            {
                var result = await mockExamService.ResumeAttemptByCodeAsync(request.AuthorizationCode, userId, ct);

                var dto = new AttemptResumeDto(
                    result.AttemptId,
                    result.ExamId,
                    result.AuthorizationCode,
                    result.ActiveSkillId,
                    result.ActiveUnitNumber,
                    result.ActiveGroupId,
                    result.LastQuestionId,
                    result.LastAudioOffsetMs,
                    result.CandidateNotesJson,
                    result.RemainingSeconds,
                    result.ExpiresAt,
                    result.PhaseDeadlineAt,
                    result.AnswerSheet.Select(a => new AttemptAnswerSheetItemDto(
                        a.QuestionId,
                        a.SelectedOptionId,
                        a.SelectedOptionIdsJson,
                        a.ResponseText,
                        a.IsFlagged)).ToList());

                return Results.Ok(dto);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotActive)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptExpired)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt expired", ApiError.Codes.AttemptExpired, ex.Message);
            }
        });

        learner.MapGet("/attempts/{attemptId:int}/result", async Task<IResult> (
            int attemptId,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var r = await mockExamService.GetAttemptResultAsync(CurrentUserId(http), attemptId, cancellationToken);
                return Results.Ok(new AttemptResultDto(
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
                    r.FailedFreeResponseCount));
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotActive)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
        });

        learner.MapGet("/exams/history", async (
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            var history = await mockExamService.GetExamHistoryAsync(CurrentUserId(http), cancellationToken);
            return Results.Ok(new ExamHistoryDto(
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
                    a.Status.MapTo<AttemptStatusDto>())).ToList()));
        });

        learner.MapGet("/attempts/{attemptId:int}/review", async Task<IResult> (
            int attemptId,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var review = await mockExamService.GetAttemptReviewAsync(CurrentUserId(http), attemptId, cancellationToken);
                return Results.Ok(new AttemptReviewDto(
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
                        n.QuestionId, n.DisplayOrder, n.SectionId, n.SectionCode, n.Modality.MapTo<ModalityDto>(), n.IsAnswered, n.IsFlagged, n.IsCorrect)).ToList()));
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFinished)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt not finished", ApiError.Codes.AttemptNotFinished, ex.Message);
            }
        });
    }
}
