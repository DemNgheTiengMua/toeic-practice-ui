using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WebMVC.Controllers;
using Xunit;

namespace WebMVC.Tests;

public sealed class AdminImportsControllerTests
{
    private static AdminExamsController CreateExamsController(
        AdminExamsControllerTests.TestApiClient examsApi,
        TestImportsClient? importsApi = null)
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

        var importsClient = importsApi ?? new TestImportsClient();

        return new AdminExamsController(examsApi, currentUser, NullLogger<AdminExamsController>.Instance, importsClient)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                RouteData = new Microsoft.AspNetCore.Routing.RouteData(),
            },
            TempData = new TempDataDictionary(httpContext, new AdminExamsControllerTests.EmptyTempDataProvider()),
        };
    }

    private static AdminImportsController CreateImportsController(TestImportsClient importsApi)
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

        return new AdminImportsController(importsApi, currentUser, NullLogger<AdminImportsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                RouteData = new Microsoft.AspNetCore.Routing.RouteData(),
            },
            TempData = new TempDataDictionary(httpContext, new AdminExamsControllerTests.EmptyTempDataProvider()),
        };
    }

    [Fact]
    public void AdminLayout_Sidebar_MustNotContain_QuestionImportNavigation()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var layoutPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Shared", "_AdminLayout.cshtml");
        Assert.True(File.Exists(layoutPath), "_AdminLayout.cshtml must exist");

        var content = File.ReadAllText(layoutPath);
        Assert.DoesNotContain("/admin/imports", content);
        Assert.DoesNotContain("Question import", content);
    }

    [Fact]
    public void ImportsView_MustBeRetired()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var importsViewPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Admin", "Imports.cshtml");
        Assert.False(File.Exists(importsViewPath), "Imports.cshtml should be retired in favor of integrated exam package import on /admin/exams");
    }

    [Fact]
    public void ExamsView_MustContain_ImportExamButtonAndModal()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var examsViewPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Admin", "Exams.cshtml");
        Assert.True(File.Exists(examsViewPath), "Exams.cshtml must exist");

        var content = File.ReadAllText(examsViewPath);
        Assert.Contains("Import Exam (.zip / .json)", content);
        Assert.Contains("id=\"import-exam-dialog\"", content);
        Assert.Contains("class=\"modal-overlay\"", content);
        Assert.Contains("action=\"/admin/exams/import\"", content);
        Assert.Contains("name=\"certId\"", content);
        Assert.Contains("accept=\".json,.zip,application/json,application/zip\"", content);
        Assert.Contains("class=\"modal__close\"", content);
    }

    [Fact]
    public void AdminImportsController_Index_RedirectsToAdminExams()
    {
        var importsApi = new TestImportsClient();
        var controller = CreateImportsController(importsApi);

        var result = controller.Index(certId: 2);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("AdminExams", redirect.ControllerName);
        Assert.Equal(2, redirect.RouteValues?["certId"]);
        Assert.Equal(true, redirect.RouteValues?["import"]);
    }

    [Fact]
    public async Task AdminExamsController_Import_WhenDone_RedirectsToExamsWithSuccess()
    {
        var examsApi = new AdminExamsControllerTests.TestApiClient();
        var importsApi = new TestImportsClient
        {
            Result = new AdminImportResultDto(
                BatchId: 42,
                Status: ImportBatchStatusDto.Done,
                ExamId: 10,
                ExamName: "ETS 2024 Test 1",
                QuestionCount: 200,
                ErrorCount: 0,
                WarningCount: 0)
        };
        var controller = CreateExamsController(examsApi, importsApi);

        var file = CreateMockFormFile("manifest.json", "{}");
        var result = await controller.Import(certId: 1, file, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal(1, redirect.RouteValues?["certId"]);
        Assert.Equal(1, importsApi.UploadedCertId);
        Assert.Equal("manifest.json", importsApi.UploadedFileName);
        Assert.Contains("Imported 'ETS 2024 Test 1' as a draft exam with 200 questions.", controller.TempData["Success"]?.ToString());
    }

    [Fact]
    public async Task AdminExamsController_Import_WhenValidationErrors_RedirectsToExamsWithError()
    {
        var examsApi = new AdminExamsControllerTests.TestApiClient();
        var importsApi = new TestImportsClient
        {
            Result = new AdminImportResultDto(
                BatchId: 43,
                Status: ImportBatchStatusDto.Failed,
                ExamId: null,
                ExamName: null,
                QuestionCount: 0,
                ErrorCount: 3,
                WarningCount: 1)
        };
        var controller = CreateExamsController(examsApi, importsApi);

        var file = CreateMockFormFile("broken.json", "{ bad }");
        var result = await controller.Import(certId: 2, file, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal(2, redirect.RouteValues?["certId"]);
        Assert.Contains("Nothing was imported: 3 problems found in manifest.", controller.TempData["Error"]?.ToString());
    }

    [Fact]
    public async Task AdminExamsController_Import_WhenCertIdMissing_RedirectsWithError()
    {
        var examsApi = new AdminExamsControllerTests.TestApiClient();
        var importsApi = new TestImportsClient();
        var controller = CreateExamsController(examsApi, importsApi);

        var file = CreateMockFormFile("test.json", "{}");
        var result = await controller.Import(certId: null, file, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Choose the certificate the questions belong to.", controller.TempData["Error"]?.ToString());
    }

    [Fact]
    public async Task AdminExamsController_Import_WhenFileMissingOrEmpty_RedirectsWithError()
    {
        var examsApi = new AdminExamsControllerTests.TestApiClient();
        var importsApi = new TestImportsClient();
        var controller = CreateExamsController(examsApi, importsApi);

        var result = await controller.Import(certId: 1, file: null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Choose a .json manifest or a .zip package to import.", controller.TempData["Error"]?.ToString());
    }

    [Fact]
    public async Task AdminImportsController_Upload_RedirectsToAdminExamsWithSuccess()
    {
        var importsApi = new TestImportsClient
        {
            Result = new AdminImportResultDto(
                BatchId: 44,
                Status: ImportBatchStatusDto.Done,
                ExamId: 11,
                ExamName: "IELTS Academic Test 2",
                QuestionCount: 40,
                ErrorCount: 0,
                WarningCount: 0)
        };
        var controller = CreateImportsController(importsApi);

        var file = CreateMockFormFile("ielts.zip", "PK...");
        var result = await controller.Upload(certId: 2, file, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("AdminExams", redirect.ControllerName);
        Assert.Equal(2, redirect.RouteValues?["certId"]);
        Assert.Contains("Imported 'IELTS Academic Test 2' as a draft exam with 40 questions.", controller.TempData["Success"]?.ToString());
    }

    private static IFormFile CreateMockFormFile(string fileName, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", fileName);
    }

    private sealed class TestImportsClient : IAdminImportsClient
    {
        public AdminImportResultDto Result { get; set; } = new(1, ImportBatchStatusDto.Done, 1, "Test", 10, 0, 0);
        public int? UploadedCertId { get; private set; }
        public string? UploadedFileName { get; private set; }

        public Task<AdminImportBatchesDto> GetAdminImportBatchesAsync(int? certId, CancellationToken cancellationToken = default)
            => Task.FromResult(new AdminImportBatchesDto([], []));

        public Task<AdminImportBatchDetailDto> GetAdminImportBatchAsync(int batchId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<AdminImportResultDto> UploadAdminImportAsync(int certId, Stream content, string fileName, CancellationToken cancellationToken = default)
        {
            UploadedCertId = certId;
            UploadedFileName = fileName;
            return Task.FromResult(Result);
        }
    }
}
