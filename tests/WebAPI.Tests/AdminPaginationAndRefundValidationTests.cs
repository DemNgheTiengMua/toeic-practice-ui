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

public sealed class AdminPaginationAndRefundValidationTests
{
    [Theory]
    [InlineData("/api/admin/exams?page=0", "page")]
    [InlineData("/api/admin/exams?page=-1", "page")]
    [InlineData("/api/admin/exams?pageSize=0", "pageSize")]
    [InlineData("/api/admin/exams?pageSize=-5", "pageSize")]
    [InlineData("/api/admin/exams?pageSize=101", "pageSize")]
    [InlineData("/api/admin/orders?page=0", "page")]
    [InlineData("/api/admin/orders?pageSize=101", "pageSize")]
    [InlineData("/api/admin/users?page=-2", "page")]
    [InlineData("/api/admin/users?pageSize=250", "pageSize")]
    [InlineData("/api/admin/complaints?page=0", "page")]
    [InlineData("/api/admin/complaints?pageSize=101", "pageSize")]
    [InlineData("/api/admin/refunds?page=0", "page")]
    [InlineData("/api/admin/refunds?pageSize=101", "pageSize")]
    public async Task Paginated_admin_routes_reject_invalid_page_or_pageSize_with_400(string url, string expectedField)
    {
        var tracker = new CallTracker();
        var (client, app) = await AdminAppAsync(tracker);
        await using (app)
        {
            var response = await client.GetAsync(url);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(tracker.AnyServiceCalled, $"RequestValidationFilter should short-circuit {url} before calling the service.");
            Assert.True(json.TryGetProperty("errors", out var errors));
            Assert.True(errors.TryGetProperty(expectedField, out var fieldErrors));
            Assert.True(fieldErrors.GetArrayLength() > 0);
        }
    }

    [Theory]
    [InlineData("/api/admin/exams")]
    [InlineData("/api/admin/exams?page=1&pageSize=100")]
    [InlineData("/api/admin/orders?page=2&pageSize=20")]
    [InlineData("/api/admin/users?page=1&pageSize=1")]
    [InlineData("/api/admin/complaints?page=1&pageSize=50")]
    [InlineData("/api/admin/refunds?page=1&pageSize=100")]
    public async Task Paginated_admin_routes_accept_valid_pagination_and_invoke_service(string url)
    {
        var tracker = new CallTracker();
        var (client, app) = await AdminAppAsync(tracker);
        await using (app)
        {
            var response = await client.GetAsync(url);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(tracker.AnyServiceCalled);
        }
    }

    [Fact]
    public async Task Admin_orders_route_forwards_the_learner_filter_to_the_service()
    {
        // #349: the refund desk lists one learner's paid orders through this route, so `userId` has to
        // survive binding and reach the service. A route that silently dropped it would answer with
        // every learner's orders, which is how a refund gets recorded against a stranger.
        var orders = new CapturingAdminOrderService();
        var (client, app) = await AdminAppAsync(new CallTracker(), orders);
        await using (app)
        {
            var response = await client.GetAsync("/api/admin/orders?userId=42&status=paid&page=1&pageSize=20");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(42, orders.LastUserId);
            Assert.Equal("paid", orders.LastStatus);
        }
    }

    [Fact]
    public async Task Admin_orders_route_omits_the_learner_filter_when_it_is_absent()
    {
        var orders = new CapturingAdminOrderService();
        var (client, app) = await AdminAppAsync(new CallTracker(), orders);
        await using (app)
        {
            var response = await client.GetAsync("/api/admin/orders");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Null(orders.LastUserId);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Post_refund_rejects_empty_reason_with_400_and_field_error(string badReason)    {
        var tracker = new CallTracker();
        var (client, app) = await AdminAppAsync(tracker);
        await using (app)
        {
            var response = await client.PostAsJsonAsync(
                "/api/admin/refunds",
                new RequestRefundRequest(42, RefundMethodDto.Credit, 5, badReason));
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(tracker.AnyServiceCalled);
            Assert.True(json.TryGetProperty("errors", out var errors));
            Assert.True(errors.TryGetProperty("reason", out var reasonErrors));
            Assert.True(reasonErrors.GetArrayLength() > 0);
        }
    }

    [Fact]
    public async Task Post_refund_rejects_reason_exceeding_400_chars_with_400_and_field_error()
    {
        var tracker = new CallTracker();
        var (client, app) = await AdminAppAsync(tracker);
        await using (app)
        {
            var tooLong = new string('a', 401);
            var response = await client.PostAsJsonAsync(
                "/api/admin/refunds",
                new RequestRefundRequest(42, RefundMethodDto.Credit, 5, tooLong));
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(tracker.AnyServiceCalled);
            Assert.True(json.TryGetProperty("errors", out var errors));
            Assert.True(errors.TryGetProperty("reason", out var reasonErrors));
            Assert.True(reasonErrors.GetArrayLength() > 0);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Post_reject_refund_rejects_empty_reason_with_400_and_field_error(string badReason)
    {
        var tracker = new CallTracker();
        var (client, app) = await AdminAppAsync(tracker);
        await using (app)
        {
            var response = await client.PostAsJsonAsync(
                "/api/admin/refunds/10/reject",
                new RejectRefundRequest(badReason));
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(tracker.AnyServiceCalled);
            Assert.True(json.TryGetProperty("errors", out var errors));
            Assert.True(errors.TryGetProperty("reason", out var reasonErrors));
            Assert.True(reasonErrors.GetArrayLength() > 0);
        }
    }

    [Fact]
    public async Task Post_reject_refund_rejects_reason_exceeding_400_chars_with_400_and_field_error()
    {
        var tracker = new CallTracker();
        var (client, app) = await AdminAppAsync(tracker);
        await using (app)
        {
            var tooLong = new string('b', 401);
            var response = await client.PostAsJsonAsync(
                "/api/admin/refunds/10/reject",
                new RejectRefundRequest(tooLong));
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(tracker.AnyServiceCalled);
            Assert.True(json.TryGetProperty("errors", out var errors));
            Assert.True(errors.TryGetProperty("reason", out var reasonErrors));
            Assert.True(reasonErrors.GetArrayLength() > 0);
        }
    }

    private static async Task<(HttpClient Client, WebApplication App)> AdminAppAsync(
        CallTracker tracker,
        IAdminOrderService? orders = null,
        IComplaintService? complaints = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, "http://127.0.0.1:0");

        builder.Services.AddSingleton(CreateTrackingStub<IAdminExamService>(tracker));
        builder.Services.AddSingleton(CreateTrackingStub<IQuestionImportService>(tracker));
        builder.Services.AddSingleton(orders ?? CreateTrackingStub<IAdminOrderService>(tracker));
        builder.Services.AddSingleton(CreateTrackingStub<IAdminUserService>(tracker));
        builder.Services.AddSingleton(complaints ?? CreateTrackingStub<IComplaintService>(tracker));
        builder.Services.AddSingleton(CreateTrackingStub<IRefundService>(tracker));

        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, AllowAdmin>("Test", null);
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(Permissions.SystemAdmin, p => p.RequireAuthenticatedUser())
            .AddPolicy(Permissions.ContentAuthor, p => p.RequireAuthenticatedUser())
            .AddPolicy(Permissions.SupportTriage, p => p.RequireAuthenticatedUser());

        var app = builder.Build();
        app.UseApiProblemDetails();
        app.UseAuthentication();
        app.UseAuthorization();

        var adminSystem = app.MapGroup("/api/admin").RequireAuthorization(Permissions.SystemAdmin);
        var adminAuthor = app.MapGroup("/api/admin").RequireAuthorization(Permissions.ContentAuthor);
        typeof(AdminEndpoints).GetMethod("MapExams", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [adminAuthor]);
        typeof(AdminEndpoints).GetMethod("MapOrders", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [adminSystem]);
        typeof(AdminEndpoints).GetMethod("MapUsers", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [adminSystem]);

        var complaintsAdmin = app.MapGroup("/api/admin/complaints").RequireAuthorization(Permissions.SupportTriage);
        typeof(ComplaintEndpoints).GetMethod("MapAdmin", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [complaintsAdmin]);

        app.MapRefundEndpoints();

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        return (new HttpClient { BaseAddress = new Uri(address) }, app);
    }

    private sealed class AllowAdmin : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public AllowAdmin(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [new Claim(IAccessTokenFactory.ClaimUserId, "1")],
                "Test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }

    /// <summary>
    /// #350: the resolve route must carry the chosen ending through unchanged. The important half is
    /// the refund arm: it reaches the complaints route only, so a promise can never arrive at the
    /// refunds route as a created refund.
    /// </summary>
    [Theory]
    [InlineData("none", 0)]
    [InlineData("make_good", 1)]
    [InlineData("refund", 0)]
    public async Task Post_resolve_forwards_the_chosen_compensation_ending_and_never_creates_a_refund(
        string compensation, int grantCredits)
    {
        var tracker = new CallTracker();
        var complaints = new CapturingComplaintService();
        var (client, app) = await AdminAppAsync(tracker, complaints: complaints);
        await using (app)
        {
            var response = await client.PostAsJsonAsync(
                "/api/admin/complaints/301/resolve",
                new
                {
                    resolution = "Verified and answered.",
                    internalNotes = "Checked the room log.",
                    compensation,
                    grantCredits,
                });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(complaints.LastRequest);
            Assert.Equal(compensation, complaints.LastRequest!.Compensation.ToWireCode());
            Assert.Equal(grantCredits, complaints.LastRequest.GrantCredits);

            // The refund desk was not called: recording a promise moves no money.
            Assert.DoesNotContain(tracker.Calls, call => call.Service == typeof(IRefundService));
        }
    }

    /// <summary>Records what the route bound and forwarded, so the assertion is about the route rather
    /// than about a stub that reimplements it.</summary>
    private sealed class CapturingComplaintService : IComplaintService
    {
        public ResolveComplaintRequest? LastRequest { get; private set; }

        public Task<AdminComplaintDetailDto> ResolveComplaintAsync(
            int adminUserId,
            int complaintId,
            ResolveComplaintRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new AdminComplaintDetailDto(
                complaintId, 15, "Learner", "learner@test.local",
                ComplaintTypeDto.MentorNoShow, "Reason", ComplaintStatusDto.Reviewing,
                "Booking", 55, "Booking #55", null, DateTimeOffset.UtcNow,
                Resolution: null, InternalNotes: null, ResolvedByUserId: null,
                ResolvedByUserName: null, ResolvedAt: null, ResolvedByGrantTxnId: null,
                GrantedCredits: null));
        }

        public Task<IReadOnlyList<LearnerComplaintDto>> GetLearnerComplaintsAsync(
            int userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<EligibleSubjectsDto> GetEligibleSubjectsAsync(
            int userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LearnerComplaintDto> FileComplaintAsync(
            int userId, FileComplaintRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdminComplaintsSummaryDto> GetAdminComplaintsAsync(
            string? q = null, string? status = null, int page = 1, int pageSize = 20,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdminComplaintDetailDto?> GetAdminComplaintDetailAsync(
            int complaintId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdminComplaintDetailDto> SetComplaintStatusAsync(
            int adminUserId, int complaintId, ComplaintStatus status,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    public sealed class CallTracker
    {
        public bool AnyServiceCalled { get; set; }

        /// <summary>The services the proxy saw called, so a test can prove one route was untouched.</summary>
        public List<(Type Service, string Method)> Calls { get; } = [];
    }

    /// <summary>Records what the route bound and forwarded, so the assertion is about the route rather
    /// than about a stub that reimplements it.</summary>
    private sealed class CapturingAdminOrderService : IAdminOrderService
    {
        public int? LastUserId { get; private set; }

        public string? LastStatus { get; private set; }

        public Task<AdminOrdersSummaryDto> GetOrdersAsync(
            string? q = null,
            string? status = null,
            string? gateway = null,
            int? userId = null,
            int page = 1,
            int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            LastUserId = userId;
            LastStatus = status;
            return Task.FromResult(new AdminOrdersSummaryDto([], 0, page, pageSize, 0, 0m, 0, 0));
        }

        public Task<AdminOrderDetailDto?> GetOrderDetailAsync(int orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AdminOrderDetailDto?>(null);

        public Task<PaymentResultDto> ReconcileOrderAsync(int orderId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static T CreateTrackingStub<T>(CallTracker tracker) where T : class
    {
        var proxy = DispatchProxy.Create<T, RecordingProxy<T>>();
        var recording = (RecordingProxy<T>)(object)proxy;
        recording.Tracker = tracker;
        recording.ServiceType = typeof(T);
        return proxy;
    }

    public class RecordingProxy<T> : DispatchProxy
    {
        internal CallTracker? Tracker { get; set; }

        internal Type? ServiceType { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (Tracker is not null)
            {
                Tracker.AnyServiceCalled = true;
                if (ServiceType is not null && targetMethod is not null)
                {
                    Tracker.Calls.Add((ServiceType, targetMethod.Name));
                }
            }

            var returnType = targetMethod!.ReturnType;
            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var resultType = returnType.GetGenericArguments()[0];
                var fromResult = typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(resultType);
                return fromResult.Invoke(null, [null]);
            }

            return Task.CompletedTask;
        }
    }
}
