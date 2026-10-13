using Contracts;

namespace WebMVC;

/// <summary>
/// Loads the signed-in learner's time zone once per request into HttpContext.Items under
/// DateTimeFormatter.ZoneItemKey, where DateTimeFormatter reads it. Anonymous requests and any
/// failure fall back to the shortlist default — a page must never break because a zone lookup
/// failed.
/// </summary>
public sealed class TimeZoneMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, CurrentUser currentUser, ICompanionApiClient api)
    {
        if (currentUser.IsSignedIn)
        {
            var token = await currentUser.GetBearerTokenAsync();
            if (token is not null)
            {
                try
                {
                    var profile = await api.GetProfileAsync(token, context.RequestAborted);
                    context.Items[DateTimeFormatter.ZoneItemKey] = profile.TimeZoneId;
                }
                catch (ApiException ex) when (ex.Code is not (ApiError.Codes.Locked or ApiError.Codes.Unauthenticated))
                {
                    // Any other refusal: render in the default zone rather than fail the page.
                    // A dead token (locked, signed out elsewhere) is rethrown so the sign-out
                    // middleware in Program.cs ends the session, the same filter
                    // LearnerControllerBase uses — a page must not render as if still signed in.
                }
                catch (HttpRequestException)
                {
                    // API unreachable: render in the default zone rather than fail the page.
                }
            }
        }

        await next(context);
    }
}
