using System.Threading;
using System.Threading.Tasks;
using Contracts;

namespace Contracts;

/// <summary>
/// #188: the pool authoring surface. Separate from <see cref="IAdminExamsClient"/> because an item
/// names no exam - a client that carries both would let a caller believe authoring needs one.
/// </summary>
public interface IAdminItemsClient
{
    Task<PoolItemDto> CreateAdminItemAsync(CreatePoolItemRequest request, CancellationToken cancellationToken = default);
    Task<PoolItemDto> CreateAdminItemAsync(CreatePoolItemRequest request, string bearerToken, CancellationToken cancellationToken = default) => CreateAdminItemAsync(request, cancellationToken);

    Task<PoolItemDto> UpdateAdminItemAsync(int itemId, UpdatePoolItemRequest request, CancellationToken cancellationToken = default);
    Task<PoolItemDto> UpdateAdminItemAsync(int itemId, UpdatePoolItemRequest request, string bearerToken, CancellationToken cancellationToken = default) => UpdateAdminItemAsync(itemId, request, cancellationToken);

    Task<ExamItemBindingDto> BindAdminItemAsync(int examId, BindPoolItemRequest request, CancellationToken cancellationToken = default);
    Task<ExamItemBindingDto> BindAdminItemAsync(int examId, BindPoolItemRequest request, string bearerToken, CancellationToken cancellationToken = default) => BindAdminItemAsync(examId, request, cancellationToken);
}
