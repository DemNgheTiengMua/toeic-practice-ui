using BusinessObjects;
using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static class SavedQuestionEndpoints
{
    public static void MapSavedQuestionEndpoints(this IEndpointRouteBuilder app)
    {
        var learner = app.MapGroup("/api/learner").RequireAuthorization(Roles.Learner);

        // ---- Saved Questions (issue #9) ----
        var savedQuestions = learner.MapGroup("/saved-questions");

        savedQuestions.MapGet("", async (
            [FromQuery] string? source,
            [FromQuery] int? sectionId,
            [FromQuery] int? categoryId,
            HttpContext http,
            ISavedQuestionService savedService,
            CancellationToken cancellationToken) =>
        {
            var filter = new SavedQuestionFilterInput(source, sectionId, categoryId);
            var view = await savedService.GetSavedQuestionsAsync(CurrentUserId(http), filter, cancellationToken);
            return Results.Ok(new SavedQuestionsViewDto(
                ToCertificateDto(view.Certificate),
                view.TotalSavedCount,
                view.ManualSavedCount,
                view.IncorrectSavedCount,
                view.Questions.Select(ToSavedQuestionDto).ToList(),
                view.Sections.Select(s => new SavedQuestionSectionFilterDto(s.SectionId, s.Code, s.Name, s.Count)).ToList(),
                view.Categories.Select(c => new SavedQuestionCategoryFilterDto(c.CategoryId, c.Name, c.Count)).ToList()));
        });

        savedQuestions.MapGet("/{questionId:int}", async Task<IResult> (
            int questionId,
            HttpContext http,
            ISavedQuestionService savedService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var q = await savedService.GetSavedQuestionAsync(CurrentUserId(http), questionId, cancellationToken);
                return Results.Ok(ToSavedQuestionDto(q));
            }
            catch (SavedQuestionException ex) when (ex.Code == SavedQuestionException.SavedQuestionNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Saved question not found", ApiError.Codes.SavedQuestionNotFound, ex.Message);
            }
        });

        savedQuestions.MapPost("/{questionId:int}", async Task<IResult> (
            int questionId,
            SaveQuestionRequest? request,
            HttpContext http,
            ISavedQuestionService savedService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var source = request?.Source ?? "manual";
                var res = await savedService.SaveQuestionAsync(CurrentUserId(http), questionId, source, cancellationToken);
                return Results.Ok(new SaveQuestionResultDto(res.IsSaved, res.Source, res.SavedAt));
            }
            catch (SavedQuestionException ex) when (ex.Code == SavedQuestionException.QuestionNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Question not found", ApiError.Codes.QuestionNotFound, ex.Message);
            }
        });

        savedQuestions.MapDelete("/{questionId:int}", async (
            int questionId,
            HttpContext http,
            ISavedQuestionService savedService,
            CancellationToken cancellationToken) =>
        {
            var res = await savedService.UnsaveQuestionAsync(CurrentUserId(http), questionId, cancellationToken);
            return Results.Ok(new SaveQuestionResultDto(res.IsSaved, res.Source, res.SavedAt));
        });
    }
}
