using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class MentorEndpoints
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    private static void MapSlots(RouteGroupBuilder admin)
    {
        admin.MapGet("/{mentorId:int}/weekly-slots", async (
            int mentorId,
            IMentorService mentorService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var slots = await mentorService.GetAdminMentorWeeklySlotsAsync(mentorId, cancellationToken);
                return Results.Ok(slots);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        admin.MapPut("/{mentorId:int}/weekly-slots", async (
            int mentorId,
            JsonElement body,
            IMentorService mentorService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                IReadOnlyList<MentorWeeklySlotInputDto> weeklySlots;
                if (body.ValueKind == JsonValueKind.Array)
                {
                    weeklySlots = body.Deserialize<List<MentorWeeklySlotInputDto>>(WebJsonOptions) ?? [];
                }
                else if (body.ValueKind == JsonValueKind.Object)
                {
                    var wrapper = body.Deserialize<ReplaceMentorWeeklySlotsRequest>(WebJsonOptions);
                    weeklySlots = wrapper?.WeeklySlots ?? [];
                }
                else
                {
                    weeklySlots = [];
                }

                var updated = await mentorService.ReplaceMentorWeeklySlotsAsync(mentorId, weeklySlots, cancellationToken);
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

        admin.MapGet("/payouts", async (
            DateOnly? from,
            DateOnly? to,
            IMentorService mentorService,
            TimeProvider clock,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var now = ReportingPeriod.LocalInstant(clock.GetUtcNow());
                var today = DateOnly.FromDateTime(now.DateTime);
                var toDay = to ?? today;
                var fromDay = from ?? new DateOnly(toDay.Year, toDay.Month, 1);

                var report = await mentorService.GetAdminMentorPayoutsAsync(fromDay, toDay, cancellationToken);
                return Results.Ok(report);
            }
            catch (ValidationException ex)
            {
                return ValidationProblem(ex);
            }
        });

        admin.MapGet("/payouts/export", async (
            DateOnly? from,
            DateOnly? to,
            IMentorService mentorService,
            TimeProvider clock,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var now = ReportingPeriod.LocalInstant(clock.GetUtcNow());
                var today = DateOnly.FromDateTime(now.DateTime);
                var toDay = to ?? today;
                var fromDay = from ?? new DateOnly(toDay.Year, toDay.Month, 1);

                var rows = await mentorService.GetMentorPayoutExportRowsAsync(fromDay, toDay, cancellationToken);
                var bytes = MentorPayoutExportCsv.Format(rows, now);

                return Results.File(
                    bytes,
                    MentorPayoutExportCsv.ContentType,
                    $"mentor-payouts-{fromDay:yyyy-MM-dd}-to-{toDay:yyyy-MM-dd}.csv");
            }
            catch (ValidationException ex)
            {
                return ValidationProblem(ex);
            }
        });
    }
}
