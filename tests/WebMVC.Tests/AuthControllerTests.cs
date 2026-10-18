using Contracts;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Controllers;
using WebMVC.Models;
using Xunit;

namespace WebMVC.Tests;

public sealed class AuthControllerTests
{
    private static AuthController CreateController()
    {
        return new AuthController(new DummyAuthClient());
    }

    [Fact]
    public void SignIn_WithoutReset_NoticeIsNull()
    {
        var controller = CreateController();
        var result = Assert.IsType<ViewResult>(controller.SignIn());
        var model = Assert.IsType<SignInViewModel>(result.Model);

        Assert.Null(model.Notice);
        Assert.Null(model.FormError);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    public void SignIn_WithResetConfirmation_SetsNoticeBanner(string resetParam)
    {
        var controller = CreateController();
        var result = Assert.IsType<ViewResult>(controller.SignIn(reset: resetParam));
        var model = Assert.IsType<SignInViewModel>(result.Model);

        Assert.Equal("Your password has been reset successfully. Please sign in with your new password.", model.Notice);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("random")]
    public void SignIn_WithNonResetParam_NoticeIsNull(string resetParam)
    {
        var controller = CreateController();
        var result = Assert.IsType<ViewResult>(controller.SignIn(reset: resetParam));
        var model = Assert.IsType<SignInViewModel>(result.Model);

        Assert.Null(model.Notice);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    public void SignIn_WithLockedParam_SetsFormError(string lockedParam)
    {
        var controller = CreateController();
        var result = Assert.IsType<ViewResult>(controller.SignIn(locked: lockedParam));
        var model = Assert.IsType<SignInViewModel>(result.Model);

        Assert.Equal("This account is locked. Contact support.", model.FormError);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("random")]
    public void SignIn_WithNonLockedParam_FormErrorIsNull(string lockedParam)
    {
        var controller = CreateController();
        var result = Assert.IsType<ViewResult>(controller.SignIn(locked: lockedParam));
        var model = Assert.IsType<SignInViewModel>(result.Model);

        Assert.Null(model.FormError);
    }

    private sealed class DummyAuthClient : IAuthClient
    {
        public Task<AuthTokenResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default) => Task.FromResult<AuthTokenResponse>(null!);
        public Task<AuthTokenResponse> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default) => Task.FromResult<AuthTokenResponse>(null!);
        public Task<AuthUserDto> GetMeAsync(CancellationToken cancellationToken = default) => Task.FromResult<AuthUserDto>(null!);
        public Task VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResendActivationAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
