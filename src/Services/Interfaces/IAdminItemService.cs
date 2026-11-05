using System.Threading;
using System.Threading.Tasks;
using Contracts;

namespace Services;

/// <summary>
/// #188: authoring writes the pool. An item is created with no exam reference at all, and every
/// scored change to it inserts a version rather than rewriting the one a past attempt pinned.
/// <para>
/// This is a second door, not a replacement. The legacy exam editor still writes
/// <c>Questions</c>/<c>QuestionOptions</c> and #231 is where those go; nothing here deletes or
/// rewrites a legacy row, so both shapes stay readable until then.
/// </para>
/// </summary>
public interface IAdminItemService
{
    /// <summary>Writes an item, its first version, and optionally a shared stimulus. No exam row.</summary>
    Task<PoolItemDto> CreateAsync(CreatePoolItemRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies an edit. A scored change inserts a new version and leaves the old one byte-identical;
    /// an explanation-only change is written in place and forks nothing (CNT-43).
    /// </summary>
    Task<PoolItemDto> UpdateAsync(int itemId, UpdatePoolItemRequest request, CancellationToken cancellationToken = default);
}
