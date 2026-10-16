using Contracts;

namespace Services.Credits;

/// <summary>
/// Domain exception raised when a credit ledger invariant, pre-condition, or database trigger check fails.
/// </summary>
public class CreditLedgerException : Exception
{
    public const string InsufficientCredits = ApiError.Codes.InsufficientCredits;
    public const string DuplicateCharge = "duplicate_charge";
    public const string TargetOwnerMismatch = "target_owner_mismatch";
    public const string NegativeBalanceBlocked = "negative_balance_blocked";
    public const string InvalidCharge = "invalid_charge";

    public string Code { get; }
    public int StatusCode { get; }

    public CreditLedgerException(string code, string message, int statusCode = 400, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
        StatusCode = statusCode;
    }
}
