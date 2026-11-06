using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>
/// #202 / D0.5: the content budget, one row per (skill, level).
/// <para>
/// A table rather than a constant because the pool is sized <em>by cell</em> and the size is budget,
/// not a rule: a later content ticket re-sizes a cell by writing a row here, where a C# constant or a
/// <c>CHECK</c> would make the volume tiers a second, silent copy that nobody can retune without a
/// schema edit. The column is width-neutral because a productive skill is sized in tasks and an
/// objective one in items (8/4 vs 30/15 in D0.5), so <see cref="TargetItemCount"/> is a target unit
/// count either way.
/// </para>
/// </summary>
[PrimaryKey(nameof(Modality), nameof(CefrLevel))]
public partial class ContentQuota
{
    /// <summary>One of the four skills (CK_ContentQuotas_Modality).</summary>
    [StringLength(10)]
    [Unicode(false)]
    public string Modality { get; set; } = null!;

    /// <summary>An authored level: A1 to C2. The floor is never a cell (CK_ContentQuotas_Level).</summary>
    [StringLength(10)]
    [Unicode(false)]
    public string CefrLevel { get; set; } = null!;

    /// <summary>How many units this cell should hold. A budget, so it is always at least one.</summary>
    public int TargetItemCount { get; set; }
}
