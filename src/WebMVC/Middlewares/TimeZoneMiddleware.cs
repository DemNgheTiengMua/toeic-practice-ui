using Contracts;
using Microsoft.AspNetCore.Http;
using WebMVC.Formatters;
using WebMVC.Infrastructure;

namespace WebMVC.Middlewares;

/// <summary>
/// Loads the signed-in learner's time zone once per request into HttpContext.Items under
/// DateTimeFormatter.ZoneItemKey, where DateTimeFormatter reads it. Anonymous requests and any
/// failure fall back to the shortlist default — a page must never break because a zone lookup
/// failed.
/// </summary>
public sealed class TimeZoneMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, CurrentUser currentUser, IProfileClient api)
    {
        if (currentUser.IsSignedIn)
        {
            try
            {
                var profile = await api.GetProfileAsync(context.RequestAborted);
                context.Items[DateTimeFormatter.ZoneItemKey] = profile.TimeZoneId;
            }
            // One clause, because ApiException is an HttpRequestException: a separate transport
            // catch would swallow what an ApiException filter let through. A dead token
            // (locked, signed out elsewhere) must reach the sign-out middleware in Program.cs,
            // or the page renders as if still signed in; the filter matches LearnerControllerBase.
            catch (HttpRequestException ex)
                when (ex is not ApiException { Code: ApiError.Codes.Locked or ApiError.Codes.Unauthenticated })
            {
                // API unreachable or any other refusal: render in the default zone.
            }
            // The learner navigated away mid-lookup. Render in the default zone and let the
            // pipeline unwind — a disconnect is not a page failure worth a 500 (F4).
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
            }
        }

        await next(context);
    }
}
