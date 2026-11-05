namespace Contracts;

public sealed partial class CompanionApiClient
{
    public Task<PoolItemDto> CreateAdminItemAsync(CreatePoolItemRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<CreatePoolItemRequest, PoolItemDto>("api/admin/items", request, bearerToken, cancellationToken);

    public Task<PoolItemDto> UpdateAdminItemAsync(int itemId, UpdatePoolItemRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PutAuthorizedAsync<UpdatePoolItemRequest, PoolItemDto>($"api/admin/items/{itemId}", request, bearerToken, cancellationToken);

    public Task<ExamItemBindingDto> BindAdminItemAsync(int examId, BindPoolItemRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<BindPoolItemRequest, ExamItemBindingDto>($"api/admin/exams/{examId}/bindings", request, bearerToken, cancellationToken);

    public Task<PoolItemDto> CreateAdminItemAsync(CreatePoolItemRequest request, CancellationToken cancellationToken = default) =>
        CreateAdminItemAsync(request, string.Empty, cancellationToken);

    public Task<PoolItemDto> UpdateAdminItemAsync(int itemId, UpdatePoolItemRequest request, CancellationToken cancellationToken = default) =>
        UpdateAdminItemAsync(itemId, request, string.Empty, cancellationToken);

    public Task<ExamItemBindingDto> BindAdminItemAsync(int examId, BindPoolItemRequest request, CancellationToken cancellationToken = default) =>
        BindAdminItemAsync(examId, request, string.Empty, cancellationToken);
}
