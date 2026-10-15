using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // ---- Admin endpoints ----
        var admin = app.MapGroup("/api/admin").RequireAuthorization(Roles.Admin);

        admin.MapGet("/summary", (HttpContext http) =>
            Results.Ok(new { admin = http.User.FindFirst(IAccessTokenFactory.ClaimEmail)!.Value }));

        admin.MapGet("/dashboard", async (IAdminDashboardService dashboard, CancellationToken cancellationToken) =>
        {
            var metrics = await dashboard.GetDashboardMetricsAsync(cancellationToken);
            return Results.Ok(metrics);
        });

        admin.MapGet("/packages", async (IPackageService packages, CancellationToken cancellationToken) =>
        {
            var summary = await packages.GetAdminPackagesAsync(cancellationToken);
            return Results.Ok(summary);
        });

        admin.MapGet("/packages/{packageId:int}", async (int packageId, IPackageService packages, CancellationToken cancellationToken) =>
        {
            var pkg = await packages.GetPackageByIdAsync(packageId, cancellationToken);
            return pkg is not null ? Results.Ok(pkg) : Results.NotFound();
        });

        admin.MapPost("/packages", async (CreatePackageRequest request, IPackageService packages, CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await packages.CreatePackageAsync(request, cancellationToken);
                return Results.Created($"/api/admin/packages/{created.PackageId}", created);
            }
            catch (ArgumentException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Validation error", ApiError.Codes.Validation, ex.Message);
            }
        });

        admin.MapPut("/packages/{packageId:int}", async (int packageId, UpdatePackageRequest request, IPackageService packages, CancellationToken cancellationToken) =>
        {
            try
            {
                var updated = await packages.UpdatePackageAsync(packageId, request, cancellationToken);
                return Results.Ok(updated);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Validation error", ApiError.Codes.Validation, ex.Message);
            }
        });

        admin.MapPost("/packages/{packageId:int}/deactivate", async (int packageId, IPackageService packages, CancellationToken cancellationToken) =>
        {
            try
            {
                await packages.DeactivatePackageAsync(packageId, cancellationToken);
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        admin.MapGet("/orders", async (
            string? q,
            string? status,
            string? gateway,
            int? page,
            int? pageSize,
            IAdminOrderService orders,
            CancellationToken cancellationToken) =>
        {
            var summary = await orders.GetOrdersAsync(q, status, gateway, page ?? 1, pageSize ?? 20, cancellationToken);
            return Results.Ok(summary);
        });

        admin.MapGet("/orders/{orderId:int}", async (
            int orderId,
            IAdminOrderService orders,
            CancellationToken cancellationToken) =>
        {
            var detail = await orders.GetOrderDetailAsync(orderId, cancellationToken);
            return detail is not null ? Results.Ok(detail) : Results.NotFound();
        });

        admin.MapPost("/orders/{orderId:int}/reconcile", async (
            int orderId,
            IAdminOrderService orders,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await orders.ReconcileOrderAsync(orderId, cancellationToken);
                return Results.Ok(result);
            }
            catch (OrderNotFoundException)
            {
                return Results.NotFound();
            }
        });

        admin.MapGet("/users", async (
            string? q,
            string? status,
            int? page,
            int? pageSize,
            IAdminUserService users,
            CancellationToken cancellationToken) =>
        {
            var summary = await users.GetUsersAsync(q, status, page ?? 1, pageSize ?? 20, cancellationToken);
            return Results.Ok(summary);
        });

        admin.MapPost("/users/{userId:int}/lock", async (
            int userId,
            HttpContext http,
            IAdminUserService users,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var adminId = int.Parse(http.User.FindFirst(IAccessTokenFactory.ClaimUserId)!.Value);
                var updated = await users.SetLockAsync(adminId, userId, isLocked: true, cancellationToken);
                return Results.Ok(updated);
            }
            catch (AdminUserException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code);
            }
        });

        admin.MapPost("/users/{userId:int}/unlock", async (
            int userId,
            HttpContext http,
            IAdminUserService users,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var adminId = int.Parse(http.User.FindFirst(IAccessTokenFactory.ClaimUserId)!.Value);
                var updated = await users.SetLockAsync(adminId, userId, isLocked: false, cancellationToken);
                return Results.Ok(updated);
            }
            catch (AdminUserException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code);
            }
        });

        admin.MapPost("/users/{userId:int}/resend-activation", async (
            int userId,
            IAdminUserService users,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await users.ResendActivationAsync(userId, cancellationToken);
                return Results.NoContent();
            }
            catch (AdminUserException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code);
            }
        });

        admin.MapPost("/users/{userId:int}/adjust-credits", async (
            int userId,
            AdjustCreditsRequest request,
            HttpContext http,
            IAdminUserService users,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var adminId = int.Parse(http.User.FindFirst(IAccessTokenFactory.ClaimUserId)!.Value);
                var result = await users.AdjustCreditsAsync(adminId, userId, request.Delta, request.Reason, cancellationToken);
                return Results.Ok(result);
            }
            catch (AdminUserException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code);
            }
        });

        admin.MapGet("/score-conversion", async (
            string? certCode,
            int? examId,
            IScoreConversionService scoreConversionService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var view = await scoreConversionService.GetScoreConversionAsync(certCode, examId, cancellationToken);
                return Results.Ok(ToAdminScoreConversionDto(view));
            }
            catch (ScoreConversionException ex) when (ex.Code == ScoreConversionException.CertificateNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Certificate not found", ApiError.Codes.CertificateUnavailable, ex.Message);
            }
        });

        admin.MapPut("/score-conversion/{certId:int}", async Task<IResult> (
            int certId,
            UpdateScoreConversionRequest request,
            IScoreConversionService scoreConversionService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var command = new UpdateScoreConversionCommand(
                    request.ExamId,
                    request.Scales.Select(s => new UpdateScoreScaleEntry(s.SkillId, s.RawScore, s.ScaledScore)).ToList(),
                    request.Bands.Select(b => new UpdateScoreBandEntry(b.BandId, b.Code, b.Name, b.MinTotal, b.MaxTotal, b.DisplayOrder)).ToList());

                var view = await scoreConversionService.UpdateScoreConversionAsync(certId, command, cancellationToken);
                return Results.Ok(ToAdminScoreConversionDto(view));
            }
            catch (ScoreConversionException ex) when (ex.Code == ScoreConversionException.CertificateNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Certificate not found", ApiError.Codes.CertificateUnavailable, ex.Message);
            }
            catch (ScoreConversionException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Score conversion invalid", ApiError.Codes.ScoreConversionInvalid, ex.Message);
            }
        });

        // ---- Admin Exam & Question Editor endpoints ----
        admin.MapGet("/exams", async (
            string? q,
            string? status,
            int? certId,
            int? page,
            int? pageSize,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            var summary = await exams.GetExamsAsync(q, status, certId, page ?? 1, pageSize ?? 20, cancellationToken);
            return Results.Ok(summary);
        });

        admin.MapPost("/exams", async (
            CreateAdminExamRequest request,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await exams.CreateExamAsync(request, cancellationToken);
                return Results.Created($"/api/admin/exams/{created.ExamId}", created);
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapGet("/exams/{examId:int}", async (
            int examId,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var exam = await exams.GetExamByIdAsync(examId, cancellationToken);
                return Results.Ok(exam);
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapPut("/exams/{examId:int}", async (
            int examId,
            UpdateAdminExamRequest request,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var updated = await exams.UpdateExamAsync(examId, request, cancellationToken);
                return Results.Ok(updated);
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapPost("/exams/{examId:int}/publish", async (
            int examId,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await exams.PublishExamAsync(examId, cancellationToken);
                return Results.Ok(result);
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapDelete("/exams/{examId:int}", async (
            int examId,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await exams.DeleteExamAsync(examId, cancellationToken);
                return Results.NoContent();
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapGet("/exams/{examId:int}/questions", async (
            int examId,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var questions = await exams.GetExamQuestionsAsync(examId, cancellationToken);
                return Results.Ok(questions);
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapPost("/exams/{examId:int}/groups", async (
            int examId,
            CreateQuestionGroupRequest request,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await exams.CreateQuestionGroupAsync(examId, request, cancellationToken);
                return Results.Created($"/api/admin/exams/{examId}/groups/{created.GroupId}", created);
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapPut("/exams/{examId:int}/groups/{groupId:int}", async (
            int examId,
            int groupId,
            UpdateQuestionGroupRequest request,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var updated = await exams.UpdateQuestionGroupAsync(examId, groupId, request, cancellationToken);
                return Results.Ok(updated);
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapDelete("/exams/{examId:int}/groups/{groupId:int}", async (
            int examId,
            int groupId,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await exams.DeleteQuestionGroupAsync(examId, groupId, cancellationToken);
                return Results.NoContent();
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapPost("/exams/{examId:int}/questions", async (
            int examId,
            CreateQuestionRequest request,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await exams.CreateQuestionAsync(examId, request, cancellationToken);
                return Results.Created($"/api/admin/questions/{created.QuestionId}", created);
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapPut("/questions/{questionId:int}", async (
            int questionId,
            UpdateQuestionRequest request,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var updated = await exams.UpdateQuestionAsync(questionId, request, cancellationToken);
                return Results.Ok(updated);
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapDelete("/questions/{questionId:int}", async (
            int questionId,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await exams.DeleteQuestionAsync(questionId, cancellationToken);
                return Results.NoContent();
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapPost("/exams/{examId:int}/audio", async (
            int examId,
            SaveExamSectionAudioRequest request,
            IAdminExamService exams,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var audio = await exams.SaveSectionAudioAsync(examId, request, cancellationToken);
                return Results.Ok(audio);
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });
    }
}
