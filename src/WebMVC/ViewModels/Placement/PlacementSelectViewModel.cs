using Contracts;

namespace WebMVC.Models;

public sealed record PlacementSelectViewModel(
    PlacementCatalogViewDto Catalog,
    string? ErrorMessage = null);
