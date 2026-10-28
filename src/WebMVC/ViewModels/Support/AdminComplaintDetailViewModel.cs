using Contracts;

namespace WebMVC.Models;

public sealed record AdminComplaintDetailViewModel(
    AdminComplaintDetailDto Complaint,
    string? ErrorMessage = null)
{
    public string TypeLabel { get; } = ComplaintLabels.Type(Complaint.Type);
    public string CreatedAt { get; init; } = "";
    public string? ResolvedAt { get; init; }
    public AdminComplaintDetailViewModel WithDates(DateTimeFormatter formatter) => this with {
        CreatedAt = formatter.FormatCustom(Complaint.CreatedAt, "dd MMM yyyy, HH:mm"),
        ResolvedAt = Complaint.ResolvedAt.HasValue ? formatter.FormatCustom(Complaint.ResolvedAt.Value, "dd MMM yyyy, HH:mm") : null
    };
}
