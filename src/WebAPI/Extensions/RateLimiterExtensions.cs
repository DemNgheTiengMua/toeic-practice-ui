using Contracts;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

namespace WebAPI.Extensions;

public static class RateLimiterExtensions
{
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IHostEnvironment environment)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("auth", httpContext =>
            {
                var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
                var clientIp = !string.IsNullOrWhiteSpace(forwardedFor)
                    ? forwardedFor.Split(',')[0].Trim()
                    : httpContext.Connection.RemoteIpAddress?.ToString();

                var partitionKey = httpContext.User.FindFirst(IAccessTokenFactory.ClaimUserId)?.Value
                    ?? clientIp
                    ?? "anonymous";

                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = environment.IsDevelopment() ? 100 : 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                });
            });

            // ADR-0018: every hit on these spends a real mail quota, and the addresses are typed by
            // whoever is asking (resend-activation and forgot-password take an email, not a session).
            // Three per hour per partition: a learner who clicks "resend" four times is out of luck
            // for an hour, which is the intended cost of not being able to spam a stranger's inbox.
            options.AddPolicy("mail", httpContext =>
            {
                var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
                var clientIp = !string.IsNullOrWhiteSpace(forwardedFor)
                    ? forwardedFor.Split(',')[0].Trim()
                    : httpContext.Connection.RemoteIpAddress?.ToString();

                var partitionKey = httpContext.User.FindFirst(IAccessTokenFactory.ClaimUserId)?.Value
                    ?? clientIp
                    ?? "anonymous";

                return RateLimitPartition.GetFixedWindowLimiter($"mail:{partitionKey}", _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = environment.IsDevelopment() ? 20 : 3,
                        Window = TimeSpan.FromHours(1),
                        QueueLimit = 0,
                    });
            });
        });

        return services;
    }
}
