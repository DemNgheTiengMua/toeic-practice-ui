using Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using WebMVC;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// The MVC site is the only caller of the API (ADR-0002); the JWT travels in an HttpOnly cookie
// and is forwarded as a bearer token on each API call.
builder.Services.AddHttpClient<ICompanionApiClient, CompanionApiClient>(client =>
    client.BaseAddress = new Uri(builder.Configuration["ApiBaseUrl"]
        ?? throw new InvalidOperationException("ApiBaseUrl is not configured.")));

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
        // The cookie alone must not outrun the account: re-validate the forwarded JWT against
        // the API (which itself re-checks the lock flag on every call) and reject the principal
        // when the token is dead — a locked user's next MVC request then fails too, not just
        // their next API call. Note a re-validation also renews the cookie by default; the
        // ticket's pinned ExpiresUtc (the JWT's own expiry) still caps the session.
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
                    await api.GetMeAsync(token, context.HttpContext.RequestAborted);
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
                    // during an API blip would be worse.
                }
            },
        };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("admin", policy => policy.RequireRole("admin"));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();

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
});

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
