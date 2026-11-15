
namespace Contracts;

public sealed record AdminComplaintDetailDto(
    int ComplaintId,
    int UserId,
    string LearnerName,
    string LearnerEmail,
    ComplaintTypeDto Type,
    string Reason,
    ComplaintStatusDto Status,
    string SubjectType,
    int SubjectId,
    string SubjectSummary,
    string? SubjectDetails,
    DateTimeOffset CreatedAt,
    string? Resolution,
    string? InternalNotes,
    int? ResolvedByUserId,
    string? ResolvedByUserName,
    DateTimeOffset? ResolvedAt,
    int? ResolvedByGrantTxnId,
    int? GrantedCredits,
    /// <summary>#347: the negative credit-ledger deltas attributable to the subject - by attempt
    /// (a sitting charge and an AI-grading charge both land on one attempt) or by booking. Never a
    /// cost constant: only the ledger knows how often this subject was actually charged.</summary>
    int CreditsSpent = 0,
    /// <summary>#347: the order's amount when the subject is an order - money that arrived.</summary>
    decimal? MoneyPaid = null,
    /// <summary>#347: the method the subject implies, so a compensation cannot return the wrong
    /// currency. Null when the complaint carries no subject at all.</summary>
    RefundMethodDto? ImpliedRefundMethod = null);
