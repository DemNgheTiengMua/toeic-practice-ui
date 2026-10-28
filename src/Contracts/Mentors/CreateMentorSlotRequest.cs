using System.ComponentModel.DataAnnotations;

namespace Contracts;

public sealed record CreateMentorSlotRequest(
    DateTime LocalStartAt,
    DateTime? LocalEndAt,
    string? TimeZoneId = null);
