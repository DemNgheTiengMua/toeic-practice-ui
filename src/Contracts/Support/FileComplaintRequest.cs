
namespace Contracts;

public sealed record FileComplaintRequest(
    ComplaintTypeDto Type,
    string Reason,
    int? BookingId = null,
    int? AttemptId = null,
    int? OrderId = null);
