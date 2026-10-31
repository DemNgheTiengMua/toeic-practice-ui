using System.Security.Claims;
using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AdminEndpoints
{
    private static void MapScoreConversion(RouteGroupBuilder admin)
    {

        admin.MapGet("/score-conversion", async (
            string? certCode,
            int? examId,
            IScoreConversionService scoreConversionService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var view = await scoreConversionService.GetScoreConversionAsync(certCode, examId, cancellationToken);
                return Results.Ok(ToAdminScoreConversionDto(view));
            }
            catch (ScoreConversionException ex) when (ex.Code == ScoreConversionException.CertificateNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Certificate not found", ApiError.Codes.CertificateUnavailable, ex.Message);
            }
        });

        admin.MapPut("/score-conversion/{certId:int}", async Task<IResult> (
            int certId,
            UpdateScoreConversionRequest request,
            IScoreConversionService scoreConversionService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var command = new UpdateScoreConversionCommand(
                    request.ExamId,
                    request.Scales.Select(s => new UpdateScoreScaleEntry(s.SkillId, s.RawScore, s.ScaledScore)).ToList(),
                    request.Bands.Select(b => new UpdateScoreBandEntry(b.BandId, b.Code, b.Name, b.MinTotal, b.MaxTotal, b.DisplayOrder)).ToList());

                var view = await scoreConversionService.UpdateScoreConversionAsync(certId, command, cancellationToken);
                return Results.Ok(ToAdminScoreConversionDto(view));
            }
            catch (ScoreConversionException ex) when (ex.Code == ScoreConversionException.CertificateNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Certificate not found", ApiError.Codes.CertificateUnavailable, ex.Message);
            }
            catch (ScoreConversionException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Score conversion invalid", ApiError.Codes.ScoreConversionInvalid, ex.Message);
            }
        });

        admin.MapPost("/score-conversion/preview", async (
            ScorePreviewRequestDto requestDto,
            IScoreConversionService scoreConversionService,
            CancellationToken cancellationToken) =>
        {
            var request = ToScorePreviewRequest(requestDto);
            try
            {
                var result = await scoreConversionService.CalculateScorePreviewAsync(request, cancellationToken);
                return Results.Ok(ToScorePreviewResultDto(result));
            }
            // E1.1 (#146): a skill with no scale is an authoring mistake the admin can fix, so it
            // comes back as a 400 naming the skill. Letting it escape made the preview answer an
            // opaque 500 that said nothing about which skill or which columns were missing.
            catch (ScoreConversionException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Score conversion invalid", ApiError.Codes.ScoreConversionInvalid, ex.Message);
            }
        });
    }
}
