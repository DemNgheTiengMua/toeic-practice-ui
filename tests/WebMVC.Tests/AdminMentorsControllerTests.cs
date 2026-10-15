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

public sealed class AdminMentorsControllerTests
{
    private static AdminMentorListItemDto SampleAdminMentor(int id = 1, string type = "human") =>
        new(
            id,
            type == "human" ? 10 : null,
            $"Mentor {id}",
            type,
            1,
            "TOEIC",
            1,
            "Listening",
            200000m,
            1,
            60,
            4.5m,
            10,
            5,
            3,
            true);

    private static AdminMentorsController CreateController(TestApiClient api)
    {
        var claims = new List<Claim>
        {
            new(IAccessTokenFactory.ClaimUserId, "1"),
            new(IAccessTokenFactory.ClaimEmail, "admin@toeic.local"),
            new(IAccessTokenFactory.ClaimRole, Roles.Admin),
        };

        var identity = new ClaimsIdentity(claims, "Cookies");
        var principal = new ClaimsPrincipal(identity);
        var authProperties = new AuthenticationProperties();
        authProperties.StoreTokens([new AuthenticationToken { Name = "jwt", Value = "admin-token" }]);

        var services = new ServiceCollection();
        services.AddRouting();
        services.AddMvcCore();
        services.AddSingleton<IAuthenticationService>(new FixedAuthentication(principal, authProperties));
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { User = principal, RequestServices = serviceProvider };
        var currentUser = new CurrentUser(new HttpContextAccessor { HttpContext = httpContext });

        return new AdminMentorsController(api, currentUser, NullLogger<AdminMentorsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                RouteData = new Microsoft.AspNetCore.Routing.RouteData(),
            },
            TempData = new TempDataDictionary(httpContext, new EmptyTempDataProvider()),
        };
    }

    [Fact]
    public async Task Index_returns_view_with_admin_mentors()
    {
        var api = new TestApiClient
        {
            AdminMentors = [SampleAdminMentor(1, "human"), SampleAdminMentor(2, "ai")],
        };

        var controller = CreateController(api);
        var result = await controller.Index(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<IReadOnlyList<AdminMentorListItemDto>>(viewResult.Model);
        Assert.Equal(2, model.Count);
    }

    [Fact]
    public async Task Create_GET_returns_view_with_certs_and_users()
    {
        var api = new TestApiClient
        {
            CertificateOptions = [new MentorCertificateOptionDto(1, "TOEIC", "TOEIC", [])],
            AdminUsers = new AdminUsersSummaryDto([new AdminUserDto(10, "m@test.local", "Mentor User", null, 0, 0, null, null, false, null, DateTimeOffset.UtcNow)], 1, 1, 10, 1, 0, 0),
        };

        var controller = CreateController(api);
        var result = await controller.Create(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<MentorFormViewModel>(viewResult.Model);
        Assert.Single(model.Certificates);
        Assert.Single(model.Users);
    }

    [Fact]
    public async Task Create_POST_with_valid_model_creates_mentor_and_redirects()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var model = new MentorFormViewModel
        {
            Type = "human",
            UserId = 10,
            DisplayName = "Dr. Tran Minh Khoa",
            CertId = 1,
            PricePerSlot = 200000m,
            PriceCredits = 1,
            SessionMinutes = 60,
            IsActive = true,
        };

        var result = await controller.Create(model, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminMentorsController.Index), redirect.ActionName);
        Assert.NotNull(api.CreatedRequest);
        Assert.Equal("Dr. Tran Minh Khoa", api.CreatedRequest.DisplayName);
    }

    [Fact]
    public async Task Create_POST_with_human_mentor_and_missing_user_returns_validation_error()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var model = new MentorFormViewModel
        {
            Type = "human",
            UserId = null, // Missing user for human mentor!
            DisplayName = "Dr. Tran Minh Khoa",
            CertId = 1,
        };

        var result = await controller.Create(model, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.True(controller.ModelState.ContainsKey(nameof(model.UserId)));
    }

    [Fact]
    public async Task Slots_GET_returns_view_with_slots()
    {
        var api = new TestApiClient
        {
            MentorSummary = new MentorSummaryDto(1, 10, "Dr. Khoa", "Bio", 1, "TOEIC", "TOEIC", 1, "LIST", "Listening", "listening", "human", 200000m, 1, null, null, 60, true, 4.5m, 10),
            AdminSlots = [new AdminMentorSlotDto(1, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), "open")],
        };

        var controller = CreateController(api);
        var result = await controller.Slots(1, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<MentorSlotsViewModel>(viewResult.Model);
        Assert.Equal("Dr. Khoa", model.Mentor.DisplayName);
        Assert.Single(model.Slots);
    }

    [Fact]
    public async Task CreateSlot_POST_with_valid_data_creates_slot_and_redirects()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var form = new CreateSlotViewModel
        {
            LocalStartAt = new DateTime(2026, 10, 20, 9, 0, 0),
        };

        var result = await controller.CreateSlot(1, form, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminMentorsController.Slots), redirect.ActionName);
        Assert.NotNull(api.CreatedSlotRequest);
        Assert.Equal(new DateTime(2026, 10, 20, 9, 0, 0), api.CreatedSlotRequest.LocalStartAt);
    }

    [Fact]
    public async Task CloseSlot_POST_closes_slot_and_redirects()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var result = await controller.CloseSlot(1, 5, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminMentorsController.Slots), redirect.ActionName);
        Assert.Equal((1, 5), api.ClosedSlot);
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
        public IReadOnlyList<AdminMentorListItemDto> AdminMentors { get; set; } = [];
        public MentorSummaryDto? MentorSummary { get; set; }
        public IReadOnlyList<CertificateDto> Certificates { get; set; } = [];
        public IReadOnlyList<MentorCertificateOptionDto> CertificateOptions { get; set; } = [];
        public AdminUsersSummaryDto? AdminUsers { get; set; }
        public IReadOnlyList<AdminMentorSlotDto> AdminSlots { get; set; } = [];
        public CreateMentorProfileRequest? CreatedRequest { get; set; }
        public CreateMentorSlotRequest? CreatedSlotRequest { get; set; }
        public (int MentorId, int SlotId)? ClosedSlot { get; set; }

        public Task<IReadOnlyList<AdminMentorListItemDto>> GetAdminMentorsAsync(string bearerToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(AdminMentors);

        public Task<MentorSummaryDto> GetAdminMentorByIdAsync(int mentorId, string bearerToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(MentorSummary ?? new MentorSummaryDto(mentorId, 10, "Name", null, 1, "TOEIC", "TOEIC", null, null, null, null, "human", 200000m, 1, null, null, 60, true, null, 0));

        public Task<IReadOnlyList<CertificateDto>> GetCertificatesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Certificates);

        public Task<IReadOnlyList<MentorCertificateOptionDto>> GetMentorCertificateOptionsAsync(string bearerToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(CertificateOptions);

        public Task<AdminUsersSummaryDto> GetAdminUsersAsync(string? q, string? status, int page, int pageSize, string bearerToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(AdminUsers ?? new AdminUsersSummaryDto([], 0, 1, 10, 0, 0, 0));

        public Task<MentorSummaryDto> CreateMentorAsync(CreateMentorProfileRequest request, string bearerToken, CancellationToken cancellationToken = default)
        {
            CreatedRequest = request;
            return Task.FromResult(new MentorSummaryDto(1, request.UserId, request.DisplayName, request.Bio, request.CertId, "TOEIC", "TOEIC", request.SkillId, null, null, null, request.Type, request.PricePerSlot, request.PriceCredits, request.Headline, request.Expertise, request.SessionMinutes, request.IsActive, null, 0));
        }

        public Task<MentorSummaryDto> UpdateMentorAsync(int mentorId, UpdateMentorProfileRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MentorSummaryDto(mentorId, request.UserId, request.DisplayName, request.Bio, request.CertId, "TOEIC", "TOEIC", request.SkillId, null, null, null, "human", request.PricePerSlot, request.PriceCredits, request.Headline, request.Expertise, request.SessionMinutes, request.IsActive, null, 0));

        public Task<IReadOnlyList<AdminMentorSlotDto>> GetAdminMentorSlotsAsync(int mentorId, string bearerToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(AdminSlots);

        public Task<AdminMentorSlotDto> CreateMentorSlotAsync(int mentorId, CreateMentorSlotRequest request, string bearerToken, CancellationToken cancellationToken = default)
        {
            CreatedSlotRequest = request;
            return Task.FromResult(new AdminMentorSlotDto(1, mentorId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), "open"));
        }

        public Task CloseMentorSlotAsync(int mentorId, int slotId, string bearerToken, CancellationToken cancellationToken = default)
        {
            ClosedSlot = (mentorId, slotId);
            return Task.CompletedTask;
        }
    }
}
