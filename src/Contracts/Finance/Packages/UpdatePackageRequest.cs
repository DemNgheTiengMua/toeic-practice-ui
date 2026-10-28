
namespace Contracts;

public sealed record UpdatePackageRequest(
    string Name,
    int Credits,
    decimal Price,
    bool IsActive,
    bool IsFeatured,
    string? Features = null,
    int DisplayOrder = 0);
