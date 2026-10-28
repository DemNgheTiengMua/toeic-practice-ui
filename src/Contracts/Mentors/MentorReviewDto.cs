using System.ComponentModel.DataAnnotations;

namespace Contracts;

public sealed record MentorReviewDto(
    int ReviewId,
    int Rating,
    string? Text,
    string UserDisplayName,
    DateTimeOffset CreatedAt);
