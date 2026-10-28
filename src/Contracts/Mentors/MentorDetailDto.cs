using System.ComponentModel.DataAnnotations;

namespace Contracts;

public sealed record MentorDetailDto(
    MentorSummaryDto Mentor,
    IReadOnlyList<MentorSlotDto> AvailableSlots,
    IReadOnlyList<MentorReviewDto> Reviews);
