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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Post_refund_rejects_empty_reason_with_400_and_field_error(string badReason)
    {
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

    private static async Task<(HttpClient Client, WebApplication App)> AdminAppAsync(CallTracker tracker)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, "http://127.0.0.1:0");

        builder.Services.AddSingleton(CreateTrackingStub<IAdminExamService>(tracker));
        builder.Services.AddSingleton(CreateTrackingStub<IQuestionImportService>(tracker));
        builder.Services.AddSingleton(CreateTrackingStub<IAdminOrderService>(tracker));
        builder.Services.AddSingleton(CreateTrackingStub<IAdminUserService>(tracker));
        builder.Services.AddSingleton(CreateTrackingStub<IComplaintService>(tracker));
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

    public sealed class CallTracker
    {
        public bool AnyServiceCalled { get; set; }
    }

    private static T CreateTrackingStub<T>(CallTracker tracker) where T : class
    {
        var proxy = DispatchProxy.Create<T, RecordingProxy<T>>();
        ((RecordingProxy<T>)(object)proxy).Tracker = tracker;
        return proxy;
    }

    public class RecordingProxy<T> : DispatchProxy
    {
        internal CallTracker? Tracker { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (Tracker is not null)
            {
                Tracker.AnyServiceCalled = true;
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
