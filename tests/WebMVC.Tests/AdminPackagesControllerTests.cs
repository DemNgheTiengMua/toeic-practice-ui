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

namespace WebMVC.Tests;

public sealed class AdminPackagesControllerTests
{
    private static AdminPackagesSummaryDto SampleSummary() => new(
        Packages:
        [
            new(1, "Pay as you go", 1, 49000, true, false, "Feature 1", 1, OrdersSold: 412, TotalRevenue: 20200000),
            new(2, "Exam Prep", 5, 199000, true, true, "Feature 2", 2, OrdersSold: 286, TotalRevenue: 56900000),
            new(3, "Intensive", 10, 349000, false, false, "Feature 3", 3, OrdersSold: 94, TotalRevenue: 32800000),
        ],
        PackagesOnSale: 2,
        FeaturedCount: 1,
        TotalOrdersSold: 792,
        TotalRevenue: 109900000);

    private static AdminPackagesController CreateController(TestApiClient api)
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
        authProperties.SetParameter("jwt", "admin-token");

        services.AddSingleton<IAuthenticationService>(new FixedAuthentication(principal, authProperties));
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { User = principal, RequestServices = serviceProvider };
        var currentUser = new CurrentUser(new HttpContextAccessor { HttpContext = httpContext });

        return new AdminPackagesController(api, currentUser, NullLogger<AdminPackagesController>.Instance)
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
    public async Task Index_returns_view_with_summary()
    {
        var api = new TestApiClient { Summary = SampleSummary() };
        var controller = CreateController(api);

        var result = await controller.Index(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<AdminPackagesViewModel>(viewResult.Model).Summary;
        Assert.Equal(3, model.Packages.Count);
        Assert.Equal(2, model.PackagesOnSale);
        Assert.Equal(1, model.FeaturedCount);
        Assert.Equal(792, model.TotalOrdersSold);
    }

    [Fact]
    public void Create_GET_returns_form_view_with_default_model()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var result = controller.Create();

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<PackageFormViewModel>(viewResult.Model);
        Assert.True(model.IsActive);
        Assert.Equal(1, model.Credits);
    }

    [Fact]
    public async Task Create_POST_with_valid_model_calls_api_and_redirects()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var formModel = new PackageFormViewModel
        {
            Name = "New Package",
            Credits = 5,
            Price = 199000,
            IsActive = true,
            IsFeatured = true,
            Features = "Bullets",
            DisplayOrder = 1,
        };

        var result = await controller.Create(formModel, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminPackagesController.Index), redirect.ActionName);
        Assert.NotNull(api.CreatedRequest);
        Assert.Equal("New Package", api.CreatedRequest.Name);
        Assert.True(api.CreatedRequest.IsFeatured);
        Assert.Equal("Package 'New Package' created successfully.", controller.TempData["Success"]);
    }

    [Fact]
    public async Task Create_POST_with_invalid_model_redisplays_form()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);
        controller.ModelState.AddModelError("Name", "Name is required");

        var formModel = new PackageFormViewModel();
        var result = await controller.Create(formModel, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Same(formModel, viewResult.Model);
        Assert.Null(api.CreatedRequest);
    }

    [Fact]
    public async Task Edit_GET_loads_package_into_form_view()
    {
        var api = new TestApiClient
        {
            PackageToReturn = new PackageDto(2, "Exam Prep", 5, 199000, true, true, "Features", 2),
        };
        var controller = CreateController(api);

        var result = await controller.Edit(2, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<PackageFormViewModel>(viewResult.Model);
        Assert.Equal(2, model.PackageId);
        Assert.Equal("Exam Prep", model.Name);
        Assert.Equal(5, model.Credits);
        Assert.True(model.IsFeatured);
    }

    [Fact]
    public async Task Edit_POST_with_valid_model_calls_api_and_redirects()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var formModel = new PackageFormViewModel
        {
            Name = "Exam Prep Updated",
            Credits = 6,
            Price = 219000,
            IsActive = true,
            IsFeatured = true,
            DisplayOrder = 2,
        };

        var result = await controller.Edit(2, formModel, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminPackagesController.Index), redirect.ActionName);
        Assert.Equal(2, api.UpdatedPackageId);
        Assert.NotNull(api.UpdatedRequest);
        Assert.Equal("Exam Prep Updated", api.UpdatedRequest.Name);
        Assert.Equal("Package 'Exam Prep Updated' updated successfully.", controller.TempData["Success"]);
    }

    [Fact]
    public async Task Deactivate_POST_calls_api_and_redirects()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var result = await controller.Deactivate(5, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminPackagesController.Index), redirect.ActionName);
        Assert.Equal(5, api.DeactivatedPackageId);
        Assert.Equal("Package deactivated.", controller.TempData["Success"]);
    }

    [Fact]
    public async Task ToggleFeatured_POST_toggles_featured_and_redirects()
    {
        var api = new TestApiClient
        {
            PackageToReturn = new PackageDto(3, "Intensive", 10, 349000, true, false, null, 3),
        };
        var controller = CreateController(api);

        var result = await controller.ToggleFeatured(3, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminPackagesController.Index), redirect.ActionName);
        Assert.NotNull(api.UpdatedRequest);
        Assert.True(api.UpdatedRequest.IsFeatured);
        Assert.Equal("Package 'Intensive' marked as featured.", controller.TempData["Success"]);
    }

    [Fact]
    public async Task ToggleFeatured_POST_refuses_to_feature_inactive_package()
    {
        var api = new TestApiClient
        {
            PackageToReturn = new PackageDto(3, "Deactivated Pkg", 10, 349000, IsActive: false, IsFeatured: false, null, 3),
        };
        var controller = CreateController(api);

        var result = await controller.ToggleFeatured(3, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminPackagesController.Index), redirect.ActionName);
        Assert.Null(api.UpdatedRequest); // Must NOT update or feature
        Assert.Equal("Cannot feature an inactive package. Activate it first.", controller.TempData["Error"]);
    }

    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class TestApiClient : IAdminPackagesClient
    {
        public AdminPackagesSummaryDto? Summary { get; set; }
        public PackageDto? PackageToReturn { get; set; }
        public CreatePackageRequest? CreatedRequest { get; set; }
        public int? UpdatedPackageId { get; set; }
        public UpdatePackageRequest? UpdatedRequest { get; set; }
        public int? DeactivatedPackageId { get; set; }

        public Task<AdminPackagesSummaryDto> GetAdminPackagesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Summary ?? new AdminPackagesSummaryDto([], 0, 0, 0, 0));

        public Task<PackageDto> GetAdminPackageAsync(int packageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(PackageToReturn ?? new PackageDto(packageId, "Test", 1, 10000, true, false, null, 0));

        public Task<PackageDto> CreatePackageAsync(CreatePackageRequest request, CancellationToken cancellationToken = default)
        {
            CreatedRequest = request;
            return Task.FromResult(new PackageDto(99, request.Name, request.Credits, request.Price, request.IsActive, request.IsFeatured, request.Features, request.DisplayOrder));
        }

        public Task<PackageDto> UpdatePackageAsync(int packageId, UpdatePackageRequest request, CancellationToken cancellationToken = default)
        {
            UpdatedPackageId = packageId;
            UpdatedRequest = request;
            return Task.FromResult(new PackageDto(packageId, request.Name, request.Credits, request.Price, request.IsActive, request.IsFeatured, request.Features, request.DisplayOrder));
        }

        public Task DeactivatePackageAsync(int packageId, CancellationToken cancellationToken = default)
        {
            DeactivatedPackageId = packageId;
            return Task.CompletedTask;
        }
    }
}
