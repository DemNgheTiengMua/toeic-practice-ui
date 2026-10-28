using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class LessonEndpoints
{
    public static void MapLessonEndpoints(this IEndpointRouteBuilder app)
    {
        var learner = app.MapGroup("/api/learner").RequireAuthorization(Roles.Learner);

        // GET /api/learner/lessons/{id:int}
        learner.MapGet("/lessons/{id:int}", GetLessonAsync);

        // GET /api/learner/lessons/{id:int}/progress
        learner.MapGet("/lessons/{id:int}/progress", GetLessonProgressAsync);

        // POST /api/learner/lessons/{id:int}/cards/{cardId:int}/view
        learner.MapPost("/lessons/{id:int}/cards/{cardId:int}/view", RecordCardViewAsync);

        // GET /api/learner/lessons/by-category/{categoryId:int}
        learner.MapGet("/lessons/by-category/{categoryId:int}", GetLessonByCategoryAsync);

        // POST /api/learner/learning-path/{pathId:int}/unlock
        learner.MapPost("/learning-path/{pathId:int}/unlock", UnlockPathAsync);

        // Direct aliases without /api/learner prefix
        var direct = app.MapGroup("").RequireAuthorization(Roles.Learner);
        direct.MapGet("/lessons/{id:int}", GetLessonAsync);
        direct.MapGet("/lessons/{id:int}/progress", GetLessonProgressAsync);
        direct.MapPost("/lessons/{id:int}/cards/{cardId:int}/view", RecordCardViewAsync);
        direct.MapGet("/lessons/by-category/{categoryId:int}", GetLessonByCategoryAsync);
        direct.MapPost("/learning-path/{pathId:int}/unlock", UnlockPathAsync);
    }
}
