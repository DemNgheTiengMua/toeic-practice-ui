using Contracts;

namespace WebMVC.Models;

public sealed record SupportReportViewModel(
    EligibleSubjectsDto EligibleSubjects,
    string? Category = "mentor",
    string? Type = "mentor_no_show",
    int? BookingId = null,
    int? AttemptId = null,
    int? OrderId = null,
    string? Reason = null,
    string? ErrorMessage = null)
{
    public IReadOnlyDictionary<int, string> BookingLabels { get; init; } = new Dictionary<int, string>();
    public IReadOnlyDictionary<int, string> AttemptLabels { get; init; } = new Dictionary<int, string>();
    public IReadOnlyDictionary<int, string> OrderLabels { get; } = EligibleSubjects.Orders.ToDictionary(o => o.OrderId,
        o => $"Order {o.OrderCode} — {o.PackageName} ({o.Amount:N0} ₫, {o.Status})");
    public SupportReportViewModel WithDates(DateTimeFormatter formatter) => this with {
        BookingLabels = EligibleSubjects.Bookings.ToDictionary(b => b.BookingId, b => $"Booking #{b.BookingId} — {b.MentorName} ({(b.ScheduledAt.HasValue ? formatter.FormatCustom(b.ScheduledAt.Value, "dd MMM yyyy, HH:mm") : "Scheduled")}, {b.Status})"),
        AttemptLabels = EligibleSubjects.Attempts.ToDictionary(a => a.AttemptId, a => $"Exam #{a.AttemptId} — {a.ExamTitle} ({formatter.FormatCustom(a.StartedAt, "dd MMM yyyy, HH:mm")}{(a.TotalScore.HasValue ? $", score {a.TotalScore}" : "")})")
    };
}
