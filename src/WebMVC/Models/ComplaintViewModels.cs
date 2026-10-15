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
    string? ErrorMessage = null);

public sealed record SupportHistoryViewModel(
    IReadOnlyList<LearnerComplaintDto> Complaints);

public sealed record AdminComplaintsViewModel(
    AdminComplaintsSummaryDto Summary,
    string? Query,
    string? Status,
    int CurrentPage);

public sealed record AdminComplaintDetailViewModel(
    AdminComplaintDetailDto Complaint,
    string? ErrorMessage = null);
