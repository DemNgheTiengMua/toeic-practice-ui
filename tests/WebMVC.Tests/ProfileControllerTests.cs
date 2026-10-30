using System.Security.Claims;
using Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WebMVC.Controllers;
using WebMVC.Models;
using Xunit;

namespace WebMVC.Tests;

public sealed class ProfileControllerTests
{
    private static (ProfileController Controller, TrackingAuthenticationService AuthService, TestProfileApiClient Api)
        CreateController()
    {
        var services = new ServiceCollection();
        services.AddRouting();
        services.AddMvcCore();

        var claims = new List<Claim>
        {
            new(IAccessTokenFactory.ClaimUserId, "42"),
            new(IAccessTokenFactory.ClaimEmail, "learner@example.local"),
            new(IAccessTokenFactory.ClaimRole, Roles.Learner),
        };

        var identity = new ClaimsIdentity(claims, "Cookies");
        var principal = new ClaimsPrincipal(identity);
        var authProperties = new AuthenticationProperties();
        authProperties.StoreTokens([new AuthenticationToken { Name = "jwt", Value = "bearer-token-42" }]);

        var trackingAuth = new TrackingAuthenticationService(principal, authProperties);
        services.AddSingleton<IAuthenticationService>(trackingAuth);
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { User = principal, RequestServices = serviceProvider };
        var currentUser = new CurrentUser(new HttpContextAccessor { HttpContext = httpContext });

        var api = new TestProfileApiClient();
        var controller = new ProfileController(api, NullLogger<ProfileController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                RouteData = new Microsoft.AspNetCore.Routing.RouteData(),
            },
            TempData = new TempDataDictionary(httpContext, new EmptyTempDataProvider()),
        };

        return (controller, trackingAuth, api);
    }

    [Fact]
    public async Task Password_signs_out_of_cookie_and_redirects_on_success()
    {
        var (controller, authService, api) = CreateController();

        var model = new ChangePasswordViewModel
        {
            CurrentPassword = "current-password-123",
            NewPassword = "new-secure-password-456",
            ConfirmPassword = "new-secure-password-456",
        };

        var result = await controller.Password(model, CancellationToken.None);

        Assert.True(authService.SignedOut);
        Assert.Equal(CookieAuthenticationDefaults.AuthenticationScheme, authService.SignedOutScheme);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/auth/sign-in?reset=1", redirect.Url);
    }

    [Fact]
    public async Task Password_handles_locked_account_response_without_signing_out()
    {
        var (controller, authService, api) = CreateController();
        api.ChangePasswordException = new ApiException(ApiError.Codes.Locked, "Account locked", StatusCodes.Status401Unauthorized);

        var model = new ChangePasswordViewModel
        {
            CurrentPassword = "current-password-123",
            NewPassword = "new-secure-password-456",
            ConfirmPassword = "new-secure-password-456",
        };

        var result = await controller.Password(model, CancellationToken.None);

        Assert.False(authService.SignedOut);

        var view = Assert.IsType<ViewResult>(result);
        var returnedModel = Assert.IsType<PasswordPageViewModel>(view.Model).Form;
        Assert.Equal("Account locked", returnedModel.FormError);
    }

    [Fact]
    public async Task Password_handles_too_many_requests_rate_limiting_without_signing_out()
    {
        var (controller, authService, api) = CreateController();
        api.ChangePasswordException = new ApiException(ApiError.Codes.TooManyRequests, "Too many requests", StatusCodes.Status429TooManyRequests);

        var model = new ChangePasswordViewModel
        {
            CurrentPassword = "current-password-123",
            NewPassword = "new-secure-password-456",
            ConfirmPassword = "new-secure-password-456",
        };

        var result = await controller.Password(model, CancellationToken.None);

        Assert.False(authService.SignedOut);

        var view = Assert.IsType<ViewResult>(result);
        var returnedModel = Assert.IsType<PasswordPageViewModel>(view.Model).Form;
        Assert.Equal("Too many attempts. Please wait a moment before trying again.", returnedModel.FormError);
    }

    [Fact]
    public async Task Password_handles_invalid_credentials_without_signing_out()
    {
        var (controller, authService, api) = CreateController();
        api.ChangePasswordException = new ApiException(ApiError.Codes.InvalidCredentials, "The current password is incorrect.", StatusCodes.Status400BadRequest);

        var model = new ChangePasswordViewModel
        {
            CurrentPassword = "wrong-password",
            NewPassword = "new-secure-password-456",
            ConfirmPassword = "new-secure-password-456",
        };

        var result = await controller.Password(model, CancellationToken.None);

        Assert.False(authService.SignedOut);

        var view = Assert.IsType<ViewResult>(result);
        var returnedModel = Assert.IsType<PasswordPageViewModel>(view.Model).Form;
        Assert.Equal("The current password is incorrect.", returnedModel.FormError);
    }

    [Fact]
    public async Task Password_returns_view_when_model_state_invalid()
    {
        var (controller, authService, _) = CreateController();
        controller.ModelState.AddModelError(nameof(ChangePasswordViewModel.CurrentPassword), "Enter your current password.");

        var model = new ChangePasswordViewModel();
        var result = await controller.Password(model, CancellationToken.None);

        Assert.False(authService.SignedOut);
        var view = Assert.IsType<ViewResult>(result);
        Assert.Same(model, Assert.IsType<PasswordPageViewModel>(view.Model).Form);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Index_when_api_throws_renders_degraded_view_instead_of_crashing(bool unreachable)
    {
        var (controller, _, api) = CreateController();
        // An error status arrives as ApiException; a dropped connection as a bare HttpRequestException.
        api.GetProfileException = unreachable
            ? new HttpRequestException("No connection could be made because the target machine actively refused it.")
            : new ApiException("upstream_unavailable", "Service unavailable", StatusCodes.Status503ServiceUnavailable);

        var result = await controller.Index(CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProfilePageViewModel>(view.Model);
        Assert.NotNull(model.LoadError);
        // The form must stay hidden behind the error placard: an empty form saved back would wipe
        // the stored profile.
        Assert.Equal("", model.Form.FullName);
    }

    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class TrackingAuthenticationService(ClaimsPrincipal principal, AuthenticationProperties properties) : IAuthenticationService
    {
        public bool SignedOut { get; private set; }
        public string? SignedOutScheme { get; private set; }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, properties, "Cookies")));

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? props) => Task.CompletedTask;
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? props) => Task.CompletedTask;
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? props) => Task.CompletedTask;

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? props)
        {
            SignedOut = true;
            SignedOutScheme = scheme;
            return Task.CompletedTask;
        }
    }

    private sealed class TestProfileApiClient : IProfileClient
    {
        public ApiException? ChangePasswordException { get; set; }
        public HttpRequestException? GetProfileException { get; set; }
        public ChangePasswordRequest? LastChangePasswordRequest { get; private set; }

        public Task<ProfileDto> GetProfileAsync(CancellationToken cancellationToken = default) =>
            GetProfileException is not null
                ? Task.FromException<ProfileDto>(GetProfileException)
                : Task.FromResult<ProfileDto>(null!);

        public Task<ProfileDto> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProfileDto>(null!);

        public Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default)
        {
            LastChangePasswordRequest = request;
            if (ChangePasswordException is not null)
            {
                throw ChangePasswordException;
            }

            return Task.CompletedTask;
        }
    }
}
