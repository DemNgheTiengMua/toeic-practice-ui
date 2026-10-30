using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Services;
using WebAPI.Extensions;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// F4: a browser that disconnects mid-request makes RequestAborted cancel the session query.
/// The "Exception occurred while processing message" fail: noise came from that exception
/// escaping OnTokenValidated (JwtBearerHandler logs only when the event throws), so the event
/// must absorb it — but by failing validation, never by letting an unchecked token through.
/// These tests drive the real OnTokenValidated event.
/// </summary>
public sealed class TokenValidationCancellationTests
{
    [Fact]
    public Task A_client_abort_reported_as_OperationCanceledException_fails_validation_without_throwing() =>
        AssertAbortFailsClosed(new OperationCanceledException());

    [Fact]
    public Task A_client_abort_reported_as_SqlException_fails_validation_without_throwing() =>
        // SqlClient surfaces a query cancelled mid-flight as SqlException "Operation cancelled by
        // user"; it has no public constructor, and only its type matters here.
        AssertAbortFailsClosed((Exception)RuntimeHelpers.GetUninitializedObject(typeof(SqlException)));

    private static async Task AssertAbortFailsClosed(Exception failure)
    {
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();
        var (handler, context) = Build(aborted.Token, Auth.Throwing(failure));

        await handler(context);

        // The session and lock checks never finished, so the token must not authenticate.
        Assert.NotNull(context.Result);
        Assert.False(context.Result.Succeeded);
    }

    [Fact]
    public async Task A_cancellation_the_client_did_not_request_still_surfaces()
    {
        var (handler, context) = Build(CancellationToken.None,
            Auth.Throwing(new OperationCanceledException("transport timeout")));

        // Swallowing every cancellation would hide a genuinely sick database behind a pass.
        await Assert.ThrowsAsync<OperationCanceledException>(() => handler(context));
    }

    [Fact]
    public async Task An_ended_session_still_fails_validation()
    {
        var (handler, context) = Build(CancellationToken.None, Auth.Returning(SessionStatus.Ended));

        await handler(context);

        Assert.NotNull(context.Result);
        Assert.False(context.Result.Succeeded);
    }

    [Fact]
    public async Task A_locked_account_still_fails_validation_with_the_locked_code()
    {
        var (handler, context) = Build(CancellationToken.None, Auth.Returning(SessionStatus.Locked));

        await handler(context);

        Assert.NotNull(context.Result);
        Assert.False(context.Result.Succeeded);
        Assert.Equal(ApiError.Codes.Locked, context.HttpContext.Items["auth-failure"]);
    }

    /// <summary>Runs AddApiAuthentication against real configuration, so the event under test is
    /// the one the app registers rather than a copy that could drift from it.</summary>
    private static (Func<TokenValidatedContext, Task> Handler, TokenValidatedContext Context) Build(
        CancellationToken requestAborted, IAuthService auth)
    {
        var configuration = BuildConfiguration();
        var services = new ServiceCollection();
        services.AddSingleton(auth);
        services.AddSingleton(_ => configuration);
        services.AddApiAuthentication(configuration);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(IAccessTokenFactory.ClaimUserId, "7"),
            new Claim(IAccessTokenFactory.ClaimSessionStamp, "stamp"),
        ], "Bearer"));

        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        httpContext.RequestAborted = requestAborted;

        var options = services.BuildServiceProvider()
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        var scheme = new AuthenticationScheme(
            JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));
        var context = new TokenValidatedContext(httpContext, scheme, options) { Principal = principal };

        return (options.Events.OnTokenValidated, context);
    }

    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // A throwaway 32-byte key: nothing here signs or verifies, it only lets
                // AddApiAuthentication past its startup guards.
                ["Jwt:SigningKey"] = Convert.ToBase64String(new byte[32]),
                ["Jwt:Issuer"] = "webapi-tests",
                ["Jwt:Audience"] = "webapi-tests",
            })
            .Build();

    /// <summary>IAuthService has a dozen members and these tests need one; a proxy keeps the fake
    /// from growing a stub per member.</summary>
    private class Auth : DispatchProxy
    {
        private Func<Task<SessionStatus>> behaviour = null!;

        public static IAuthService Throwing(Exception failure) => Create(() => Task.FromException<SessionStatus>(failure));

        public static IAuthService Returning(SessionStatus status) => Create(() => Task.FromResult(status));

        private static IAuthService Create(Func<Task<SessionStatus>> behaviour)
        {
            var proxy = DispatchProxy.Create<IAuthService, Auth>();
            ((Auth)(object)proxy).behaviour = behaviour;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IAuthService.CheckSessionAsync)
                ? behaviour()
                : throw new InvalidOperationException($"Unexpected auth call: {targetMethod?.Name}");
    }
}
