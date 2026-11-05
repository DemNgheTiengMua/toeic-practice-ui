using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>
/// #193: one calibrated benchmark response for a criterion, at band 2, 3 or 4 (D0.6 mechanism 5).
/// <see cref="Band"/> is the CEFR band the response was written to demonstrate, so three rows are what
/// "anchored at two, three and four" counts from.
/// </summary>
public partial class GradingCriterionAnchor
{
    public int CriterionId { get; set; }

    [Range(2, 4)]
    public byte Band { get; set; }

    /// <summary>A whole written answer, not a note about one. NVARCHAR(MAX) in the schema.</summary>
    public string ResponseText { get; set; } = null!;

    [ForeignKey("CriterionId")]
    [InverseProperty("Anchors")]
    public virtual GradingCriterion Criterion { get; set; } = null!;
}
