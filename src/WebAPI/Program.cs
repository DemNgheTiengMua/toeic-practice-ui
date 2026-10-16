using Contracts;
using DataAccess;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Services;
using System.Threading.RateLimiting;
using WebAPI;
using WebAPI.Endpoints;

// Load .env file if present in workspace root or current directory
var candidateDirs = new[]
{
    Directory.GetCurrentDirectory(),
    Path.Combine(Directory.GetCurrentDirectory(), "..", ".."),
    AppContext.BaseDirectory
};
foreach (var dir in candidateDirs)
{
    var envFile = Path.Combine(dir, ".env");
    if (File.Exists(envFile))
    {
        foreach (var line in File.ReadAllLines(envFile))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;
            var eqIdx = trimmed.IndexOf('=');
            if (eqIdx <= 0) continue;
            var key = trimmed[..eqIdx].Trim();
            var val = trimmed[(eqIdx + 1)..].Trim().Trim('\'', '"');
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
            {
                Environment.SetEnvironmentVariable(key, val);
            }
        }
        break;
    }
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<CompanionDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("CompanionDb")));
builder.Services.AddHealthChecks()
    .AddDbContextCheck<CompanionDbContext>();

// One token shape: HS256, 8-hour lifetime, no refresh token (issue #4). The signing key comes
// from configuration ("Jwt:SigningKey", base64); in development it lives in user-secrets.
// Config is parsed once, up front, with a friendly error — a malformed key must not surface
// as a raw FormatException on first use.
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? new JwtOptions();
const string jwtConfigHelp =
    "Configure the JWT in the \"Jwt\" section. In development: " +
    "dotnet user-secrets set \"Jwt:SigningKey\" <base64-32-bytes> --project src/WebAPI (plus Jwt:Issuer and Jwt:Audience).";
byte[] signingKeyBytes;
try
{
    signingKeyBytes = Convert.FromBase64String(jwtOptions.SigningKey ?? "");
}
catch (FormatException ex)
{
    throw new InvalidOperationException("Jwt:SigningKey is not valid base64. " + jwtConfigHelp, ex);
}
if (string.IsNullOrEmpty(jwtOptions.Issuer) || string.IsNullOrEmpty(jwtOptions.Audience))
{
    throw new InvalidOperationException("Jwt:Issuer and Jwt:Audience are required. " + jwtConfigHelp);
}
if (signingKeyBytes.Length < 32)
{
    throw new InvalidOperationException("Jwt:SigningKey must decode to at least 32 bytes (256 bits) for HS256. " + jwtConfigHelp);
}
builder.Services.AddSingleton(Options.Create(jwtOptions));
builder.Services.AddSingleton<IAccessTokenFactory, JwtAccessTokenFactory>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(signingKeyBytes),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RoleClaimType = IAccessTokenFactory.ClaimRole,
        };
        // A locked account is refused on every request, not only at sign-in: re-check the live
        // row on each token validation. Auth failures get a ProblemDetails body with a stable
        // code here, because the exception-handler middleware never sees auth rejections.
        static Task WriteAuthProblem(HttpContext http, int status, string title, string code)
        {
            var problem = new ProblemDetails { Title = title, Status = status };
            problem.Extensions["code"] = code;
            http.Response.StatusCode = status;
            return http.Response.WriteAsJsonAsync(problem);
        }

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var auth = context.HttpContext.RequestServices.GetRequiredService<IAuthService>();
                var idClaim = context.Principal?.FindFirst(IAccessTokenFactory.ClaimUserId)?.Value;
                if (!int.TryParse(idClaim, out var userId))
                {
                    context.Fail("Token carries no usable user id.");
                    return;
                }

                // The stamp ends sessions issued before a password change (e.g. a reset after a
                // stolen session) without waiting out the 8-hour token.
                var stamp = context.Principal?.FindFirst(IAccessTokenFactory.ClaimSessionStamp)?.Value;
                switch (await auth.CheckSessionAsync(userId, stamp, context.HttpContext.RequestAborted))
                {
                    case SessionStatus.Locked:
                        context.Fail("Account is locked.");
                        // Items, not a response header: this is server-internal plumbing between events.
                        context.HttpContext.Items["auth-failure"] = ApiError.Codes.Locked;
                        break;
                    case SessionStatus.Ended:
                        context.Fail("Account no longer exists or the password has changed.");
                        break;
                }
            },
            OnChallenge = async context =>
            {
                context.HandleResponse();
                var code = context.HttpContext.Items["auth-failure"] as string == ApiError.Codes.Locked
                    ? ApiError.Codes.Locked
                    : ApiError.Codes.Unauthenticated;
                var title = code == ApiError.Codes.Locked ? "Account locked" : "Authentication required";
                await WriteAuthProblem(context.HttpContext, StatusCodes.Status401Unauthorized, title, code);
            },
            OnForbidden = context =>
                WriteAuthProblem(context.HttpContext, StatusCodes.Status403Forbidden, "Forbidden", ApiError.Codes.Forbidden),
        };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Roles.Admin, policy => policy.RequireRole(Roles.Admin))
    .AddPolicy(Roles.Learner, policy => policy.RequireRole(Roles.Learner));

builder.Services.AddRateLimiter(options =>
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
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });
});

// Every failure — thrown exception or bare status-code result — leaves the API as RFC 9457
// ProblemDetails with a stable machine-readable "code" extension the MVC site switches on.
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["code"] = context.ProblemDetails.Status switch
        {
            StatusCodes.Status404NotFound => ApiError.Codes.NotFound,
            StatusCodes.Status429TooManyRequests => ApiError.Codes.TooManyRequests,
            _ => ApiError.Codes.Unexpected,
        };
    });
builder.Services.AddScoped<ICertificateService, CertificateService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddSingleton(TimeProvider.System);
// Emailed links point at the MVC site (the browser never talks to the API, ADR-0002).
builder.Services.AddSingleton(
    builder.Configuration.GetSection(AuthLinkOptions.SectionName).Get<AuthLinkOptions>() ?? new AuthLinkOptions());
// Dev mail: every message lands as a .eml file in a folder; swap the implementation for real SMTP.
var emailOptions = builder.Configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>() ?? new EmailOptions();
builder.Services.AddSingleton<IEmailSender>(sp => new DropFolderEmailSender(
    Path.GetFullPath(emailOptions.DropFolder, builder.Environment.ContentRootPath),
    emailOptions.From,
    sp.GetRequiredService<ILogger<DropFolderEmailSender>>()));
builder.Services.AddScoped<ILearnerService, LearnerService>();
builder.Services.AddScoped<IPracticeService, PracticeService>();
builder.Services.AddScoped<ISavedQuestionService, SavedQuestionService>();
builder.Services.AddScoped<IWeaknessAnalysisService, WeaknessAnalysisService>();
builder.Services.AddScoped<IPackageService, PackageService>();
builder.Services.AddScoped<IMockExamService, MockExamService>();
builder.Services.AddScoped<IAdminOrderService, AdminOrderService>();
builder.Services.AddScoped<IAdminUserService, AdminUserService>();
builder.Services.AddScoped<IScoreConversionService, ScoreConversionService>();
builder.Services.AddScoped<IAdminExamService, AdminExamService>();
builder.Services.AddScoped<IQuestionImportService, QuestionImportService>();
builder.Services.AddScoped<IAdminDashboardService, AdminDashboardService>();
var speechSuperOptions = builder.Configuration.GetSection(SpeechSuperOptions.SectionName).Get<SpeechSuperOptions>() ?? new SpeechSuperOptions();
speechSuperOptions.AppKey = builder.Configuration["SPEECHSUPER_APP_KEY"] ?? speechSuperOptions.AppKey;
speechSuperOptions.SecretKey = builder.Configuration["SPEECHSUPER_SECRET_KEY"] ?? speechSuperOptions.SecretKey;
speechSuperOptions.BaseUrl = builder.Configuration["SPEECHSUPER_BASE_URL"] ?? speechSuperOptions.BaseUrl;
speechSuperOptions.CoreType = builder.Configuration["SPEECHSUPER_CORE_TYPE"] ?? speechSuperOptions.CoreType;

builder.Services.AddSingleton(speechSuperOptions);
builder.Services.AddHttpClient<ISpeechSuperClient, SpeechSuperClient>();

var claudeOptions = builder.Configuration.GetSection(ClaudeOptions.SectionName).Get<ClaudeOptions>() ?? new ClaudeOptions();
claudeOptions.ApiKey = builder.Configuration["CLAUDE_API_KEY"] ?? claudeOptions.ApiKey;
claudeOptions.BaseUrl = builder.Configuration["CLAUDE_BASE_URL"] ?? claudeOptions.BaseUrl;
claudeOptions.Model = builder.Configuration["CLAUDE_MODEL"] ?? claudeOptions.Model;

builder.Services.AddSingleton(claudeOptions);

var audioStoragePath = builder.Configuration["FileStore:AudioDirectory"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "..", "..", "App_Data", "audio");
builder.Services.AddSingleton<IFileStore>(new LocalFileStore(audioStoragePath));

var aiProvider = builder.Configuration["AI_PROVIDER"] ?? builder.Configuration["AiProviders:Provider"] ?? "fake";
if (aiProvider.Equals("real", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IAiGrader, SpeechSuperAiGrader>();
}
else
{
    builder.Services.AddSingleton<IAiGrader, FakeAiGrader>();
}
builder.Services.AddScoped<IAiGradingProcessor, AiGradingProcessor>();
builder.Services.AddScoped<IWritingPracticeService, WritingPracticeService>();
builder.Services.AddScoped<ISpeakingPracticeService, SpeakingPracticeService>();
builder.Services.AddScoped<IPlacementService, PlacementService>();
builder.Services.AddScoped<ILearningPathService, LearningPathService>();
builder.Services.AddScoped<IMentorService, MentorService>();
builder.Services.AddScoped<IComplaintService, ComplaintService>();
builder.Services.AddHostedService<AiGradingBackgroundService>();

builder.Services.AddSingleton<IAiInsightGenerator, FakeAiInsightGenerator>();
builder.Services.AddScoped<IAiInsightService, AiInsightService>();
builder.Services.AddHostedService<AiInsightBackgroundService>();

var payOsOptions = builder.Configuration.GetSection(PayOsOptions.SectionName).Get<PayOsOptions>() ?? new PayOsOptions();
builder.Services.AddSingleton(payOsOptions);
builder.Services.AddSingleton<PayOsPaymentGateway>();

var mock = new MockPaymentGateway();
builder.Services.AddSingleton(mock);
builder.Services.AddSingleton<IMockPaymentGateway>(mock);

var gatewayChoice = builder.Configuration["Payment:Gateway"]?.ToLowerInvariant() ?? "mock";
if (gatewayChoice == "payos")
{
    builder.Services.AddSingleton<IPaymentGateway>(sp => sp.GetRequiredService<PayOsPaymentGateway>());
}
else
{
    builder.Services.AddSingleton<IPaymentGateway>(mock);
}
builder.Services.AddScoped<IPaymentService, PaymentService>();

var app = builder.Build();

// Every unhandled failure leaves the API as RFC 9457 ProblemDetails with a stable code.
app.UseExceptionHandler(errorApp =>
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        var problem = new ProblemDetails
        {
            Title = "An unexpected error occurred.",
            Status = StatusCodes.Status500InternalServerError,
        };
        problem.Extensions["code"] = ApiError.Codes.Unexpected;
        await context.Response.WriteAsJsonAsync(problem);
    }));

app.MapHealthChecks("/healthz");

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

// Bare status-code results get a ProblemDetails body. Bearer-auth 401/403s are answered by the
// JWT events above (OnChallenge / OnForbidden), which run first; this fallback fires for 404s
// and any other status — including a hypothetical future non-bearer 401/403, which would then
// get the generic body rather than a precise auth code.
app.UseStatusCodePages(async context =>
{
    var status = context.HttpContext.Response.StatusCode;
    if (status is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden)
    {
        return;
    }

    var response = context.HttpContext.Response;
    var problem = new ProblemDetails
    {
        Title = status == StatusCodes.Status404NotFound ? "Not Found" : "Error",
        Status = status,
    };
    problem.Extensions["code"] = status == StatusCodes.Status404NotFound
        ? ApiError.Codes.NotFound
        : ApiError.Codes.Unexpected;
    await response.WriteAsJsonAsync(problem);
});

app.MapAuthEndpoints();
app.MapAdminEndpoints();
app.MapAdminImportEndpoints();
app.MapLearnerEndpoints();
app.MapExamEndpoints();
app.MapPracticeEndpoints();
app.MapSavedQuestionEndpoints();
app.MapWeaknessEndpoints();
app.MapCatalogEndpoints();
app.MapCheckoutEndpoints();
app.MapWritingEndpoints();
app.MapSpeakingEndpoints();
app.MapInsightEndpoints();
app.MapPlacementEndpoints();
app.MapLearningPathEndpoints();
app.MapMentorEndpoints();
app.MapComplaintEndpoints();

app.Run();


// For WebApplicationFactory-based tests in later slices.
public partial class Program;
