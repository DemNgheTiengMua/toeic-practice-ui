using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class InsightEndpoints
{
    public static void MapInsightEndpoints(this IEndpointRouteBuilder app)
    {
        var learner = app.MapGroup("/api/learner/insights").RequireAuthorization(Roles.Learner);
        MapPractice(learner);
        MapExams(learner);
        MapRetries(learner);
        MapDashboard(learner);
        MapPaths(learner);
    }
}
