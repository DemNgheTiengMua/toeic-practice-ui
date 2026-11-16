using System.Security.Claims;
using BusinessObjects;
using Contracts;
using Services;
using WebAPI.Filters;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AdminEndpoints
{
    private static void MapCertificates(RouteGroupBuilder admin)
    {

        // ---- Certificate Blueprint & Wizard Endpoints ----
        admin.MapGet("/certificates", async (ICertificateService certificates, CancellationToken cancellationToken) =>
        {
            var list = await certificates.GetAdminCertificatesAsync(cancellationToken);
            return Results.Ok(list);
        });

        admin.MapGet("/certificates/presets", (ICertificateService certificates) =>
        {
            var presets = certificates.GetOfficialPresets();
            return Results.Ok(presets);
        });

        admin.MapGet("/certificates/{certId:int}/blueprint", async (int certId, ICertificateService certificates, CancellationToken cancellationToken) =>
        {
            var blueprint = await certificates.GetCertificateBlueprintAsync(certId, cancellationToken);
            return blueprint is not null ? Results.Ok(blueprint) : Results.NotFound();
        });

        admin.MapGet("/certificates/by-code/{code}/blueprint", async (string code, ICertificateService certificates, CancellationToken cancellationToken) =>
        {
            var blueprint = await certificates.GetCertificateBlueprintAsync(code, cancellationToken);
            return blueprint is not null ? Results.Ok(blueprint) : Results.NotFound();
        });

        admin.MapPost("/certificates/wizard", async (SaveCertificateWizardRequest request, ICertificateService certificates, CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await certificates.SaveCertificateWizardAsync(request, cancellationToken);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Validation error", ApiError.Codes.Validation, ex.Message);
            }
            // E1.2 (#147): the certificate configuration rules are the admin's to fix, so they come
            // back as a 400 carrying the message that names the gap, band or missing rubric.
            // Results.Json rather than Problem(): AddApiProblemDetails rewrites every non-404/429
            // code extension to UNEXPECTED, which would erase the code the admin client switches on.
            catch (ScoreConversionException ex)
            {
                return Results.Json(
                    new { code = ApiError.Codes.ScoreConversionInvalid, title = "Certificate configuration invalid", status = 400, detail = ex.Message },
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }).AddEndpointFilter<RequestValidationFilter<SaveCertificateWizardRequest>>();

        // Deleting a certificate is a destructive act, so the answer says which of the two things
        // actually happened: destroyed, or withdrawn because learners depend on it.
        admin.MapDelete("/certificates/{certId:int}", async Task<IResult> (
            int certId,
            ICertificateService certificates,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await certificates.RemoveCertificateAsync(certId, cancellationToken);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return Problem(StatusCodes.Status404NotFound, "Certificate not found", ApiError.Codes.NotFound, ex.Message);
            }
            catch (CertificateRemovalException ex) when (ex.Code == CertificateRemovalException.LastActiveCertificate)
            {
                return Problem(StatusCodes.Status409Conflict, "Cannot remove the last certificate", ApiError.Codes.Validation, ex.Message);
            }
        });
    }
}
