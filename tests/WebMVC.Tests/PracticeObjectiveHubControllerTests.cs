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

/// <summary>
/// #238: the controller side of the hub. Two facts belong here rather than in the row view model:
/// the matrix is built from the service's cells, and the reopen instant is converted in the learner's
/// own zone — a conversion the view cannot do and must not be trusted to.
/// </summary>
public sealed class PracticeObjectiveHubControllerTests
{
    private static PracticeController CreateController(StubApi api, string? zoneId = null)
    {
        var identity = new ClaimsIdentity(
            AuthClaims.For(7, "learner@test.local", Roles.Learner, "Learner"), "Cookies");
        var principal = new ClaimsPrincipal(identity);
        var properties = new AuthenticationProperties(new Dictionary<string, string?> { ["jwt"] = "token" });

        var services = new ServiceCollection();
        services.AddRouting();
        services.AddMvcCore();
        services.AddSingleton<IAuthenticationService>(new FixedAuthentication(principal, properties));
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { User = principal, RequestServices = serviceProvider };
        if (zoneId is not null)
        {
            httpContext.Items[WebMVC.Formatters.DateTimeFormatter.ZoneItemKey] = zoneId;
        }

        var currentUser = new CurrentUser(new HttpContextAccessor { HttpContext = httpContext });
        var dates = new WebMVC.Formatters.DateTimeFormatter(new HttpContextAccessor { HttpContext = httpContext });

        return new PracticeController(api, currentUser, NullLogger<PracticeController>.Instance, dates)
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
    public async Task The_hub_builds_a_matrix_row_for_every_cell_it_is_given()
    {
        var api = new StubApi
        {
            Cells =
            [
                new PracticeCellDto(5, "R-DET", "Detail", ModalityDto.Reading, "B1",
                    PracticeCellMasteryDto.Reviewing, IsPrioritised: true, ReopensAt: null),
                new PracticeCellDto(6, "R-INF", "Inference", ModalityDto.Reading, "B2",
                    PracticeCellMasteryDto.Untested, IsPrioritised: false, ReopensAt: null),
            ],
        };

        var controller = CreateController(api);
        var result = await controller.Index(CancellationToken.None);

        var model = Assert.IsType<PracticeCatalogViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(2, model.Matrix.Count);
        Assert.Equal("R-DET", model.Matrix[0].TopicCode);
        Assert.Equal("Reviewing", model.Matrix[0].MasteryLabel);
        Assert.True(model.Matrix[0].IsPrioritised);
        Assert.Equal(1, model.PrioritisedCellCount);
        Assert.False(model.Matrix[1].IsPrioritised);
    }

    /// <summary>
    /// The zone guard. A reopen instant of 02:00Z is 09:00 for a UTC+7 learner. A controller that
    /// printed the instant as UTC, or a view that formatted it, would show 02:00 — the exact class of
    /// error #159 fixed on this page and #235 repeated in the daily-volume view.
    /// </summary>
    [Fact]
    public async Task The_reopen_time_is_converted_to_the_learners_zone_in_the_controller()
    {
        var reopens = new DateTimeOffset(2026, 11, 5, 2, 0, 0, TimeSpan.Zero);
        var api = new StubApi
        {
            Cells =
            [
                new PracticeCellDto(5, "R-DET", "Detail", ModalityDto.Reading, "B1",
                    PracticeCellMasteryDto.Mastered, IsPrioritised: true, ReopensAt: reopens, IsResting: true),
            ],
        };

        var controller = CreateController(api, zoneId: "Asia/Ho_Chi_Minh");
        var result = await controller.Index(CancellationToken.None);

        var model = Assert.IsType<PracticeCatalogViewModel>(Assert.IsType<ViewResult>(result).Model);
        var row = Assert.Single(model.Matrix);
        Assert.True(row.IsResting);
        Assert.Equal("09:00 05 Nov", row.ReopensDisplay);
        Assert.Equal("Reopens 09:00 05 Nov", row.StatusLine);
    }

    /// <summary>
    /// A matrix read failure is an addition to the page, never a precondition: the drill must still be
    /// startable. The wrong input that would pass a weaker test is a page that renders an empty matrix
    /// and no error — the learner would think they have no cells.
    /// </summary>
    [Fact]
    public async Task A_failed_matrix_read_still_renders_the_page_with_its_sections()
    {
        var api = new StubApi { MatrixThrows = new ApiException(ApiError.Codes.Unexpected, "boom", 500) };

        var controller = CreateController(api);
        var result = await controller.Index(CancellationToken.None);

        var model = Assert.IsType<PracticeCatalogViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.False(model.HasMatrix);
        Assert.True(model.HasAnySections);
    }

    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class StubApi : IPracticeClient
    {
        public IReadOnlyList<PracticeCellDto> Cells { get; set; } = [];
        public Exception? MatrixThrows { get; set; }

        public Task<PracticeCatalogDto> GetPracticeCatalogAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PracticeCatalogDto(
                new CertificateOptionDto("DEMO", "DEMO"),
                [new PracticeSectionDto(1, "listening", "Listening", ModalityDto.Listening, "P1", "Part 1", 4, 6, 6, 80, 5, 1)],
                [], [], null));

        public Task<IReadOnlyList<PracticeCellDto>> GetPracticeMatrixAsync(CancellationToken cancellationToken = default) =>
            MatrixThrows is not null
                ? Task.FromException<IReadOnlyList<PracticeCellDto>>(MatrixThrows)
                : Task.FromResult(Cells);

        public Task<PracticeSessionDto> StartPracticeSessionAsync(StartPracticeRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PracticeSessionDto> GetPracticeSessionInfoAsync(int sessionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PracticeQuestionDto> GetPracticeQuestionAsync(int sessionId, int displayOrder, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RevealHintResponse> RevealPracticeHintAsync(int sessionId, int questionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GradedAnswerDto> SubmitPracticeAnswerAsync(int sessionId, int questionId, SubmitPracticeAnswerRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PracticeSummaryDto> GetPracticeSummaryAsync(int sessionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AiInsightDto> GetPracticeInsightAsync(int sessionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RetryPracticeInsightAsync(int sessionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
