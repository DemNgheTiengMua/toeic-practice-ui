using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class ExamEndpoints
{
    private static void MapExamCatalogEndpoints(RouteGroupBuilder learner)
    {
        learner.MapGet("/exams", async (
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            var view = await mockExamService.GetMockExamsAsync(CurrentUserId(http), cancellationToken);
            return Results.Ok(ToMockExamListViewDto(view));
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
                return Results.Ok(ToMockExamInstructionsDto(view));
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
                return Results.Ok(ToStartMockExamResponse(attempt));
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
    }
}
