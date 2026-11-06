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

        // ---- Saved Questions (issue #9, #224) ----
        var savedQuestions = learner.MapGroup("/saved-questions");

        savedQuestions.MapGet("", async (
            [FromQuery] string? source,
            [FromQuery] int? sectionId,
            [FromQuery] int? categoryId,
            HttpContext http,
            ISavedQuestionService savedService,
            CancellationToken cancellationToken) =>
        {
            // An unknown source shows every saved question, as the string filter always did.
            SavedQuestionSource? sourceFilter = EnumCodes.TryParse<SavedQuestionSource>(source, out var parsedSource) ? parsedSource : null;
            var filter = new SavedQuestionFilterInput(sourceFilter, sectionId, categoryId);
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

        // #224: addressed by the bookmark's own row id, which is unique across both shapes.
        savedQuestions.MapGet("/{savedQuestionId:int}", async Task<IResult> (
            int savedQuestionId,
            HttpContext http,
            ISavedQuestionService savedService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var q = await savedService.GetSavedQuestionAsync(CurrentUserId(http), savedQuestionId, cancellationToken);
                return Results.Ok(ToSavedQuestionDto(q));
            }
            catch (SavedQuestionException ex) when (ex.Code == SavedQuestionException.SavedQuestionNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Saved question not found", ApiError.Codes.SavedQuestionNotFound, ex.Message);
            }
        });

        // The two save shapes are two routes: a question id and an item-version id are separate
        // counters, so one address would file the wrong thing.
        savedQuestions.MapPost("/questions/{questionId:int}", async Task<IResult> (
            int questionId,
            SaveQuestionRequest? request,
            HttpContext http,
            ISavedQuestionService savedService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var res = await savedService.SaveQuestionAsync(CurrentUserId(http), questionId, ParseSource(request), cancellationToken);
                return Results.Ok(ToResultDto(res));
            }
            catch (SavedQuestionException ex) when (ex.Code == SavedQuestionException.QuestionNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Question not found", ApiError.Codes.QuestionNotFound, ex.Message);
            }
        });

        // #224: a pool item is saved by the version on screen, which is what the link pins.
        savedQuestions.MapPost("/item-versions/{itemVersionId:int}", async Task<IResult> (
            int itemVersionId,
            SaveQuestionRequest? request,
            HttpContext http,
            ISavedQuestionService savedService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var res = await savedService.SaveItemVersionAsync(CurrentUserId(http), itemVersionId, ParseSource(request), cancellationToken);
                return Results.Ok(ToResultDto(res));
            }
            catch (SavedQuestionException ex) when (ex.Code == SavedQuestionException.QuestionNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Item version not found", ApiError.Codes.QuestionNotFound, ex.Message);
            }
        });

        savedQuestions.MapDelete("/{savedQuestionId:int}", async (
            int savedQuestionId,
            HttpContext http,
            ISavedQuestionService savedService,
            CancellationToken cancellationToken) =>
        {
            var res = await savedService.UnsaveAsync(CurrentUserId(http), savedQuestionId, cancellationToken);
            return Results.Ok(ToResultDto(res));
        });
    }

    /// <summary>Only the practice flow asks for "incorrect"; anything else is a manual save.</summary>
    private static SavedQuestionSource ParseSource(SaveQuestionRequest? request) =>
        EnumCodes.TryParse<SavedQuestionSource>(request?.Source, out var parsedSource)
            ? parsedSource
            : SavedQuestionSource.Manual;

    private static SaveQuestionResultDto ToResultDto(SaveQuestionResult res) =>
        new(res.IsSaved, res.Source?.MapTo<SavedQuestionSourceDto>(), res.SavedAt, res.SavedQuestionId);
}
