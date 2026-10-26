using WebAPI.Endpoints;

namespace WebAPI.Extensions;

public static class EndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapAuthEndpoints();
        app.MapAdminEndpoints();
        app.MapAdminImportEndpoints();
        app.MapAdminLessonEndpoints();
        app.MapLearnerEndpoints();
        app.MapExamEndpoints();
        app.MapPracticeEndpoints();
        app.MapSavedQuestionEndpoints();
        app.MapWeaknessEndpoints();
        app.MapCatalogEndpoints();
        app.MapCheckoutEndpoints();
        app.MapWritingEndpoints();
        app.MapSpeakingEndpoints();
        app.MapInsightEndpoints();
        app.MapPlacementEndpoints();
        app.MapLearningPathEndpoints();
        app.MapLessonEndpoints();
        app.MapMentorEndpoints();
        app.MapComplaintEndpoints();

        return app;
    }
}
