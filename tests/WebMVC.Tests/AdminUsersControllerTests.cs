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

public sealed class AdminUsersControllerTests
{
    private static AdminUsersSummaryDto SampleUsersSummary() => new(
        Users:
        [
            new(10, "an.nguyen@gmail.com", "Nguyen Van A", "0901234567", 8, 5, 760, DateTimeOffset.UtcNow, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(-1)),
            new(11, "locked@demo.local", "Le Minh Cuong", null, 0, 1, 460, DateTimeOffset.UtcNow.AddDays(-10), true, DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(-2)),
            new(12, "unverified@demo.local", "Pham Thu Ha", null, 0, 0, null, null, false, null, DateTimeOffset.UtcNow.AddDays(-2)),
        ],
        TotalCount: 3,
        Page: 1,
        PageSize: 20,
        ActiveCount: 1,
        LockedCount: 1,
        NotActivatedCount: 1);

    private static AdminUsersController CreateController(TestUsersApiClient api)
    {
        var services = new ServiceCollection();
        services.AddRouting();
        services.AddMvcCore();
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

        services.AddSingleton<IAuthenticationService>(new FixedAuthentication(principal, authProperties));
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { User = principal, RequestServices = serviceProvider };
        var currentUser = new CurrentUser(new HttpContextAccessor { HttpContext = httpContext });

        return new AdminUsersController(api, currentUser, NullLogger<AdminUsersController>.Instance)
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
    public async Task Index_returns_view_with_summary_and_adjust_user()
    {
        var api = new TestUsersApiClient
        {
            Summary = SampleUsersSummary(),
        };
        var controller = CreateController(api);

        var result = await controller.Index(q: null, status: null, page: 1, adjustUserId: 10, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<AdminUsersViewModel>(viewResult.Model);
        Assert.Equal(3, vm.Summary.Users.Count);
        Assert.NotNull(vm.AdjustUser);
        Assert.Equal(10, vm.AdjustUser.UserId);
        Assert.Equal(8, vm.AdjustUser.CreditsLeft);
    }

    [Fact]
    public async Task Lock_calls_api_and_redirects_with_success_message()
    {
        var api = new TestUsersApiClient();
        var controller = CreateController(api);

        var result = await controller.Lock(10, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminUsersController.Index), redirect.ActionName);
        Assert.Equal(10, api.LockedUserId);
        Assert.NotNull(controller.TempData["Success"]);
    }

    [Fact]
    public async Task Unlock_calls_api_and_redirects_with_success_message()
    {
        var api = new TestUsersApiClient();
        var controller = CreateController(api);

        var result = await controller.Unlock(11, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminUsersController.Index), redirect.ActionName);
        Assert.Equal(11, api.UnlockedUserId);
        Assert.NotNull(controller.TempData["Success"]);
    }

    [Fact]
    public async Task ResendActivation_calls_api_and_redirects_with_success_message()
    {
        var api = new TestUsersApiClient();
        var controller = CreateController(api);

        var result = await controller.ResendActivation(12, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminUsersController.Index), redirect.ActionName);
        Assert.Equal(12, api.ResentUserId);
        Assert.NotNull(controller.TempData["Success"]);
    }

    [Fact]
    public async Task AdjustCredits_rejects_zero_delta_or_empty_reason()
    {
        var api = new TestUsersApiClient();
        var controller = CreateController(api);

        // Zero delta
        var resZero = await controller.AdjustCredits(10, delta: 0, reason: "Valid reason", CancellationToken.None);
        var redZero = Assert.IsType<RedirectToActionResult>(resZero);
        Assert.NotNull(controller.TempData["Error"]);

        // Empty reason
        var resEmpty = await controller.AdjustCredits(10, delta: 2, reason: "   ", CancellationToken.None);
        var redEmpty = Assert.IsType<RedirectToActionResult>(resEmpty);
        Assert.NotNull(controller.TempData["Error"]);
        Assert.Null(api.AdjustedRequest);
    }

    [Fact]
    public async Task AdjustCredits_calls_api_and_redirects_with_success_message()
    {
        var api = new TestUsersApiClient
        {
            AdjustResult = new AdjustCreditsResultDto(101, 10, 13, 5, "Compensation"),
        };
        var controller = CreateController(api);

        var result = await controller.AdjustCredits(10, delta: 5, reason: "Compensation", CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminUsersController.Index), redirect.ActionName);
        Assert.Equal(10, api.AdjustedUserId);
        Assert.NotNull(api.AdjustedRequest);
        Assert.Equal(5, api.AdjustedRequest.Delta);
        Assert.Equal("Compensation", api.AdjustedRequest.Reason);
        Assert.NotNull(controller.TempData["Success"]);
    }

    [Fact]
    public async Task AdjustCredits_catches_insufficient_credits_and_sets_error()
    {
        var api = new TestUsersApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.InsufficientCredits, "Insufficient", 400),
        };
        var controller = CreateController(api);

        var result = await controller.AdjustCredits(10, delta: -10, reason: "Deduction", CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminUsersController.Index), redirect.ActionName);
        Assert.NotNull(controller.TempData["Error"]);
        Assert.Contains("Deduction cannot exceed", controller.TempData["Error"]!.ToString()!);
    }

    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class TestUsersApiClient : IAdminUsersClient
    {
        public AdminUsersSummaryDto? Summary { get; set; }
        public int? LockedUserId { get; set; }
        public int? UnlockedUserId { get; set; }
        public int? ResentUserId { get; set; }
        public int? AdjustedUserId { get; set; }
        public AdjustCreditsRequest? AdjustedRequest { get; set; }
        public AdjustCreditsResultDto? AdjustResult { get; set; }
        public Exception? ExceptionToThrow { get; set; }

        public Task<AdminUsersSummaryDto> GetAdminUsersAsync(string? q = null, string? status = null, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default) =>
            Task.FromResult(Summary ?? new AdminUsersSummaryDto([], 0, 1, 20, 0, 0, 0));

        public Task<AdminUserDto> LockUserAsync(int userId, CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is not null) throw ExceptionToThrow;
            LockedUserId = userId;
            return Task.FromResult(new AdminUserDto(userId, "locked@demo.local", "Locked", null, 0, 0, null, null, true, null, DateTimeOffset.UtcNow));
        }

        public Task<AdminUserDto> UnlockUserAsync(int userId, CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is not null) throw ExceptionToThrow;
            UnlockedUserId = userId;
            return Task.FromResult(new AdminUserDto(userId, "unlocked@demo.local", "Unlocked", null, 0, 0, null, null, false, null, DateTimeOffset.UtcNow));
        }

        public Task ResendUserActivationAsync(int userId, CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is not null) throw ExceptionToThrow;
            ResentUserId = userId;
            return Task.CompletedTask;
        }

        public Task<AdjustCreditsResultDto> AdjustUserCreditsAsync(int userId, AdjustCreditsRequest request, CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is not null) throw ExceptionToThrow;
            AdjustedUserId = userId;
            AdjustedRequest = request;
            return Task.FromResult(AdjustResult ?? new AdjustCreditsResultDto(1, userId, 10, request.Delta, request.Reason));
        }
    }
}
