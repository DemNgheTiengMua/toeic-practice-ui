using System;
using System.Collections.Generic;
using System.IO;
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

    private static AdminExamListSummaryDto SampleSummaryWithVariousPurposes() => new(
        Exams:
        [
            new(1, 1, "toeic", "TOEIC", "ETS 2024 — Test 1", ExamStatusDto.Published, ExamPurposeDto.Mock, 120, 200, 200, "4 parts complete", 612, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "ETS 2024"),
            new(2, 1, "toeic", "TOEIC", "ETS 2024 — Test 6", ExamStatusDto.Draft, ExamPurposeDto.Mock, 120, 200, 186, "Part 3 missing", 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "ETS 2024"),
            new(3, 1, "toeic", "TOEIC", "TOEIC Placement Diagnostic", ExamStatusDto.Published, ExamPurposeDto.Placement, 45, 50, 50, "N/A", 15, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Official"),
            new(4, 2, "ielts", "IELTS", "IELTS Academic Writing Task 1", ExamStatusDto.Published, ExamPurposeDto.WritingPractice, 60, 2, 2, "N/A", 5, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Cambridge"),
            new(5, 2, "ielts", "IELTS", "IELTS Speaking Interview", ExamStatusDto.Published, ExamPurposeDto.SpeakingPractice, 15, 3, 3, "No audio", 2, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Cambridge"),
        ],
        TotalCount: 5,
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

        var result = await controller.Index(q: "ETS 2024", status: "published", certId: 1, purpose: null, page: 1, cancellationToken: CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/Admin/Exams.cshtml", viewResult.ViewName);
        var vm = Assert.IsType<AdminExamListViewModel>(viewResult.Model);
        Assert.Equal("ETS 2024", vm.Query);
        Assert.Equal("published", vm.Status);
        Assert.Equal(2, vm.Summary.Exams.Count);
        Assert.Equal("ETS 2024 — Test 1", vm.Summary.Exams[0].Name);
    }

    [Fact]
    public async Task Index_FiltersByPurpose_Placement_PassesPurposeToApiAndViewModel()
    {
        var api = new TestApiClient { Summary = SampleSummaryWithVariousPurposes() };
        var controller = CreateController(api);

        var result = await controller.Index(q: null, status: null, certId: null, purpose: "placement", page: 1, cancellationToken: CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<AdminExamListViewModel>(viewResult.Model);
        Assert.Equal("placement", vm.Purpose);
        Assert.Equal("placement", api.LastPurposeFilter);
    }

    [Fact]
    public async Task Index_FiltersByPurpose_Practice_PassesPurposeToApiAndViewModel()
    {
        var api = new TestApiClient { Summary = SampleSummaryWithVariousPurposes() };
        var controller = CreateController(api);

        var result = await controller.Index(q: null, status: null, certId: null, purpose: "practice", page: 1, cancellationToken: CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<AdminExamListViewModel>(viewResult.Model);
        Assert.Equal("practice", vm.Purpose);
        Assert.Equal("practice", api.LastPurposeFilter);
    }

    [Fact]
    public async Task Index_FiltersByPurpose_Mock_PassesPurposeToApiAndViewModel()
    {
        var api = new TestApiClient { Summary = SampleSummaryWithVariousPurposes() };
        var controller = CreateController(api);

        var result = await controller.Index(q: null, status: null, certId: null, purpose: "mock", page: 1, cancellationToken: CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<AdminExamListViewModel>(viewResult.Model);
        Assert.Equal("mock", vm.Purpose);
        Assert.Equal("mock", api.LastPurposeFilter);
    }

    [Fact]
    public async Task Index_FiltersByCertId_PassesCertIdToApiAndViewModel()
    {
        var api = new TestApiClient { Summary = SampleSummary() };
        var controller = CreateController(api);

        var result = await controller.Index(q: null, status: null, certId: 2, purpose: null, page: 1, cancellationToken: CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<AdminExamListViewModel>(viewResult.Model);
        Assert.Equal(2, vm.CertId);
        Assert.Equal(2, api.LastCertIdFilter);
    }

    [Fact]
    public void AdminExamsView_ContainsCertAndPurposeFilters_Badges_ScopedAudio_AndStreamlinedButtons()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var examsViewPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Admin", "Exams.cshtml");
        Assert.True(File.Exists(examsViewPath), "Admin/Exams.cshtml must exist");

        var content = File.ReadAllText(examsViewPath);

        // Certificate filter dropdown
        Assert.Contains(@"name=""certId""", content);
        Assert.Contains(@"id=""f-cert""", content);
        Assert.Contains(@"Model.Summary.Certificates", content);

        // Purpose filter dropdown
        Assert.Contains(@"name=""purpose""", content);
        Assert.Contains(@"id=""f-purpose""", content);
        Assert.Contains(@"value=""mock""", content);
        Assert.Contains(@"value=""placement""", content);
        Assert.Contains(@"value=""practice""", content);

        // Reset button checks certId and purpose
        Assert.Contains(@"Model.CertId", content);
        Assert.Contains(@"Model.Purpose", content);

        // Certificate & Purpose badges
        Assert.Contains("row.CertificateCode", content);
        var rows = SampleSummaryWithVariousPurposes().Exams.Select(exam => new AdminExamRowViewModel(exam)).ToArray();
        Assert.Equal("TOEIC", rows[0].CertificateCode);
        Assert.Equal("badge badge--success", rows[0].StatusBadgeClass);
        Assert.Equal("badge badge--warning", rows[1].StatusBadgeClass);
        Assert.Equal("PLACEMENT", rows[2].PurposeDisplay);
        Assert.False(rows[3].IsAudioApplicable);
        Assert.False(rows[4].IsAudioApplicable);
        Assert.Matches(@"badge.*font-mono", content);

        // Scoped Audio column: text-muted when audio is N/A or not applicable
        Assert.Contains(@"text-muted", content);

        // Streamlined action buttons: Studio (primary), Edit Info (secondary), Delete (danger if 0 attempts)
        Assert.Contains(@"/admin/exams/@exam.ExamId/studio", content);
        Assert.Contains(@"Edit Info", content);
        Assert.DoesNotMatch(@"<a class=""btn btn--sm"" href=""/admin/exams/@exam\.ExamId/edit"">Edit</a>", content);
        Assert.Contains(@"exam.FinishedAttemptCount == 0", content);
    }

    [Fact]
    public async Task Create_Get_ReturnsViewWithCertificates()
    {
        var api = new TestApiClient { Summary = SampleSummary() };
        var controller = CreateController(api);

        var result = await controller.Create(certId: 1, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/Admin/ExamEditor.cshtml", viewResult.ViewName);
        var vm = Assert.IsType<ExamEditorViewModel>(viewResult.Model).Form;
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
        var vm = Assert.IsType<ExamEditorViewModel>(viewResult.Model).Form;
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
        var vm = Assert.IsType<ExamEditorViewModel>(viewResult.Model).Form;
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
    public void Questions_Get_WithQueryParams_RedirectsToStudioPreservingParameters()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var result = controller.Questions(1, sectionId: 10, questionId: 205);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminExamsController.Studio), redirect.ActionName);
        Assert.Equal(1, redirect.RouteValues?["id"]);
        Assert.Equal(10, redirect.RouteValues?["sectionId"]);
        Assert.Equal(205, redirect.RouteValues?["questionId"]);
    }

    [Fact]
    public void Questions_Get_WithoutQueryParams_RedirectsToStudioWithExamId()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var result = controller.Questions(42, sectionId: null, questionId: null);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminExamsController.Studio), redirect.ActionName);
        Assert.Equal(42, redirect.RouteValues?["id"]);
        Assert.False(redirect.RouteValues?.ContainsKey("sectionId") == true && redirect.RouteValues["sectionId"] != null);
        Assert.False(redirect.RouteValues?.ContainsKey("questionId") == true && redirect.RouteValues["questionId"] != null);
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
        Assert.Equal(nameof(AdminExamsController.Studio), redirect.ActionName);
        Assert.Equal(5, redirect.RouteValues?["id"]);
        Assert.NotNull(api.CreatedQuestionRequest);
        Assert.Equal("New Question Stem", api.CreatedQuestionRequest.Stem);
        Assert.Single(api.CreatedQuestionRequest.Options!, o => o.IsCorrect && o.Label == "B");
    }

    [Fact]
    public async Task SaveQuestion_Update_Success_RedirectsToStudio()
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
        Assert.Equal(nameof(AdminExamsController.Studio), redirect.ActionName);
        Assert.Equal(5, redirect.RouteValues?["id"]);
        Assert.Equal(101, redirect.RouteValues?["questionId"]);
        Assert.NotNull(api.UpdatedQuestionRequest);
        Assert.Equal("Updated Stem", api.UpdatedQuestionRequest.Stem);
    }

    [Fact]
    public async Task DeleteQuestion_CallsApi_RedirectsToStudio()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var result = await controller.DeleteQuestion(5, 101, sectionId: 1, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminExamsController.Studio), redirect.ActionName);
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

    [Fact]
    public async Task ParseQuickIngest_CallsApi_ReturnsJsonResult()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var request = new QuickIngestParseRequest("1. Stem\n*A. Yes\nB. No");
        var result = await controller.ParseQuickIngest(5, request, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        var parsed = Assert.IsType<QuickIngestParseResultDto>(json.Value);
        Assert.True(parsed.Success);
    }

    [Fact]
    public async Task CommitQuickIngest_CallsApi_ReturnsJsonResult()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var request = new QuickIngestCommitRequest(
            SectionId: 1,
            Mode: "append",
            Questions: [new QuickIngestParsedQuestionDto(1, 1, 5, "Stem", 3, null, null, [new QuickIngestParsedOptionDto("A", "Yes", true), new QuickIngestParsedOptionDto("B", "No", false)], [], true)]);

        var result = await controller.CommitQuickIngest(5, request, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        var commit = Assert.IsType<QuickIngestCommitResultDto>(json.Value);
        Assert.True(commit.Success);
        Assert.Equal(1, commit.QuestionsCommitted);
    }

    [Fact]
    public async Task GetQuickIngestSample_CallsApi_ReturnsJsonResult()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var result = await controller.GetQuickIngestSample(10, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        var sample = Assert.IsType<QuickIngestSampleTemplateDto>(json.Value);
        Assert.NotNull(sample);
        Assert.Equal("1. A", sample.Template);
    }

    [Fact]
    public async Task GenerateSectionQuestions_CallsApi_ReturnsJsonResult()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var result = await controller.GenerateSectionQuestions(5, 10, new SectionGenerateRequest(2), CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        var gen = Assert.IsType<SectionGenerationResultDto>(json.Value);
        Assert.NotNull(gen);
        Assert.Equal(5, gen.ExamId);
        Assert.Equal(10, gen.SectionId);
        Assert.Equal(2, gen.QuestionsGenerated);
    }

    [Fact]
    public async Task Studio_ReturnsViewResult_WithStudioModel()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var result = await controller.Studio(5, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ExamStudioViewModel>(view.Model);
        Assert.Equal(5, model.Detail.ExamId);
        Assert.NotEmpty(model.Detail.Sections);
        Assert.Equal("Listening", Assert.Single(model.Skills).Key);
        Assert.Equal(1, model.Skills[0].TotalQuestions);
        Assert.Equal(6, model.Skills[0].ExpectedQuestions);
        Assert.Equal(200, model.RequiredQuestions);
        Assert.False(model.SectionStates[1].IsComplete);
        Assert.True(model.SectionStates[1].HasAudioStimulus);
        var question = model.Questions[1];
        Assert.Equal("mcq", question.TypeCode);
        Assert.Equal(4, question.RadioOptions.Count);
        Assert.Equal("Photo A", question.RadioOptions[0].Text);
        Assert.True(question.RadioOptions[0].IsCorrect);
        Assert.False(question.RadioOptions[1].IsCorrect);
        using var config = System.Text.Json.JsonDocument.Parse(model.ConfigurationJson);
        Assert.Equal(5, config.RootElement.GetProperty("ExamId").GetInt32());
        Assert.Equal(1, config.RootElement.GetProperty("CurrentSectionId").GetInt32());
        Assert.Equal("Listening", config.RootElement.GetProperty("Sections")[0].GetProperty("Modality").GetString());
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[null]")]
    public async Task StudioPresentation_InvalidAllowedComponents_KeepsExistingQuestionEditable(string components)
    {
        var detail = await new TestApiClient().GetAdminExamStudioAsync(5);
        var section = detail.Sections[0] with { AllowedComponentTypesJson = components };
        var question = section.Groups[0].Questions[0] with { QuestionType = QuestionTypeDto.Mcq_3 };
        var model = new StudioQuestionViewModel(question, section, [], []);

        Assert.Contains("mcq", model.AllowedTypes);
        Assert.Contains("free_response", model.AllowedTypes);
        Assert.Contains("mcq_3", model.AllowedTypes);
        Assert.Equal(new[] { "A", "B", "C" }, model.RadioOptions.Select(o => o.Label));
        Assert.Equal("Photo B", model.RadioOptions[1].Text);
        Assert.False(model.RadioOptions[1].IsCorrect);
    }

    [Fact]
    public async Task StudioPresentation_GroupsBySkill_AndIncludesOnlyRelevantRubricCriteria()
    {
        var detail = await new TestApiClient().GetAdminExamStudioAsync(5);
        var section = detail.Sections[0] with { SkillId = 10, SkillName = "Reading", SkillCode = "R" };
        var second = section with { SectionId = 2, CurrentQuestionCount = 2, ExpectedQuestionCount = 2, Groups = [] };
        var grouped = new ExamStudioViewModel(detail with { Sections = [section, second] });
        var skill = Assert.Single(grouped.Skills);
        Assert.Equal("10", skill.Key);
        Assert.Equal("Reading", skill.Name);
        Assert.Equal(3, skill.TotalQuestions);
        Assert.Equal(8, skill.ExpectedQuestions);
        Assert.True(grouped.SectionStates[2].IsComplete);
        Assert.Equal("[ĐỦ]", grouped.SectionStates[2].Indicator);

        var assigned = new ExamStudioCriterionDto(1, "A", "Accuracy", null, 5, 10);
        var irrelevant = new ExamStudioCriterionDto(2, "F", "Fluency", null, 5, 99);
        var question = section.Groups[0].Questions[0] with { Criteria = [assigned] };
        var model = new StudioQuestionViewModel(question, section, [], [assigned, irrelevant]);
        Assert.True(Assert.Single(model.Criteria).IsAssigned);
        Assert.Equal(1, model.Criteria[0].Criterion.CriterionId);
    }

    [Fact]
    public async Task Reorder_CallsApi_ReturnsSuccessJson()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var request = new ReorderSectionItemsRequest(1, [new ReorderSectionItemEntry(null, 101, 1)]);
        var result = await controller.Reorder(5, request, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        Assert.NotNull(json.Value);
    }

    [Fact]
    public async Task Audit_CallsApi_ReturnsAuditJson()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var result = await controller.Audit(5, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        var audit = Assert.IsType<ExamAuditResultDto>(json.Value);
        Assert.True(audit.CanPublish);
    }

    [Fact]
    public async Task CreateCluster_CallsApi_ReturnsSuccessJson()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var request = new CreateExamClusterRequest(1, QuestionCount: 3);
        var result = await controller.CreateCluster(5, request, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        Assert.NotNull(json.Value);
    }

    [Fact]
    public async Task UploadMedia_ValidFile_ReturnsMediaUrlJson()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var file = new FormFile(new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes("fake audio data")), 0, 15, "file", "sample.mp3");
        var result = await controller.UploadMedia(5, file, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        Assert.NotNull(json.Value);
    }

    [Fact]
    public async Task DeleteStudioGroup_CallsApi_ReturnsSuccess()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var result = await controller.DeleteStudioGroup(5, 1, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        Assert.NotNull(json.Value);
    }

    [Fact]
    public async Task DeleteStudioQuestion_CallsApi_ReturnsSuccess()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var result = await controller.DeleteStudioQuestion(5, 101, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        Assert.NotNull(json.Value);
    }

    [Fact]
    public async Task Export_ReturnsFileResult_WithZip_ByDefault()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var result = await controller.Export(5, null, CancellationToken.None);

        var fileResult = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("application/zip", fileResult.ContentType);
        Assert.Equal("test_exam.zip", fileResult.FileDownloadName);
    }

    [Fact]
    public async Task Export_ReturnsFileResult_WithJson_WhenRequested()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var result = await controller.Export(5, "json", CancellationToken.None);

        var fileResult = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("application/json", fileResult.ContentType);
        Assert.Equal("test_exam.json", fileResult.FileDownloadName);
    }

    [Fact]
    public async Task Export_RedirectsToStudio_WhenApiExceptionThrown()
    {
        var api = new TestApiClient
        {
            ExportException = new ApiException("NOT_FOUND", "Exam not found", 404)
        };
        var controller = CreateController(api);

        var result = await controller.Export(999, "zip", CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminExamsController.Studio), redirect.ActionName);
        Assert.Equal(999, redirect.RouteValues?["id"]);
    }

    [Fact]
    public async Task ManifestTemplate_ReturnsFileResult_WhenCertIdValid()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var result = await controller.ManifestTemplate(1, CancellationToken.None);

        var fileResult = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("application/json", fileResult.ContentType);
        Assert.Equal("manifest_template_1.json", fileResult.FileDownloadName);
    }

    [Fact]
    public async Task ManifestTemplate_RedirectsToIndex_WhenCertIdInvalid()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var result = await controller.ManifestTemplate(0, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminExamsController.Index), redirect.ActionName);
        Assert.Contains("Select a certificate", controller.TempData["Error"]?.ToString());
    }

    [Fact]
    public void ExamEditorView_ContainsMultipartFormDataAndAudioFileInput()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var editorPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Admin", "ExamEditor.cshtml");
        Assert.True(File.Exists(editorPath), "Admin/ExamEditor.cshtml must exist");

        var content = AdminAuthoringPresentationTests.ReadComposedView("ExamEditor");
        Assert.Contains("enctype=\"multipart/form-data\"", content);
        Assert.Contains("type=\"file\"", content);
        Assert.Contains("name=\"AudioFile\"", content);
        Assert.Contains("accept=\"audio/mpeg,audio/mp3,audio/wav,audio/*\"", content);
        Assert.DoesNotContain("Advanced: manual metadata override", content);
        Assert.DoesNotContain("Audio Path / Filename", content);
    }

    [Fact]
    public async Task SaveAudio_WithAudioFile_UploadsMediaAndSavesSectionAudio()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var contentBytes = "test audio stream content"u8.ToArray();
        var fileMock = new FormFile(new MemoryStream(contentBytes), 0, contentBytes.Length, "AudioFile", "listening_p1.mp3")
        {
            Headers = new HeaderDictionary(),
            ContentType = "audio/mpeg"
        };

        var model = new UploadAudioModel
        {
            SectionId = 10,
            AudioFile = fileMock,
            DurationSec = 45
        };

        var result = await controller.SaveAudio(42, model, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminExamsController.Edit), redirect.ActionName);
        Assert.Equal(42, redirect.RouteValues?["id"]);
        Assert.Equal(2, redirect.RouteValues?["step"]);

        Assert.Equal(42, api.LastUploadedMediaExamId);
        Assert.Equal("listening_p1.mp3", api.LastUploadedMediaFileName);

        Assert.NotNull(api.LastSavedAudioRequest);
        Assert.Equal(10, api.LastSavedAudioRequest!.SectionId);
        Assert.Equal("/media/exams/42/listening_p1.mp3", api.LastSavedAudioRequest.AudioPath);
        Assert.Equal(45, api.LastSavedAudioRequest.DurationSec);
        Assert.Equal(contentBytes.Length, api.LastSavedAudioRequest.SizeBytes);
        Assert.Equal("Audio file saved successfully.", controller.TempData["Success"]);
    }

    [Fact]
    public async Task SaveAudio_WithoutAudioFile_RedirectsWithError()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var model = new UploadAudioModel
        {
            SectionId = 12,
            AudioFile = null
        };

        var result = await controller.SaveAudio(42, model, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminExamsController.Edit), redirect.ActionName);
        Assert.Equal(42, redirect.RouteValues?["id"]);
        Assert.Equal(2, redirect.RouteValues?["step"]);

        Assert.Null(api.LastUploadedMediaFileName);
        Assert.Null(api.LastSavedAudioRequest);
    }

    [Fact]
    public async Task SaveAudio_WithManualPath_NormalizesPath()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var model = new UploadAudioModel
        {
            SectionId = 15,
            AudioPath = "exams/42/sample_track.mp3",
            DurationSec = 120,
            SizeBytes = 1024
        };

        var result = await controller.SaveAudio(42, model, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminExamsController.Edit), redirect.ActionName);

        Assert.NotNull(api.LastSavedAudioRequest);
        Assert.Equal(15, api.LastSavedAudioRequest!.SectionId);
        Assert.Equal("/media/exams/42/sample_track.mp3", api.LastSavedAudioRequest.AudioPath);
    }

    [Fact]
    public async Task SaveAudio_WithAjaxHeader_ReturnsJson()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);
        controller.ControllerContext.HttpContext.Request.Headers.Accept = "application/json";

        var model = new UploadAudioModel
        {
            SectionId = 15,
            AudioPath = "exams/42/sample_track.mp3",
            DurationSec = 120,
            SizeBytes = 1024
        };

        var result = await controller.SaveAudio(42, model, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        Assert.NotNull(json.Value);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    [InlineData("/media/exams/1/audio.mp3", "/media/exams/1/audio.mp3")]
    [InlineData("exams/1/audio.mp3", "/media/exams/1/audio.mp3")]
    [InlineData("/exams/1/audio.mp3", "/media/exams/1/audio.mp3")]
    [InlineData("imports/5/photo.jpg", "/media/imports/5/photo.jpg")]
    [InlineData("/imports/5/photo.jpg", "/media/imports/5/photo.jpg")]
    [InlineData("http://example.com/audio.mp3", "http://example.com/audio.mp3")]
    [InlineData("https://example.com/audio.mp3", "https://example.com/audio.mp3")]
    [InlineData("/audio/toeic/test1-p1.mp3", "/audio/toeic/test1-p1.mp3")]
    public void MediaUrlHelper_NormalizesCorrectly(string? input, string? expected)
    {
        var actual = MediaUrlHelper.NormalizeMediaUrl(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Generate_WithBlueprintJson_RedirectsToStudioWithSuccessMessage()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);
        var json = """{ "certCode": "TOEIC", "title": "AI Mock Form", "sections": [{ "sectionCode": "P5", "questionCount": 2 }] }""";

        var result = await controller.Generate(
            certCode: null,
            title: null,
            purpose: null,
            status: null,
            targetBand: null,
            difficultyLevel: null,
            blueprintJson: json,
            certId: 1);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminExamsController.Studio), redirect.ActionName);
        Assert.Equal(99, redirect.RouteValues?["id"]);
        Assert.Contains("Generated exam", controller.TempData["Success"]?.ToString());
        Assert.NotNull(api.LastGeneratedBlueprint);
        Assert.Equal("TOEIC", api.LastGeneratedBlueprint.CertCode);
    }

    [Fact]
    public async Task Generate_WithFormFields_ConstructsBlueprintAndRedirectsToStudio()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var result = await controller.Generate(
            certCode: "TOEIC",
            title: "Quick Form",
            purpose: "mock",
            status: "published",
            targetBand: "B2",
            difficultyLevel: 3,
            blueprintJson: null,
            certId: 1);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminExamsController.Studio), redirect.ActionName);
        Assert.Equal(99, redirect.RouteValues?["id"]);
        Assert.Equal("published", api.LastGeneratedBlueprint?.Status);
    }

    [Fact]
    public async Task Generate_WhenMissingCertCode_RedirectsToIndexWithError()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var result = await controller.Generate(
            certCode: null,
            title: "Form",
            purpose: "mock",
            status: "draft",
            targetBand: null,
            difficultyLevel: null,
            blueprintJson: null,
            certId: 1);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminExamsController.Index), redirect.ActionName);
        Assert.Contains("target certificate", controller.TempData["Error"]?.ToString());
    }

    [Fact]
    public async Task SaveQuestion_PassesAllExpandedComponentPropertiesToApi()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var model = new QuestionSaveModel
        {
            GroupId = 1,
            SectionId = 1,
            Stem = "Inline question stem",
            DifficultyLevel = 3,
            QuestionType = QuestionTypeDto.InlineCompletion,
            DisplayOrder = 1,
            MaxSelections = 2,
            AllowNumber = false,
            AcceptedAnswersJson = @"[""answer1"",""answer2""]",
            RequiredKeywords = "keyword1, keyword2"
        };

        var result = await controller.SaveQuestion(5, model, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminExamsController.Studio), redirect.ActionName);
        Assert.NotNull(api.CreatedQuestionRequest);
        Assert.Equal(QuestionTypeDto.InlineCompletion, api.CreatedQuestionRequest.QuestionType);
        Assert.Equal(2, api.CreatedQuestionRequest.MaxSelections);
        Assert.False(api.CreatedQuestionRequest.AllowNumber);
        Assert.Equal(@"[""answer1"",""answer2""]", api.CreatedQuestionRequest.AcceptedAnswersJson);
        Assert.Equal("keyword1, keyword2", api.CreatedQuestionRequest.RequiredKeywords);
    }

    [Fact]
    public async Task SaveGroup_PassesUnitNumberToApi()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);

        var model = new QuestionGroupSaveModel
        {
            GroupId = 10,
            SectionId = 2,
            DisplayOrder = 1,
            UnitNumber = 2
        };

        var result = await controller.SaveGroup(5, model, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminExamsController.Studio), redirect.ActionName);
        Assert.NotNull(api.UpdatedQuestionGroupRequest);
        Assert.Equal(2, api.UpdatedQuestionGroupRequest.UnitNumber);
    }

    [Fact]
    public async Task AutosaveQuestion_WhenSuccessful_ReturnsJsonUpdatedDto()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);
        var request = new UpdateQuestionRequest(
            GroupId: 76,
            Stem: "Updated stem",
            DifficultyLevel: 2,
            QuestionType: QuestionTypeDto.Mcq,
            Explanation: "Use 'before' directly in front of a day of the week to denote a deadline.",
            Options: [
                new AdminQuestionOptionInput(270, "A", "before", true, null),
                new AdminQuestionOptionInput(271, "B", "prior", false, null)
            ]);

        var result = await controller.AutosaveQuestion(17, 89, request, CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var dto = Assert.IsType<AdminQuestionDto>(jsonResult.Value);
        Assert.Equal(89, dto.QuestionId);
        Assert.Equal("Updated stem", dto.Stem);
        Assert.Equal("Use 'before' directly in front of a day of the week to denote a deadline.", dto.Explanation);
        Assert.NotNull(api.UpdatedQuestionRequest);
        Assert.Equal(2, api.UpdatedQuestionRequest.Options?.Count);
    }

    [Fact]
    public async Task AutosaveQuestion_WhenApiThrowsApiException_ReturnsStatusCodeWithError()
    {
        var api = new TestApiClient
        {
            UpdateQuestionException = new ApiException("Validation", "Option labels must be unique.", 400, "Option labels must be unique.")
        };
        var controller = CreateController(api);
        var request = new UpdateQuestionRequest(
            GroupId: 76,
            Stem: "Stem",
            DifficultyLevel: 2,
            QuestionType: QuestionTypeDto.Mcq);

        var result = await controller.AutosaveQuestion(17, 89, request, CancellationToken.None);

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, statusResult.StatusCode);
    }

    [Fact]
    public async Task AutosaveGroup_WhenSuccessful_ReturnsJsonUpdatedDto()
    {
        var api = new TestApiClient();
        var controller = CreateController(api);
        var request = new UpdateQuestionGroupRequest(
            SectionId: 28,
            Passage: "New passage",
            UnitNumber: 2);

        var result = await controller.AutosaveGroup(17, 76, request, CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        Assert.NotNull(api.UpdatedQuestionGroupRequest);
        Assert.Equal(2, api.UpdatedQuestionGroupRequest.UnitNumber);
    }

    internal sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    internal sealed class TestApiClient : IAdminExamsClient
    {
        public AdminExamListSummaryDto? Summary { get; set; }
        public AdminExamDetailDto? DetailToReturn { get; set; }
        public AdminPublishExamResultDto? PublishResultToReturn { get; set; }
        public AdminExamQuestionsDto? QuestionsToReturn { get; set; }

        public CreateAdminExamRequest? CreatedExamRequest { get; set; }
        public UpdateAdminExamRequest? UpdatedExamRequest { get; set; }
        public CreateQuestionGroupRequest? CreatedQuestionGroupRequest { get; set; }
        public UpdateQuestionGroupRequest? UpdatedQuestionGroupRequest { get; set; }
        public CreateQuestionRequest? CreatedQuestionRequest { get; set; }
        public UpdateQuestionRequest? UpdatedQuestionRequest { get; set; }
        public int? DeletedExamId { get; set; }
        public int? DeletedQuestionId { get; set; }

        public ApiException? CreateExamException { get; set; }
        public ApiException? PublishException { get; set; }
        public ApiException? ExportException { get; set; }
        public ApiException? UpdateQuestionException { get; set; }
        public ApiException? UpdateGroupException { get; set; }
        public int? LastCertIdFilter { get; set; }
        public string? LastPurposeFilter { get; set; }
        public SaveExamSectionAudioRequest? LastSavedAudioRequest { get; set; }
        public int? LastSavedAudioExamId { get; set; }
        public string? LastUploadedMediaFileName { get; set; }
        public int? LastUploadedMediaExamId { get; set; }

        public Task<AdminExamListSummaryDto> GetAdminExamsAsync(string? q, string? status, int? certId, string? purpose, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
        {
            LastCertIdFilter = certId;
            LastPurposeFilter = purpose;
            return Task.FromResult(Summary ?? SampleSummary());
        }

        public Task<AdminExamListSummaryDto> GetAdminExamsAsync(string? q = null, string? status = null, int? certId = null, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default) =>
            GetAdminExamsAsync(q, status, certId, null, page, pageSize, cancellationToken);

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

        public Task<AdminQuestionGroupDto> CreateAdminQuestionGroupAsync(int examId, CreateQuestionGroupRequest request, CancellationToken cancellationToken = default)
        {
            CreatedQuestionGroupRequest = request;
            return Task.FromResult(new AdminQuestionGroupDto(
                1001, examId, 1, request.SectionId, "SEC", "Section", ModalityDto.Reading, request.Passage, request.AudioPath, request.AudioStartMs, request.AudioEndMs, request.ImagePath, request.ImageAlt, request.Transcript, request.PrepSeconds, request.ResponseSeconds, request.DisplayOrder, [], request.UnitNumber));
        }

        public Task<AdminQuestionGroupDto> UpdateAdminQuestionGroupAsync(int examId, int groupId, UpdateQuestionGroupRequest request, CancellationToken cancellationToken = default)
        {
            if (UpdateGroupException != null) throw UpdateGroupException;
            UpdatedQuestionGroupRequest = request;
            return Task.FromResult(new AdminQuestionGroupDto(
                groupId, examId, 1, request.SectionId, "SEC", "Section", ModalityDto.Reading, request.Passage, request.AudioPath, request.AudioStartMs, request.AudioEndMs, request.ImagePath, request.ImageAlt, request.Transcript, request.PrepSeconds, request.ResponseSeconds, request.DisplayOrder, [], request.UnitNumber));
        }

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
            if (UpdateQuestionException != null) throw UpdateQuestionException;
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

        public Task<AdminExamSectionAudioDto> SaveAdminExamSectionAudioAsync(int examId, SaveExamSectionAudioRequest request, CancellationToken cancellationToken = default)
        {
            LastSavedAudioExamId = examId;
            LastSavedAudioRequest = request;
            return Task.FromResult<AdminExamSectionAudioDto>(null!);
        }

        public Task<QuickIngestParseResultDto> ParseQuickIngestAsync(int examId, QuickIngestParseRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new QuickIngestParseResultDto(true, 1, 1, 0, [], []));

        public Task<QuickIngestCommitResultDto> CommitQuickIngestAsync(int examId, QuickIngestCommitRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new QuickIngestCommitResultDto(true, request.Questions.Count, request.Questions.Count, "Success"));

        public Task<QuickIngestSampleTemplateDto> GetQuickIngestSampleTemplateAsync(int sectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new QuickIngestSampleTemplateDto("1. A", "Blueprint Template"));

        public Task<SectionGenerationResultDto> GenerateSectionQuestionsAsync(int examId, int sectionId, SectionGenerateRequest? request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SectionGenerationResultDto(examId, sectionId, request?.QuestionCount ?? 2, 1, "Generated"));

        public Task<ExamStudioDetailDto> GetAdminExamStudioAsync(int examId, CancellationToken cancellationToken = default) =>
            Task.FromResult(SampleStudioDetail(examId));

        public Task<ExamAuditResultDto> GetAdminExamAuditAsync(int examId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExamAuditResultDto(true, 1, 1, 0, 0, [], []));

        public Task ReorderAdminSectionItemsAsync(int examId, ReorderSectionItemsRequest request, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<AdminQuestionGroupDto> CreateAdminExamClusterAsync(int examId, CreateExamClusterRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AdminQuestionGroupDto(
                999, examId, 1, request.SectionId, "P3", "Conversations", ModalityDto.Listening,
                request.Passage, request.AudioPath, request.AudioStartMs, request.AudioEndMs, null, null, request.Transcript,
                null, null, 1, []));

        public Task<AdminExamMediaUploadResultDto> UploadAdminExamMediaAsync(int examId, Stream stream, string fileName, CancellationToken cancellationToken = default)
        {
            LastUploadedMediaExamId = examId;
            LastUploadedMediaFileName = fileName;
            return Task.FromResult(new AdminExamMediaUploadResultDto($"/media/exams/{examId}/{fileName}", $"exams/{examId}/{fileName}", fileName));
        }

        public ExamBlueprintDto? LastGeneratedBlueprint { get; private set; }
        public Exception? GenerateException { get; set; }
        public Task<ExamGenerationResultDto> GenerateAdminExamAsync(ExamBlueprintDto blueprint, CancellationToken cancellationToken = default)
        {
            if (GenerateException != null) throw GenerateException;
            LastGeneratedBlueprint = blueprint;
            return Task.FromResult(new ExamGenerationResultDto(
                ExamId: 99,
                CertId: 1,
                CertCode: blueprint.CertCode,
                ExamName: blueprint.Title ?? "Generated Test",
                Status: blueprint.Status ?? "draft",
                Purpose: blueprint.Purpose ?? "mock",
                QuestionCount: 4));
        }

        public Task<(Stream Stream, string ContentType, string FileName)> ExportAdminExamAsync(int examId, string? format = "zip", CancellationToken cancellationToken = default)
        {
            if (ExportException != null) throw ExportException;
            var fmt = format ?? "zip";
            return Task.FromResult(((Stream)new MemoryStream([1, 2, 3]), fmt == "json" ? "application/json" : "application/zip", $"test_exam.{fmt}"));
        }

        public Task<(Stream Stream, string ContentType, string FileName)> GetManifestTemplateAsync(int certId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(((Stream)new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{}")), "application/json", $"manifest_template_{certId}.json"));
        }

        private static ExamStudioDetailDto SampleStudioDetail(int examId) =>
            new ExamStudioDetailDto(
                examId, 1, "toeic", "TOEIC", "Mock Test", ExamStatusDto.Draft, ExamPurposeDto.Mock,
                120, "Desc", "ETS", "Note", 200, 1, 0, DateTimeOffset.UtcNow, null,
                [
                    new ExamStudioSectionDto(1, 1, "P1", "Photographs", ModalityDto.Listening, 6, 1, 1, false, null, null,
                    [
                        new ExamStudioGroupDto(1, examId, 1, 1, "P1", "Photographs", ModalityDto.Listening, null, null, null, null, null, null, null, null, null, 1,
                        [
                            new ExamStudioQuestionDto(1, 1, examId, 1, "P1", "Photographs", 1, "P1", "Photo stem", 3, QuestionTypeDto.Mcq, "Expl", null, null, null, null, null, null, null, null, 1, null,
                            [
                                new ExamStudioQuestionOptionDto(1, 1, "A", "Photo A", true, null),
                                new ExamStudioQuestionOptionDto(2, 1, "B", "Photo B", false, null)
                            ],
                            [], false)
                        ])
                    ])
                ],
                [], [],
                new ExamAuditResultDto(true, 1, 1, 0, 0, [], []));
    }
}
