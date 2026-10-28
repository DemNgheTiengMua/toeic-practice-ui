
namespace Contracts;

public sealed record CreatePackageRequest(
    string Name,
    int Credits,
    decimal Price,
    bool IsActive = true,
    bool IsFeatured = false,
    string? Features = null,
    int DisplayOrder = 0);
