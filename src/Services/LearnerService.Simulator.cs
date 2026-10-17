using BusinessObjects;

namespace Services;

public sealed partial class LearnerService
{
    private Task<SimulatorConfig?> BuildSimulatorConfigAsync(
        int userId,
        int certId,
        TargetReadiness? targetReadiness,
        CancellationToken cancellationToken) =>
        Task.FromResult<SimulatorConfig?>(null);
}
