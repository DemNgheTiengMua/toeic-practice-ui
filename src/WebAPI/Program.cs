using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using DataAccess;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Services;
using WebAPI;

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
// Every failure — thrown exception or bare status-code result — leaves the API as RFC 9457
// ProblemDetails with a stable machine-readable "code" extension the MVC site switches on.
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["code"] = context.ProblemDetails.Status switch
        {
            StatusCodes.Status404NotFound => ApiError.Codes.NotFound,
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

var gatewayChoice = builder.Configuration["Payment:Gateway"]?.ToLowerInvariant() ?? "mock";
if (gatewayChoice == "payos")
{
    var payOsOptions = builder.Configuration.GetSection(PayOsOptions.SectionName).Get<PayOsOptions>() ?? new PayOsOptions();
    builder.Services.AddSingleton(payOsOptions);
    builder.Services.AddSingleton<IPaymentGateway, PayOsPaymentGateway>();
}
else
{
    var mock = new MockPaymentGateway();
    builder.Services.AddSingleton(mock);
    builder.Services.AddSingleton<IMockPaymentGateway>(mock);
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

// ---- Auth ----

app.MapPost("/api/auth/register", async Task<IResult> (
    RegisterRequest request,
    IAuthService auth,
    IAccessTokenFactory tokens,
    CancellationToken cancellationToken) =>
{
    AuthUser user;
    try
    {
        user = await auth.RegisterAsync(
            new RegisterInput(request.FullName, request.Email, request.Phone, request.Password, request.TermsAccepted),
            cancellationToken);
    }
    catch (ValidationException ex)
    {
        return ValidationProblem(ex);
    }
    catch (AuthException ex) when (ex.Code == AuthException.DuplicateEmail)
    {
        return Problem(StatusCodes.Status409Conflict, "Email already registered", ApiError.Codes.EmailTaken, ex.Message);
    }

    return ToTokenResponse(user, tokens);
});

app.MapPost("/api/auth/sign-in", async Task<IResult> (
    SignInRequest request,
    IAuthService auth,
    IAccessTokenFactory tokens,
    CancellationToken cancellationToken) =>
{
    AuthUser user;
    try
    {
        user = await auth.SignInAsync(new SignInInput(request.Email, request.Password), cancellationToken);
    }
    catch (ValidationException ex)
    {
        return ValidationProblem(ex);
    }
    catch (AuthException ex) when (ex.Code == AuthException.InvalidCredentials)
    {
        return Problem(StatusCodes.Status401Unauthorized, "Sign-in failed", ApiError.Codes.InvalidCredentials, ex.Message);
    }
    catch (AuthException ex) when (ex.Code == AuthException.Locked)
    {
        return Problem(StatusCodes.Status401Unauthorized, "Account locked", ApiError.Codes.Locked, ex.Message);
    }

    return ToTokenResponse(user, tokens);
});

// Returns the live database row, not the token's claims: the MVC site's cookie revalidation
// compares against this to catch server-side changes (role, name) since sign-in.
app.MapGet("/api/auth/me", async Task<IResult> (HttpContext http, IAuthService auth, CancellationToken cancellationToken) =>
{
    var user = await auth.GetUserAsync(CurrentUserId(http), cancellationToken);
    if (user is null)
    {
        return Problem(StatusCodes.Status404NotFound, "Not Found", ApiError.Codes.NotFound);
    }

    return Results.Ok(ToDto(user));
}).RequireAuthorization();

// ---- Profile (issue #6) ----

app.MapGet("/api/profile", async (HttpContext http, IAuthService auth, CancellationToken cancellationToken) =>
{
    var profile = await auth.GetProfileAsync(CurrentUserId(http), cancellationToken);
    return Results.Ok(new ProfileDto(profile.FullName, profile.Phone, profile.TimeZoneId));
}).RequireAuthorization();

app.MapPut("/api/profile", async Task<IResult> (
    UpdateProfileRequest request,
    HttpContext http,
    IAuthService auth,
    CancellationToken cancellationToken) =>
{
    try
    {
        var profile = await auth.UpdateProfileAsync(
            CurrentUserId(http), new UpdateProfileInput(request.FullName, request.Phone, request.TimeZoneId), cancellationToken);
        return Results.Ok(new ProfileDto(profile.FullName, profile.Phone, profile.TimeZoneId));
    }
    catch (ValidationException ex)
    {
        return ValidationProblem(ex);
    }
}).RequireAuthorization();

app.MapPost("/api/profile/password", async Task<IResult> (
    ChangePasswordRequest request,
    HttpContext http,
    IAuthService auth,
    CancellationToken cancellationToken) =>
{
    try
    {
        await auth.ChangePasswordAsync(
            CurrentUserId(http), new ChangePasswordInput(request.CurrentPassword, request.NewPassword), cancellationToken);
        return Results.NoContent();
    }
    catch (ValidationException ex)
    {
        return ValidationProblem(ex);
    }
    catch (AuthException ex) when (ex.Code == AuthException.InvalidCredentials)
    {
        return Problem(StatusCodes.Status400BadRequest, "Password change failed", ApiError.Codes.InvalidCredentials, ex.Message);
    }
}).RequireAuthorization();

// ---- Email verification and password reset (issue #5) ----
// Links in mail point at the MVC site; it posts the token here. Tokens are single-use and expire.

app.MapPost("/api/auth/verify-email", async Task<IResult> (
    VerifyEmailRequest request, IAuthService auth, CancellationToken cancellationToken) =>
{
    try
    {
        await auth.VerifyEmailAsync(request.Token, cancellationToken);
    }
    catch (AuthException ex) when (ex.Code == AuthException.TokenInvalid)
    {
        return Problem(StatusCodes.Status400BadRequest, "Link not valid", ApiError.Codes.TokenInvalid, ex.Message);
    }

    return Results.NoContent();
});

app.MapPost("/api/auth/resend-activation", async Task<IResult> (
    HttpContext http, IAuthService auth, CancellationToken cancellationToken) =>
{
    var userId = CurrentUserId(http);
    try
    {
        await auth.ResendActivationAsync(userId, cancellationToken);
    }
    catch (EmailDeliveryException)
    {
        // Safe to surface here: the caller is signed in and owns the account.
        return Problem(StatusCodes.Status503ServiceUnavailable, "Could not send the email", ApiError.Codes.MailUnavailable);
    }

    return Results.NoContent();
}).RequireAuthorization();

// Always 204 for a well-formed address, account or not, sent or not: the answer must not
// enumerate users. The service logs a failed send instead of raising it.
app.MapPost("/api/auth/forgot-password", async Task<IResult> (
    ForgotPasswordRequest request, IAuthService auth, CancellationToken cancellationToken) =>
{
    try
    {
        await auth.RequestPasswordResetAsync(new ForgotPasswordInput(request.Email), cancellationToken);
    }
    catch (ValidationException ex)
    {
        return Problem(StatusCodes.Status400BadRequest, "Validation failed", ApiError.Codes.Validation, ex.Message);
    }

    return Results.NoContent();
});

app.MapPost("/api/auth/reset-password", async Task<IResult> (
    ResetPasswordRequest request, IAuthService auth, CancellationToken cancellationToken) =>
{
    try
    {
        await auth.ResetPasswordAsync(new ResetPasswordInput(request.Token, request.NewPassword), cancellationToken);
    }
    catch (ValidationException ex)
    {
        return Problem(StatusCodes.Status400BadRequest, "Validation failed", ApiError.Codes.Validation, ex.Message);
    }
    catch (AuthException ex) when (ex.Code == AuthException.TokenInvalid)
    {
        return Problem(StatusCodes.Status400BadRequest, "Link not valid", ApiError.Codes.TokenInvalid, ex.Message);
    }

    return Results.NoContent();
});

// ---- Admin endpoints ----
var admin = app.MapGroup("/api/admin").RequireAuthorization(Roles.Admin);

admin.MapGet("/summary", (HttpContext http) =>
    Results.Ok(new { admin = http.User.FindFirst(IAccessTokenFactory.ClaimEmail)!.Value }));

admin.MapGet("/packages", async (IPackageService packages, CancellationToken cancellationToken) =>
{
    var summary = await packages.GetAdminPackagesAsync(cancellationToken);
    return Results.Ok(summary);
});

admin.MapGet("/packages/{packageId:int}", async (int packageId, IPackageService packages, CancellationToken cancellationToken) =>
{
    var pkg = await packages.GetPackageByIdAsync(packageId, cancellationToken);
    return pkg is not null ? Results.Ok(pkg) : Results.NotFound();
});

admin.MapPost("/packages", async (CreatePackageRequest request, IPackageService packages, CancellationToken cancellationToken) =>
{
    try
    {
        var created = await packages.CreatePackageAsync(request, cancellationToken);
        return Results.Created($"/api/admin/packages/{created.PackageId}", created);
    }
    catch (ArgumentException ex)
    {
        return Problem(StatusCodes.Status400BadRequest, "Validation error", ApiError.Codes.Validation, ex.Message);
    }
});

admin.MapPut("/packages/{packageId:int}", async (int packageId, UpdatePackageRequest request, IPackageService packages, CancellationToken cancellationToken) =>
{
    try
    {
        var updated = await packages.UpdatePackageAsync(packageId, request, cancellationToken);
        return Results.Ok(updated);
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound();
    }
    catch (ArgumentException ex)
    {
        return Problem(StatusCodes.Status400BadRequest, "Validation error", ApiError.Codes.Validation, ex.Message);
    }
});

admin.MapPost("/packages/{packageId:int}/deactivate", async (int packageId, IPackageService packages, CancellationToken cancellationToken) =>
{
    try
    {
        await packages.DeactivatePackageAsync(packageId, cancellationToken);
        return Results.NoContent();
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound();
    }
});

// ---- Learner (issue #7) ----
// The learner is always the token's own user: no endpoint takes a user id or a certificate to
// filter by, so one learner can't read another's data and no screen can mix certificates.

var learner = app.MapGroup("/api/learner").RequireAuthorization(Roles.Learner);

learner.MapGet("/context", async (HttpContext http, ILearnerService learners, CancellationToken cancellationToken) =>
{
    var context = await learners.GetContextAsync(CurrentUserId(http), cancellationToken);
    return Results.Ok(new LearnerContextDto(
        ToCertificateDto(context.Active),
        context.Certificates.Select(ToCertificateDto).ToList(),
        context.CreditBalance));
});

learner.MapPut("/active-certificate", async Task<IResult> (
    SwitchCertificateRequest request,
    HttpContext http,
    ILearnerService learners,
    CancellationToken cancellationToken) =>
{
    try
    {
        await learners.SwitchCertificateAsync(CurrentUserId(http), request.Code, cancellationToken);
    }
    catch (ValidationException ex)
    {
        return ValidationProblem(ex);
    }
    catch (CertificateUnavailableException ex)
    {
        return Problem(StatusCodes.Status404NotFound, "Certificate unavailable", ApiError.Codes.CertificateUnavailable, ex.Message);
    }

    return Results.NoContent();
});

learner.MapGet("/dashboard", async (HttpContext http, ILearnerService learners, CancellationToken cancellationToken) =>
{
    var dashboard = await learners.GetDashboardAsync(CurrentUserId(http), cancellationToken);
    return Results.Ok(new LearnerDashboardDto(
        ToCertificateDto(dashboard.Certificate),
        dashboard.LatestResult is { } latest
            ? new LatestResultDto(latest.TotalScore, latest.BandCode, latest.BandName, latest.TakenAt)
            : null,
        dashboard.IsNew,
        dashboard.AttemptCount,
        dashboard.PracticeCount,
        dashboard.RecentAttempts.Select(a => new RecentAttemptDto(
            a.AttemptId, a.ExamName, a.Purpose, a.Status, a.StartedAt, a.TotalScore, a.BandCode)).ToList(),
        dashboard.RecentPractice.Select(p => new RecentPracticeDto(
            p.SessionId, p.SectionName, p.Status, p.CorrectCount, p.QuestionCount, p.StartedAt)).ToList(),
        dashboard.UnfinishedAttempt is { } ua
            ? new RecentAttemptDto(ua.AttemptId, ua.ExamName, ua.Purpose, ua.Status, ua.StartedAt, ua.TotalScore, ua.BandCode)
            : null,
        dashboard.UnfinishedPractice is { } up
            ? new RecentPracticeDto(up.SessionId, up.SectionName, up.Status, up.CorrectCount, up.QuestionCount, up.StartedAt)
            : null));
});

// ---- Mock Exams (issue #15) ----
learner.MapGet("/exams", async (
    HttpContext http,
    IMockExamService mockExamService,
    CancellationToken cancellationToken) =>
{
    var view = await mockExamService.GetMockExamsAsync(CurrentUserId(http), cancellationToken);
    return Results.Ok(new MockExamListViewDto(
        ToCertificateDto(view.Certificate),
        view.CreditBalance,
        view.Exams.Select(e => new MockExamItemDto(
            e.ExamId,
            e.CertId,
            e.CertCode,
            e.Name,
            e.Description,
            e.DurationMinutes,
            e.QuestionCount,
            e.SourceName,
            e.CreditCost,
            e.ActiveAttemptId,
            e.ActiveAttemptExpiresAt,
            e.CompletedAttemptScore,
            e.CompletedAttemptBand)).ToList()));
});

learner.MapGet("/exams/{examId:int}/instructions", async Task<IResult> (
    int examId,
    HttpContext http,
    IMockExamService mockExamService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var view = await mockExamService.GetExamInstructionsAsync(CurrentUserId(http), examId, cancellationToken);
        return Results.Ok(new MockExamInstructionsDto(
            view.ExamId,
            view.CertId,
            view.CertCode,
            view.ExamName,
            view.Description,
            view.DurationMinutes,
            view.QuestionCount,
            view.CreditCost,
            view.CreditBalance,
            view.ActiveAttemptId,
            view.Sections.Select(s => new MockExamSectionInfoDto(
                s.SectionId,
                s.SkillCode,
                s.SkillName,
                s.Modality,
                s.SectionCode,
                s.SectionName,
                s.ExpectedQuestionCount,
                s.DurationMinutes,
                s.DisplayOrder)).ToList()));
    }
    catch (MockExamException ex) when (ex.Code == MockExamException.ExamNotFound)
    {
        return Problem(StatusCodes.Status404NotFound, "Exam not found", ApiError.Codes.ExamNotFound, ex.Message);
    }
});

learner.MapPost("/exams/{examId:int}/start", async Task<IResult> (
    int examId,
    HttpContext http,
    IMockExamService mockExamService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var attempt = await mockExamService.StartAttemptAsync(CurrentUserId(http), examId, cancellationToken);
        return Results.Ok(new StartMockExamResponse(
            attempt.AttemptId,
            attempt.ExamId,
            attempt.Status,
            attempt.StartedAt,
            attempt.ExpiresAt,
            attempt.RemainingSeconds,
            attempt.IsAutoSubmitted));
    }
    catch (MockExamException ex) when (ex.Code == MockExamException.InsufficientCredits)
    {
        return Problem(StatusCodes.Status402PaymentRequired, "Insufficient credits", ApiError.Codes.InsufficientCredits, ex.Message);
    }
    catch (MockExamException ex) when (ex.Code == MockExamException.ExamNotFound)
    {
        return Problem(StatusCodes.Status404NotFound, "Exam not found", ApiError.Codes.ExamNotFound, ex.Message);
    }
});

learner.MapGet("/attempts/{attemptId:int}", async Task<IResult> (
    int attemptId,
    [FromQuery] int? q,
    HttpContext http,
    IMockExamService mockExamService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var take = await mockExamService.GetAttemptTakeAsync(CurrentUserId(http), attemptId, q, cancellationToken);
        var qInfo = take.CurrentQuestion;
        var currentQDto = new AttemptQuestionDto(
            qInfo.QuestionId,
            qInfo.DisplayOrder,
            qInfo.TotalQuestions,
            qInfo.SectionId,
            qInfo.SectionCode,
            qInfo.SectionName,
            qInfo.SkillCode,
            qInfo.SkillName,
            qInfo.Modality,
            qInfo.Stem,
            qInfo.ImagePath,
            qInfo.ImageAlt,
            qInfo.AudioPath,
            qInfo.Transcript,
            qInfo.Passage,
            qInfo.Options.Select(o => new AttemptOptionDto(o.OptionId, o.Label, o.Text)).ToList(),
            qInfo.SelectedOptionId,
            qInfo.IsFlagged);

        return Results.Ok(new AttemptTakeDto(
            take.AttemptId,
            take.ExamId,
            take.ExamName,
            take.CertCode,
            take.Status,
            take.StartedAt,
            take.ExpiresAt,
            take.RemainingSeconds,
            take.TotalQuestions,
            take.AnsweredCount,
            take.FlaggedCount,
            currentQDto,
            take.Navigator.Select(n => new NavigatorItemDto(
                n.QuestionId, n.DisplayOrder, n.SectionId, n.SectionCode, n.Modality, n.IsAnswered, n.IsFlagged, n.IsCurrent)).ToList(),
            take.IsAutoSubmitted));
    }
    catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
    {
        return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
    }
    catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotActive)
    {
        return Problem(StatusCodes.Status400BadRequest, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
    }
});

learner.MapPost("/attempts/{attemptId:int}/answers/{questionId:int}", async Task<IResult> (
    int attemptId,
    int questionId,
    SaveAnswerRequest request,
    HttpContext http,
    IMockExamService mockExamService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var res = await mockExamService.SaveAnswerAsync(
            CurrentUserId(http), attemptId, questionId, request.SelectedOptionId, request.IsFlagged, cancellationToken);
        return Results.Ok(new SaveAnswerResultDto(
            res.QuestionId, res.SelectedOptionId, res.IsFlagged, res.RemainingSeconds, res.IsAutoSubmitted));
    }
    catch (MockExamException ex) when (ex.Code == MockExamException.AttemptExpired)
    {
        return Problem(StatusCodes.Status400BadRequest, "Exam deadline expired", ApiError.Codes.AttemptExpired, ex.Message);
    }
    catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
    {
        return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
    }
    catch (MockExamException ex) when (ex.Code == MockExamException.QuestionNotFound)
    {
        return Problem(StatusCodes.Status404NotFound, "Question not found", ApiError.Codes.QuestionNotFound, ex.Message);
    }
    catch (MockExamException ex) when (ex.Code == MockExamException.InvalidOption)
    {
        return Problem(StatusCodes.Status400BadRequest, "Invalid option", ApiError.Codes.PracticeInvalidOption, ex.Message);
    }
    catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotActive)
    {
        return Problem(StatusCodes.Status400BadRequest, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
    }
});

learner.MapPost("/attempts/{attemptId:int}/submit", async Task<IResult> (
    int attemptId,
    HttpContext http,
    IMockExamService mockExamService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var res = await mockExamService.SubmitAttemptAsync(CurrentUserId(http), attemptId, isAutoSubmit: false, cancellationToken);
        return Results.Ok(new SubmitAttemptResponse(
            res.AttemptId, res.Status, res.TotalScore, res.BandCode, res.BandName, res.SubmittedAt, res.IsAutoSubmitted));
    }
    catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
    {
        return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
    }
    catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotActive)
    {
        return Problem(StatusCodes.Status400BadRequest, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
    }
});

learner.MapPost("/attempts/{attemptId:int}/cancel", async Task<IResult> (
    int attemptId,
    HttpContext http,
    IMockExamService mockExamService,
    CancellationToken cancellationToken) =>
{
    try
    {
        await mockExamService.CancelAttemptAsync(CurrentUserId(http), attemptId, cancellationToken);
        return Results.NoContent();
    }
    catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
    {
        return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
    }
    catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotActive)
    {
        return Problem(StatusCodes.Status400BadRequest, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
    }
});

learner.MapGet("/attempts/{attemptId:int}/result", async Task<IResult> (
    int attemptId,
    HttpContext http,
    IMockExamService mockExamService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var r = await mockExamService.GetAttemptResultAsync(CurrentUserId(http), attemptId, cancellationToken);
        return Results.Ok(new AttemptResultDto(
            r.AttemptId,
            r.ExamId,
            r.ExamName,
            r.CertId,
            r.CertCode,
            r.CertName,
            r.Status,
            r.StartedAt,
            r.SubmittedAt,
            r.DurationSeconds,
            r.IsAutoSubmitted,
            r.TotalScore,
            r.BandCode,
            r.BandName,
            r.SkillScores.Select(s => new AttemptSkillScoreDto(
                s.SkillId, s.SkillCode, s.SkillName, s.Modality, s.RawScore, s.ScaledScore, s.TotalQuestions)).ToList(),
            r.PreviousAttemptScore,
            r.ScoreDelta));
    }
    catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
    {
        return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
    }
    catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotActive)
    {
        return Problem(StatusCodes.Status400BadRequest, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
    }
});

// ---- Practice (issue #8) ----
var practice = learner.MapGroup("/practice");

practice.MapGet("/catalog", async (HttpContext http, IPracticeService practiceService, CancellationToken cancellationToken) =>
{
    var view = await practiceService.GetPracticeSectionsAsync(CurrentUserId(http), cancellationToken);
    static PracticeSectionDto ToSectionDto(PracticeSectionItem s) => new(
        s.SectionId, s.SkillCode, s.SkillName, s.Modality, s.Code, s.Name, s.OptionCount,
        s.ExpectedQuestionCount, s.AvailableQuestionCount, s.AccuracyPercentage, s.AnsweredCount, s.DisplayOrder);

    return Results.Ok(new PracticeCatalogDto(
        ToCertificateDto(view.Certificate),
        view.ListeningSections.Select(ToSectionDto).ToList(),
        view.ReadingSections.Select(ToSectionDto).ToList(),
        view.OtherSections.Select(ToSectionDto).ToList(),
        view.WeakestSection is { } w ? ToSectionDto(w) : null,
        view.UnfinishedSession is { } ufs
            ? new PracticeSessionDto(
                ufs.SessionId, ufs.SectionId, ufs.SectionCode, ufs.SectionName, ufs.Modality,
                ufs.QuestionCount, ufs.Status, ufs.CorrectCount, ufs.QuestionSource, ufs.FeedbackMode,
                ufs.StartedAt, ufs.FinishedAt)
            : null));
});

practice.MapPost("/sessions", async Task<IResult> (
    StartPracticeRequest request,
    HttpContext http,
    IPracticeService practiceService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var input = new StartPracticeSessionInput(
            request.SectionId,
            request.QuestionCount,
            request.QuestionSource ?? "all",
            request.FeedbackMode ?? "immediate");

        var session = await practiceService.StartSessionAsync(CurrentUserId(http), input, cancellationToken);
        return Results.Ok(new PracticeSessionDto(
            session.SessionId,
            session.SectionId,
            session.SectionCode,
            session.SectionName,
            session.Modality,
            session.QuestionCount,
            session.Status,
            session.CorrectCount,
            session.QuestionSource,
            session.FeedbackMode,
            session.StartedAt,
            session.FinishedAt));
    }
    catch (ValidationException ex)
    {
        return ValidationProblem(ex);
    }
    catch (PracticeException ex) when (ex.Code == PracticeException.ModalityNotAllowed)
    {
        return Problem(StatusCodes.Status400BadRequest, "Modality not allowed for free practice", ApiError.Codes.PracticeModalityNotAllowed, ex.Message);
    }
    catch (PracticeException ex) when (ex.Code == PracticeException.SectionUnavailable)
    {
        return Problem(StatusCodes.Status404NotFound, "Section unavailable", ApiError.Codes.PracticeSectionUnavailable, ex.Message);
    }
    catch (PracticeException ex) when (ex.Code == PracticeException.NoQuestionsAvailable)
    {
        return Problem(StatusCodes.Status404NotFound, "No questions available", ApiError.Codes.PracticeNoQuestionsAvailable, ex.Message);
    }
});

practice.MapGet("/sessions/{sessionId:int}", async Task<IResult> (
    int sessionId,
    HttpContext http,
    IPracticeService practiceService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var session = await practiceService.GetSessionInfoAsync(CurrentUserId(http), sessionId, cancellationToken);
        return Results.Ok(new PracticeSessionDto(
            session.SessionId,
            session.SectionId,
            session.SectionCode,
            session.SectionName,
            session.Modality,
            session.QuestionCount,
            session.Status,
            session.CorrectCount,
            session.QuestionSource,
            session.FeedbackMode,
            session.StartedAt,
            session.FinishedAt));
    }
    catch (PracticeException ex) when (ex.Code == PracticeException.SessionNotFound)
    {
        return Problem(StatusCodes.Status404NotFound, "Session not found", ApiError.Codes.PracticeSessionNotFound, ex.Message);
    }
});

practice.MapGet("/sessions/{sessionId:int}/questions/{displayOrder:int}", async Task<IResult> (
    int sessionId,
    int displayOrder,
    HttpContext http,
    IPracticeService practiceService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var q = await practiceService.GetQuestionAsync(CurrentUserId(http), sessionId, displayOrder, cancellationToken);
        return Results.Ok(new PracticeQuestionDto(
            q.SessionId,
            q.SectionName,
            q.QuestionId,
            q.DisplayOrder,
            q.TotalQuestions,
            q.Stem,
            q.CategoryName,
            q.Hint,
            q.HintUsed,
            q.Options.Select(o => new PracticeOptionDto(o.OptionId, o.Label, o.Text)).ToList(),
            q.SelectedOptionId,
            q.IsCorrect,
            q.CorrectOptionId,
            q.Explanation,
            q.Transcript,
            q.Passage,
            q.AudioPath,
            q.ImagePath,
            q.ImageAlt,
            q.IsSaved));
    }
    catch (PracticeException ex) when (ex.Code == PracticeException.SessionNotFound)
    {
        return Problem(StatusCodes.Status404NotFound, "Session not found", ApiError.Codes.PracticeSessionNotFound, ex.Message);
    }
    catch (PracticeException ex) when (ex.Code == PracticeException.QuestionNotFound)
    {
        return Problem(StatusCodes.Status404NotFound, "Question not found", ApiError.Codes.PracticeQuestionNotFound, ex.Message);
    }
});

practice.MapPost("/sessions/{sessionId:int}/questions/{questionId:int}/hint", async Task<IResult> (
    int sessionId,
    int questionId,
    HttpContext http,
    IPracticeService practiceService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var hint = await practiceService.RevealHintAsync(CurrentUserId(http), sessionId, questionId, cancellationToken);
        return Results.Ok(new RevealHintResponse(hint, true));
    }
    catch (PracticeException ex) when (ex.Code == PracticeException.SessionNotFound)
    {
        return Problem(StatusCodes.Status404NotFound, "Session not found", ApiError.Codes.PracticeSessionNotFound, ex.Message);
    }
    catch (PracticeException ex) when (ex.Code == PracticeException.QuestionNotFound)
    {
        return Problem(StatusCodes.Status404NotFound, "Question not found", ApiError.Codes.PracticeQuestionNotFound, ex.Message);
    }
    catch (PracticeException ex) when (ex.Code == PracticeException.SessionAlreadyFinished)
    {
        return Problem(StatusCodes.Status400BadRequest, "Session already finished", ApiError.Codes.PracticeSessionAlreadyFinished, ex.Message);
    }
});

practice.MapPost("/sessions/{sessionId:int}/questions/{questionId:int}/answer", async Task<IResult> (
    int sessionId,
    int questionId,
    SubmitPracticeAnswerRequest request,
    HttpContext http,
    IPracticeService practiceService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await practiceService.SubmitAnswerAsync(
            CurrentUserId(http), sessionId, questionId, new SubmitPracticeAnswerInput(request.SelectedOptionId), cancellationToken);
        return Results.Ok(new GradedAnswerDto(
            result.IsCorrect,
            result.SelectedOptionId,
            result.CorrectOptionId,
            result.Explanation,
            result.Transcript,
            result.IsSessionFinished,
            result.NextQuestionDisplayOrder,
            result.IsSaved));
    }
    catch (PracticeException ex) when (ex.Code == PracticeException.SessionNotFound)
    {
        return Problem(StatusCodes.Status404NotFound, "Session not found", ApiError.Codes.PracticeSessionNotFound, ex.Message);
    }
    catch (PracticeException ex) when (ex.Code == PracticeException.QuestionNotFound)
    {
        return Problem(StatusCodes.Status404NotFound, "Question not found", ApiError.Codes.PracticeQuestionNotFound, ex.Message);
    }
    catch (PracticeException ex) when (ex.Code == PracticeException.InvalidOption)
    {
        return Problem(StatusCodes.Status400BadRequest, "Invalid option", ApiError.Codes.PracticeInvalidOption, ex.Message);
    }
    catch (PracticeException ex) when (ex.Code == PracticeException.SessionAlreadyFinished)
    {
        return Problem(StatusCodes.Status400BadRequest, "Session already finished", ApiError.Codes.PracticeSessionAlreadyFinished, ex.Message);
    }
});

practice.MapGet("/sessions/{sessionId:int}/summary", async Task<IResult> (
    int sessionId,
    HttpContext http,
    IPracticeService practiceService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var s = await practiceService.GetSummaryAsync(CurrentUserId(http), sessionId, cancellationToken);
        return Results.Ok(new PracticeSummaryDto(
            s.SessionId,
            s.SectionCode,
            s.SectionName,
            s.Modality,
            s.StartedAt,
            s.FinishedAt,
            s.TotalQuestions,
            s.AnsweredCount,
            s.CorrectCount,
            s.IncorrectCount,
            s.SkippedCount,
            s.AccuracyPercentage,
            s.HintUsedCount,
            s.CorrectWithHintCount,
            s.IncorrectWithHintCount,
            s.Questions.Select(q => new QuestionOutcomeDto(
                q.DisplayOrder,
                q.QuestionId,
                q.CategoryName,
                q.Stem,
                q.SelectedOptionLabel,
                q.SelectedOptionText,
                q.CorrectOptionLabel,
                q.CorrectOptionText,
                q.IsCorrect,
                q.HintUsed,
                q.Explanation)).ToList()));
    }
    catch (PracticeException ex) when (ex.Code == PracticeException.SessionNotFound)
    {
        return Problem(StatusCodes.Status404NotFound, "Session not found", ApiError.Codes.PracticeSessionNotFound, ex.Message);
    }
});

// ---- Saved Questions (issue #9) ----
var savedQuestions = learner.MapGroup("/saved-questions");

savedQuestions.MapGet("", async (
    [FromQuery] string? source,
    [FromQuery] int? sectionId,
    [FromQuery] int? categoryId,
    HttpContext http,
    ISavedQuestionService savedService,
    CancellationToken cancellationToken) =>
{
    var filter = new SavedQuestionFilterInput(source, sectionId, categoryId);
    var view = await savedService.GetSavedQuestionsAsync(CurrentUserId(http), filter, cancellationToken);
    return Results.Ok(new SavedQuestionsViewDto(
        ToCertificateDto(view.Certificate),
        view.TotalSavedCount,
        view.ManualSavedCount,
        view.IncorrectSavedCount,
        view.Questions.Select(ToSavedQuestionDto).ToList(),
        view.Sections.Select(s => new SavedQuestionSectionFilterDto(s.SectionId, s.Code, s.Name, s.Count)).ToList(),
        view.Categories.Select(c => new SavedQuestionCategoryFilterDto(c.CategoryId, c.Name, c.Count)).ToList()));
});

savedQuestions.MapGet("/{questionId:int}", async Task<IResult> (
    int questionId,
    HttpContext http,
    ISavedQuestionService savedService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var q = await savedService.GetSavedQuestionAsync(CurrentUserId(http), questionId, cancellationToken);
        return Results.Ok(ToSavedQuestionDto(q));
    }
    catch (SavedQuestionException ex) when (ex.Code == SavedQuestionException.SavedQuestionNotFound)
    {
        return Problem(StatusCodes.Status404NotFound, "Saved question not found", ApiError.Codes.SavedQuestionNotFound, ex.Message);
    }
});

savedQuestions.MapPost("/{questionId:int}", async Task<IResult> (
    int questionId,
    SaveQuestionRequest? request,
    HttpContext http,
    ISavedQuestionService savedService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var source = request?.Source ?? "manual";
        var res = await savedService.SaveQuestionAsync(CurrentUserId(http), questionId, source, cancellationToken);
        return Results.Ok(new SaveQuestionResultDto(res.IsSaved, res.Source, res.SavedAt));
    }
    catch (SavedQuestionException ex) when (ex.Code == SavedQuestionException.QuestionNotFound)
    {
        return Problem(StatusCodes.Status404NotFound, "Question not found", ApiError.Codes.QuestionNotFound, ex.Message);
    }
});

savedQuestions.MapDelete("/{questionId:int}", async (
    int questionId,
    HttpContext http,
    ISavedQuestionService savedService,
    CancellationToken cancellationToken) =>
{
    var res = await savedService.UnsaveQuestionAsync(CurrentUserId(http), questionId, cancellationToken);
    return Results.Ok(new SaveQuestionResultDto(res.IsSaved, res.Source, res.SavedAt));
});

var weakness = app.MapGroup("/api/learner")
    .RequireAuthorization(policy => policy.RequireRole(Roles.Learner));

weakness.MapGet("/weakness-analysis", async (
    HttpContext http,
    IWeaknessAnalysisService weaknessService,
    CancellationToken cancellationToken) =>
{
    var view = await weaknessService.GetWeaknessAnalysisAsync(CurrentUserId(http), cancellationToken);
    var dto = new WeaknessAnalysisDto(
        ToCertificateDto(view.Certificate),
        view.HasData,
        view.GradedExamCount,
        view.CompletedPracticeCount,
        view.TotalAnswered,
        view.TotalCorrect,
        view.OverallAccuracyPercentage,
        view.Sections.Select(s => new SectionAccuracyDto(
            s.SectionId, s.Code, s.Name, s.Answered, s.Correct, s.AccuracyPercentage, s.IsWeakest, s.IsStrongest)).ToList(),
        view.FrequentlyMissedCategories.Select(c => new CategoryAccuracyDto(
            c.CategoryId, c.Code, c.Name, c.Answered, c.Correct, c.AccuracyPercentage)).ToList(),
        view.Recommendations.Select(r => new RecommendationItemDto(
            r.RecId, r.Text, r.Severity, r.TargetBandCode, r.SkillName, r.CreatedAt, r.IsResolved)).ToList(),
        view.PriorityPracticeSection is null ? null : new SectionAccuracyDto(
            view.PriorityPracticeSection.SectionId,
            view.PriorityPracticeSection.Code,
            view.PriorityPracticeSection.Name,
            view.PriorityPracticeSection.Answered,
            view.PriorityPracticeSection.Correct,
            view.PriorityPracticeSection.AccuracyPercentage,
            view.PriorityPracticeSection.IsWeakest,
            view.PriorityPracticeSection.IsStrongest));
    return Results.Ok(dto);
});

weakness.MapPost("/recommendations/{recId:int}/resolve", async (
    int recId,
    HttpContext http,
    IWeaknessAnalysisService weaknessService,
    CancellationToken cancellationToken) =>
{
    try
    {
        await weaknessService.ResolveRecommendationAsync(CurrentUserId(http), recId, cancellationToken);
        return Results.Ok(new ResolveRecommendationResultDto(recId, true));
    }
    catch (RecommendationNotFoundException ex)
    {
        return Problem(StatusCodes.Status404NotFound, "Recommendation not found", ApiError.Codes.RecommendationNotFound, ex.Message);
    }
});

static CertificateOptionDto ToCertificateDto(CertificateOption option) => new(option.Code, option.Name);

static SavedQuestionItemDto ToSavedQuestionDto(SavedQuestionItem q) => new(
    q.QuestionId,
    q.Source,
    q.SavedAt,
    q.SectionId,
    q.SectionCode,
    q.SectionName,
    q.CategoryId,
    q.CategoryName,
    q.Stem,
    q.Passage,
    q.ImagePath,
    q.AudioPath,
    q.Hint,
    q.Explanation,
    q.Transcript,
    q.Options.Select(o => new SavedQuestionOptionDto(o.OptionId, o.Label, o.Text, o.IsCorrect)).ToList());

static int CurrentUserId(HttpContext http) =>
    int.Parse(http.User.FindFirst(IAccessTokenFactory.ClaimUserId)!.Value);

static IResult ValidationProblem(ValidationException ex) =>
    Problem(StatusCodes.Status400BadRequest, "Validation failed", ApiError.Codes.Validation, ex.Message);

static IResult ToTokenResponse(AuthUser user, IAccessTokenFactory tokens)
{
    var token = tokens.Create(user.UserId, user.Email, user.Role, user.FullName, user.SessionStamp);
    return Results.Ok(new AuthTokenResponse(token.Token, token.ExpiresAt, ToDto(user)));
}

static AuthUserDto ToDto(AuthUser user) =>
    new(user.UserId, user.Email, user.FullName, user.Role, user.EmailVerified);

static IResult Problem(int status, string title, string code, string? detail = null)
{
    var problem = new ProblemDetails { Title = title, Status = status, Detail = detail };
    problem.Extensions["code"] = code;
    return Results.Json(problem, statusCode: status);
}

app.MapGet("/api/certificates", async (ICertificateService certificates, CancellationToken cancellationToken) =>
{
    var catalog = await certificates.GetCatalogAsync(cancellationToken);
    var dtos = catalog.Select(c => new CertificateDto(
        c.Code,
        c.Name,
        c.Description,
        c.Skills.Select(s => new SkillDto(
            s.Code,
            s.Name,
            s.Modality,
            s.DurationMinutes,
            s.Sections.Select(sec => new SectionDto(sec.Code, sec.Name, sec.ExpectedQuestionCount)).ToList())).ToList(),
        c.Bands.Select(b => new BandDto(b.Code, b.Name, b.MinTotal, b.MaxTotal)).ToList()));
    return Results.Ok(dtos);
});

app.MapGet("/api/packages", async (IPackageService packages, CancellationToken cancellationToken) =>
{
    var list = await packages.GetActivePackagesAsync(cancellationToken);
    return Results.Ok(list);
});

app.MapGet("/api/checkout/preview", async (
    int packageId,
    HttpContext http,
    IPaymentService payments,
    CancellationToken cancellationToken) =>
{
    try
    {
        var preview = await payments.GetCheckoutPreviewAsync(CurrentUserId(http), packageId, cancellationToken);
        return Results.Ok(preview);
    }
    catch (EmailNotVerifiedException ex)
    {
        return Problem(StatusCodes.Status403Forbidden, "Email verification required", ApiError.Codes.EmailNotVerified, ex.Message);
    }
    catch (PackageNotFoundException ex)
    {
        return Problem(StatusCodes.Status404NotFound, "Package not found", ApiError.Codes.PackageNotFound, ex.Message);
    }
}).RequireAuthorization(Roles.Learner);

app.MapPost("/api/checkout/orders", async (
    CreateOrderRequest request,
    HttpContext http,
    IPaymentService payments,
    AuthLinkOptions authLinks,
    CancellationToken cancellationToken) =>
{
    try
    {
        var baseUrl = !string.IsNullOrWhiteSpace(authLinks.PublicBaseUrl)
            ? authLinks.PublicBaseUrl
            : $"{http.Request.Scheme}://{http.Request.Host}";
        var response = await payments.CreateOrderAsync(CurrentUserId(http), request.PackageId, baseUrl, cancellationToken);
        return Results.Ok(response);
    }
    catch (EmailNotVerifiedException ex)
    {
        return Problem(StatusCodes.Status403Forbidden, "Email verification required", ApiError.Codes.EmailNotVerified, ex.Message);
    }
    catch (OrderAlreadyOpenException ex)
    {
        return Problem(StatusCodes.Status409Conflict, "Order already open", ApiError.Codes.OrderAlreadyOpen, ex.Message);
    }
    catch (PackageNotFoundException ex)
    {
        return Problem(StatusCodes.Status404NotFound, "Package not found", ApiError.Codes.PackageNotFound, ex.Message);
    }
}).RequireAuthorization(Roles.Learner);

app.MapPost("/api/checkout/return", async (
    HandleReturnRequest request,
    HttpContext http,
    IPaymentService payments,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await payments.HandleReturnRedirectAsync(CurrentUserId(http), request.OrderCode, cancellationToken);
        return Results.Ok(result);
    }
    catch (OrderNotFoundException ex)
    {
        return Problem(StatusCodes.Status404NotFound, "Order not found", ApiError.Codes.OrderNotFound, ex.Message);
    }
}).RequireAuthorization(Roles.Learner);

app.MapGet("/api/checkout/orders/{orderCode}/result", async (
    string orderCode,
    HttpContext http,
    IPaymentService payments,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await payments.GetPaymentResultAsync(CurrentUserId(http), orderCode, cancellationToken);
        return Results.Ok(result);
    }
    catch (OrderNotFoundException ex)
    {
        return Problem(StatusCodes.Status404NotFound, "Order not found", ApiError.Codes.OrderNotFound, ex.Message);
    }
}).RequireAuthorization(Roles.Learner);

app.MapPost("/api/mock-gateway/action", async (
    MockGatewayActionRequest request,
    HttpContext http,
    IPaymentService payments,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await payments.ApplyMockGatewayActionAsync(
            CurrentUserId(http),
            request.OrderCode,
            request.Action,
            request.ErrorCode,
            cancellationToken);
        return Results.Ok(result);
    }
    catch (OrderNotFoundException ex)
    {
        return Problem(StatusCodes.Status404NotFound, "Order not found", ApiError.Codes.OrderNotFound, ex.Message);
    }
}).RequireAuthorization(Roles.Learner);

app.MapPost("/api/payments/webhook", async (
    HttpContext http,
    IPaymentService payments,
    CancellationToken cancellationToken) =>
{
    using var reader = new StreamReader(http.Request.Body);
    var body = await reader.ReadToEndAsync(cancellationToken);
    var signature = http.Request.Headers["x-signature"].FirstOrDefault()
        ?? http.Request.Headers["signature"].FirstOrDefault();

    var result = await payments.HandleWebhookAsync(body, signature, cancellationToken);
    if (!result.IsValid)
    {
        return Results.BadRequest(new { error = "Invalid signature" });
    }
    return Results.Ok(new { message = "Webhook processed" });
}).AllowAnonymous();

app.MapGet("/api/learner/wallet", async (
    HttpContext http,
    IPaymentService payments,
    CancellationToken cancellationToken) =>
{
    var wallet = await payments.GetWalletAsync(CurrentUserId(http), cancellationToken);
    return Results.Ok(wallet);
}).RequireAuthorization(Roles.Learner);

app.Run();

// For WebApplicationFactory-based tests in later slices.
public partial class Program;
