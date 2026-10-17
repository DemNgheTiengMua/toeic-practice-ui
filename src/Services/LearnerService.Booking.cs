using BusinessObjects;

namespace Services;

public sealed partial class LearnerService
{
    private Task<UpcomingBookingCard?> BuildUpcomingBookingAsync(
        int userId,
        int certId,
        CancellationToken cancellationToken) =>
        Task.FromResult<UpcomingBookingCard?>(null);
}
