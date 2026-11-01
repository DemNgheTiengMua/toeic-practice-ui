using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

/// <summary>#155: the lesson micro-check is graded on the server so its answer counts toward mastery,
/// exactly as a drill answer does. The correct option leaves the page markup and arrives only in this
/// reply.</summary>
public static partial class LessonEndpoints
{
    private static async Task<IResult> SubmitMicroCheckAsync(
        int id,
        int cardId,
        MicroCheckRequest request,
        HttpContext http,
        IPracticeService practice)
    {
        try
        {
            var result = await practice.SubmitLessonMicroCheckAsync(
                CurrentUserId(http), id, cardId, request.SelectedOption);
            return Results.Ok(new MicroCheckResultDto(
                result.IsCorrect, result.CorrectOption, result.Explanation, result.CategoryId));
        }
        catch (PracticeException ex) when (ex.Code is PracticeException.QuestionNotFound or PracticeException.InvalidOption)
        {
            return Problem(StatusCodes.Status404NotFound, "Micro-check not found", ApiError.Codes.PracticeQuestionNotFound, ex.Message);
        }
    }
}
