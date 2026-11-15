
namespace Contracts;

/// <summary>
/// #345 story 11: one refund recorded against a complaint, as the complaint detail shows it. The
/// method owns which value is reported - a credit refund returned credits, a money refund returned
/// dong - so exactly one of <paramref name="CreditCount"/> and <paramref name="Amount"/> is set.
/// <para>
/// Read-only, and deliberately not the whole <c>AdminRefundDetailDto</c>: the complaint's history needs
/// to say what was returned and what became of it, while the refund's own screen owns the decision trail.
/// </para>
/// </summary>
public sealed record AdminComplaintRefundDto(
    int RefundId,
    RefundMethodDto Method,
    RefundStatusDto Status,
    int? CreditCount,
    decimal? Amount,
    DateTimeOffset CreatedAt);

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
    RefundMethodDto? ImpliedRefundMethod = null,
    /// <summary>#345: the refunds recorded against this complaint, newest first. A promise with no refund
    /// yet is an empty list; a rejected one stays, because a refusal is part of the incident's history.</summary>
    IReadOnlyList<AdminComplaintRefundDto>? Refunds = null);
