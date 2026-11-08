using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>
/// #227 / D20: the edition a learner sat - frozen at the moment the paper went live.
/// <para>
/// #186 already pins the <em>items</em> per attempt: <c>AttemptAnswers.ItemVersionId</c> means a
/// re-versioned or retired item cannot change what a finished attempt renders. That is half of an
/// edition. The other half is everything a score depends on that is shared certificate-wide, and
/// which a review would otherwise re-read from today's configuration:
/// </para>
/// <list type="bullet">
/// <item>the score aggregation rule (a total means nothing without the rule that made it),</item>
/// <item>the band grid, by which a total becomes a band <em>name</em> a page prints,</item>
/// <item>the cut-score table version a level was projected from (the gap #222 flagged),</item>
/// <item>and the per-skill timer weights the sitting is divided into phases by.</item>
/// </list>
/// <para>
/// <b>An edition that re-derives any of those at read time is not pinned.</b> A historical result must
/// render the numbers it was scored with, so the values are columns here, written once by
/// <c>trg_Exams_PublishFreezesEdition</c> and never recomputed by a reader.
/// </para>
/// <para>
/// The items themselves are not copied. They cannot be: an attempt already names its version per
/// answer, and copying 200 rows per publish would be a second answer to "which version did this
/// learner see". The exam row is the edition (<see cref="Exam.EditionNumber"/>), and a structural
/// repair forks a new one rather than mutating this.
/// </para>
/// </summary>
public partial class ExamEdition
{
    [Key]
    public int ExamId { get; set; }

    /// <summary>Carried down so the cut-score version can be pinned by a composite key.</summary>
    public int CertId { get; set; }

    public int EditionNumber { get; set; }

    /// <summary>The sitting's allotted time in seconds, as the paper declared it when it went live.</summary>
    public int TotalDurationSeconds { get; set; }

    /// <summary>
    /// The phase weights, frozen: one object per skill the paper examines, in sitting order. This is
    /// the exact input <c>AttemptEngine.BuildAttemptPhases</c> divides the attempt by, so the phase bar
    /// a mid-sitting learner sees cannot move when a <c>Skills</c> row is edited underneath them.
    /// </summary>
    public string PhasePlanJson { get; set; } = null!;

    [StringLength(20)]
    [Unicode(false)]
    public string ScoreAggregationRule { get; set; } = null!;

    /// <summary>
    /// The cut-score table version a level was projected from, or NULL when the certificate had none
    /// at freeze time. NULL is a recorded fact - "no table existed" - not a value to be filled in from
    /// today's newest table.
    /// </summary>
    public int? CutScoreTableVersion { get; set; }

    /// <summary>The band grid, frozen: <c>[{Code, Name, MinTotal, MaxTotal}]</c>.</summary>
    public string BandsJson { get; set; } = null!;

    public DateTimeOffset FrozenAt { get; set; }

    /// <summary>ADR-0012: the one service allowed to write this snapshot.</summary>
    [StringLength(64)]
    [Unicode(false)]
    public string WrittenByService { get; set; } = null!;

    public int ProjectionVersion { get; set; } = 1;

    [ForeignKey("ExamId")]
    [InverseProperty("Edition")]
    public virtual Exam Exam { get; set; } = null!;

    [ForeignKey("CertId, CutScoreTableVersion")]
    public virtual CefrCutScoreVersion? CutScoreVersion { get; set; }
}
