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
                catch (ApiException)
                {
                    // Token rejected mid-request (e.g. just locked): the auth pipeline handles the
                    // sign-out; the page just renders in the default zone.
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
