
namespace Contracts;

public sealed record UpdateProfileRequest(string FullName, string? Phone, string TimeZoneId);
