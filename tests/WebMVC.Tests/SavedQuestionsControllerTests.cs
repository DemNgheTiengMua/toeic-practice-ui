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

public sealed class SavedQuestionsControllerTests
{
    private static SavedQuestionsViewDto SampleView() => new(
        new CertificateOptionDto("TOEIC", "Test of English for International Communication"),
        TotalSavedCount: 2,
        ManualSavedCount: 1,
        IncorrectSavedCount: 1,
        Questions:
        [
            new SavedQuestionItemDto(
                SavedQuestionId: 11,
                QuestionId: 101,
                ItemId: null,
                ItemVersionId: null,
                IsRetiredDefective: false,
                Source: SavedQuestionSourceDto.Manual,
                SavedAt: DateTimeOffset.UtcNow,
                SectionId: 5,
                SectionCode: "P5",
                SectionName: "Incomplete Sentences",
                TopicCode: null,
                TopicName: null,
                CategoryId: 1,
                CategoryName: "Verb tense",
                Stem: "The manager asked that the report ______ by Friday.",
                Passage: null,
                ImagePath: null,
                AudioPath: null,
                Hint: "Look at the main verb.",
                Explanation: "Use future tense will review.",
                Transcript: null,
                Options:
                [
                    new SavedQuestionOptionDto(1, "A", "review", false),
                    new SavedQuestionOptionDto(2, "B", "will review", true)
                ]),
            new SavedQuestionItemDto(
                SavedQuestionId: 12,
                QuestionId: 102,
                ItemId: null,
                ItemVersionId: null,
                IsRetiredDefective: false,
                Source: SavedQuestionSourceDto.Incorrect,
                SavedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
                SectionId: 5,
                SectionCode: "P5",
                SectionName: "Incomplete Sentences",
                TopicCode: null,
                TopicName: null,
                CategoryId: 2,
                CategoryName: "Prepositions",
                Stem: "Inquiries must be resolved ______ forty-eight hours.",
                Passage: null,
                ImagePath: null,
                AudioPath: null,
                Hint: "Preposition of time.",
                Explanation: "Within indicates duration.",
                Transcript: null,
                Options:
                [
                    new SavedQuestionOptionDto(3, "A", "within", true),
                    new SavedQuestionOptionDto(4, "B", "between", false)
                ])
        ],
        Sections: [new SavedQuestionSectionFilterDto(5, "P5", "Incomplete Sentences", 2)],
        Categories: [new SavedQuestionCategoryFilterDto(1, "Verb tense", 1), new SavedQuestionCategoryFilterDto(2, "Prepositions", 1)]);

    [Fact]
    public async Task Index_returns_view_with_saved_questions_model()
    {
        var api = new TestApiClient
        {
            SavedQuestions = SampleView(),
        };

        var controller = CreateController(api);
        var result = await controller.Index(null, null, null, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<SavedQuestionsIndexViewModel>(viewResult.Model);
        Assert.Equal(2, model.Data.TotalSavedCount);
        Assert.Equal("TOEIC", model.Data.Certificate.Code);
        Assert.Equal(2, model.Data.Questions.Count);
    }

    [Fact]
    public async Task Index_forwards_filter_query_params()
    {
        var api = new TestApiClient
        {
            SavedQuestions = SampleView(),
        };

        var controller = CreateController(api);
        var result = await controller.Index("manual", 5, 1, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<SavedQuestionsIndexViewModel>(viewResult.Model);
        Assert.Equal("manual", model.CurrentSource);
        Assert.Equal(5, model.CurrentSectionId);
        Assert.Equal(1, model.CurrentCategoryId);
    }

    [Fact]
    public async Task Index_redirects_to_dashboard_on_api_failure()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.Unexpected, "Error", 500),
        };

        var controller = CreateController(api);
        var result = await controller.Index(null, null, null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Dashboard", redirect.ControllerName);
        Assert.NotNull(controller.TempData["ShellError"]);
    }

    [Fact]
    public async Task Review_returns_view_with_question_details()
    {
        var sampleQuestion = SampleView().Questions[0];
        var api = new TestApiClient
        {
            SavedItem = sampleQuestion,
        };

        var controller = CreateController(api);
        var result = await controller.Review(101, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<SavedQuestionReviewViewModel>(viewResult.Model);
        Assert.Equal(101, model.Question.QuestionId);
        Assert.Equal("Use future tense will review.", model.Question.Explanation);
    }

    [Fact]
    public async Task Review_returns_json_when_ajax_requested()
    {
        var sampleQuestion = SampleView().Questions[0];
        var api = new TestApiClient
        {
            SavedItem = sampleQuestion,
        };

        var controller = CreateController(api);
        controller.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        var result = await controller.Review(101, CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var dto = Assert.IsType<SavedQuestionItemDto>(jsonResult.Value);
        Assert.Equal(101, dto.QuestionId);
    }

    [Fact]
    public async Task Review_redirects_to_index_when_not_found()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.SavedQuestionNotFound, "Not found", 404),
        };

        var controller = CreateController(api);
        var result = await controller.Review(999, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(SavedQuestionsController.Index), redirect.ActionName);
        Assert.NotNull(controller.TempData["ErrorMessage"]);
    }

    /// <summary>
    /// #224 / D21: the flag must be <b>rendered</b>, not merely stored. The service test proves the
    /// projection carries <c>IsRetiredDefective</c>; this one proves the learner can see it, by reading
    /// the list view itself. A flag that stops at the DTO reads as a normal answer to the learner.
    /// </summary>
    [Fact]
    public void A_retired_defective_link_is_flagged_in_the_list_view()
    {
        var view = File.ReadAllText(Path.Combine(
            TestPathHelper.FindRepoRoot(), "src", "WebMVC", "Views", "SavedQuestions", "Index.cshtml"));

        Assert.Contains("item.IsRetiredDefective", view);
        Assert.Contains("Withdrawn as defective", view);
    }

    /// <summary>The dedicated review page carries the same notice, since that is where the key is read.</summary>
    [Fact]
    public void A_retired_defective_link_is_flagged_on_the_review_page()
    {
        var view = File.ReadAllText(Path.Combine(
            TestPathHelper.FindRepoRoot(), "src", "WebMVC", "Views", "SavedQuestions", "Review.cshtml"));

        Assert.Contains("q.IsRetiredDefective", view);
        Assert.Contains("Withdrawn as defective", view);
    }

    /// <summary>
    /// The practice Save control is live for a pool item now. Before #224 it was disabled, because the
    /// address it would have sent was a legacy question id and a pool item has none — so the only
    /// honest control was a disabled one. The route is now named for the shape, so the disable must be
    /// gone; a disabled control left behind would make the ticket's own feature unreachable.
    /// </summary>
    [Fact]
    public void The_practice_save_control_is_live_for_a_pool_item()
    {
        var view = File.ReadAllText(Path.Combine(
            TestPathHelper.FindRepoRoot(), "src", "WebMVC", "Views", "Practice", "_TakeQuestion.cshtml"));

        Assert.DoesNotContain("Saving a pool question to your bank is not available yet", view);
        Assert.Contains("data-item-version-id", view);
        Assert.Contains("data-question-id", view);
    }

    /// <summary>
    /// A pool item saves through the item-version route, never the question route. The two counters
    /// overlap, so posting one to the other's route is how a bookmark lands on the wrong row.
    /// </summary>
    [Fact]
    public async Task Toggle_saves_a_pool_item_through_the_item_version_route()
    {
        var api = new TestApiClient
        {
            SaveResult = new SaveQuestionResultDto(true, SavedQuestionSourceDto.Manual, DateTimeOffset.UtcNow, 77),
        };

        var controller = CreateController(api);
        controller.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        var result = await controller.Toggle(
            savedQuestionId: null, questionId: null, itemVersionId: 4242,
            currentlySaved: false, source: "manual", CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var val = System.Text.Json.JsonSerializer.SerializeToElement(jsonResult.Value);
        Assert.True(val.GetProperty("isSaved").GetBoolean());
        Assert.Equal(77, val.GetProperty("savedQuestionId").GetInt32());

        Assert.Equal(4242, api.LastSavedItemVersionId);
        Assert.Null(api.LastSavedQuestionId);
    }

    /// <summary>A legacy drill still saves through the question route.</summary>
    [Fact]
    public async Task Toggle_saves_a_legacy_question_through_the_question_route()
    {
        var api = new TestApiClient
        {
            SaveResult = new SaveQuestionResultDto(true, SavedQuestionSourceDto.Manual, DateTimeOffset.UtcNow, 78),
        };

        var controller = CreateController(api);
        controller.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        await controller.Toggle(
            savedQuestionId: null, questionId: 9001, itemVersionId: null,
            currentlySaved: false, source: "manual", CancellationToken.None);

        Assert.Equal(9001, api.LastSavedQuestionId);
        Assert.Null(api.LastSavedItemVersionId);
    }

    /// <summary>Unsaving addresses the bookmark's own row id, not the item or the question.</summary>
    [Fact]
    public async Task Toggle_unsaves_by_the_bookmark_row_id()
    {
        var api = new TestApiClient
        {
            UnsaveResult = new SaveQuestionResultDto(false, null, null),
        };

        var controller = CreateController(api);
        controller.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        await controller.Toggle(
            savedQuestionId: 55, questionId: null, itemVersionId: 4242,
            currentlySaved: true, source: null, CancellationToken.None);

        Assert.Equal(55, api.LastUnsavedSavedQuestionId);
    }

    /// <summary>Review addresses the bookmark's row id, so two shapes cannot collide on one address.</summary>
    [Fact]
    public async Task Review_addresses_the_bookmark_row_id()
    {
        var api = new TestApiClient { SavedItem = SampleView().Questions[0] };

        var controller = CreateController(api);
        await controller.Review(4321, CancellationToken.None);

        Assert.Equal(4321, api.LastReviewedSavedQuestionId);
    }

    [Fact]
    public async Task Save_returns_json_when_ajax_requested()
    {
        var api = new TestApiClient
        {
            SaveResult = new SaveQuestionResultDto(true, SavedQuestionSourceDto.Manual, DateTimeOffset.UtcNow),
        };

        var controller = CreateController(api);
        controller.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        var result = await controller.Save(101, "manual", CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var val = System.Text.Json.JsonSerializer.SerializeToElement(jsonResult.Value);
        Assert.True(val.GetProperty("isSaved").GetBoolean());
        Assert.Equal("manual", val.GetProperty("source").GetString());
    }

    [Fact]
    public async Task Remove_returns_json_when_ajax_requested()
    {
        var api = new TestApiClient
        {
            UnsaveResult = new SaveQuestionResultDto(false, null, null),
        };

        var controller = CreateController(api);
        controller.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        var result = await controller.Remove(101, CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var val = System.Text.Json.JsonSerializer.SerializeToElement(jsonResult.Value);
        Assert.False(val.GetProperty("isSaved").GetBoolean());
    }

    [Fact]
    public async Task Toggle_unsaves_when_currently_saved()
    {
        var api = new TestApiClient
        {
            UnsaveResult = new SaveQuestionResultDto(false, null, null),
        };

        var controller = CreateController(api);
        var result = await controller.Toggle(savedQuestionId: 11, questionId: null, itemVersionId: null, currentlySaved: true, source: null, CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var val = System.Text.Json.JsonSerializer.SerializeToElement(jsonResult.Value);
        Assert.False(val.GetProperty("isSaved").GetBoolean());
    }

    [Fact]
    public async Task Toggle_saves_when_not_currently_saved()
    {
        var api = new TestApiClient
        {
            SaveResult = new SaveQuestionResultDto(true, SavedQuestionSourceDto.Manual, DateTimeOffset.UtcNow),
        };

        var controller = CreateController(api);
        var result = await controller.Toggle(savedQuestionId: null, questionId: 101, itemVersionId: null, currentlySaved: false, source: "manual", CancellationToken.None);

        var jsonResult = Assert.IsType<JsonResult>(result);
        var val = System.Text.Json.JsonSerializer.SerializeToElement(jsonResult.Value);
        Assert.True(val.GetProperty("isSaved").GetBoolean());
        Assert.Equal("manual", val.GetProperty("source").GetString());
    }

    [Fact]
    public async Task Save_rejects_external_referer_open_redirect()
    {
        var api = new TestApiClient
        {
            SaveResult = new SaveQuestionResultDto(true, SavedQuestionSourceDto.Manual, DateTimeOffset.UtcNow),
        };

        var controller = CreateController(api);
        controller.Request.Headers.Referer = "https://evil.com/phishing";
        var result = await controller.Save(101, "manual", CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(SavedQuestionsController.Index), redirect.ActionName);
    }

    [Fact]
    public async Task Save_redirects_to_valid_local_referer()
    {
        var api = new TestApiClient
        {
            SaveResult = new SaveQuestionResultDto(true, SavedQuestionSourceDto.Manual, DateTimeOffset.UtcNow),
        };

        var controller = CreateController(api);
        controller.Request.Headers.Referer = "https://localhost:5000/practice/42?q=3";
        var result = await controller.Save(101, "manual", CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/practice/42?q=3", redirect.Url);
    }

    private static SavedQuestionsController CreateController(TestApiClient api)
    {
        var identity = new ClaimsIdentity(AuthClaims.For(7, "learner@test.local", Roles.Learner, "Learner"), "Cookies");
        var principal = new ClaimsPrincipal(identity);
        var properties = new AuthenticationProperties(new Dictionary<string, string?> { ["jwt"] = "token" });

        var services = new ServiceCollection();
        services.AddRouting();
        services.AddMvcCore();
        services.AddSingleton<IAuthenticationService>(new FixedAuthentication(principal, properties));
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { User = principal, RequestServices = serviceProvider };
        httpContext.Request.Host = new HostString("localhost", 5000);
        httpContext.Request.Scheme = "https";
        var currentUser = new CurrentUser(new HttpContextAccessor { HttpContext = httpContext });

        var controller = new SavedQuestionsController(api, currentUser, NullLogger<SavedQuestionsController>.Instance, new WebMVC.Formatters.DateTimeFormatter(new HttpContextAccessor()))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                RouteData = new Microsoft.AspNetCore.Routing.RouteData(),
            },
            TempData = new TempDataDictionary(httpContext, new EmptyTempDataProvider()),
        };

        return controller;
    }

    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class TestApiClient : ISavedQuestionsClient
    {
        public Exception? ExceptionToThrow { get; set; }
        public SavedQuestionsViewDto? SavedQuestions { get; set; }
        public SavedQuestionItemDto? SavedItem { get; set; }
        public SaveQuestionResultDto? SaveResult { get; set; }
        public SaveQuestionResultDto? UnsaveResult { get; set; }

        /// <summary>#224: which save route was driven, so a test can prove the pool shape reached it.</summary>
        public int? LastSavedQuestionId { get; private set; }
        public int? LastSavedItemVersionId { get; private set; }
        public int? LastUnsavedSavedQuestionId { get; private set; }
        public int? LastReviewedSavedQuestionId { get; private set; }

        public Task<SavedQuestionsViewDto> GetSavedQuestionsAsync(string? source = null, int? sectionId = null, int? categoryId = null, CancellationToken cancellationToken = default) =>
            ExceptionToThrow != null ? Task.FromException<SavedQuestionsViewDto>(ExceptionToThrow) : Task.FromResult(SavedQuestions!);

        public Task<SavedQuestionItemDto> GetSavedQuestionAsync(int savedQuestionId, CancellationToken cancellationToken = default)
        {
            LastReviewedSavedQuestionId = savedQuestionId;
            return ExceptionToThrow != null ? Task.FromException<SavedQuestionItemDto>(ExceptionToThrow) : Task.FromResult(SavedItem!);
        }

        public Task<SaveQuestionResultDto> SaveQuestionAsync(int questionId, string? source = null, CancellationToken cancellationToken = default)
        {
            LastSavedQuestionId = questionId;
            return ExceptionToThrow != null ? Task.FromException<SaveQuestionResultDto>(ExceptionToThrow) : Task.FromResult(SaveResult!);
        }

        public Task<SaveQuestionResultDto> SaveItemVersionAsync(int itemVersionId, string? source = null, CancellationToken cancellationToken = default)
        {
            LastSavedItemVersionId = itemVersionId;
            return ExceptionToThrow != null ? Task.FromException<SaveQuestionResultDto>(ExceptionToThrow) : Task.FromResult(SaveResult!);
        }

        public Task<SaveQuestionResultDto> UnsaveQuestionAsync(int savedQuestionId, CancellationToken cancellationToken = default)
        {
            LastUnsavedSavedQuestionId = savedQuestionId;
            return ExceptionToThrow != null ? Task.FromException<SaveQuestionResultDto>(ExceptionToThrow) : Task.FromResult(UnsaveResult!);
        }
    }
}
