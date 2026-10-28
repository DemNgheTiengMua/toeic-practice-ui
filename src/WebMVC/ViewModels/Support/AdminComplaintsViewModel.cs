using Contracts;

namespace WebMVC.Models;

public sealed record AdminComplaintsViewModel(
    AdminComplaintsSummaryDto Summary,
    string? Query,
    string? Status,
    int CurrentPage)
{
    public IReadOnlyDictionary<int, string> TypeLabels { get; } = Summary.Complaints.ToDictionary(c => c.ComplaintId, c => ComplaintLabels.Type(c.Type));
    public string OldestType { get; } = Summary.OldestPendingType.HasValue ? ComplaintLabels.Type(Summary.OldestPendingType.Value) : "";
    public string OldestDate { get; init; } = "—";
    public int FirstRow { get; } = (Summary.Page - 1) * Summary.PageSize + 1;
    public int LastRow { get; } = Math.Min(Summary.Page * Summary.PageSize, Summary.TotalCount);
    public IReadOnlyDictionary<DateTimeOffset, FormattedDateViewModel> Dates { get; init; } = new Dictionary<DateTimeOffset, FormattedDateViewModel>();
    public AdminComplaintsViewModel WithDates(DateTimeFormatter formatter) => this with {
        Dates = FormattedDateViewModel.Prepare(Summary.Complaints.Select(c => c.CreatedAt), formatter),
        OldestDate = Summary.OldestPendingCreatedAt.HasValue ? formatter.FormatCustom(Summary.OldestPendingCreatedAt.Value, "dd MMM") : "—"
    };
}
