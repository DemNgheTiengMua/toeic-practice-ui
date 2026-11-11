using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using BusinessObjects;
using Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Services;
using WebAPI.Endpoints;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// #188: the two refusals a pool write can produce reach the caller as a coded 400 rather than as a
/// 500. This maps the real <see cref="AdminEndpoints"/> item routes into a minimal app — the actual
/// route, the actual catch — so the assertion is about production behaviour with only the service
/// replaced.
/// <para>
/// The ticket names "the admin sees a 500" as the failure mode of a scored edit attempted as an
/// UPDATE: the immutability trigger throws, and an endpoint that does not catch it answers 500. The
/// service now inserts a version instead, so that particular 500 is unreachable; these prove the
/// refusals that <em>are</em> reachable are not 500s either.
/// </para>
/// </summary>
public sealed class PoolItemEndpointTests
{
    [Fact]
    public async Task A_refused_item_write_answers_a_coded_400_not_a_500()
    {
        var service = new RefusingItemService();
        var (client, app) = await ItemsAppAsync(service);
        using (app)
        {
            var response = await client.PostAsJsonAsync(
                "/api/admin/items",
                new CreatePoolItemRequest(ModalityDto.Listening, 1, "B1", "short_exchange", "Who signs the ledger?"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
            Assert.NotNull(problem);
            Assert.Equal(ApiError.Codes.Validation, problem.Extensions["code"]?.ToString());
            Assert.Contains("reading topic", problem.Detail, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_refused_binding_answers_a_coded_400_not_a_500()
    {
        var service = new RefusingExamService();
        var (client, app) = await ItemsAppAsync(new ThrowingItemService(), service);
        using (app)
        {
            var response = await client.PostAsJsonAsync(
                "/api/admin/exams/4/bindings",
                new BindPoolItemRequest(2, 9, 1));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
            Assert.NotNull(problem);
            Assert.Contains("INV-6", problem.Detail, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The mapped routes are the real ones: a group built here rather than the capability-protected
    /// one, because the gate is asserted in <see cref="PoolAuthoringRoutesTests"/> and this file is
    /// about body handling.
    /// </summary>
    private static async Task<(HttpClient Client, WebApplication App)> ItemsAppAsync(
        IAdminItemService items, IAdminExamService? exams = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, "http://127.0.0.1:0");
        builder.Services.AddSingleton(items);
        builder.Services.AddSingleton(exams ?? new RefusingExamService());
        builder.Services.AddProblemDetails();

        var app = builder.Build();
        app.UseExceptionHandler();

        typeof(AdminEndpoints)
            .GetMethod("MapItems", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [app.MapGroup("/api/admin")]);

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        return (new HttpClient { BaseAddress = new Uri(address) }, app);
    }

    private sealed class RefusingItemService : IAdminItemService
    {
        public Task<PoolItemDto> CreateAsync(CreatePoolItemRequest request, CancellationToken cancellationToken = default) =>
            throw new AdminItemException(
                ApiError.Codes.Validation,
                "Topic 'R-DET' is a reading topic; a listening item cannot sit on it. Pick a topic of the same skill.");

        public Task<PoolItemDto> UpdateAsync(int itemId, UpdatePoolItemRequest request, CancellationToken cancellationToken = default) =>
            throw new ItemNotFoundException(itemId);
    }

    private sealed class ThrowingItemService : IAdminItemService
    {
        public Task<PoolItemDto> CreateAsync(CreatePoolItemRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PoolItemDto> UpdateAsync(int itemId, UpdatePoolItemRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RefusingExamService : IAdminExamService
    {
        public Task<ExamItemBindingDto> BindItemAsync(int examId, BindPoolItemRequest request, CancellationToken cancellationToken = default) =>
            throw new AdminExamException(
                ApiError.Codes.Validation,
                "INV-6: a production exam cannot bind a demo-tier item. Promote the item through review, or make the paper demo.");

        // The binding route is the only member this file exercises.
        public Task<AdminExamListSummaryDto> GetExamsAsync(string? q, string? status, int? certId, string? purpose, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AdminExamDetailDto> GetExamByIdAsync(int examId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AdminExamDetailDto> CreateExamAsync(CreateAdminExamRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AdminExamDetailDto> UpdateExamAsync(int examId, UpdateAdminExamRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AdminPublishExamResultDto> PublishExamAsync(int examId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AdminPublishExamResultDto> WithdrawExamAsync(int examId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteExamAsync(int examId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AdminForkEditionResultDto> ForkEditionAsync(int examId, ForkExamEditionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AdminExamQuestionsDto> GetExamQuestionsAsync(int examId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AdminQuestionGroupDto> CreateQuestionGroupAsync(int examId, CreateQuestionGroupRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AdminQuestionGroupDto> UpdateQuestionGroupAsync(int examId, int groupId, UpdateQuestionGroupRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteQuestionGroupAsync(int examId, int groupId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
  // #273: the Studio's pool-native group write surface. Not exercised here.
  public Task UpdateExamStudioGroupAsync(int examId, int groupId, UpdateQuestionGroupRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
  public Task DeleteExamStudioGroupAsync(int examId, int groupId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AdminQuestionDto> CreateQuestionAsync(int examId, CreateQuestionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AdminQuestionDto> UpdateQuestionAsync(int questionId, UpdateQuestionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteQuestionAsync(int questionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AdminExamSectionAudioDto> SaveSectionAudioAsync(int examId, SaveExamSectionAudioRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<QuickIngestCommitResultDto> CommitQuickIngestAsync(int examId, QuickIngestCommitRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExamStudioDetailDto> GetExamStudioAsync(int examId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExamAuditResultDto> AuditExamPublishReadinessAsync(int examId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReorderSectionItemsAsync(int examId, ReorderSectionItemsRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AdminQuestionGroupDto> CreateExamClusterAsync(int examId, CreateExamClusterRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(Stream Stream, string ContentType, string FileName)> ExportExamPackageAsync(int examId, string? format = "zip", CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExamPreviewTakeView> GetExamPreviewTakeAsync(int examId, int? questionDisplayOrder, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExamAssemblyDto> GetExamAssemblyAsync(int examId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
