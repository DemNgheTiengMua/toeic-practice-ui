
namespace Contracts;

/// <summary>A refusal is on record with its justification; the spec's AC is "reject requires a
/// reason".</summary>
public sealed record RejectRefundRequest(string Reason);
