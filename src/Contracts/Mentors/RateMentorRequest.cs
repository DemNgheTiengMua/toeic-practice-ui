using System.ComponentModel.DataAnnotations;

namespace Contracts;

public sealed record RateMentorRequest(
    [property: Range(1, 5, ErrorMessage = "Rating must be between 1 and 5.")] int Rating,
    [property: StringLength(2000, ErrorMessage = "Review text cannot exceed 2000 characters.")] string? Text = null);
