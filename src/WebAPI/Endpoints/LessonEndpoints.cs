using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class LessonEndpoints
{
    public static void MapLessonEndpoints(this IEndpointRouteBuilder app)
    {
        var learner = app.MapGroup("/api/learner").RequireAuthorization(Roles.Learner);

        // GET /api/learner/lessons/{id:int}?module=remedial
        learner.MapGet("/lessons/{id:int}", GetLessonAsync);

        // GET /api/learner/lessons/cell?skillId=&topicId=&level=&module=
        learner.MapGet("/lessons/cell", GetLessonForCellAsync);

        // GET /api/learner/lessons/{id:int}/progress?module=remedial
        learner.MapGet("/lessons/{id:int}/progress", GetLessonProgressAsync);

        // POST /api/learner/lessons/{id:int}/cards/{cardId:int}/view
        learner.MapPost("/lessons/{id:int}/cards/{cardId:int}/view", RecordCardViewAsync);

        // POST /api/learner/lessons/{id:int}/cards/{cardId:int}/micro-check
        learner.MapPost("/lessons/{id:int}/cards/{cardId:int}/micro-check", SubmitMicroCheckAsync);

        // GET /api/learner/lessons/by-category/{categoryId:int}
        learner.MapGet("/lessons/by-category/{categoryId:int}", GetLessonByCategoryAsync);

        // Direct aliases without /api/learner prefix
        var direct = app.MapGroup("").RequireAuthorization(Roles.Learner);
        direct.MapGet("/lessons/{id:int}", GetLessonAsync);
        direct.MapGet("/lessons/cell", GetLessonForCellAsync);
        direct.MapGet("/lessons/{id:int}/progress", GetLessonProgressAsync);
        direct.MapPost("/lessons/{id:int}/cards/{cardId:int}/view", RecordCardViewAsync);
        direct.MapPost("/lessons/{id:int}/cards/{cardId:int}/micro-check", SubmitMicroCheckAsync);
        direct.MapGet("/lessons/by-category/{categoryId:int}", GetLessonByCategoryAsync);
    }

    /// <summary>#240: the module a request names, normalised so an unknown code reads the primary one
    /// rather than an empty reader.</summary>
    private static string ModuleOrDefault(string? module) =>
        string.Equals(module, LessonModule.Remedial, StringComparison.OrdinalIgnoreCase)
            ? LessonModule.Remedial
            : LessonModule.Primary;
}
