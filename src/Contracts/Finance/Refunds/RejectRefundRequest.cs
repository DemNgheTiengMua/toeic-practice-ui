
using System.ComponentModel.DataAnnotations;

namespace Contracts;

/// <summary>A refusal is on record with its justification; the spec's AC is "reject requires a
/// reason".</summary>
public sealed record RejectRefundRequest(
    [property: Required(ErrorMessage = "A reason is required on every refund.")]
    [property: MaxLength(400, ErrorMessage = "A refund reason cannot exceed 400 characters.")]
    string Reason);
