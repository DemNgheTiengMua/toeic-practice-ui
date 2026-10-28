using System.ComponentModel.DataAnnotations;
using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class MentorEndpoints
{
    private static void MapCatalog(IEndpointRouteBuilder app)
    {
        // Public / learner endpoints
        app.MapGet("/api/mentors", async (
            [FromQuery] string? certCode,
            [FromQuery] string? skill,
            IMentorService mentorService,
            CancellationToken cancellationToken) =>
        {
            var mentors = await mentorService.GetMentorsAsync(certCode, skill, cancellationToken);
            return Results.Ok(mentors);
        });

        app.MapGet("/api/mentors/{mentorId:int}", async (
            int mentorId,
            IMentorService mentorService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var detail = await mentorService.GetMentorDetailAsync(mentorId, cancellationToken);
                return Results.Ok(detail);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });
    }
}
