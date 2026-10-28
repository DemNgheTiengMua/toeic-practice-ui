using Contracts;

namespace WebMVC.Models;

public sealed record SupportHistoryViewModel(
    IReadOnlyList<LearnerComplaintDto> Complaints)
{
    public IReadOnlyDictionary<int, string> TypeLabels { get; } = Complaints.ToDictionary(c => c.ComplaintId, c => ComplaintLabels.Type(c.Type));
    public IReadOnlyDictionary<DateTimeOffset, FormattedDateViewModel> Dates { get; init; } = new Dictionary<DateTimeOffset, FormattedDateViewModel>();
    public SupportHistoryViewModel WithDates(DateTimeFormatter formatter) => this with { Dates = FormattedDateViewModel.Prepare(
        Complaints.Select(c => c.CreatedAt).Concat(Complaints.Where(c => c.ResolvedAt.HasValue).Select(c => c.ResolvedAt!.Value)), formatter) };
}
