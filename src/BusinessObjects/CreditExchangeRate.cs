namespace BusinessObjects;

/// <summary>
/// Platform-wide credit conversion rate for mentor compensation and payout exports (ADR-0016).
/// </summary>
public static class CreditExchangeRate
{
    public const decimal VndPerCredit = 20_000m;

    public static decimal ToVnd(int credits) => credits * VndPerCredit;
}
