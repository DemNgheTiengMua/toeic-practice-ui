using System.Security.Claims;
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

public sealed class MentorsControllerTests
{
    private static MentorSummaryDto SampleHumanMentor(int id = 1) =>
        new(
            id,
            10,
            "Dr. Trần Minh Khoa",
            "10 years teaching TOEIC.",
            1,
            "TOEIC",
            "TOEIC Listening & Reading",
            1,
            "LIST",
            "Listening",
            "listening",
            "human",
            200000m,
            1,
            "10 years teaching TOEIC",
            "Listening strategies, pronunciation",
            60,
            true,
            4.5m,
            20);

    private static MentorSummaryDto SampleAiMentor(int id = 2) =>
        new(
            id,
            null,
            "AI Mentor — TOEIC Coach",
            "AI speaking practice available 24/7.",
            1,
            "TOEIC",
            "TOEIC Listening & Reading",
            null,
            null,
            null,
            null,
            "ai",
            0m,
            1,
            "AI speaking practice 24/7",
            "Instant speaking feedback",
            60,
            true,
            5.0m,
            120);

    private static MentorsController CreateController(TestApiClient api)
    {
        var identity = new ClaimsIdentity(AuthClaims.For(7, "learner@test.local", Roles.Learner, "Learner"), "Cookies");
        var principal = new ClaimsPrincipal(identity);
        var properties = new AuthenticationProperties(new Dictionary<string, string?> { ["jwt"] = "token" });

        var services = new ServiceCollection();
        services.AddRouting();
        services.AddMvcCore();
        services.AddSingleton<IAuthenticationService>(new FixedAuthentication(principal, properties));
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { User = principal, RequestServices = serviceProvider };
        LearnerShell.Set(httpContext, new LearnerContextDto(new CertificateOptionDto("TOEIC", "TOEIC"), [new CertificateOptionDto("TOEIC", "TOEIC")], 8));
        var currentUser = new CurrentUser(new HttpContextAccessor { HttpContext = httpContext });

        var controller = new MentorsController(api, currentUser, NullLogger<MentorsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                RouteData = new Microsoft.AspNetCore.Routing.RouteData(),
            },
            TempData = new TempDataDictionary(httpContext, new EmptyTempDataProvider()),
        };

        return controller;
    }

    [Fact]
    public async Task Index_returns_view_with_mentor_list()
    {
        var api = new TestApiClient
        {
            Mentors = [SampleHumanMentor(1), SampleAiMentor(2)],
        };

        var controller = CreateController(api);
        var result = await controller.Index(null, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<MentorListViewModel>(viewResult.Model);
        Assert.Equal(2, model.Mentors.Count);
        Assert.Equal("TOEIC", model.ActiveCertCode);
        Assert.Null(model.ActiveSkill);
    }

    [Fact]
    public async Task Index_with_skill_filter_passes_filter_to_api()
    {
        var api = new TestApiClient
        {
            Mentors = [SampleHumanMentor(1)],
        };

        var controller = CreateController(api);
        var result = await controller.Index("listening", CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<MentorListViewModel>(viewResult.Model);
        Assert.Single(model.Mentors);
        Assert.Equal("listening", api.LastSkillQuery);
        Assert.Equal("listening", model.ActiveSkill);
    }

    [Fact]
    public async Task Index_handles_api_exception_gracefully()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException("ERROR", "API Failure", 500),
        };

        var controller = CreateController(api);
        var result = await controller.Index(null, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<MentorListViewModel>(viewResult.Model);
        Assert.Empty(model.Mentors);
        Assert.NotNull(model.ErrorMessage);
    }

    [Fact]
    public async Task Detail_returns_view_with_mentor_detail()
    {
        var futureSlot = new MentorSlotDto(1, 1, DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(1).AddHours(1), "open");
        var review = new MentorReviewDto(1, 5, "Great teacher!", "Demo Learner", DateTimeOffset.UtcNow.AddDays(-2));

        var api = new TestApiClient
        {
            Detail = new MentorDetailDto(SampleHumanMentor(1), [futureSlot], [review]),
        };

        var controller = CreateController(api);
        var result = await controller.Detail(1, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<MentorDetailViewModel>(viewResult.Model);
        Assert.Equal("Dr. Trần Minh Khoa", model.Detail.Mentor.DisplayName);
        Assert.Single(model.Detail.AvailableSlots);
        Assert.Single(model.Detail.Reviews);
    }

    [Fact]
    public async Task Detail_returns_not_found_when_mentor_does_not_exist()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.NotFound, "Not Found", 404),
        };

        var controller = CreateController(api);
        var result = await controller.Detail(999, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Detail_redirects_to_index_on_unexpected_error()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.Unexpected, "Error", 500),
        };

        var controller = CreateController(api);
        var result = await controller.Detail(1, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(MentorsController.Index), redirect.ActionName);
        Assert.NotNull(controller.TempData["ShellError"]);
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

    private sealed class TestApiClient : ICompanionApiClient
    {
        public IReadOnlyList<MentorSummaryDto> Mentors { get; set; } = [];
        public MentorDetailDto? Detail { get; set; }
        public string? LastSkillQuery { get; set; }
        public Exception? ExceptionToThrow { get; set; }

        public Task<IReadOnlyList<MentorSummaryDto>> GetMentorsAsync(string? certCode = null, string? skill = null, CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is not null) throw ExceptionToThrow;
            LastSkillQuery = skill;
            return Task.FromResult(Mentors);
        }

        public Task<MentorDetailDto> GetMentorDetailAsync(int mentorId, CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is not null) throw ExceptionToThrow;
            return Task.FromResult(Detail!);
        }
    }
}
