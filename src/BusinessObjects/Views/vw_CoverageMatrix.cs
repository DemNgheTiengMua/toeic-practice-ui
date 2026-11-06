using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Views;

/// <summary>
/// #202 / S-01: one cell of the warehouse matrix. Read-only, keyless, and <b>always 120 rows</b> —
/// the cell grid is (topic, quota level), never a group over the items, so a cell that holds nothing
/// is a row reporting a deficit rather than an absent row the screen cannot show.
/// </summary>
[Keyless]
public partial class vw_CoverageMatrix
{
    public int TopicId { get; set; }

    public string Modality { get; set; } = null!;

    public string TopicCode { get; set; } = null!;

    public string TopicName { get; set; } = null!;

    public string CefrLevel { get; set; } = null!;

    /// <summary>The cell's content budget, read from its <c>ContentQuotas</c> row.</summary>
    public int TargetItemCount { get; set; }

    /// <summary>Active stock only: a retired or draft item is not depth a learner can drill.</summary>
    public int ActiveItemCount { get; set; }

    /// <summary>Active and placement-eligible: a retired eligible item is still not stock.</summary>
    public int PlacementEligibleItemCount { get; set; }

    /// <summary><c>TargetItemCount - ActiveItemCount</c>, floored at zero.</summary>
    public int Deficit { get; set; }
}
