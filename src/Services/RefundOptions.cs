
namespace Services;

/// <summary>
/// #281: the ceiling a refund with no order behind it is held to. It is configuration because the
/// number is a business decision, not a fact about the code: a technical failure worth 500 credits
/// should not require a deployment to compensate. A refund tied to an order is capped by that order
/// instead (the credits it granted), which is why this one only applies when
/// <c>Refunds.OrderId</c> is null.
/// </summary>
public sealed class RefundOptions
{
    public const string SectionName = "Refunds";

    /// <summary>Default 200, the ticket's number. A negative or zero value falls back to the default
    /// rather than making every orderless refund impossible.</summary>
    public int OrderlessCeiling { get; set; } = 200;
}
