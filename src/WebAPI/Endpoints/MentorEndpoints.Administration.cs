using System.ComponentModel.DataAnnotations;
using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class MentorEndpoints
{
    private static void MapAdministration(RouteGroupBuilder admin)
    {

        admin.MapGet("", async (IMentorService mentorService, CancellationToken cancellationToken) =>
        {
            var list = await mentorService.GetAdminMentorsAsync(cancellationToken);
            return Results.Ok(list);
        });

        admin.MapGet("/options", async (IMentorService mentorService, CancellationToken cancellationToken) =>
        {
            var options = await mentorService.GetCertificateOptionsAsync(cancellationToken);
            return Results.Ok(options);
        });

        admin.MapGet("/{mentorId:int}", async (int mentorId, IMentorService mentorService, CancellationToken cancellationToken) =>
        {
            try
            {
                var mentor = await mentorService.GetAdminMentorByIdAsync(mentorId, cancellationToken);
                return Results.Ok(mentor);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        admin.MapPost("", async (CreateMentorProfileRequest request, IMentorService mentorService, CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await mentorService.CreateMentorAsync(request, cancellationToken);
                return Results.Created($"/api/admin/mentors/{created.MentorId}", created);
            }
            catch (ValidationException ex)
            {
                return ValidationProblem(ex);
            }
            catch (ArgumentException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Validation error", ApiError.Codes.Validation, ex.Message);
            }
        });

        admin.MapPut("/{mentorId:int}", async (int mentorId, UpdateMentorProfileRequest request, IMentorService mentorService, CancellationToken cancellationToken) =>
        {
            try
            {
                var updated = await mentorService.UpdateMentorAsync(mentorId, request, cancellationToken);
                return Results.Ok(updated);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ValidationException ex)
            {
                return ValidationProblem(ex);
            }
            catch (ArgumentException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Validation error", ApiError.Codes.Validation, ex.Message);
            }
        });
    }
}
