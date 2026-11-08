using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>
/// #237 / D4 / D-19: one learner's mastery of one <b>cell</b> — a (topic, level) pair — never of an
/// item and never of a certificate's category.
/// <para>
/// <b>Why there is no <c>CertId</c>.</b> A topic belongs to the language, not to a certificate (D-3), so
/// a learner who switches certificate keeps one comparable record per cell (D-19). The legacy
/// <see cref="UserCategoryMastery"/> carries <c>CertId</c> because <c>QuestionCategories</c> was
/// certificate-scoped; that row is keyed to a bucket that no longer exists in the rebuilt model.
/// </para>
/// <para>
/// <b>The row is the state, and the estimate is stored as its sufficient statistic.</b>
/// <see cref="WeightedCorrect"/> and <see cref="WeightedObservations"/> are PLC-67's
/// <c>Σ w·c</c> and <c>Σ w·n</c>; the probability is a function of them and the prior, so it is
/// deliberately <b>not</b> a column (a stored probability could disagree with its own arithmetic).
/// <see cref="ObservationCount"/> is the unweighted count PLC-68's minimum reads, kept separately
/// because a weighted sum is not a count.
/// </para>
/// </summary>
public partial class UserCellMastery
{
    [Key]
    public int UserId { get; set; }

    [Key]
    public int TopicId { get; set; }

    /// <summary>Authored levels only: a cell is content, and content is never authored at the floor.</summary>
    [Key]
    public CefrLevel CefrLevel { get; set; }

    /// <summary>
    /// One of the four <see cref="MasteryState"/> codes. Stored as its snake_case code, exactly as the
    /// legacy table stores it, because <c>CK_UserCellMasteries_State</c> is a closed set.
    /// </summary>
    [StringLength(12)]
    [Unicode(false)]
    public string State { get; set; } = null!;

    /// <summary>PLC-67 <c>Σ w·c</c>: the recency-weighted correct count.</summary>
    public double WeightedCorrect { get; set; }

    /// <summary>PLC-67 <c>Σ w·n</c>: the recency-weighted observation count.</summary>
    public double WeightedObservations { get; set; }

    /// <summary>PLC-68's <c>n_min</c> counts observations, not weights: 12 items or 3 tasks.</summary>
    public int ObservationCount { get; set; }

    /// <summary>
    /// Consecutive wrong answers. <b>A correct answer resets it to zero</b> — the half a counter that
    /// only increments gets wrong, and the half that makes "wrong, correct, wrong" not a lapse.
    /// </summary>
    public int ConsecutiveWrong { get; set; }

    public DateTimeOffset? NextReviewDueAt { get; set; }

    /// <summary>
    /// The last graded answer in this cell. PLC-71's 28-day clock reads it, and it is also the boundary
    /// a mock section must postdate to save the cell from decay.
    /// </summary>
    public DateTimeOffset? LastPractisedAt { get; set; }

    /// <summary>Set on entering mastered and never cleared by a lapse or a decay.</summary>
    public DateTimeOffset? MasteredAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// #191 / ADR-0012: which service declared this write. Nullable for the same reason every other
    /// covered table's is: the trigger, not the column, is what refuses a write that declares nothing.
    /// </summary>
    [StringLength(64)]
    [Unicode(false)]
    public string? WrittenByService { get; set; }

    /// <summary>#191: monotonic ordering token, so a stale callback cannot overwrite a newer state.</summary>
    public int ProjectionVersion { get; set; }

    [ForeignKey("TopicId")]
    [InverseProperty("UserCellMasteries")]
    public virtual Topic Topic { get; set; } = null!;

    [ForeignKey("UserId")]
    [InverseProperty("UserCellMasteries")]
    public virtual User User { get; set; } = null!;
}
