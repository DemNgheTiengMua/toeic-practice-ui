using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>
/// #226 / ADM-11: the recorded verdict that a paper's six publish invariants all held, and when.
/// <para>
/// <b>Why a row and not a recomputation.</b> The six invariants are C#: gate four's envelope, span
/// independence and audio-gap rules are pure functions shared with ingestion, and the stratum audit
/// and rubric lock are joins across four tables. A T-SQL trigger cannot call any of that, so the
/// choice is between persisting the verdict and copying the rules into a second language. #208 made
/// the same call for the rubric ladder, for the same reason: a copy of the arithmetic would drift,
/// and the copy that drifted would be the one nothing asserts.
/// </para>
/// <para>
/// <b>What the trigger does with it.</b> <c>trg_Exams_PublishRequiresAudit</c> refuses a transition
/// into <c>published</c> unless this row exists and is at least as new as the paper's own
/// <c>UpdatedAt</c>. The second half is what makes a publish-then-edit sequence unable to ride the
/// first pass through a raw <c>UPDATE</c>.
/// </para>
/// </summary>
public partial class ExamPublishAudit
{
    /// <summary>
    /// #226: the one service allowed to write a publish pass. A trigger refuses any other name, because
    /// a pass nobody can attribute is a pass nobody took.
    /// <para>
    /// The constant lives here rather than in <see cref="DerivedFactWriters"/> because this fact is not
    /// in the registry: that set is audited for a <c>ProjectionVersion</c> column, and a pass has
    /// neither a callback nor a race to version - <c>PassedAt</c> against <c>Exams.UpdatedAt</c> is the
    /// currency test. #208 made the same call for the rubric lock.
    /// </para>
    /// </summary>
    public const string Writer = "AdminExamService";

    [Key]
    public int ExamId { get; set; }

    /// <summary>When the six invariants last all held. Compared against <see cref="Exam.UpdatedAt"/>.</summary>
    public DateTimeOffset PassedAt { get; set; }

    /// <summary>
    /// ADR-0012: the one service allowed to write this verdict. A trigger refuses any other name,
    /// because a pass nobody can attribute is a pass nobody took.
    /// </summary>
    [StringLength(64)]
    [Unicode(false)]
    public string WrittenByService { get; set; } = null!;

    [ForeignKey("ExamId")]
    [InverseProperty("PublishAudit")]
    public virtual Exam Exam { get; set; } = null!;
}
