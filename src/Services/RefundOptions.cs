
namespace Services;

/// <summary>
/// #281/#282: the ceiling a refund with no order behind it is held to. It is configuration because the
/// number is a business decision, not a fact about the code: a technical failure worth 500 credits
/// should not require a deployment to compensate.
/// <para>
/// <b>One setting, two methods.</b> The number is read as credits by a credit refund and as dong by a
/// money refund. A second knob would let the two drift, and two ceilings for one question is how two
/// screens come to disagree. A refund tied to an order is capped by that order instead - the credits it
/// granted, or the money it was paid - which is why this only applies when <c>Refunds.OrderId</c> is null.
/// </para>
/// </summary>
public sealed class RefundOptions
{
    public const string SectionName = "Refunds";

    /// <summary>Default 200, the ticket's number. A negative or zero value falls back to the default
    /// rather than making every orderless refund impossible.</summary>
    public int OrderlessCeiling { get; set; } = 200;
}
