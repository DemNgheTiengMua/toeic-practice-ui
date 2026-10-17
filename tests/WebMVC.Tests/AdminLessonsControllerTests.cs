using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
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
using WebMVC.Models;
using Xunit;

namespace WebMVC.Tests;

public sealed class AdminLessonsControllerTests
{
    private static AdminCertificateOptionDto[] SampleCertificates() =>
    [
        new(1, "toeic", "TOEIC"),
        new(2, "ielts", "IELTS")
    ];

    private static AdminLessonSummaryDto SampleLessonSummary(int id = 1, string title = "Grammar 101") => new(
        LessonId: id,
        CertId: 1,
        CertCode: "toeic",
        SkillId: 2,
        SkillCode: "reading",
        SkillName: "Reading",
        CategoryId: 10,
        CategoryCode: "GRAMMAR",
        CategoryName: "Grammar",
        Title: title,
        Summary: "Summary of grammar 101",
        EstimatedMinutes: 10,
        DisplayOrder: 1,
        CardCount: 3,
        CreatedAt: DateTimeOffset.UtcNow);

    private static AdminLessonDetailDto SampleLessonDetail(int id = 1, string title = "Grammar 101") => new(
        LessonId: id,
        CertId: 1,
        CertCode: "toeic",
        SkillId: 2,
        SkillCode: "reading",
        CategoryId: 10,
        CategoryCode: "GRAMMAR",
        Title: title,
        Summary: "Summary of grammar 101",
        EstimatedMinutes: 10,
        DisplayOrder: 1,
        CreatedAt: DateTimeOffset.UtcNow,
        Cards:
        [
            new(101, "rule", "Subject-Verb Agreement", "Always match singular subjects with singular verbs.", null, null, null, null, null, null, null, 0),
            new(102, "example", "Example Sentences", "The cat walks vs The cats walk.", null, null, null, null, null, null, null, 1),
            new(103, "micro_check", "Quick Check", "", "Choose the correct sentence", "The cat run.", "The cat runs.", "The cats runs.", "The cat running.", "B", "Singular subject takes runs.", 2, "Subject is singular.")
        ]);

    private static LessonManifestDto SampleManifest(int id = 1) => new(
        Lessons:
        [
            new(
                Title: "Grammar 101",
                Skill: "reading",
                CategoryCode: "GRAMMAR",
                Summary: "Summary of grammar 101",
                EstimatedMinutes: 10,
                DisplayOrder: 1,
                Cards:
                [
                    new("rule", "Rule 1", "Markdown body", null, null, null, null, null, null, null, null)
                ])
        ]);

    private static AdminLessonsController CreateController(TestApiClient api, TestImportsClient? importsClient = null)
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

        var imports = importsClient ?? new TestImportsClient
        {
            Batches = new AdminImportBatchesDto(Array.Empty<AdminImportBatchListItemDto>(), SampleCertificates())
        };

        return new AdminLessonsController(api, imports, currentUser, NullLogger<AdminLessonsController>.Instance)
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
    public async Task Index_ReturnsViewWithLessonsAndCertificates()
    {
        var api = new TestApiClient
        {
            Lessons = [SampleLessonSummary(1, "Lesson 1"), SampleLessonSummary(2, "Lesson 2")]
        };
        var controller = CreateController(api);

        var result = await controller.Index(certId: 1, skillId: 2, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/Admin/Lessons/Index.cshtml", viewResult.ViewName);

        var vm = Assert.IsType<AdminLessonListViewModel>(viewResult.Model);
        Assert.Equal(2, vm.Lessons.Count);
        Assert.Equal(1, vm.SelectedCertId);
        Assert.Equal(2, vm.SelectedSkillId);
        Assert.Equal(2, vm.Certificates.Count);
    }

    [Fact]
    public async Task Index_WhenApiThrows_SetsErrorMessageInTempData()
    {
        var api = new TestApiClient
        {
            GetLessonsException = new Exception("Backend network error")
        };
        var controller = CreateController(api);

        var result = await controller.Index(null, null, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<AdminLessonListViewModel>(viewResult.Model);
        Assert.Empty(vm.Lessons);
        Assert.NotNull(controller.TempData["Error"]);
    }

    [Fact]
    public async Task Create_ReturnsViewWithDefaultCardAndCertificates()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var result = await controller.Create(certId: 2, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/Admin/Lessons/Edit.cshtml", viewResult.ViewName);

        var vm = Assert.IsType<AdminLessonEditViewModel>(viewResult.Model);
        Assert.Equal(0, vm.LessonId);
        Assert.Equal(2, vm.CertId);
        Assert.Equal(5, vm.EstimatedMinutes);
        Assert.Single(vm.Cards);
        Assert.Equal("rule", vm.Cards[0].CardType);
        Assert.Equal(2, vm.Certificates.Count);
    }

    [Fact]
    public async Task Edit_WhenLessonFound_ReturnsViewWithMappedCards()
    {
        var api = new TestApiClient
        {
            LessonDetail = SampleLessonDetail(1, "Advanced Grammar")
        };
        var controller = CreateController(api);

        var result = await controller.Edit(1, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/Admin/Lessons/Edit.cshtml", viewResult.ViewName);

        var vm = Assert.IsType<AdminLessonEditViewModel>(viewResult.Model);
        Assert.Equal(1, vm.LessonId);
        Assert.Equal("Advanced Grammar", vm.Title);
        Assert.Equal(3, vm.Cards.Count);
        Assert.Equal("rule", vm.Cards[0].CardType);
        Assert.Equal("example", vm.Cards[1].CardType);
        Assert.Equal("micro_check", vm.Cards[2].CardType);
        Assert.Equal("B", vm.Cards[2].CorrectOption);
        Assert.Equal("Subject is singular.", vm.Cards[2].Rationale);
    }

    [Fact]
    public async Task Edit_WhenLessonNotFound_RedirectsToIndexWithError()
    {
        var api = new TestApiClient
        {
            GetLessonDetailException = new ApiException("NOT_FOUND", "Not found", 404, "Lesson #99 not found")
        };
        var controller = CreateController(api);

        var result = await controller.Edit(99, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminLessonsController.Index), redirect.ActionName);
        Assert.Contains("99", controller.TempData["Error"]?.ToString());
    }

    [Fact]
    public async Task Save_WhenNoCards_ReturnsEditViewWithValidationError()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var vm = new AdminLessonEditViewModel
        {
            LessonId = 0,
            CertId = 1,
            Title = "No Cards Lesson",
            Cards = []
        };

        var result = await controller.Save(vm, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/Admin/Lessons/Edit.cshtml", viewResult.ViewName);
        Assert.False(controller.ModelState.IsValid);
        Assert.Null(api.CreatedRequest);
    }

    [Fact]
    public async Task Save_CreateLesson_CallsApiAndRedirects()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var vm = new AdminLessonEditViewModel
        {
            LessonId = 0,
            CertId = 1,
            SkillId = 2,
            CategoryId = 5,
            Title = "Brand New Lesson",
            Summary = "Lesson summary",
            EstimatedMinutes = 8,
            DisplayOrder = 1,
            Cards =
            [
                new AdminLessonCardEditViewModel
                {
                    CardType = "rule",
                    Title = "Rule 1",
                    ContentMarkdown = "Markdown explanation",
                    DisplayOrder = 0
                }
            ]
        };

        var result = await controller.Save(vm, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminLessonsController.Index), redirect.ActionName);
        Assert.Equal(1, redirect.RouteValues?["certId"]);
        Assert.NotNull(api.CreatedRequest);
        Assert.Equal("Brand New Lesson", api.CreatedRequest.Title);
        Assert.Single(api.CreatedRequest.Cards);
        Assert.Contains("created successfully", controller.TempData["Success"]?.ToString());
    }

    [Fact]
    public async Task Save_UpdateLesson_CallsApiAndRedirects()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var vm = new AdminLessonEditViewModel
        {
            LessonId = 42,
            CertId = 1,
            SkillId = 2,
            Title = "Updated Lesson Title",
            Cards =
            [
                new AdminLessonCardEditViewModel
                {
                    CardId = 101,
                    CardType = "example",
                    Title = "Example 1",
                    ContentMarkdown = "Sample content",
                    DisplayOrder = 0
                }
            ]
        };

        var result = await controller.Save(vm, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminLessonsController.Index), redirect.ActionName);
        Assert.Equal(42, api.UpdatedLessonId);
        Assert.NotNull(api.UpdatedRequest);
        Assert.Equal("Updated Lesson Title", api.UpdatedRequest.Title);
        Assert.Contains("updated successfully", controller.TempData["Success"]?.ToString());
    }

    [Fact]
    public async Task Save_WhenApiExceptionOccurs_ReturnsViewWithErrorMessage()
    {
        var api = new TestApiClient
        {
            SaveException = new ApiException("INVALID_PAYLOAD", "Payload invalid", 400, "Duplicate card titles")
        };
        var controller = CreateController(api);

        var vm = new AdminLessonEditViewModel
        {
            LessonId = 0,
            CertId = 1,
            Title = "Bad Lesson",
            Cards =
            [
                new AdminLessonCardEditViewModel
                {
                    CardType = "rule",
                    Title = "Duplicate",
                    DisplayOrder = 0
                }
            ]
        };

        var result = await controller.Save(vm, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/Admin/Lessons/Edit.cshtml", viewResult.ViewName);
        var resultVm = Assert.IsType<AdminLessonEditViewModel>(viewResult.Model);
        Assert.Equal("Duplicate card titles", resultVm.ErrorMessage);
        Assert.NotEmpty(resultVm.Certificates);
    }

    [Fact]
    public async Task Delete_CallsApiAndRedirectsToIndex()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var result = await controller.Delete(15, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminLessonsController.Index), redirect.ActionName);
        Assert.Equal(15, api.DeletedLessonId);
        Assert.Contains("deleted successfully", controller.TempData["Success"]?.ToString());
    }

    [Fact]
    public async Task Delete_WhenApiExceptionOccurs_SetsTempDataError()
    {
        var api = new TestApiClient
        {
            DeleteException = new ApiException("LESSON_IN_USE", "Cannot delete", 400, "Lesson is assigned to a learning path")
        };
        var controller = CreateController(api);

        var result = await controller.Delete(15, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminLessonsController.Index), redirect.ActionName);
        Assert.Equal("Lesson is assigned to a learning path", controller.TempData["Error"]);
    }

    [Fact]
    public async Task Upload_WhenFileNullOrEmpty_RedirectsWithError()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var result = await controller.Upload(certId: 1, overwrite: false, file: null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminLessonsController.Index), redirect.ActionName);
        Assert.Equal(1, redirect.RouteValues?["certId"]);
        Assert.NotNull(controller.TempData["Error"]);
    }

    [Fact]
    public async Task Upload_WhenValidFile_CallsApiAndSetsSuccessMessage()
    {
        var api = new TestApiClient
        {
            ImportResult = new AdminLessonImportResultDto(
                CreatedCount: 2,
                UpdatedCount: 0,
                TotalCardsImported: 6,
                Messages: [])
        };
        var controller = CreateController(api);

        var bytes = Encoding.UTF8.GetBytes("{ \"lessons\": [] }");
        var fakeFile = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "lessons.json");

        var result = await controller.Upload(certId: 1, overwrite: true, file: fakeFile, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminLessonsController.Index), redirect.ActionName);
        Assert.Equal(1, redirect.RouteValues?["certId"]);
        Assert.Equal(1, api.UploadedCertId);
        Assert.True(api.UploadedOverwrite);
        Assert.Equal("lessons.json", api.UploadedFileName);
        Assert.Contains("Import completed: 2 created, 0 updated (6 cards total)", controller.TempData["Success"]?.ToString());
    }

    [Fact]
    public async Task Upload_WhenApiExceptionOccurs_SetsTempDataError()
    {
        var api = new TestApiClient
        {
            UploadException = new ApiException("INVALID_FORMAT", "Invalid manifest format", 400, "Missing title in lesson")
        };
        var controller = CreateController(api);

        var bytes = Encoding.UTF8.GetBytes("invalid");
        var fakeFile = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "test.json");

        var result = await controller.Upload(certId: 1, overwrite: false, file: fakeFile, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminLessonsController.Index), redirect.ActionName);
        Assert.Equal("Missing title in lesson", controller.TempData["Error"]);
    }

    [Fact]
    public async Task Export_ReturnsJsonFileResult()
    {
        var api = new TestApiClient
        {
            ExportManifest = SampleManifest(42)
        };
        var controller = CreateController(api);

        var result = await controller.Export(42, CancellationToken.None);

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/json", fileResult.ContentType);
        Assert.Equal("lesson-42.json", fileResult.FileDownloadName);

        var json = Encoding.UTF8.GetString(fileResult.FileContents);
        Assert.Contains("Grammar 101", json);
    }

    [Fact]
    public async Task Template_ReturnsJsonFileResult()
    {
        var api = new TestApiClient
        {
            TemplateManifest = SampleManifest(0)
        };
        var controller = CreateController(api);

        var result = await controller.Template(CancellationToken.None);

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/json", fileResult.ContentType);
        Assert.Equal("lesson-template.json", fileResult.FileDownloadName);

        var json = Encoding.UTF8.GetString(fileResult.FileContents);
        Assert.Contains("Grammar 101", json);
    }

    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext? context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext? context, IDictionary<string, object> values) { }
    }

    private sealed class TestImportsClient : IAdminImportsClient
    {
        public AdminImportBatchesDto Batches { get; set; } = new(Array.Empty<AdminImportBatchListItemDto>(), Array.Empty<AdminCertificateOptionDto>());

        public Task<AdminImportBatchesDto> GetAdminImportBatchesAsync(int? certId, CancellationToken cancellationToken = default)
            => Task.FromResult(Batches);

        public Task<AdminImportBatchDetailDto> GetAdminImportBatchAsync(int batchId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<AdminImportResultDto> UploadAdminImportAsync(int certId, Stream content, string fileName, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class TestApiClient : IAdminLessonsClient
    {
        public IReadOnlyList<AdminLessonSummaryDto> Lessons { get; set; } = Array.Empty<AdminLessonSummaryDto>();
        public Exception? GetLessonsException { get; set; }

        public AdminLessonDetailDto? LessonDetail { get; set; }
        public Exception? GetLessonDetailException { get; set; }

        public AdminLessonUpsertDto? CreatedRequest { get; private set; }
        public int? UpdatedLessonId { get; private set; }
        public AdminLessonUpsertDto? UpdatedRequest { get; private set; }
        public Exception? SaveException { get; set; }

        public int? DeletedLessonId { get; private set; }
        public Exception? DeleteException { get; set; }

        public int? UploadedCertId { get; private set; }
        public bool? UploadedOverwrite { get; private set; }
        public string? UploadedFileName { get; private set; }
        public AdminLessonImportResultDto ImportResult { get; set; } = new(0, 0, 0, []);
        public Exception? UploadException { get; set; }

        public LessonManifestDto ExportManifest { get; set; } = SampleManifest(1);
        public LessonManifestDto TemplateManifest { get; set; } = SampleManifest(0);

        public Task<IReadOnlyList<AdminLessonSummaryDto>> GetAdminLessonsAsync(int? certId = null, int? skillId = null, CancellationToken cancellationToken = default)
        {
            if (GetLessonsException != null) throw GetLessonsException;
            return Task.FromResult(Lessons);
        }

        public Task<AdminLessonDetailDto> GetAdminLessonByIdAsync(int lessonId, CancellationToken cancellationToken = default)
        {
            if (GetLessonDetailException != null) throw GetLessonDetailException;
            return Task.FromResult(LessonDetail ?? SampleLessonDetail(lessonId));
        }

        public Task<AdminLessonDetailDto> CreateAdminLessonAsync(AdminLessonUpsertDto request, CancellationToken cancellationToken = default)
        {
            if (SaveException != null) throw SaveException;
            CreatedRequest = request;
            return Task.FromResult(SampleLessonDetail(99, request.Title));
        }

        public Task<AdminLessonDetailDto> UpdateAdminLessonAsync(int lessonId, AdminLessonUpsertDto request, CancellationToken cancellationToken = default)
        {
            if (SaveException != null) throw SaveException;
            UpdatedLessonId = lessonId;
            UpdatedRequest = request;
            return Task.FromResult(SampleLessonDetail(lessonId, request.Title));
        }

        public Task DeleteAdminLessonAsync(int lessonId, CancellationToken cancellationToken = default)
        {
            if (DeleteException != null) throw DeleteException;
            DeletedLessonId = lessonId;
            return Task.CompletedTask;
        }

        public Task<AdminLessonImportResultDto> UploadAdminLessonImportAsync(int certId, bool overwrite, Stream content, string fileName, CancellationToken cancellationToken = default)
        {
            if (UploadException != null) throw UploadException;
            UploadedCertId = certId;
            UploadedOverwrite = overwrite;
            UploadedFileName = fileName;
            return Task.FromResult(ImportResult);
        }

        public Task<LessonManifestDto> ExportAdminLessonAsync(int lessonId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ExportManifest);
        }

        public Task<LessonManifestDto> GetAdminLessonTemplateAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(TemplateManifest);
        }
    }
}
