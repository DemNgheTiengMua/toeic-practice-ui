using BusinessObjects;

namespace Services;

public sealed partial class LearnerService
{
    private Task<IReadOnlyList<WeaknessStrip>> BuildWeaknessStripsAsync(
        int userId,
        int certId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<WeaknessStrip>>([]);
}
