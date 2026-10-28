using System.ComponentModel.DataAnnotations;
using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class MentorEndpoints
{
    public static void MapMentorEndpoints(this IEndpointRouteBuilder app)
    {

        // Learner booking endpoints
        var learnerMentors = app.MapGroup("/api/mentors").RequireAuthorization(Roles.Learner);

        // Admin endpoints
        var admin = app.MapGroup("/api/admin/mentors").RequireAuthorization(Roles.Admin);
        MapCatalog(app);
        MapBookings(learnerMentors);
        MapAdministration(admin);
        MapSlots(admin);
    }
}
