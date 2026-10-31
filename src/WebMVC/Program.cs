using Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using WebMVC;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
});


builder.Services.AddTransient<CookieBearerHandler>();
builder.Services.AddTransient<ApiResilienceHandler>();
builder.Services.AddSingleton(sp => ApiResilienceOptions.FromConfiguration(builder.Configuration));
builder.Services.AddSingleton(sp => new ApiCircuitBreaker(
    sp.GetRequiredService<ApiResilienceOptions>(),
    // Real time in production; tests swap in a manual clock so outage timing is deterministic.
    sp.GetService<TimeProvider>() ?? TimeProvider.System));

// The MVC site is the only caller of the API (ADR-0002); the JWT travels in an HttpOnly cookie
// and is forwarded as a bearer token on each API call via CookieBearerHandler. ApiResilienceHandler
// sits outermost so its per-request fault marker short-circuits the cheapest possible place, and
// the connect budget lives on the primary handler, where the socket is actually opened (#140).
builder.Services.AddHttpClient<ICompanionApiClient, CompanionApiClient>(client =>
    client.BaseAddress = new Uri(builder.Configuration["ApiBaseUrl"]
        ?? throw new InvalidOperationException("ApiBaseUrl is not configured.")))
    .AddHttpMessageHandler<ApiResilienceHandler>()
    .AddHttpMessageHandler<CookieBearerHandler>()
    .ConfigurePrimaryHttpMessageHandler(sp => new SocketsHttpHandler
    {
        ConnectTimeout = sp.GetRequiredService<ApiResilienceOptions>().ConnectTimeout,
    });

builder.Services.AddScoped<ICertificatesClient>(sp => sp.GetRequiredService<ICompanionApiClient>());
builder.Services.AddScoped<IAuthClient>(sp => sp.GetRequiredService<ICompanionApiClient>());
builder.Services.AddScoped<IProfileClient>(sp => sp.GetRequiredService<ICompanionApiClient>());
builder.Services.AddScoped<ILearnerContextClient>(sp => sp.GetRequiredService<ICompanionApiClient>());
builder.Services.AddScoped<IPracticeClient>(sp => sp.GetRequiredService<ICompanionApiClient>());
builder.Services.AddScoped<ISavedQuestionsClient>(sp => sp.GetRequiredService<ICompanionApiClient>());
builder.Services.AddScoped<IWeaknessAnalysisClient>(sp => sp.GetRequiredService<ICompanionApiClient>());
builder.Services.AddScoped<IOrdersClient>(sp => sp.GetRequiredService<ICompanionApiClient>());
builder.Services.AddScoped<IWalletClient>(sp => sp.GetRequiredService<ICompanionApiClient>());
builder.Services.AddScoped<IMockExamsClient>(sp => sp.GetRequiredService<ICompanionApiClient>());
builder.Services.AddScoped<IWritingClient>(sp => sp.GetRequiredService<ICompanionApiClient>());
builder.Services.AddScoped<ISpeakingClient>(sp => sp.GetRequiredService<ICompanionApiClient>());
builder.Services.AddScoped<IPlacementClient>(sp => sp.GetRequiredService<ICompanionApiClient>());
builder.Services.AddScoped<ILearningPathClient>(sp => sp.GetRequiredService<ICompanionApiClient>());
builder.Services.AddScoped<ILessonClient>(sp => sp.GetRequiredService<ICompanionApiClient>());
builder.Services.AddScoped<IMentorsClient>(sp => sp.GetRequiredService<ICompanionApiClient>());
builder.Services.AddScoped<ISupportClient>(sp => sp.GetRequiredService<ICompanionApiClient>());
builder.Services.AddScoped<IAiInsightsClient>(sp => sp.GetRequiredService<ICompanionApiClient>());
builder.Services.AddScoped<IMediaClient>(sp => sp.GetRequiredService<ICompanionApiClient>());
builder.Services.AddScoped<IAdminPackagesClient>(sp => sp.GetRequiredService<ICompanionApiClient>());
builder.Services.AddScoped<IAdminScoreConversionClient>(sp => sp.GetRequiredService<ICompanionApiClient>());
builder.Services.AddScoped<IAdminOrdersClient>(sp => sp.GetRequiredService<ICompanionApiClient>());
builder.Services.AddScoped<IAdminUsersClient>(sp => sp.GetRequiredService<ICompanionApiClient>());
builder.Services.AddScoped<IAdminDashboardClient>(sp => sp.GetRequiredService<ICompanionApiClient>());
builder.Services.AddScoped<IAdminMentorsClient>(sp => sp.GetRequiredService<ICompanionApiClient>());
builder.Services.AddScoped<IAdminComplaintsClient>(sp => sp.GetRequiredService<ICompanionApiClient>());
builder.Services.AddScoped<IAdminExamsClient>(sp => sp.GetRequiredService<ICompanionApiClient>());
builder.Services.AddScoped<IAdminImportsClient>(sp => sp.GetRequiredService<ICompanionApiClient>());
builder.Services.AddScoped<IAdminLessonsClient>(sp => sp.GetRequiredService<ICompanionApiClient>());
builder.Services.AddScoped<IAdminCertificatesClient>(sp => sp.GetRequiredService<ICompanionApiClient>());

// Cookie lifetime matches the token's single lifetime; there is no sliding renewal because
// there is no refresh token. SecurePolicy: SameAsRequest because local dev and the grading
// environment run plain HTTP; flip to Always when the site is deployed behind TLS.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "ee.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.LoginPath = "/auth/sign-in";
        options.AccessDeniedPath = "/auth/denied";
        options.ExpireTimeSpan = TimeSpan.FromHours(IAccessTokenFactory.LifetimeHours);
        // No sliding renewal: re-validation would otherwise re-issue the cookie past the JWT's
        // own expiry. The ticket's ExpiresUtc is pinned to the token's expiry at sign-in.
        options.SlidingExpiration = false;
        // The cookie alone must not outrun the account: re-validate the forwarded JWT against
        // the API (which itself re-checks the lock flag on every call). A dead token, or one
        // whose role changed since the cookie was issued, ends the session.
        options.Events = new CookieAuthenticationEvents
        {
            OnValidatePrincipal = async context =>
            {
                var token = context.Properties.GetString("jwt");
                if (string.IsNullOrEmpty(token))
                {
                    context.RejectPrincipal();
                    return;
                }

                var api = context.HttpContext.RequestServices.GetRequiredService<ICompanionApiClient>();
                try
                {
                    var me = await api.GetMeAsync(token, context.HttpContext.RequestAborted);
                    var cookieRole = context.Principal?.FindFirst(IAccessTokenFactory.ClaimRole)?.Value;
                    if (me.Role != cookieRole)
                    {
                        // Role changed server-side (e.g. promoted or demoted); refuse rather than
                        // serve pages under the stale role.
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    }
                }
                catch (ApiException)
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                }
                catch (HttpRequestException)
                {
                    // API unreachable: keep the principal — page-level calls already degrade
                    // (CertificatesController renders "Unavailable"), and signing everyone out
                    // during an API blip would be worse. Fail-open is a deliberate availability
                    // tradeoff; the API still enforces the lock on every forwarded call, so a
                    // locked user can browse static pages but gets nothing from the API.
                }
                catch (OperationCanceledException) when (!context.HttpContext.RequestAborted.IsCancellationRequested)
                {
                    // Transport timeout, not a client abort: same deliberate fail-open as above.
                    // A real client abort propagates to the RequestAborted catch in the pipeline
                    // middleware below, which ends the request as 499 without logging a failure (F4).
                }
            },
        };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Roles.Admin, policy => policy.RequireRole(Roles.Admin))
    .AddPolicy(Roles.Learner, policy => policy.RequireRole(Roles.Learner));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<WebMVC.Formatters.DateTimeFormatter>();
builder.Services.AddScoped<WebMVC.DateTimeFormatter>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// When the API answers a forwarded call with AUTH_LOCKED / AUTH_REQUIRED the cookie's JWT is
// dead even though the cookie itself still looks valid: drop the cookie and send the browser
// to sign-in rather than showing a 500. Locked gets the "contact support" message; a merely
// expired token goes to a plain sign-in.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (ApiException ex) when (ex.Code is ApiError.Codes.Locked or ApiError.Codes.Unauthenticated)
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        context.Response.Redirect(ex.Code == ApiError.Codes.Locked ? "/auth/sign-in?locked=1" : "/auth/sign-in");
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        // The learner closed the tab or navigated away while a forwarded API call was in flight.
        // There is no client left to read a status, and letting this reach the exception handler
        // would log a 500 for a request that succeeded in doing exactly what was asked (F4).
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
        }
    }
});

app.UseRouting();

// Static assets are anonymous by construction: no authorization metadata, nothing in them to
// personalise. ShortCircuit makes routing run the matched asset endpoint inside UseRouting and
// end the request there, so a css/js/mp3/woff2 request never reaches cookie re-validation
// (GET /api/auth/me), TimeZoneMiddleware (GET /api/profile) or the controller pipeline. Without
// it, MapStaticAssets merely registers endpoints that execute at the far end of the pipeline,
// after every middleware — which is the flood of per-asset API calls this guards against (F3).
app.MapStaticAssets().ShortCircuit();

app.UseAuthentication();
app.UseAuthorization();

// Load the signed-in learner's time zone for this request; DateTimeFormatter renders every
// stored UTC instant in it (UI-52).
app.UseMiddleware<TimeZoneMiddleware>();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

// For WebApplicationFactory-based tests
public partial class Program;
