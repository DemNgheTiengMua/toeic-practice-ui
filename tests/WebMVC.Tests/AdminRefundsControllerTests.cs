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

/// <summary>
/// #281: the admin refund screens. These assert what a staff member sees and what the screen sends -
/// the request body the API receives, the redirect after a decision, and the error text on a refusal.
/// </summary>
public sealed class AdminRefundsControllerTests
{
    private static AdminRefundsSummaryDto SampleSummary() => new(
        Refunds:
        [
            new(31, 3421, "Nguyen Van A", "a@test.local", RefundMethodDto.Credit, RefundStatusDto.Requested,
                5, "Mentor did not attend", null, DateTimeOffset.UtcNow, null),
            new(30, 3422, "Tran Thi B", "b@test.local", RefundMethodDto.Credit, RefundStatusDto.Approved,
                2, "Grading failure", 8801, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow),
            new(29, 3423, "Le Minh C", "c@test.local", RefundMethodDto.Credit, RefundStatusDto.Rejected,
                9, "Duplicate", null, DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1)),
        ],
        TotalCount: 3,
        Page: 1,
        PageSize: 20,
        RequestedCount: 1,
        ApprovedCount: 1,
        RejectedCount: 1,
        CreditsRefundedTotal: 2);

    private static AdminRefundDetailDto SampleDetail(int id, RefundStatusDto status = RefundStatusDto.Requested) => new(
        id,
        3421,
        "Nguyen Van A",
        "a@test.local",
        RefundMethodDto.Credit,
        status,
        5,
        "Mentor did not attend",
        null,
        null,
        null,
        DateTimeOffset.UtcNow.AddDays(-1),
        1,
        "Admin Requester",
        status == RefundStatusDto.Requested ? null : 2,
        status == RefundStatusDto.Requested ? null : "Admin Decider",
        status == RefundStatusDto.Requested ? null : DateTimeOffset.UtcNow,
        status == RefundStatusDto.Approved ? 99 : null,
        status == RefundStatusDto.Approved ? 5 : null);

    private static AdminRefundsController CreateController(TestAdminRefundsApiClient api, ITempDataProvider? tempData = null)
    {
        var claims = new List<Claim>
        {
            new(IAccessTokenFactory.ClaimUserId, "1"),
            new(IAccessTokenFactory.ClaimEmail, "support@toeic.local"),
            new(IAccessTokenFactory.ClaimRole, Roles.Admin),
        };

        var identity = new ClaimsIdentity(claims, "Cookies");
        var principal = new ClaimsPrincipal(identity);
        var authProperties = new AuthenticationProperties();
        authProperties.StoreTokens([new AuthenticationToken { Name = "jwt", Value = "staff-token" }]);

        var services = new ServiceCollection();
        services.AddRouting();
        services.AddMvcCore();
        services.AddSingleton<IAuthenticationService>(new FixedAuthentication(principal, authProperties));
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { User = principal, RequestServices = serviceProvider };
        var currentUser = new CurrentUser(new HttpContextAccessor { HttpContext = httpContext });

        return new AdminRefundsController(api, currentUser, NullLogger<AdminRefundsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                RouteData = new Microsoft.AspNetCore.Routing.RouteData(),
            },
            TempData = new TempDataDictionary(httpContext, tempData ?? new EmptyTempDataProvider()),
        };
    }

    [Fact]
    public async Task Index_renders_the_list_with_status_and_method_words_not_codes()
    {
        var api = new TestAdminRefundsApiClient { Summary = SampleSummary() };
        var controller = CreateController(api);

        var result = await controller.Index(null, null, 1);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<AdminRefundsViewModel>(view.Model);
        Assert.Equal("Refunds", view.ViewName!.Split('/').Last().Replace(".cshtml", ""));

        // The page must never print a code: the label maps are what the view reads.
        Assert.Equal("Requested", model.StatusLabels[31]);
        Assert.Equal("Credits", model.MethodLabels[31]);
        Assert.Equal("Rejected", model.StatusLabels[29]);
        Assert.Contains("badge--danger", model.StatusClasses[29]);
        Assert.Contains("badge--success", model.StatusClasses[30]);
    }

    [Fact]
    public async Task Index_when_the_api_is_unreachable_renders_an_empty_list_instead_of_crashing()
    {
        var api = new TestAdminRefundsApiClient { ShouldFail = true };
        var controller = CreateController(api);

        var result = await controller.Index(null, null, 1);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<AdminRefundsViewModel>(view.Model);
        Assert.Empty(model.Summary.Refunds);
        Assert.Equal("Could not load the refunds list. Please try again.", controller.TempData["Error"]);
    }

    [Fact]
    public async Task Create_always_requests_a_credit_refund_so_the_money_path_stays_unreachable()
    {
        var api = new TestAdminRefundsApiClient { DetailToReturn = SampleDetail(31) };
        var controller = CreateController(api);

        var result = await controller.Create(userId: 3421, creditCount: 5, reason: "Mentor did not attend", orderId: null);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminRefundsController.Detail), redirect.ActionName);

        Assert.NotNull(api.LastRequest);
        Assert.Equal(RefundMethodDto.Credit, api.LastRequest!.Method);
        Assert.Equal(3421, api.LastRequest.UserId);
        Assert.Equal(5, api.LastRequest.CreditCount);
        Assert.Equal("Mentor did not attend", api.LastRequest.Reason);
        Assert.Null(api.LastRequest.OrderId);
    }

    [Fact]
    public async Task Create_refuses_a_blank_reason_or_zero_credits_without_calling_the_api()
    {
        var api = new TestAdminRefundsApiClient { DetailToReturn = SampleDetail(31) };
        var controller = CreateController(api);

        await controller.Create(userId: 3421, creditCount: 5, reason: "   ", orderId: null);
        Assert.Null(api.LastRequest);
        Assert.Equal("A reason is required on every refund.", controller.TempData["Error"]);

        await controller.Create(userId: 3421, creditCount: 0, reason: "Something", orderId: null);
        Assert.Null(api.LastRequest);
        Assert.Equal("A credit refund must return at least one credit.", controller.TempData["Error"]);
    }

    [Fact]
    public async Task Create_shows_the_ceiling_the_api_refused_with()
    {
        var api = new TestAdminRefundsApiClient
        {
            RequestException = new ApiException(
                ApiError.Codes.RefundExceedsOrderCeiling,
                "Ceiling refused",
                400,
                "Order DH-1 granted 5 credits and 5 are already refunded against it, so 3 more cannot be returned."),
        };
        var controller = CreateController(api);

        var result = await controller.Create(userId: 3421, creditCount: 3, reason: "Extra", orderId: 8801);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminRefundsController.Index), redirect.ActionName);
        Assert.Contains("already refunded against it", (string)controller.TempData["Error"]!);
    }

    [Fact]
    public async Task Approve_posts_the_decision_and_says_the_credits_landed()
    {
        var api = new TestAdminRefundsApiClient { DetailToReturn = SampleDetail(31, RefundStatusDto.Approved) };
        var controller = CreateController(api);

        var result = await controller.Approve(31);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminRefundsController.Detail), redirect.ActionName);
        Assert.Equal(31, api.ApprovedId);
        Assert.Contains("credits are in the learner's balance", (string)controller.TempData["Success"]!);
    }

    [Fact]
    public async Task Approve_shows_the_already_decided_refusal_verbatim()
    {
        var api = new TestAdminRefundsApiClient
        {
            ApproveException = new ApiException(
                ApiError.Codes.RefundAlreadyDecided,
                "Already decided",
                400,
                "Refund 31 has already been decided and cannot be decided again."),
        };
        var controller = CreateController(api);

        await controller.Approve(31);

        Assert.Contains("already been decided", (string)controller.TempData["Error"]!);
    }

    [Fact]
    public async Task Reject_requires_a_reason_and_posts_nothing_without_one()
    {
        var api = new TestAdminRefundsApiClient { DetailToReturn = SampleDetail(31, RefundStatusDto.Rejected) };
        var controller = CreateController(api);

        var blank = await controller.Reject(31, "  ");
        Assert.Equal(nameof(AdminRefundsController.Detail), Assert.IsType<RedirectToActionResult>(blank).ActionName);
        Assert.Null(api.LastReject);
        Assert.Equal("A reason is required to reject a refund.", controller.TempData["Error"]);

        api.LastReject = null;
        await controller.Reject(31, "The order was never paid.");
        Assert.NotNull(api.LastReject);
        Assert.Equal("The order was never paid.", api.LastReject!.Reason);
        Assert.Contains("Nothing was paid", (string)controller.TempData["Success"]!);
    }

    [Fact]
    public async Task Detail_when_the_refund_does_not_exist_answers_404()
    {
        var api = new TestAdminRefundsApiClient
        {
            DetailException = new ApiException(ApiError.Codes.RefundNotFound, "Not found", 404),
        };
        var controller = CreateController(api);

        var result = await controller.Detail(4040);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Detail_gates_the_decision_forms_on_a_requested_refund()
    {
        var api = new TestAdminRefundsApiClient { DetailToReturn = SampleDetail(31, RefundStatusDto.Requested) };
        var controller = CreateController(api);

        var result = await controller.Detail(31);

        var model = Assert.IsType<AdminRefundDetailViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.True(model.IsRequested);
        Assert.Equal("Requested", model.StatusLabel);
        Assert.Equal("Credits", model.MethodLabel);
    }

    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class TestAdminRefundsApiClient : IAdminRefundsClient
    {
        public AdminRefundsSummaryDto? Summary { get; set; }
        public AdminRefundDetailDto? DetailToReturn { get; set; }
        public RequestRefundRequest? LastRequest { get; set; }
        public RejectRefundRequest? LastReject { get; set; }
        public int? ApprovedId { get; set; }
        public bool ShouldFail { get; set; }
        public ApiException? RequestException { get; set; }
        public ApiException? ApproveException { get; set; }
        public ApiException? DetailException { get; set; }

        public Task<AdminRefundsSummaryDto> GetAdminRefundsAsync(string? q = null, string? status = null, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
        {
            if (ShouldFail) throw new HttpRequestException("Network failure");
            return Task.FromResult(Summary ?? new AdminRefundsSummaryDto([], 0, 1, 20, 0, 0, 0, 0));
        }

        public Task<AdminRefundDetailDto> GetAdminRefundDetailAsync(int refundId, CancellationToken cancellationToken = default)
        {
            if (DetailException is not null) throw DetailException;
            return Task.FromResult(DetailToReturn ?? SampleDetail(refundId));
        }

        public Task<AdminRefundDetailDto> RequestAdminRefundAsync(RequestRefundRequest request, CancellationToken cancellationToken = default)
        {
            if (RequestException is not null) throw RequestException;
            LastRequest = request;
            return Task.FromResult(DetailToReturn ?? SampleDetail(1));
        }

        public Task<AdminRefundDetailDto> ApproveAdminRefundAsync(int refundId, CancellationToken cancellationToken = default)
        {
            if (ApproveException is not null) throw ApproveException;
            ApprovedId = refundId;
            return Task.FromResult(DetailToReturn ?? SampleDetail(refundId, RefundStatusDto.Approved));
        }

        public Task<AdminRefundDetailDto> RejectAdminRefundAsync(int refundId, RejectRefundRequest request, CancellationToken cancellationToken = default)
        {
            LastReject = request;
            return Task.FromResult(DetailToReturn ?? SampleDetail(refundId, RefundStatusDto.Rejected));
        }
    }
}
