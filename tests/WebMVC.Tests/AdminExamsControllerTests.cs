using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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

public sealed class AdminExamsControllerTests
{
    private static AdminExamListSummaryDto SampleSummary() => new(
        Exams:
        [
            new(1, 1, "toeic", "TOEIC", "ETS 2024 — Test 1", ExamStatusDto.Published, ExamPurposeDto.Mock, 120, 200, 200, "4 parts complete", 612, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "ETS 2024"),
            new(2, 1, "toeic", "TOEIC", "ETS 2024 — Test 6", ExamStatusDto.Draft, ExamPurposeDto.Mock, 120, 200, 186, "Part 3 missing", 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "ETS 2024"),
        ],
        TotalCount: 2,
        Page: 1,
        PageSize: 20,
        Certificates:
        [
            new(1, "toeic", "TOEIC"),
            new(2, "ielts", "IELTS")
        ]);

    private static AdminExamDetailDto SampleDetail(int examId = 1) => new(
        ExamId: examId,
        CertId: 1,
        CertCode: "toeic",
        CertName: "TOEIC",
        Name: "ETS 2024 — Test 1",
        Status: ExamStatusDto.Draft,
        DurationMinutes: 120,
        Purpose: ExamPurposeDto.Mock,
        Description: "Sample description",
        SourceName: "ETS 2024",
        InternalNote: "Internal admin notes",
        QuestionCount: 200,
        ActualQuestionCount: 200,
        FinishedAttemptCount: 0,
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: null,
        Audios:
        [
            new(examId, 1, "P1", "Listening Part 1", "audio/part1.mp3", 192, 2900000, DateTimeOffset.UtcNow)
        ],
        SectionProgress:
        [
            new(1, "P1", "Listening Part 1", ModalityDto.Listening, 6, 6, true, true, "Complete"),
            new(2, "P5", "Reading Part 5", ModalityDto.Reading, 30, 30, true, true, "Complete")
        ]);

    private static AdminExamQuestionsDto SampleQuestions(int examId = 1) => new(
        ExamId: examId,
        ExamName: "ETS 2024 — Test 1",
        CertId: 1,
        CertCode: "toeic",
        Groups:
        [
            new(1, examId, 1, 1, "P1", "Photos", ModalityDto.Listening, null, "audio/p1.mp3", 0, 5000, "img/p1.jpg", "An office desk", null, null, null, 1,
            [
                new(101, 1, examId, 1, "P1", "Photos", 1, "P1", "What is on the desk?", 2, QuestionTypeDto.Mcq, "Explanation text", null, null, null, null, null, null, null, 1, DateTimeOffset.UtcNow,
                [
                    new(1001, 101, "A", "A laptop", true, "Correct rationale"),
                    new(1002, 101, "B", "A cat", false, "Wrong rationale")
                ],
                HasAnswers: false)
            ])
        ],
        AvailableSections:
        [
            new(1, "P1", "Listening Part 1", ModalityDto.Listening, 6, 1),
            new(2, "P5", "Reading Part 5", ModalityDto.Reading, 30, 2)
        ],
        AvailableCategories:
        [
            new(1, "GRAMMAR", "Grammar"),
            new(2, "VOCAB", "Vocabulary")
        ]);

    private static AdminExamsController CreateController(TestApiClient api)
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

        return new AdminExamsController(api, currentUser, NullLogger<AdminExamsController>.Instance)
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
    public async Task Index_ReturnsViewWithExamListAndSearchFilter()
    {
        var api = new TestApiClient { Summary = SampleSummary() };
        var controller = CreateController(api);

        var result = await controller.Index(q: "ETS 2024", status: "published", certId: 1, page: 1, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/Admin/Exams.cshtml", viewResult.ViewName);
        var vm = Assert.IsType<AdminExamListViewModel>(viewResult.Model);
        Assert.Equal("ETS 2024", vm.Query);
        Assert.Equal("published", vm.Status);
        Assert.Equal(2, vm.Summary.Exams.Count);
        Assert.Equal("ETS 2024 — Test 1", vm.Summary.Exams[0].Name);
    }

    [Fact]
    public async Task Create_Get_ReturnsViewWithCertificates()
    {
        var api = new TestApiClient { Summary = SampleSummary() };
        var controller = CreateController(api);

        var result = await controller.Create(certId: 1, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/Admin/ExamEditor.cshtml", viewResult.ViewName);
        var vm = Assert.IsType<ExamFormViewModel>(viewResult.Model);
        Assert.Equal(1, vm.CertId);
        Assert.Equal(1, vm.Step);
        Assert.False(vm.ExamId.HasValue);
    }

    [Fact]
    public async Task Create_Post_Success_RedirectsToEditStep2()
    {
        var api = new TestApiClient { DetailToReturn = SampleDetail(42) };
        var controller = CreateController(api);

        var model = new ExamFormViewModel
        {
            CertId = 1,
            Name = "New Practice Exam",
            DurationMinutes = 120,
            Purpose = ExamPurposeDto.Mock,
            Status = ExamStatusDto.Draft,
            QuestionCount = 200,
            SourceName = "ETS 2024"
        };

        var result = await controller.Create(model, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminExamsController.Edit), redirect.ActionName);
        Assert.Equal(42, redirect.RouteValues?["id"]);
        Assert.Equal(2, redirect.RouteValues?["step"]);
        Assert.NotNull(api.CreatedExamRequest);
        Assert.Equal("New Practice Exam", api.CreatedExamRequest.Name);
    }

    [Fact]
    public async Task Create_Post_ApiException_ReturnsViewWithErrorMessage()
    {
        var api = new TestApiClient
        {
            CreateExamException = new ApiException(ApiError.Codes.Validation, "Duplicate exam name", 400, "An exam with this name already exists for this certificate.")
        };
        var controller = CreateController(api);

        var model = new ExamFormViewModel
        {
            CertId = 1,
            Name = "Existing Name",
            DurationMinutes = 120
        };

        var result = await controller.Create(model, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/Admin/ExamEditor.cshtml", viewResult.ViewName);
        var vm = Assert.IsType<ExamFormViewModel>(viewResult.Model);
        Assert.Equal("An exam with this name already exists for this certificate.", vm.ErrorMessage);
    }

    [Fact]
    public async Task Edit_Get_ReturnsViewWithExamDetailAndProgress()
    {
        var api = new TestApiClient { DetailToReturn = SampleDetail(7) };
        var controller = CreateController(api);

        var result = await controller.Edit(id: 7, step: 3, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/Admin/ExamEditor.cshtml", viewResult.ViewName);
        var vm = Assert.IsType<ExamFormViewModel>(viewResult.Model);
        Assert.Equal(7, vm.ExamId);
        Assert.Equal(3, vm.Step);
        Assert.NotNull(vm.Detail);
        Assert.Equal(2, vm.Detail.SectionProgress.Count);
    }

    [Fact]
    public async Task Edit_Post_Success_RedirectsToNextStep()
    {
        var api = new TestApiClient { DetailToReturn = SampleDetail(7) };
        var controller = CreateController(api);

        var model = new ExamFormViewModel
        {
            ExamId = 7,
            CertId = 1,
            Name = "Updated Name",
            DurationMinutes = 120,
            Purpose = ExamPurposeDto.Mock,
            Status = ExamStatusDto.Draft,
            Step = 1
        };

        var result = await controller.Edit(7, model, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminExamsController.Edit), redirect.ActionName);
        Assert.Equal(7, redirect.RouteValues?["id"]);
        Assert.Equal(2, redirect.RouteValues?["step"]);
        Assert.NotNull(api.UpdatedExamRequest);
        Assert.Equal("Updated Name", api.UpdatedExamRequest.Name);
    }

    [Fact]
    public async Task Publish_Success_RedirectsToIndexWithSuccessMessage()
    {
        var api = new TestApiClient
        {
            PublishResultToReturn = new AdminPublishExamResultDto(true, "Exam published successfully.", [])
        };
        var controller = CreateController(api);

        var result = await controller.Publish(12, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminExamsController.Index), redirect.ActionName);
        Assert.Equal("Exam published successfully.", controller.TempData["Success"]);
    }

    [Fact]
    public async Task Publish_ApiException_RedirectsToStep3WithErrorMessage()
    {
        var api = new TestApiClient
        {
            PublishException = new ApiException(ApiError.Codes.ExamIncomplete, "Incomplete exam", 400, "Missing audio for listening section 'Part 1'.")
        };
        var controller = CreateController(api);

        var result = await controller.Publish(12, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminExamsController.Edit), redirect.ActionName);
        Assert.Equal(12, redirect.RouteValues?["id"]);
        Assert.Equal(3, redirect.RouteValues?["step"]);
        Assert.Equal("Missing audio for listening section 'Part 1'.", controller.TempData["Error"]);
    }

    [Fact]
    public async Task Questions_Get_ReturnsViewWithQuestionsData()
    {
        var api = new TestApiClient { QuestionsToReturn = SampleQuestions(1) };
        var controller = CreateController(api);

        var result = await controller.Questions(1, sectionId: 1, questionId: 101, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/Admin/QuestionEditor.cshtml", viewResult.ViewName);
        var vm = Assert.IsType<QuestionEditorViewModel>(viewResult.Model);
        Assert.Equal(1, vm.QuestionsData.ExamId);
        Assert.Equal(1, vm.SelectedSectionId);
        Assert.Equal(101, vm.SelectedQuestionId);
        Assert.Single(vm.QuestionsData.Groups);
    }

    [Fact]
    public async Task SaveQuestion_Create_Success_RedirectsToQuestions()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var model = new QuestionSaveModel
        {
            GroupId = 1,
            SectionId = 1,
            Stem = "New Question Stem",
            DifficultyLevel = 3,
            QuestionType = QuestionTypeDto.Mcq,
            DisplayOrder = 1,
            CorrectOptionLabel = "B",
            Options =
            [
                new QuestionOptionInputModel { Label = "A", Text = "Opt A", IsCorrect = false },
                new QuestionOptionInputModel { Label = "B", Text = "Opt B", IsCorrect = true }
            ]
        };

        var result = await controller.SaveQuestion(5, model, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminExamsController.Questions), redirect.ActionName);
        Assert.Equal(5, redirect.RouteValues?["id"]);
        Assert.NotNull(api.CreatedQuestionRequest);
        Assert.Equal("New Question Stem", api.CreatedQuestionRequest.Stem);
        Assert.Single(api.CreatedQuestionRequest.Options!, o => o.IsCorrect && o.Label == "B");
    }

    [Fact]
    public async Task SaveQuestion_Update_Success_RedirectsToQuestions()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var model = new QuestionSaveModel
        {
            QuestionId = 101,
            GroupId = 1,
            SectionId = 1,
            Stem = "Updated Stem",
            DifficultyLevel = 4,
            QuestionType = QuestionTypeDto.Mcq,
            DisplayOrder = 1,
            CorrectOptionLabel = "A",
            Options =
            [
                new QuestionOptionInputModel { Label = "A", Text = "Opt A", IsCorrect = true },
                new QuestionOptionInputModel { Label = "B", Text = "Opt B", IsCorrect = false }
            ]
        };

        var result = await controller.SaveQuestion(5, model, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminExamsController.Questions), redirect.ActionName);
        Assert.Equal(5, redirect.RouteValues?["id"]);
        Assert.Equal(101, redirect.RouteValues?["questionId"]);
        Assert.NotNull(api.UpdatedQuestionRequest);
        Assert.Equal("Updated Stem", api.UpdatedQuestionRequest.Stem);
    }

    [Fact]
    public async Task DeleteQuestion_CallsApi_RedirectsToQuestions()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var result = await controller.DeleteQuestion(5, 101, sectionId: 1, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminExamsController.Questions), redirect.ActionName);
        Assert.Equal(101, api.DeletedQuestionId);
    }

    [Fact]
    public async Task DeleteExam_CallsApi_RedirectsToIndex()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var result = await controller.Delete(5, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminExamsController.Index), redirect.ActionName);
        Assert.Equal(5, api.DeletedExamId);
    }

    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class TestApiClient : IAdminExamsClient
    {
        public AdminExamListSummaryDto? Summary { get; set; }
        public AdminExamDetailDto? DetailToReturn { get; set; }
        public AdminPublishExamResultDto? PublishResultToReturn { get; set; }
        public AdminExamQuestionsDto? QuestionsToReturn { get; set; }

        public CreateAdminExamRequest? CreatedExamRequest { get; set; }
        public UpdateAdminExamRequest? UpdatedExamRequest { get; set; }
        public CreateQuestionRequest? CreatedQuestionRequest { get; set; }
        public UpdateQuestionRequest? UpdatedQuestionRequest { get; set; }
        public int? DeletedExamId { get; set; }
        public int? DeletedQuestionId { get; set; }

        public ApiException? CreateExamException { get; set; }
        public ApiException? PublishException { get; set; }

        public Task<AdminExamListSummaryDto> GetAdminExamsAsync(string? q = null, string? status = null, int? certId = null, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default) =>
            Task.FromResult(Summary ?? SampleSummary());

        public Task<AdminExamDetailDto> GetAdminExamByIdAsync(int examId, CancellationToken cancellationToken = default) =>
            Task.FromResult(DetailToReturn ?? SampleDetail(examId));

        public Task<AdminExamDetailDto> CreateAdminExamAsync(CreateAdminExamRequest request, CancellationToken cancellationToken = default)
        {
            if (CreateExamException != null) throw CreateExamException;
            CreatedExamRequest = request;
            return Task.FromResult(DetailToReturn ?? SampleDetail(42));
        }

        public Task<AdminExamDetailDto> UpdateAdminExamAsync(int examId, UpdateAdminExamRequest request, CancellationToken cancellationToken = default)
        {
            UpdatedExamRequest = request;
            return Task.FromResult(DetailToReturn ?? SampleDetail(examId));
        }

        public Task<AdminPublishExamResultDto> PublishAdminExamAsync(int examId, CancellationToken cancellationToken = default)
        {
            if (PublishException != null) throw PublishException;
            return Task.FromResult(PublishResultToReturn ?? new AdminPublishExamResultDto(true, "Exam published successfully.", []));
        }

        public Task DeleteAdminExamAsync(int examId, CancellationToken cancellationToken = default)
        {
            DeletedExamId = examId;
            return Task.CompletedTask;
        }

        public Task<AdminExamQuestionsDto> GetAdminExamQuestionsAsync(int examId, CancellationToken cancellationToken = default) =>
            Task.FromResult(QuestionsToReturn ?? SampleQuestions(examId));

        public Task<AdminQuestionGroupDto> CreateAdminQuestionGroupAsync(int examId, CreateQuestionGroupRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<AdminQuestionGroupDto>(null!);

        public Task<AdminQuestionGroupDto> UpdateAdminQuestionGroupAsync(int examId, int groupId, UpdateQuestionGroupRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<AdminQuestionGroupDto>(null!);

        public Task DeleteAdminQuestionGroupAsync(int examId, int groupId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<AdminQuestionDto> CreateAdminQuestionAsync(int examId, CreateQuestionRequest request, CancellationToken cancellationToken = default)
        {
            CreatedQuestionRequest = request;
            return Task.FromResult(new AdminQuestionDto(
                999,
                request.GroupId,
                examId,
                1,
                "P1",
                "Photos",
                1,
                "P1",
                request.Stem,
                request.DifficultyLevel,
                request.QuestionType,
                request.Explanation,
                request.CategoryId,
                null,
                request.PrepSeconds,
                request.ResponseSeconds,
                request.MinWords,
                request.MaxWords,
                request.Hint,
                request.DisplayOrder,
                DateTimeOffset.UtcNow,
                request.Options?.Select((o, i) => new AdminQuestionOptionDto(i + 1, 999, o.Label, o.Text, o.IsCorrect, o.Rationale)).ToList() ?? [],
                HasAnswers: false));
        }

        public Task<AdminQuestionDto> UpdateAdminQuestionAsync(int questionId, UpdateQuestionRequest request, CancellationToken cancellationToken = default)
        {
            UpdatedQuestionRequest = request;
            return Task.FromResult(new AdminQuestionDto(
                questionId,
                request.GroupId ?? 1,
                1,
                1,
                "P1",
                "Photos",
                1,
                "P1",
                request.Stem,
                request.DifficultyLevel,
                request.QuestionType,
                request.Explanation,
                request.CategoryId,
                null,
                request.PrepSeconds,
                request.ResponseSeconds,
                request.MinWords,
                request.MaxWords,
                request.Hint,
                request.DisplayOrder,
                DateTimeOffset.UtcNow,
                request.Options?.Select((o, i) => new AdminQuestionOptionDto(i + 1, questionId, o.Label, o.Text, o.IsCorrect, o.Rationale)).ToList() ?? [],
                HasAnswers: false));
        }

        public Task DeleteAdminQuestionAsync(int questionId, CancellationToken cancellationToken = default)
        {
            DeletedQuestionId = questionId;
            return Task.CompletedTask;
        }

        public Task<AdminExamSectionAudioDto> SaveAdminExamSectionAudioAsync(int examId, SaveExamSectionAudioRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<AdminExamSectionAudioDto>(null!);
    }
}
