using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
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

/// <summary>
/// #345: the refund desk is support work, so a support-only account must be able to complete the whole
/// flow. It searches the learner, reads that learner's balance, and picks one of their paid orders before
/// it can record anything. Those three reads used to live on <c>/api/admin/users</c> and
/// <c>/api/admin/orders</c>, which are gated on system governance - so a support-only agent got a 403 on
/// the picker, on the balance, and on the carried-learner autofill that arrives from a complaint.
///
/// <para>
/// <b>Why this test and not a unit test.</b> Every existing fixture holds all five staff namespaces, so any
/// assertion it makes passes for the demo admin and says nothing about the persona the ticket is about. The
/// authentication handler here emits exactly one <c>perm</c> claim, and the policies are the real
/// claim-requiring ones, so the gate is exercised rather than bypassed.
/// </para>
/// </summary>
public sealed class RefundDeskLookupGatesTests
{
    private const string SupportOnly = Permissions.SupportTriage;
    private const string GovernanceOnly = Permissions.SystemAdmin;

    [Fact]
    public async Task A_support_only_account_can_search_learners_read_one_balance_and_list_paid_orders()
    {
        var (client, app) = await HostAsync();
        await using (app)
        {
            var learners = await Get(client, "/api/admin/refunds/lookups/learners?q=nguyen", SupportOnly);
            Assert.Equal(HttpStatusCode.OK, learners.StatusCode);

            var learner = await Get(client, "/api/admin/refunds/lookups/learners/15", SupportOnly);
            Assert.Equal(HttpStatusCode.OK, learner.StatusCode);

            var orders = await Get(client, "/api/admin/refunds/lookups/orders?userId=15", SupportOnly);
            Assert.Equal(HttpStatusCode.OK, orders.StatusCode);
        }
    }

    /// <summary>
    /// The mirror image, and the reason the first test means something: the governance routes are still
    /// governance. If this one ever starts passing for a support-only account, the fix widened
    /// <c>system.governance.admin</c> instead of giving support a lookup of its own.
    /// </summary>
    [Fact]
    public async Task The_governance_user_and_order_routes_stay_closed_to_a_support_only_account()
    {
        var (client, app) = await HostAsync();
        await using (app)
        {
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await Get(client, "/api/admin/users?q=nguyen", SupportOnly)).StatusCode);
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await Get(client, "/api/admin/users/15", SupportOnly)).StatusCode);
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await Get(client, "/api/admin/orders?userId=15", SupportOnly)).StatusCode);
        }
    }

    [Fact]
    public async Task A_governance_only_account_keeps_its_user_and_order_reads()
    {
        var (client, app) = await HostAsync();
        await using (app)
        {
            Assert.Equal(
                HttpStatusCode.OK,
                (await Get(client, "/api/admin/users?q=nguyen", GovernanceOnly)).StatusCode);
            Assert.Equal(
                HttpStatusCode.OK,
                (await Get(client, "/api/admin/orders?userId=15", GovernanceOnly)).StatusCode);
        }
    }

    private static Task<HttpResponseMessage> Get(HttpClient client, string path, string capability)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(TestPermissionsHeader, capability);
        return client.SendAsync(request);
    }

    private const string TestPermissionsHeader = "X-Test-Permission";

    private static async Task<(HttpClient Client, WebApplication App)> HostAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, "http://127.0.0.1:0");

        builder.Services.AddSingleton<IAdminUserService, StubUsers>();
        builder.Services.AddSingleton<IAdminOrderService, StubOrders>();
        builder.Services.AddSingleton<IRefundService, StubRefunds>();

        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, OneCapability>( "Test", null);

        // The real policy shape: one policy per namespace, each requiring the `perm` claim it names. A
        // policy that merely required authentication would let this test pass on a gate that refuses
        // nothing.
        var authorization = builder.Services.AddAuthorizationBuilder();
        foreach (var permission in Permissions.All)
        {
            authorization.AddPolicy(
                permission,
                policy => policy.RequireClaim(IAccessTokenFactory.ClaimPermission, permission));
        }

        var app = builder.Build();
        app.UseApiProblemDetails();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapRefundEndpoints();

        var adminSystem = app.MapGroup("/api/admin").RequireAuthorization(Permissions.SystemAdmin);
        typeof(AdminEndpoints).GetMethod("MapOrders", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [adminSystem]);
        typeof(AdminEndpoints).GetMethod("MapUsers", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [adminSystem]);

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        return (new HttpClient { BaseAddress = new Uri(address) }, app);
    }

    /// <summary>Emits the one capability the request names, and nothing else - the whole point of the test.</summary>
    private sealed class OneCapability : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public OneCapability(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var capability = Request.Headers[TestPermissionsHeader].ToString();
            if (string.IsNullOrEmpty(capability))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [
                    new Claim(IAccessTokenFactory.ClaimUserId, "1"),
                    new Claim(IAccessTokenFactory.ClaimPermission, capability),
                ],
                "Test");
            return Task.FromResult(
                AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }

    private sealed class StubUsers : IAdminUserService
    {
        public Task<AdminUsersSummaryDto> GetUsersAsync(
            string? q = null, string? status = null, int page = 1, int pageSize = 20,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AdminUsersSummaryDto([Learner], 1, 1, 20, 1, 0, 0));

        public Task<AdminUserDetailDto> GetUserDetailAsync(
            int targetUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AdminUserDetailDto(Learner, []));

        public Task<AdminUserDto> SetLockAsync(
            int adminUserId, int targetUserId, bool isLocked,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The lookup must not lock an account.");

        public Task<AdjustCreditsResultDto> AdjustCreditsAsync(
            int adminUserId, int targetUserId, int delta, string reason,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The lookup must not move credits.");

        public Task ResendActivationAsync(int targetUserId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The lookup must not send mail.");
    }

    private static readonly AdminUserDto Learner = new(
        UserId: 15,
        Email: "learner@demo.local",
        FullName: "Nguyễn Văn A",
        Phone: null,
        CreditsLeft: 7,
        ExamsTaken: 0,
        BestScore: null,
        LastLoginAt: null,
        IsLocked: false,
        EmailVerifiedAt: null,
        CreatedAt: DateTimeOffset.UnixEpoch);

    private sealed class StubOrders : IAdminOrderService
    {
        public Task<AdminOrdersSummaryDto> GetOrdersAsync(
            string? q = null, string? status = null, string? gateway = null, int? userId = null,
            int page = 1, int pageSize = 20, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AdminOrdersSummaryDto(
                [
                    new AdminOrderDto(
                        OrderId: 101,
                        OrderCode: "DH-1001",
                        PaymentNo: 1,
                        UserId: 15,
                        UserEmail: "learner@demo.local",
                        UserFullName: "Nguyễn Văn A",
                        Credits: 5,
                        Amount: 199_000m,
                        Status: OrderStatusDto.Paid,
                        Gateway: PaymentGatewayDto.Payos,
                        GatewayTxnRef: null,
                        GatewayErrorCode: null,
                        CreatedAt: DateTimeOffset.UnixEpoch,
                        PaidAt: DateTimeOffset.UnixEpoch),
                ],
                1, 1, 20, 1, 199_000m, 0, 0));

        public Task<AdminOrderDetailDto?> GetOrderDetailAsync(
            int orderId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The lookup must not read a payment event log.");

        public Task<PaymentResultDto> ReconcileOrderAsync(
            int orderId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The lookup must not reconcile an order.");
    }

    private sealed class StubRefunds : IRefundService
    {
        public Task<AdminRefundsSummaryDto> GetAdminRefundsAsync(
            string? q = null, string? status = null, int page = 1, int pageSize = 20,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This test reads only the desk's lookups.");

        public Task<AdminRefundDetailDto?> GetAdminRefundDetailAsync(
            int refundId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This test reads only the desk's lookups.");

        public Task<AdminRefundDetailDto> RequestRefundAsync(
            int staffUserId, RequestRefundRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This test reads only the desk's lookups.");

        public Task<AdminRefundDetailDto> RequestCreditRefundAsync(
            int staffUserId, int learnerUserId, int creditCount, string reason,
            int? orderId = null, int? complaintId = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This test reads only the desk's lookups.");

        public Task<AdminRefundDetailDto> ApproveRefundAsync(
            int staffUserId, int refundId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This test reads only the desk's lookups.");

        public Task<AdminRefundDetailDto> RejectRefundAsync(
            int staffUserId, int refundId, string reason,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This test reads only the desk's lookups.");

        public Task<AdminRefundDetailDto> SettleRefundAsync(
            int staffUserId, int refundId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This test reads only the desk's lookups.");

        public Task<IReadOnlyList<RefundExportRow>> GetRefundExportRowsAsync(
            DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This test reads only the desk's lookups.");
    }
}
