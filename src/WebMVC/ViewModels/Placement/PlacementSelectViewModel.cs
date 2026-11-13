using Contracts;

namespace WebMVC.Models;

public sealed record PlacementSelectViewModel(
    PlacementCatalogViewDto Catalog,
    string? ErrorMessage = null)
{
    public bool IsRetake => Catalog.IsRetake;
    public bool HasInProgress => Catalog.ActivePlacementId.HasValue;
    public string IdempotencyKey { get; } = Guid.NewGuid().ToString("N");
}
