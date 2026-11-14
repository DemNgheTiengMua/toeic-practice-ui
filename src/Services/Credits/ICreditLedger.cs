using BusinessObjects;
using Contracts;

namespace Services.Credits;

/// <summary>
/// Authoritative seam for the append-only credit ledger.
/// Encapsulates wallet balance queries, "block before charge" overdraft checks,
/// trigger invariants, and transaction persistence.
/// </summary>
public interface ICreditLedger
{
    /// <summary>
    /// Gets the current running credit balance for the learner.
    /// </summary>
    Task<int> GetBalanceAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Debits credits for starting a mock exam attempt.
    /// </summary>
    Task<CreditReceipt> ChargeMockExamAsync(int userId, int attemptId, int amount = 1, CancellationToken cancellationToken = default);

    /// <summary>
    /// Debits credits for submitting free responses to AI grading.
    /// </summary>
    Task<CreditReceipt> ChargeAiGradingAsync(int userId, int attemptId, int amount = 1, CancellationToken cancellationToken = default);

    /// <summary>
    /// Debits credits for booking a mentor session.
    /// </summary>
    Task<CreditReceipt> ChargeMentorBookingAsync(int userId, int bookingId, int priceCredits, CancellationToken cancellationToken = default);

    /// <summary>
    /// Credits a refund for a cancelled mentor booking.
    /// </summary>
    Task<CreditReceipt> RefundBookingAsync(int userId, int bookingId, int amount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Credits purchased credits from a paid order into the user's wallet.
    /// </summary>
    Task<CreditReceipt> GrantPurchaseAsync(int userId, int orderId, int amount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs an administrative credit adjustment (grant or revocation).
    /// </summary>
    Task<CreditReceipt> AdjustCreditsAsync(int targetUserId, int? adminUserId, int delta, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// #281: the credits an approved credit refund returns. It is the same adjust path as
    /// <see cref="AdjustCreditsAsync"/> but states its own reason, because the ledger's
    /// <c>refund_credit</c> code is what lets "credits refunded this month" be a number the product can
    /// answer - and what <c>trg_Refunds_CreditTxnIsRefundCredit</c> demands the refund point at.
    /// </summary>
    Task<CreditReceipt> GrantRefundCreditsAsync(int userId, int adminUserId, int amount, string note, CancellationToken cancellationToken = default);

    /// <summary>
    /// #289: debits credits for starting a placement retake, posting a <c>placement_retake</c>
    /// negative delta with the chosen scope recorded in <c>Note</c> and no source/admin columns.
    /// </summary>
    Task<CreditReceipt> ChargePlacementRetakeAsync(int userId, PlacementScope scope, int amount, CancellationToken cancellationToken = default);

    /// <summary>
    /// #313: debits 5 credits for generating a 4-week Learning Path (<c>learning_path</c> reason).
    /// </summary>
    Task<CreditReceipt> ChargeLearningPathAsync(int userId, int amount = 5, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the ordered ledger history for a learner's wallet.
    /// </summary>
    Task<IReadOnlyList<CreditLedgerEntryDto>> GetLedgerHistoryAsync(int userId, CancellationToken cancellationToken = default);
}
