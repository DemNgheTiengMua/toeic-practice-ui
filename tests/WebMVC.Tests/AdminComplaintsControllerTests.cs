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

public sealed class AdminComplaintsControllerTests
{
    private static AdminComplaintsSummaryDto SampleSummary() => new(
        Complaints:
        [
            new(1042, 3421, "Nguyen Van A", "a@test.local", ComplaintTypeDto.MentorNoShow, "Booking", 8821, "Booking #8821", ComplaintStatusDto.Open, DateTimeOffset.UtcNow, null),
            new(1041, 3422, "Tran Thi B", "b@test.local", ComplaintTypeDto.GradingIssue, "ExamAttempt", 1176, "Exam #1176", ComplaintStatusDto.Reviewing, DateTimeOffset.UtcNow.AddDays(-1), null),
            new(1040, 3423, "Le Minh C", "c@test.local", ComplaintTypeDto.TechnicalError, "ExamAttempt", 1178, "Exam #1178", ComplaintStatusDto.Resolved, DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1)),
        ],
        TotalCount: 3,
        Page: 1,
        PageSize: 20,
        PendingCount: 2,
        ResolvedCount: 1,
        OldestPendingCreatedAt: DateTimeOffset.UtcNow.AddDays(-1),
        OldestPendingId: 1041,
        OldestPendingType: ComplaintTypeDto.GradingIssue);

    private static AdminComplaintDetailDto SampleDetail(int id, ComplaintStatusDto status = ComplaintStatusDto.Open) => new(
        id,
        3421,
        "Nguyen Van A",
        "a@test.local",
        ComplaintTypeDto.MentorNoShow,
        "Mentor did not show up in the meeting room.",
        status,
        "Booking",
        8821,
        "Booking #8821",
        "Dr. Tran Minh Khoa — 24 Sep 2026, 09:00",
        DateTimeOffset.UtcNow.AddDays(-1),
        status == ComplaintStatusDto.Resolved ? "1 credit make-good issued." : null,
        status == ComplaintStatusDto.Resolved ? "Mentor absence confirmed." : null,
        status == ComplaintStatusDto.Resolved ? 1 : null,
        status == ComplaintStatusDto.Resolved ? "Admin User" : null,
        status == ComplaintStatusDto.Resolved ? DateTimeOffset.UtcNow : null,
        status == ComplaintStatusDto.Resolved ? 99 : null,
        status == ComplaintStatusDto.Resolved ? 1 : null);

    private static AdminComplaintsController CreateController(TestAdminComplaintsApiClient api)
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

        return new AdminComplaintsController(api, currentUser, NullLogger<AdminComplaintsController>.Instance)
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
    public async Task Index_returns_view_with_summary_and_complaints_list()
    {
        var api = new TestAdminComplaintsApiClient
        {
            Summary = SampleSummary(),
        };
        var controller = CreateController(api);

        var result = await controller.Index(q: null, status: null, page: 1, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<AdminComplaintsViewModel>(viewResult.Model);
        Assert.Equal(3, vm.Summary.Complaints.Count);
        Assert.Equal(2, vm.Summary.PendingCount);
        Assert.Equal(1, vm.Summary.ResolvedCount);
    }

    [Fact]
    public async Task Index_on_api_error_renders_view_with_error_flash_and_empty_list()
    {
        var api = new TestAdminComplaintsApiClient { ShouldFail = true };
        var controller = CreateController(api);

        var result = await controller.Index(q: null, status: null, page: 1, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<AdminComplaintsViewModel>(viewResult.Model);
        Assert.Empty(vm.Summary.Complaints);
        Assert.NotNull(controller.TempData["Error"]);
    }

    [Fact]
    public async Task Detail_returns_view_with_complaint_details()
    {
        var api = new TestAdminComplaintsApiClient
        {
            DetailToReturn = SampleDetail(1042),
        };
        var controller = CreateController(api);

        var result = await controller.Detail(1042, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<AdminComplaintDetailViewModel>(viewResult.Model);
        Assert.Equal(1042, vm.Complaint.ComplaintId);
        Assert.Equal(ComplaintStatusDto.Open, vm.Complaint.Status);
    }

    [Fact]
    public async Task Detail_returns_not_found_when_complaint_does_not_exist()
    {
        var api = new TestAdminComplaintsApiClient
        {
            DetailToReturn = null,
        };
        var controller = CreateController(api);

        var result = await controller.Detail(9999, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task SetStatus_updates_status_and_redirects_to_detail()
    {
        var api = new TestAdminComplaintsApiClient
        {
            DetailToReturn = SampleDetail(1042, ComplaintStatusDto.Reviewing),
        };
        var controller = CreateController(api);

        var result = await controller.SetStatus(1042, "reviewing", CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminComplaintsController.Detail), redirect.ActionName);
        Assert.Equal(1042, redirect.RouteValues?["id"]);
        Assert.Equal(ComplaintStatusDto.Reviewing, api.LastStatusUpdate?.Status);
        Assert.NotNull(controller.TempData["Success"]);
    }

    [Fact]
    public async Task Resolve_with_credits_forwards_to_api_and_redirects_with_flash()
    {
        var api = new TestAdminComplaintsApiClient
        {
            DetailToReturn = SampleDetail(1042, ComplaintStatusDto.Resolved),
        };
        var controller = CreateController(api);

        var result = await controller.Resolve(
            id: 1042,
            resolution: "Refunded 1 credit.",
            internalNotes: "Verified absence.",
            issue_credit: "on",
            credit_amount: 1,
            cancellationToken: CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminComplaintsController.Detail), redirect.ActionName);
        Assert.Equal(1042, redirect.RouteValues?["id"]);
        Assert.NotNull(api.LastResolveRequest);
        Assert.Equal(1, api.LastResolveRequest.GrantCredits);
        Assert.Equal("Refunded 1 credit.", api.LastResolveRequest.Resolution);
        Assert.NotNull(controller.TempData["Success"]);
    }

    [Fact]
    public async Task Resolve_without_credits_forwards_to_api_and_redirects()
    {
        var api = new TestAdminComplaintsApiClient
        {
            DetailToReturn = SampleDetail(1042, ComplaintStatusDto.Resolved),
        };
        var controller = CreateController(api);

        var result = await controller.Resolve(
            id: 1042,
            resolution: "Explained policy to learner.",
            internalNotes: "No credit needed.",
            issue_credit: null,
            credit_amount: null,
            cancellationToken: CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminComplaintsController.Detail), redirect.ActionName);
        Assert.NotNull(api.LastResolveRequest);
        Assert.Equal(0, api.LastResolveRequest.GrantCredits);
    }

    [Fact]
    public async Task Resolve_with_validation_errors_redirects_with_error_flash()
    {
        var api = new TestAdminComplaintsApiClient();
        var controller = CreateController(api);

        // Missing resolution
        var resultNoRes = await controller.Resolve(
            id: 1042,
            resolution: "",
            internalNotes: "Some note",
            issue_credit: null,
            credit_amount: null,
            cancellationToken: CancellationToken.None);

        var redirectNoRes = Assert.IsType<RedirectToActionResult>(resultNoRes);
        Assert.Equal(nameof(AdminComplaintsController.Detail), redirectNoRes.ActionName);
        Assert.NotNull(controller.TempData["Error"]);

        // Missing internal notes
        var resultNoNotes = await controller.Resolve(
            id: 1042,
            resolution: "Some resolution",
            internalNotes: "",
            issue_credit: null,
            credit_amount: null,
            cancellationToken: CancellationToken.None);

        var redirectNoNotes = Assert.IsType<RedirectToActionResult>(resultNoNotes);
        Assert.Equal(nameof(AdminComplaintsController.Detail), redirectNoNotes.ActionName);
        Assert.NotNull(controller.TempData["Error"]);
    }

    [Fact]
    public async Task Resolve_with_excessive_credits_redirects_with_error_flash()
    {
        var api = new TestAdminComplaintsApiClient();
        var controller = CreateController(api);

        var result = await controller.Resolve(
            id: 1042,
            resolution: "Some resolution",
            internalNotes: "Some note",
            issue_credit: "on",
            credit_amount: 50,
            cancellationToken: CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminComplaintsController.Detail), redirect.ActionName);
        Assert.NotNull(controller.TempData["Error"]);
        Assert.Contains("between 1 and 20", (string)controller.TempData["Error"]!);
        Assert.Null(api.LastResolveRequest);
    }

    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class TestAdminComplaintsApiClient : IAdminComplaintsClient
    {
        public AdminComplaintsSummaryDto? Summary { get; set; }
        public AdminComplaintDetailDto? DetailToReturn { get; set; }
        public UpdateComplaintStatusRequest? LastStatusUpdate { get; set; }
        public ResolveComplaintRequest? LastResolveRequest { get; set; }
        public bool ShouldFail { get; set; }

        public Task<AdminComplaintsSummaryDto> GetAdminComplaintsAsync(string? q = null, string? status = null, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
        {
            if (ShouldFail) throw new HttpRequestException("Network failure");
            return Task.FromResult(Summary ?? new AdminComplaintsSummaryDto([], 0, 1, 20, 0, 0, null, null, null));
        }

        public Task<AdminComplaintDetailDto> GetAdminComplaintDetailAsync(int complaintId, CancellationToken cancellationToken = default)
        {
            if (ShouldFail) throw new HttpRequestException("Network failure");
            if (DetailToReturn is null) throw new ApiException(ApiError.Codes.ComplaintNotFound, "Not found", 404);
            return Task.FromResult(DetailToReturn);
        }

        public Task<AdminComplaintDetailDto> SetAdminComplaintStatusAsync(int complaintId, UpdateComplaintStatusRequest request, CancellationToken cancellationToken = default)
        {
            LastStatusUpdate = request;
            return Task.FromResult(DetailToReturn ?? SampleDetail(complaintId));
        }

        public Task<AdminComplaintDetailDto> ResolveAdminComplaintAsync(int complaintId, ResolveComplaintRequest request, CancellationToken cancellationToken = default)
        {
            LastResolveRequest = request;
            return Task.FromResult(DetailToReturn ?? SampleDetail(complaintId, ComplaintStatusDto.Resolved));
        }
    }
}
