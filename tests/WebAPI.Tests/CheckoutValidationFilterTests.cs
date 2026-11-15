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
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Services;
using WebAPI.Endpoints;
using WebAPI.Extensions;
using Xunit;

namespace WebAPI.Tests;

public sealed class CheckoutValidationFilterTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-50)]
    [InlineData(1001)]
    [InlineData(int.MaxValue)]
    public async Task Post_orders_rejects_out_of_range_credits_with_400_and_field_error(int badCredits)
    {
        var stub = new StubPaymentService();
        var (client, app) = await CheckoutAppAsync(stub);
        await using (app)
        {
            var response = await client.PostAsJsonAsync("/api/checkout/orders", new CreateOrderRequest(badCredits));
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(stub.CreateOrderCalled, "RequestValidationFilter should short-circuit before calling IPaymentService.");
            Assert.True(json.TryGetProperty("errors", out var errors));
            Assert.True(errors.TryGetProperty("credits", out var creditErrors));
            Assert.True(creditErrors.GetArrayLength() > 0);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(1000)]
    public async Task Post_orders_accepts_valid_credits_and_invokes_service(int validCredits)
    {
        var stub = new StubPaymentService();
        var (client, app) = await CheckoutAppAsync(stub);
        await using (app)
        {
            var response = await client.PostAsJsonAsync("/api/checkout/orders", new CreateOrderRequest(validCredits));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(stub.CreateOrderCalled);
            Assert.Equal(validCredits, stub.LastCredits);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public async Task Get_preview_rejects_out_of_range_credits_with_400_and_field_error(int badCredits)
    {
        var stub = new StubPaymentService();
        var (client, app) = await CheckoutAppAsync(stub);
        await using (app)
        {
            var response = await client.GetAsync($"/api/checkout/preview?credits={badCredits}");
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(stub.PreviewCalled, "Validation filter should short-circuit before calling IPaymentService.");
            Assert.True(json.TryGetProperty("errors", out var errors));
            Assert.True(errors.TryGetProperty("credits", out var creditErrors));
            Assert.True(creditErrors.GetArrayLength() > 0);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1000)]
    public async Task Get_preview_accepts_valid_credits_and_invokes_service(int validCredits)
    {
        var stub = new StubPaymentService();
        var (client, app) = await CheckoutAppAsync(stub);
        await using (app)
        {
            var response = await client.GetAsync($"/api/checkout/preview?credits={validCredits}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(stub.PreviewCalled);
            Assert.Equal(validCredits, stub.LastCredits);
        }
    }

    [Fact]
    public async Task Post_orders_through_real_host_pipeline_returns_400_with_validation_code()
    {
        using var factory = new CheckoutSite();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/checkout/orders", new CreateOrderRequest(0));
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(json.TryGetProperty("code", out var code), $"Expected 'code' in response: {json}");
        Assert.Equal(ApiError.Codes.Validation, code.GetString());
    }

    private static async Task<(HttpClient Client, WebApplication App)> CheckoutAppAsync(IPaymentService paymentService)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, "http://127.0.0.1:0");
        builder.Services.AddSingleton(paymentService);
        builder.Services.AddSingleton(new AuthLinkOptions { PublicBaseUrl = "http://localhost:5000" });
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, AllowLearner>("Test", null);
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(Roles.Learner, p => p.RequireRole(Roles.Learner));

        var app = builder.Build();
        app.UseApiProblemDetails();
        app.UseAuthentication();
        app.UseAuthorization();

        typeof(CheckoutEndpoints)
            .GetMethod("MapOrders", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [app]);

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        return (new HttpClient { BaseAddress = new Uri(address) }, app);
    }

    private sealed class CheckoutSite : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(new byte[32]));
            builder.UseSetting("Jwt:Issuer", "webapi-tests");
            builder.UseSetting("Jwt:Audience", "webapi-tests");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IPaymentService>(new StubPaymentService());
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, AllowLearner>("Test", null);
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = "Test";
                    options.DefaultChallengeScheme = "Test";
                });
            });
        }
    }

    private sealed class AllowLearner : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public AllowLearner(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [new Claim(IAccessTokenFactory.ClaimUserId, "42"), new Claim(ClaimTypes.Role, Roles.Learner)],
                "Test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }

    private sealed class StubPaymentService : IPaymentService
    {
        public bool PreviewCalled { get; private set; }
        public bool CreateOrderCalled { get; private set; }
        public int LastCredits { get; private set; }

        public Task<CheckoutPreviewDto> GetCheckoutPreviewAsync(int userId, int credits, CancellationToken cancellationToken = default)
        {
            PreviewCalled = true;
            LastCredits = credits;
            return Task.FromResult(new CheckoutPreviewDto(credits, credits * 20000m, 10, 10 + credits, null));
        }

        public Task<CreateOrderResponse> CreateOrderAsync(int userId, int credits, string baseUrl, CancellationToken cancellationToken = default)
        {
            CreateOrderCalled = true;
            LastCredits = credits;
            return Task.FromResult(new CreateOrderResponse(1, "DH-1", 1001, credits * 20000m, PaymentGatewayDto.Mock, "http://localhost:5000/mock", OrderStatusDto.Pending));
        }

        public Task<PaymentResultDto> HandleReturnRedirectAsync(int userId, string orderCode, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task CancelOrderAsync(int userId, string orderCode, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PaymentResultDto> GetPaymentResultAsync(int userId, string orderCode, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> SettleOrderAsync(long paymentNo, PaymentEventSource source, bool signatureValid, string? rawPayload = null, string? note = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WebhookVerificationResult> HandleWebhookAsync(string payload, string? signature, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WalletDto> GetWalletAsync(int userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ExpireStaleOrdersAsync(int olderThanMinutes = 15, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PaymentResultDto> ApplyMockGatewayActionAsync(int userId, string orderCode, string action, string? errorCode = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
