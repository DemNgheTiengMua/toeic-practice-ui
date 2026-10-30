using System.Reflection;
using System.Security.Claims;
using Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using WebMVC;

namespace WebMVC.Tests;

/// <summary>The zone lookup may fail soft, but a dead token must reach the sign-out middleware
/// (Program.cs), or the page renders as if the learner were still signed in.</summary>
public sealed class TimeZoneMiddlewareTests
{
    [Theory]
    [InlineData(ApiError.Codes.Locked)]
    [InlineData(ApiError.Codes.Unauthenticated)]
    public async Task A_dead_token_is_rethrown_for_the_sign_out_middleware(string code)
    {
        var (context, user) = SignedInRequest();
        var api = Stub(new ApiException(code, "dead", 401));
        var reachedPage = false;
        var middleware = new TimeZoneMiddleware(_ => { reachedPage = true; return Task.CompletedTask; });

        var ex = await Assert.ThrowsAsync<ApiException>(() => middleware.InvokeAsync(context, user, api));

        Assert.Equal(code, ex.Code);
        Assert.False(reachedPage);
    }

    [Fact]
    public async Task Any_other_api_refusal_renders_in_the_default_zone()
    {
        var (context, user) = SignedInRequest();
        var api = Stub(new ApiException(ApiError.Codes.Unexpected, "boom", 500));
        var reachedPage = false;
        var middleware = new TimeZoneMiddleware(_ => { reachedPage = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context, user, api);

        Assert.True(reachedPage);
        Assert.False(context.Items.ContainsKey(DateTimeFormatter.ZoneItemKey));
    }

    [Fact]
    public async Task An_unreachable_api_renders_in_the_default_zone()
    {
        var (context, user) = SignedInRequest();
        var api = Stub(new HttpRequestException("connection refused"));
        var reachedPage = false;
        var middleware = new TimeZoneMiddleware(_ => { reachedPage = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context, user, api);

        Assert.True(reachedPage);
    }

    /// <summary>F4: the learner navigated away while the zone lookup ran. The request unwinds in
    /// the default zone instead of throwing a 500 out of the middleware.</summary>
    [Fact]
    public async Task A_client_abort_during_the_lookup_renders_in_the_default_zone()
    {
        var (context, user) = SignedInRequest();
        using var aborted = new CancellationTokenSource();
        context.RequestAborted = aborted.Token;
        var api = Stub(new OperationCanceledException(aborted.Token));
        var reachedPage = false;
        var middleware = new TimeZoneMiddleware(_ => { reachedPage = true; return Task.CompletedTask; });

        aborted.Cancel();
        await middleware.InvokeAsync(context, user, api);

        Assert.True(reachedPage);
        Assert.False(context.Items.ContainsKey(DateTimeFormatter.ZoneItemKey));
    }

    /// <summary>A lookup that timed out (HttpClient's TaskCanceledException, with no client abort)
    /// is a failed lookup like any other, so the page renders in the default zone, as the class
    /// promises. The page's own API calls still surface a sick API.</summary>
    [Fact]
    public async Task A_timed_out_lookup_renders_in_the_default_zone()
    {
        var (context, user) = SignedInRequest();
        var api = Stub(new TaskCanceledException("transport timeout", new TimeoutException()));
        var reachedPage = false;
        var middleware = new TimeZoneMiddleware(_ => { reachedPage = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context, user, api);

        Assert.True(reachedPage);
        Assert.False(context.Items.ContainsKey(DateTimeFormatter.ZoneItemKey));
    }

    /// <summary>A signed-in principal whose cookie ticket carries a JWT, as AuthController issues it.</summary>
    private static (HttpContext Context, CurrentUser User) SignedInRequest()
    {
        var identity = new ClaimsIdentity(AuthClaims.For(7, "learner@test.local", Roles.Learner, "Learner"), "Cookies");
        var principal = new ClaimsPrincipal(identity);
        var properties = new AuthenticationProperties(new Dictionary<string, string?> { ["jwt"] = "token" });

        var services = new ServiceCollection()
            .AddSingleton<IAuthenticationService>(new FixedAuthentication(principal, properties))
            .BuildServiceProvider();
        var context = new DefaultHttpContext { User = principal, RequestServices = services };
        return (context, new CurrentUser(new HttpContextAccessor { HttpContext = context }));
    }

    private static IProfileClient Stub(Exception profileFailure) =>
        DispatchProxyStub.Create(profileFailure);

    /// <summary>The API client fake: GetProfileAsync fails with the given exception; nothing else
    /// may be called. A proxy keeps the fake from growing a stub per API method.</summary>
    public class DispatchProxyStub : DispatchProxy
    {
        private Exception failure = null!;

        public static IProfileClient Create(Exception failure)
        {
            var proxy = DispatchProxy.Create<IProfileClient, DispatchProxyStub>();
            ((DispatchProxyStub)(object)proxy).failure = failure;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IProfileClient.GetProfileAsync)
                ? Task.FromException<ProfileDto>(failure)
                : throw new InvalidOperationException($"Unexpected API call: {targetMethod?.Name}");
    }
}
