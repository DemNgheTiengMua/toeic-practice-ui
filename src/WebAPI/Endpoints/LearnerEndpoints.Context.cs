using System.ComponentModel.DataAnnotations;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class LearnerEndpoints
{
    private static void MapContext(RouteGroupBuilder learner)
    {

        learner.MapGet("/context", async (HttpContext http, ILearnerService learners, CancellationToken cancellationToken) =>
        {
            var context = await learners.GetContextAsync(CurrentUserId(http), cancellationToken);
            return Results.Ok(new LearnerContextDto(
                ToCertificateDto(context.Active),
                context.Certificates.Select(ToCertificateDto).ToList(),
                context.CreditBalance,
                context.Skills.Select(s => s.MapTo<ModalityDto>()).ToList(),
                context.ExamDate));
        });

        learner.MapGet("/onboarding-state", async (HttpContext http, ILearnerService learners, CancellationToken cancellationToken) =>
        {
            var state = await learners.GetOnboardingStateAsync(CurrentUserId(http), cancellationToken);
            return Results.Ok(new OnboardingStateDto(
                state.HasCompletedPlacement,
                state.ActivePlacementAttemptId,
                state.ActivePlacementId,
                state.LatestPlacementId,
                state.HasTargetGoal,
                state.HasActiveLearningPath,
                state.IsOobeComplete));
        });

        learner.MapPut("/active-certificate", async Task<IResult> (
            SwitchCertificateRequest request,
            HttpContext http,
            ILearnerService learners,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await learners.SwitchCertificateAsync(CurrentUserId(http), request.Code, cancellationToken);
            }
            catch (ValidationException ex)
            {
                return ValidationProblem(ex);
            }
            catch (CertificateUnavailableException ex)
            {
                return Problem(StatusCodes.Status404NotFound, "Certificate unavailable", ApiError.Codes.CertificateUnavailable, ex.Message);
            }

            return Results.NoContent();
        });
    }
}
