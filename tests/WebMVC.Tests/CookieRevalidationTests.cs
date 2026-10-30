using System.Reflection;
using System.Security.Claims;
using Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace WebMVC.Tests;

/// <summary>
/// Cookie re-validation in Program.cs (OnValidatePrincipal -> GET /api/auth/me) fails open when the
/// API cannot answer: a stalled API must not sign every learner out or turn every page into a 500.
/// Only a real client abort is left to propagate, to the RequestAborted catch that ends the request
/// as 499. These run the real cookie handler and its events from Program.cs; only the outbound API
/// is replaced.
/// </summary>
public sealed class CookieRevalidationTests : IClassFixture<CookieRevalidationTests.Site>
{
    private readonly Site site;

    public CookieRevalidationTests(Site site) => this.site = site;

    [Fact]
    public async Task A_timed_out_revalidation_keeps_the_signed_in_principal()
    {
        site.Upstream.MeFailure = new TaskCanceledException("transport timeout", new TimeoutException());
        using var scope = site.Services.CreateScope();
        var context = SignedInRequest(scope.ServiceProvider);

        var result = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        Assert.True(result.Succeeded);
        Assert.True(result.Principal!.Identity!.IsAuthenticated);
    }

    [Fact]
    public async Task A_client_abort_during_revalidation_is_left_to_the_pipeline_abort_handler()
    {
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();
        site.Upstream.MeFailure = new OperationCanceledException(aborted.Token);
        using var scope = site.Services.CreateScope();
        var context = SignedInRequest(scope.ServiceProvider);
        context.RequestAborted = aborted.Token;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme));
    }

    /// <summary>A request carrying the auth cookie exactly as sign-in issues it: the learner's
    /// principal with the JWT in the ticket properties, protected by the scheme's own format.</summary>
    private static DefaultHttpContext SignedInRequest(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            AuthClaims.For(7, "learner@test.local", Roles.Learner, "Learner"),
            CookieAuthenticationDefaults.AuthenticationScheme));
        var properties = new AuthenticationProperties(new Dictionary<string, string?> { ["jwt"] = "token" })
        {
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1),
        };
        var ticket = new AuthenticationTicket(principal, properties, CookieAuthenticationDefaults.AuthenticationScheme);

        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Headers.Cookie = $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}";
        return context;
    }

    /// <summary>The real WebMVC host with only the outbound API swapped for <see cref="UpstreamStub"/>.</summary>
    public sealed class Site : WebApplicationFactory<Program>
    {
        public UpstreamStub Upstream { get; } = UpstreamStub.Create();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ApiBaseUrl", "http://localhost:5001");
            builder.ConfigureTestServices(services => services.AddSingleton(Upstream.Client));
        }
    }

    /// <summary>The upstream API: GetMeAsync fails with <see cref="MeFailure"/>; nothing else may
    /// be called.</summary>
    public class UpstreamStub : DispatchProxy
    {
        public Exception MeFailure { get; set; } = new InvalidOperationException("MeFailure not set");

        public ICompanionApiClient Client { get; private set; } = null!;

        public static UpstreamStub Create()
        {
            var proxy = DispatchProxy.Create<ICompanionApiClient, UpstreamStub>();
            var stub = (UpstreamStub)(object)proxy;
            stub.Client = proxy;
            return stub;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IAuthClient.GetMeAsync)
                ? Task.FromException<AuthUserDto>(MeFailure)
                : throw new InvalidOperationException($"Unexpected upstream call: {targetMethod?.Name}");
    }
}
