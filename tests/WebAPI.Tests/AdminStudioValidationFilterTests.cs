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
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Services;
using WebAPI.Endpoints;
using Xunit;

namespace WebAPI.Tests;

public sealed class AdminStudioValidationFilterTests
{
    [Fact]
    public async Task Post_items_rejects_empty_stem_and_missing_explanation_with_400_field_errors()
    {
        var items = new StubAdminItemService();
        var (client, app) = await AdminAppAsync(items: items);
        await using (app)
        {
            var request = new CreatePoolItemRequest(
                ModalityDto.Reading,
                TopicId: 1,
                CefrLevelCode: "B1",
                ItemType: "sentence_cloze",
                Stem: "   ",
                OptionA: "A1",
                OptionB: "B1",
                OptionC: "C1",
                OptionD: "D1",
                KeyLabel: "A",
                Explanation: "  ");

            var response = await client.PostAsJsonAsync("/api/admin/items", request);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(items.CreateCalled, "RequestValidationFilter should short-circuit before calling AdminItemService.");
            Assert.True(json.TryGetProperty("errors", out var errors));
            Assert.True(errors.TryGetProperty("stem", out var stemErrors));
            Assert.True(stemErrors.GetArrayLength() > 0);
            Assert.True(errors.TryGetProperty("explanation", out var explanationErrors));
            Assert.True(explanationErrors.GetArrayLength() > 0);
        }
    }

    [Fact]
    public async Task Post_items_rejects_invalid_key_label_with_400_field_error()
    {
        var items = new StubAdminItemService();
        var (client, app) = await AdminAppAsync(items: items);
        await using (app)
        {
            var request = new CreatePoolItemRequest(
                ModalityDto.Reading,
                TopicId: 1,
                CefrLevelCode: "B1",
                ItemType: "sentence_cloze",
                Stem: "Valid stem?",
                OptionA: "Opt A",
                OptionB: "Opt B",
                OptionC: "Opt C",
                OptionD: "Opt D",
                KeyLabel: "Z",
                Explanation: "Because A is correct.");

            var response = await client.PostAsJsonAsync("/api/admin/items", request);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(items.CreateCalled);
            Assert.True(json.TryGetProperty("errors", out var errors));
            Assert.True(errors.TryGetProperty("keyLabel", out var keyErrors));
            Assert.True(keyErrors.GetArrayLength() > 0);
        }
    }

    [Fact]
    public async Task Put_items_rejects_empty_explanation_with_400_field_error()
    {
        var items = new StubAdminItemService();
        var (client, app) = await AdminAppAsync(items: items);
        await using (app)
        {
            var request = new UpdatePoolItemRequest(
                Stem: "Updated stem?",
                OptionA: "A",
                OptionB: "B",
                OptionC: "C",
                OptionD: "D",
                KeyLabel: "B",
                Explanation: "");

            var response = await client.PutAsJsonAsync("/api/admin/items/42", request);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(items.UpdateCalled);
            Assert.True(json.TryGetProperty("errors", out var errors));
            Assert.True(errors.TryGetProperty("explanation", out var expErrors));
            Assert.True(expErrors.GetArrayLength() > 0);
        }
    }

    [Fact]
    public async Task Post_exams_rejects_empty_name_and_non_positive_duration_with_400_field_errors()
    {
        var exams = new StubAdminExamService();
        var (client, app) = await AdminAppAsync(exams: exams);
        await using (app)
        {
            var request = new CreateAdminExamRequest(
                CertId: 1,
                Name: " ",
                DurationMinutes: 0,
                Purpose: ExamPurposeDto.Mock);

            var response = await client.PostAsJsonAsync("/api/admin/exams", request);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(exams.CreateExamCalled);
            Assert.True(json.TryGetProperty("errors", out var errors));
            Assert.True(errors.TryGetProperty("name", out _));
            Assert.True(errors.TryGetProperty("durationMinutes", out _));
        }
    }

    [Fact]
    public async Task Post_certificate_wizard_rejects_non_positive_score_step_and_inverted_bounds_with_400_field_errors()
    {
        var certs = new StubCertificateService();
        var (client, app) = await AdminAppAsync(certs: certs);
        await using (app)
        {
            var request = new SaveCertificateWizardRequest(
                CertId: null,
                Code: "TOEIC",
                Name: "TOEIC Listening & Reading",
                Description: null,
                ScoreAggregationRule: "sum",
                PresetCode: "two_skill",
                IsActive: true,
                Skills: [],
                MinScaledScore: 500,
                MaxScaledScore: 100,
                ScoreStep: 0,
                Bands: [new SaveScoreBandWizardDto(null, "B1", "Band 1", 0, 500, 1)],
                Criteria: []);

            var response = await client.PostAsJsonAsync("/api/admin/certificates/wizard", request);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(certs.SaveCalled);
            Assert.True(json.TryGetProperty("errors", out var errors));
            Assert.True(errors.TryGetProperty("scoreStep", out _));
            Assert.True(errors.TryGetProperty("minScaledScore", out _));
        }
    }

    [Fact]
    public async Task Put_score_conversion_rejects_negative_scale_and_inverted_band_with_400_field_errors()
    {
        var scoring = new StubScoreConversionService();
        var (client, app) = await AdminAppAsync(scoring: scoring);
        await using (app)
        {
            var request = new UpdateScoreConversionRequest(
                ExamId: null,
                Scales: [new UpdateScoreScaleEntryDto(1, RawScore: -1, ScaledScore: 100)],
                Bands: [new UpdateScoreBandEntryDto(null, "B1", "Band 1", MinTotal: 500, MaxTotal: 100, DisplayOrder: 1)]);

            var response = await client.PutAsJsonAsync("/api/admin/score-conversion/1", request);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(scoring.UpdateCalled);
            Assert.True(json.TryGetProperty("errors", out var errors));
            Assert.True(errors.TryGetProperty("scales", out _));
            Assert.True(errors.TryGetProperty("bands", out _));
        }
    }

    private static async Task<(HttpClient Client, WebApplication App)> AdminAppAsync(
        StubAdminItemService? items = null,
        StubAdminExamService? exams = null,
        StubCertificateService? certs = null,
        StubScoreConversionService? scoring = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, "http://127.0.0.1:0");
        builder.Services.AddSingleton<IAdminItemService>(items ?? new StubAdminItemService());
        builder.Services.AddSingleton<IAdminExamService>(exams ?? new StubAdminExamService());
        builder.Services.AddSingleton<ICertificateService>(certs ?? new StubCertificateService());
        builder.Services.AddSingleton<IScoreConversionService>(scoring ?? new StubScoreConversionService());
        builder.Services.AddSingleton(NoopProxy<IFileStore>.Create());
        builder.Services.AddSingleton(NoopProxy<IQuestionImportService>.Create());
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, AllowAdmin>("Test", null);
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(Roles.Admin, p => p.RequireRole(Roles.Admin));

        var app = builder.Build();
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();

        var admin = app.MapGroup("/api/admin").RequireAuthorization(Roles.Admin);
        InvokeMap("MapItems", admin);
        InvokeMap("MapExams", admin);
        InvokeMap("MapStudio", admin);
        InvokeMap("MapCertificates", admin);
        InvokeMap("MapScoreConversion", admin);

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        return (new HttpClient { BaseAddress = new Uri(address) }, app);
    }

    private static void InvokeMap(string methodName, RouteGroupBuilder group) =>
        typeof(AdminEndpoints)
            .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [group]);

    private sealed class AllowAdmin : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public AllowAdmin(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.Admin)], "Test");
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }

    private sealed class StubAdminItemService : IAdminItemService
    {
        public bool CreateCalled { get; private set; }
        public bool UpdateCalled { get; private set; }

        public Task<PoolItemDto> CreateAsync(CreatePoolItemRequest request, CancellationToken cancellationToken = default)
        {
            CreateCalled = true;
            throw new NotSupportedException();
        }

        public Task<PoolItemDto> UpdateAsync(int itemId, UpdatePoolItemRequest request, CancellationToken cancellationToken = default)
        {
            UpdateCalled = true;
            throw new NotSupportedException();
        }
    }

    private sealed class StubAdminExamService : IAdminExamService
    {
        public bool CreateExamCalled { get; private set; }

        public Task<AdminExamDetailDto> CreateExamAsync(CreateAdminExamRequest request, CancellationToken cancellationToken = default)
        {
            CreateExamCalled = true;
            throw new NotSupportedException();
        }

        public Task<AdminExamListSummaryDto> GetExamsAsync(string? q, string? status, int? certId, string? purpose, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AdminExamDetailDto> GetExamByIdAsync(int examId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AdminExamDetailDto> UpdateExamAsync(int examId, UpdateAdminExamRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AdminPublishExamResultDto> PublishExamAsync(int examId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AdminPublishExamResultDto> WithdrawExamAsync(int examId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteExamAsync(int examId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AdminForkEditionResultDto> ForkEditionAsync(int examId, ForkExamEditionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AdminExamSectionAudioDto> SaveSectionAudioAsync(int examId, SaveExamSectionAudioRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExamStudioDetailDto> GetExamStudioAsync(int examId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExamAssemblyDto> GetExamAssemblyAsync(int examId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExamAuditResultDto> AuditExamPublishReadinessAsync(int examId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReorderSectionItemsAsync(int examId, ReorderSectionItemsRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AdminQuestionGroupDto> CreateExamClusterAsync(int examId, CreateExamClusterRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateExamStudioGroupAsync(int examId, int groupId, UpdateQuestionGroupRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteExamStudioGroupAsync(int examId, int groupId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AdminExamQuestionsDto> GetExamQuestionsAsync(int examId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExamItemBindingDto> BindItemAsync(int examId, BindPoolItemRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<QuickIngestCommitResultDto> CommitQuickIngestAsync(int examId, QuickIngestCommitRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(Stream Stream, string ContentType, string FileName)> ExportExamPackageAsync(int examId, string? format = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExamPreviewTakeView> GetExamPreviewTakeAsync(int examId, int? questionNumber = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubCertificateService : ICertificateService
    {
        public bool SaveCalled { get; private set; }

        public Task<SaveCertificateWizardResultDto> SaveCertificateWizardAsync(SaveCertificateWizardRequest request, CancellationToken cancellationToken = default)
        {
            SaveCalled = true;
            throw new NotSupportedException();
        }

        
        public Task<IReadOnlyList<AdminCertificateSummaryDto>> GetAdminCertificatesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CertificateRemovalResultDto> RemoveCertificateAsync(int certId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IReadOnlyList<CertificateBlueprintPresetDto> GetOfficialPresets() => throw new NotSupportedException();
        public Task<CertificateBlueprintDetailDto?> GetCertificateBlueprintAsync(int certId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CertificateBlueprintDetailDto?> GetCertificateBlueprintAsync(string code, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CertificateCatalog>> GetCatalogAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CertificateOption?> GetCertificateOptionAsync(string code, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubScoreConversionService : IScoreConversionService
    {
        public bool UpdateCalled { get; private set; }

        public Task<AdminScoreConversionView> UpdateScoreConversionAsync(int certId, UpdateScoreConversionCommand command, CancellationToken cancellationToken = default)
        {
            UpdateCalled = true;
            throw new NotSupportedException();
        }

        public Task<AdminScoreConversionView> GetScoreConversionAsync(string? certCode, int? examId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ScorePreviewResult> CalculateScorePreviewAsync(ScorePreviewRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ScorePreviewResult CalculateScorePreview(ScorePreviewRequest request, IReadOnlyList<ScoreScaleRowItem>? scales = null, IReadOnlyList<ScoreBandConfigItem>? bands = null, IReadOnlyDictionary<int, (int Min, int Max, int Step)>? skillBounds = null) => throw new NotSupportedException();
    }

    private class NoopProxy<T> : DispatchProxy where T : class
    {
        public static T Create() => Create<T, NoopProxy<T>>();
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new NotSupportedException();
    }
}
