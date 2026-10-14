using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WebMVC.Controllers;
using WebMVC.Models;
using Xunit;

namespace WebMVC.Tests;

public sealed class AdminScoreConversionControllerTests
{
    private static AdminScoreConversionController CreateController(ICompanionApiClient api)
    {
        var identity = new ClaimsIdentity(AuthClaims.For(1, "admin@toeic.local", Roles.Admin, "Admin"), "Cookies");
        var principal = new ClaimsPrincipal(identity);
        var properties = new AuthenticationProperties(new Dictionary<string, string?> { ["jwt"] = "token" });

        var services = new ServiceCollection();
        services.AddRouting();
        services.AddMvcCore();
        services.AddSingleton<IAuthenticationService>(new FixedAuthentication(principal, properties));
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { User = principal, RequestServices = serviceProvider };
        var currentUser = new CurrentUser(new HttpContextAccessor { HttpContext = httpContext });

        return new AdminScoreConversionController(
            api,
            currentUser,
            NullLogger<AdminScoreConversionController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                RouteData = new Microsoft.AspNetCore.Routing.RouteData(),
            },
            TempData = new TempDataDictionary(httpContext, new EmptyTempDataProvider())
        };
    }

    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class FixedAuthentication(ClaimsPrincipal principal, AuthenticationProperties properties) : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, properties, "Cookies")));

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? props) => Task.CompletedTask;
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? props) => Task.CompletedTask;
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? props) => Task.CompletedTask;
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? props) => Task.CompletedTask;
    }

    [Fact]
    public async Task Index_returns_view_with_score_conversion_model()
    {
        var api = new TestScoreConversionApiClient
        {
            ConversionDto = new AdminScoreConversionDto(
                1, "TOEIC", "TOEIC Test",
                [new CertificateOptionDto("TOEIC", "TOEIC Test")],
                null, null,
                [new SkillScoreScaleGroupDto(1, "LIST", "Listening", 100, [new ScoreScaleRowDto(1, 1, "LIST", "Listening", 0, 5)])],
                [new ScoreBandConfigDto(1, "B1", "Intermediate", 550, 780, 1)])
        };

        var controller = CreateController(api);
        var result = await controller.Index(cert: "TOEIC", examId: null, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<AdminScoreConversionViewModel>(viewResult.Model);
        Assert.Equal("TOEIC", model.Conversion.CertCode);
        Assert.Single(model.Conversion.SkillScales);
        Assert.Single(model.Conversion.Bands);
    }

    [Fact]
    public async Task Save_valid_form_redirects_to_Index_with_success_message()
    {
        var api = new TestScoreConversionApiClient
        {
            UpdatedDto = new AdminScoreConversionDto(
                1, "TOEIC", "TOEIC Test",
                [new CertificateOptionDto("TOEIC", "TOEIC Test")],
                null, null, [], [])
        };

        var controller = CreateController(api);
        var form = new ScoreConversionFormModel
        {
            ExamId = null,
            Scales =
            [
                new ScoreScaleFormEntry { SkillId = 1, RawScore = 0, ScaledScore = 5 },
                new ScoreScaleFormEntry { SkillId = 1, RawScore = 1, ScaledScore = 10 }
            ],
            Bands =
            [
                new ScoreBandFormEntry { BandId = 1, Code = "LOW", Name = "Low Band", MinTotal = 0, MaxTotal = 499, DisplayOrder = 1 }
            ]
        };

        var result = await controller.Save(certId: 1, form, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminScoreConversionController.Index), redirect.ActionName);
        Assert.Equal("TOEIC", redirect.RouteValues?["cert"]);
        Assert.Equal("Score conversion table saved successfully.", controller.TempData["Success"]);
    }

    [Fact]
    public async Task Save_when_api_exception_redirects_to_Index_with_error_message()
    {
        var api = new TestScoreConversionApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.ScoreConversionInvalid, "Non-monotonic scales", 400, "Scales must be monotonic.")
        };

        var controller = CreateController(api);
        var form = new ScoreConversionFormModel { CertCode = "TOEIC" };

        var result = await controller.Save(certId: 1, form, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminScoreConversionController.Index), redirect.ActionName);
        Assert.Equal("TOEIC", redirect.RouteValues?["cert"]);
        Assert.Equal("Scales must be monotonic.", controller.TempData["Error"]);
    }

    private sealed class TestScoreConversionApiClient : ICompanionApiClient
    {
        public Exception? ExceptionToThrow { get; set; }
        public AdminScoreConversionDto? ConversionDto { get; set; }
        public AdminScoreConversionDto? UpdatedDto { get; set; }

        public Task<AdminScoreConversionDto> GetAdminScoreConversionAsync(string? certCode, int? examId, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<AdminScoreConversionDto>(ExceptionToThrow) : Task.FromResult(ConversionDto!);

        public Task<AdminScoreConversionDto> UpdateAdminScoreConversionAsync(int certId, UpdateScoreConversionRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<AdminScoreConversionDto>(ExceptionToThrow) : Task.FromResult(UpdatedDto!);
    }
}
