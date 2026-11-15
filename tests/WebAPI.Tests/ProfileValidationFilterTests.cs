using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using BusinessObjects;
using Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Services;
using WebAPI.Endpoints;
using WebAPI.Extensions;
using Xunit;

namespace WebAPI.Tests;

public sealed class ProfileValidationFilterTests
{
    [Fact]
    public async Task Get_profile_returns_200_ok_with_profile_dto()
    {
        var stub = new StubAuthService();
        var (client, app) = await ProfileAppAsync(stub);
        await using (app)
        {
            var response = await client.GetAsync("/api/profile");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var dto = await response.Content.ReadFromJsonAsync<ProfileDto>();
            Assert.NotNull(dto);
            Assert.Equal("Nguyễn Văn A", dto.FullName);
            Assert.Equal("0901234567", dto.Phone);
            Assert.Equal("Asia/Ho_Chi_Minh", dto.TimeZoneId);
        }
    }

    [Theory]
    [InlineData("Demo Learner Meow 293819238")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("John<b>Doe</b>")]
    [InlineData("   ")]
    [InlineData("A")]
    [InlineData("John\nDoe")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // 71 chars
    public async Task Put_profile_rejects_invalid_fullName_with_400_and_field_error(string badName)
    {
        var stub = new StubAuthService();
        var (client, app) = await ProfileAppAsync(stub);
        await using (app)
        {
            var response = await client.PutAsJsonAsync("/api/profile", new UpdateProfileRequest(badName, "0901234567", "Asia/Ho_Chi_Minh"));
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(stub.UpdateCalled, "RequestValidationFilter should short-circuit before calling AuthService.");
            Assert.True(json.TryGetProperty("errors", out var errors));
            Assert.True(errors.TryGetProperty("fullName", out var fullNameErrors));
            Assert.True(fullNameErrors.GetArrayLength() > 0);
        }
    }

    [Theory]
    [InlineData("not-a-phone")]
    [InlineData("123")]
    [InlineData("0901234567890123456789")] // > 20 chars
    public async Task Put_profile_rejects_invalid_phone_with_400_and_field_error(string badPhone)
    {
        var stub = new StubAuthService();
        var (client, app) = await ProfileAppAsync(stub);
        await using (app)
        {
            var response = await client.PutAsJsonAsync("/api/profile", new UpdateProfileRequest("Nguyễn Văn A", badPhone, "Asia/Ho_Chi_Minh"));
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(stub.UpdateCalled);
            Assert.True(json.TryGetProperty("errors", out var errors));
            Assert.True(errors.TryGetProperty("phone", out var phoneErrors));
            Assert.True(phoneErrors.GetArrayLength() > 0);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("America/Sao_Paulo")]
    public async Task Put_profile_rejects_unknown_timezone_with_400_and_field_error(string badZone)
    {
        var stub = new StubAuthService();
        var (client, app) = await ProfileAppAsync(stub);
        await using (app)
        {
            var response = await client.PutAsJsonAsync("/api/profile", new UpdateProfileRequest("Nguyễn Văn A", "0901234567", badZone));
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(stub.UpdateCalled);
            Assert.True(json.TryGetProperty("errors", out var errors));
            Assert.True(errors.TryGetProperty("timeZoneId", out var tzErrors));
            Assert.True(tzErrors.GetArrayLength() > 0);
        }
    }

    [Theory]
    [InlineData("  Nguyễn   Thị  Minh  Khai  ", "Nguyễn Thị Minh Khai")]
    [InlineData("Jean-Luc O'Connor", "Jean-Luc O'Connor")]
    [InlineData("O'Connor-Smith", "O'Connor-Smith")]
    public async Task Put_profile_accepts_valid_unicode_and_international_names(string inputName, string expectedName)
    {
        var stub = new StubAuthService();
        var (client, app) = await ProfileAppAsync(stub);
        await using (app)
        {
            var response = await client.PutAsJsonAsync("/api/profile", new UpdateProfileRequest(inputName, "+84 912 000 000", "Asia/Ho_Chi_Minh"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(stub.UpdateCalled);
            var dto = await response.Content.ReadFromJsonAsync<ProfileDto>();
            Assert.NotNull(dto);
            Assert.Equal(expectedName, dto.FullName);
        }
    }

    private static async Task<(HttpClient Client, WebApplication App)> ProfileAppAsync(IAuthService authService)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, "http://127.0.0.1:0");
        builder.Services.AddSingleton(authService);
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, AllowLearner>("Test", null);
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseApiProblemDetails();
        app.UseAuthentication();
        app.UseAuthorization();

        typeof(AuthEndpoints)
            .GetMethod("MapProfile", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [app]);

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        return (new HttpClient { BaseAddress = new Uri(address) }, app);
    }

    private sealed class AllowLearner : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public AllowLearner(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [new Claim(IAccessTokenFactory.ClaimUserId, "42")],
                "Test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }

    private sealed class StubAuthService : IAuthService
    {
        public bool UpdateCalled { get; private set; }

        public Task<ProfileInfo> UpdateProfileAsync(int userId, UpdateProfileInput input, CancellationToken cancellationToken = default)
        {
            UpdateCalled = true;
            var normalized = ProfileValidation.NormalizeFullName(input.FullName);
            return Task.FromResult(new ProfileInfo(normalized, input.Phone?.Trim(), input.TimeZoneId));
        }

        public Task<AuthUser> RegisterAsync(RegisterInput input, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<AuthUser> SignInAsync(SignInInput input, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<AuthUser?> GetUserAsync(int userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SessionStatus> CheckSessionAsync(int userId, string? sessionStamp, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProfileInfo> GetProfileAsync(int userId, CancellationToken cancellationToken = default)
            => Task.FromResult(new ProfileInfo("Nguyễn Văn A", "0901234567", "Asia/Ho_Chi_Minh"));
        public Task ChangePasswordAsync(int userId, ChangePasswordInput input, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task VerifyEmailAsync(string token, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ResendActivationAsync(string email, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task RequestPasswordResetAsync(ForgotPasswordInput input, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ResetPasswordAsync(ResetPasswordInput input, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
