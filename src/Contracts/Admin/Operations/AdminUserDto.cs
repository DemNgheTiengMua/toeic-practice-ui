
namespace Contracts;

public sealed record AdminUserDto(
    int UserId,
    string Email,
    string? FullName,
    string? Phone,
    int CreditsLeft,
    int ExamsTaken,
    int? BestScore,
    DateTimeOffset? LastLoginAt,
    bool IsLocked,
    DateTimeOffset? EmailVerifiedAt,
    DateTimeOffset CreatedAt);
