using System.Security.Claims;
using Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WebMVC.Controllers;

namespace WebMVC.Tests;

public sealed class PricingControllerTests
{
    private static IReadOnlyList<PackageDto> SamplePackages() =>
    [
        new(1, "Pay as you go", 1, 49000, true, false, "1 full-test credit\nReview answers\nNever expires", 1),
        new(2, "Exam Prep", 5, 199000, true, true, "5 full-test credits\nNever expires", 2),
        new(3, "Intensive", 10, 349000, true, false, "10 full-test credits\nNever expires", 3),
    ];

    private static PricingController CreateController(
        TestApiClient api,
        bool isSignedIn = false,
        string role = Roles.Learner)
    {
        var services = new ServiceCollection();
        services.AddRouting();
        services.AddMvcCore();
        var claims = new List<Claim>();
        if (isSignedIn)
        {
            claims.Add(new Claim(IAccessTokenFactory.ClaimUserId, "42"));
            claims.Add(new Claim(IAccessTokenFactory.ClaimEmail, "learner@demo.local"));
            claims.Add(new Claim(IAccessTokenFactory.ClaimRole, role));
        }

        var identity = isSignedIn ? new ClaimsIdentity(claims, "Cookies") : new ClaimsIdentity();
        var principal = new ClaimsPrincipal(identity);
        var authProperties = new AuthenticationProperties(new Dictionary<string, string?> { ["jwt"] = "test-jwt-token" });

        services.AddSingleton<IAuthenticationService>(new FixedAuthentication(principal, authProperties));
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { User = principal, RequestServices = serviceProvider };
        var currentUser = new CurrentUser(new HttpContextAccessor { HttpContext = httpContext });

        return new PricingController(api, currentUser, NullLogger<PricingController>.Instance, api)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                RouteData = new Microsoft.AspNetCore.Routing.RouteData(),
            },
            TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(httpContext, new EmptyTempDataProvider()),
        };
    }

    private sealed class EmptyTempDataProvider : Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    [Fact]
    public async Task Index_returns_view_with_active_packages()
    {
        var api = new TestApiClient
        {
            PublicPackages = SamplePackages(),
        };

        var controller = CreateController(api);
        var result = await controller.Index(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<IReadOnlyList<PackageDto>>(viewResult.Model);
        Assert.Equal(3, model.Count);
        Assert.Equal("Exam Prep", model.Single(p => p.IsFeatured).Name);
    }

    [Fact]
    public async Task Index_returns_unavailable_view_on_api_error()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new HttpRequestException("Service unavailable"),
        };

        var controller = CreateController(api);
        var result = await controller.Index(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("PricingUnavailable", viewResult.ViewName);
    }

    [Fact]
    public async Task Index_sets_learner_shell_when_learner_is_signed_in()
    {
        var api = new TestApiClient
        {
            PublicPackages = SamplePackages(),
            LearnerContext = new LearnerContextDto(
                new CertificateOptionDto("TOEIC", "TOEIC"),
                [new CertificateOptionDto("TOEIC", "TOEIC")],
                CreditBalance: 8),
        };

        var controller = CreateController(api, isSignedIn: true, role: Roles.Learner);
        var result = await controller.Index(CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        var shell = LearnerShell.Get(controller.HttpContext);
        Assert.NotNull(shell);
        Assert.Equal(8, shell.CreditBalance);
    }

    private sealed class TestApiClient : IOrdersClient, ILearnerContextClient
    {
        public Exception? ExceptionToThrow { get; set; }
        public IReadOnlyList<PackageDto>? PublicPackages { get; set; }
        public LearnerContextDto? LearnerContext { get; set; }

        public Task<IReadOnlyList<PackageDto>> GetPublicPackagesAsync(CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is not null) throw ExceptionToThrow;
            return Task.FromResult(PublicPackages ?? []);
        }

        public Task<CheckoutPreviewDto> GetCheckoutPreviewAsync(int packageId, CancellationToken cancellationToken = default) => Task.FromResult<CheckoutPreviewDto>(null!);
        public Task<CreateOrderResponse> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken = default) => Task.FromResult<CreateOrderResponse>(null!);
        public Task CancelOrderAsync(CancelOrderRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<PaymentResultDto> HandleReturnAsync(HandleReturnRequest request, CancellationToken cancellationToken = default) => Task.FromResult<PaymentResultDto>(null!);
        public Task<PaymentResultDto> GetPaymentResultAsync(string orderCode, CancellationToken cancellationToken = default) => Task.FromResult<PaymentResultDto>(null!);
        public Task<PaymentResultDto> ApplyMockGatewayActionAsync(MockGatewayActionRequest request, CancellationToken cancellationToken = default) => Task.FromResult<PaymentResultDto>(null!);

        public Task<LearnerContextDto> GetLearnerContextAsync(CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is not null) throw ExceptionToThrow;
            return Task.FromResult(LearnerContext ?? new LearnerContextDto(new CertificateOptionDto("", ""), [], 0));
        }

        public Task SwitchCertificateAsync(string code, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LearnerDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default) => Task.FromResult<LearnerDashboardDto>(null!);
        public Task<AiInsightDto> GetDashboardInsightAsync(int? certId = null, CancellationToken cancellationToken = default) => Task.FromResult<AiInsightDto>(null!);
        public Task RetryDashboardInsightAsync(int? certId = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
