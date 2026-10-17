using BusinessObjects;

namespace Services;

public sealed partial class LearnerService
{
    private Task<RubricHighlight?> BuildRubricHighlightAsync(
        int userId,
        int certId,
        CancellationToken cancellationToken) =>
        Task.FromResult<RubricHighlight?>(null);
}
