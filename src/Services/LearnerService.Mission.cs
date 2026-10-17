using BusinessObjects;

namespace Services;

public sealed partial class LearnerService
{
    private Task<IReadOnlyList<MissionStep>> BuildMissionStepsAsync(
        int userId,
        int certId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MissionStep>>([]);
}
