using Contracts;

namespace WebMVC.Models;

public sealed record PlacementSelectViewModel(
    PlacementCatalogViewDto Catalog,
    string? ErrorMessage = null);

public sealed record PlacementTakeViewModel(
    PlacementTakeViewDto Take,
    string? ErrorMessage = null);

public sealed record PlacementResultViewModel(
    PlacementResultViewDto Result,
    string? ErrorMessage = null,
    AiInsightDto? Insight = null);

public sealed record TargetBandViewModel(
    TargetBandViewDto View,
    string? ErrorMessage = null,
    string? SuccessMessage = null);

public sealed class SetTargetBandFormModel
{
    public string TargetBandCode { get; set; } = string.Empty;
}
