using System;
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

/// <summary>
/// #188: the MVC door onto pool authoring answers with the API's status and reason, so the authoring
/// screen (#203) can show the refusal instead of a bare "HTTP 400". The controller proxies the API,
/// as every staff controller here does (ADR-0002): the browser never reaches WebAPI.
/// </summary>
public sealed class AdminItemsControllerTests
{
    private static AdminItemsController CreateController(IAdminItemsClient api)
    {
        var services = new ServiceCollection();
        services.AddRouting();
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        var currentUser = new CurrentUser(new HttpContextAccessor { HttpContext = httpContext });

        return new AdminItemsController(api, currentUser, NullLogger<AdminItemsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new AdminExamsControllerTests.EmptyTempDataProvider()),
        };
    }

    [Fact]
    public async Task Create_returns_the_created_item_as_json()
    {
        var api = new RecordingClient();
        var controller = CreateController(api);

        var result = await controller.Create(
            new CreatePoolItemRequest(ModalityDto.Reading, 3, "B1", "sentence_cloze", "Which window?"),
            CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        var dto = Assert.IsType<PoolItemDto>(json.Value);
        Assert.Equal(api.Created.ItemId, dto.ItemId);
        Assert.NotNull(api.SeenCreateRequest);
    }

    /// <summary>
    /// The routed status, not a 200 with an error body: a client that only reads the status code
    /// must still see the refusal. This is the shape <c>AutosaveQuestion</c> already has.
    /// </summary>
    [Fact]
    public async Task Create_surfaces_the_api_refusal_with_its_status_and_code()
    {
        var api = new RecordingClient
        {
            Refusal = new ApiException(
                ApiError.Codes.Validation,
                "Refused",
                400,
                "Topic 'R-DET' is a reading topic; a listening item cannot sit on it."),
        };
        var controller = CreateController(api);

        var result = await controller.Create(
            new CreatePoolItemRequest(ModalityDto.Listening, 3, "B1", "short_exchange", "Who signs it?"),
            CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, status.StatusCode);
    }

    private sealed class RecordingClient : IAdminItemsClient
    {
        public CreatePoolItemRequest? SeenCreateRequest { get; private set; }
        public ApiException? Refusal { get; set; }

        public PoolItemDto Created { get; } = new(
            7, 11, 2, 2, ModalityDto.Reading, 3, "R-DET", "B1", "sentence_cloze", "demo", "draft",
            false, null, "Which window?", "a", "b", "c", "d", "A", null,
            null, null, null, null, null, null, DateTimeOffset.UnixEpoch);

        public Task<PoolItemDto> CreateAdminItemAsync(CreatePoolItemRequest request, CancellationToken cancellationToken = default)
        {
            if (Refusal is not null) throw Refusal;
            SeenCreateRequest = request;
            return Task.FromResult(Created);
        }

        public Task<PoolItemDto> UpdateAdminItemAsync(int itemId, UpdatePoolItemRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Created with { ItemId = itemId });

        public Task<ExamItemBindingDto> BindAdminItemAsync(int examId, BindPoolItemRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
