using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace WebMVC;

/// <summary>
/// Delegating HTTP handler that intercepts outgoing API requests and automatically attaches
/// the authenticated learner's JWT bearer token from the HttpOnly session cookie (ADR-0002).
/// If an Authorization header is already present, it is preserved.
/// </summary>
public sealed class CookieBearerHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization is null)
        {
            var httpContext = httpContextAccessor.HttpContext;
            if (httpContext is not null)
            {
                var currentUser = httpContext.RequestServices.GetService<CurrentUser>();
                if (currentUser is not null)
                {
                    var token = await currentUser.GetBearerTokenAsync();
                    if (!string.IsNullOrEmpty(token))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    }
                }
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
