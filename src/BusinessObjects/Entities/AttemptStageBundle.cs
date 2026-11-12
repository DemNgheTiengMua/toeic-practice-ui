using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>
/// #274: <b>why</b> this sitting was served the paper it was served - one row per skill per stage.
/// <para>
/// A paper normally comes from the exam's shared roster (<c>ExamItems</c>), which is per EXAM:
/// <c>UQ_ExamItems_Exam_Section_Order</c> allows one position per (exam, section, position). A
/// placement sitting draws its own, so the draw has nowhere to live on the roster. This table records
/// the decision; <see cref="AttemptItemResponse"/> records the positions it produced.
/// </para>
/// <para>
/// The band pair is two columns, not a packed <c>B1_B2</c> string. A packed token would have to be
/// parsed by every reader, which is a second definition of the level scale #182 forbids.
/// </para>
/// </summary>
[PrimaryKey(nameof(AttemptId), nameof(SkillId), nameof(StageNumber))]
public partial class AttemptStageBundle
{
    [Key]
    public int AttemptId { get; set; }

    /// <summary>H-5: carried down from the attempt, so the skill must be this certificate's own.</summary>
    [Key]
    public int CertId { get; set; }

    [Key]
    public int SkillId { get; set; }

    /// <summary>1 = the fixed per-band draw; 2 = the pair stage one routed to.</summary>
    [Key]
    public int StageNumber { get; set; }

    public CefrLevel BandLower { get; set; }

    public CefrLevel BandUpper { get; set; }

    /// <summary>ADR-0012: the writer identity, required on any write of this fact.</summary>
    public string? WrittenByService { get; set; }

    public int ProjectionVersion { get; set; }

    [ForeignKey("AttemptId, CertId")]
    [InverseProperty("AttemptStageBundleExamAttempts")]
    public virtual ExamAttempt ExamAttempt { get; set; } = null!;

    [ForeignKey("SkillId, CertId")]
    [InverseProperty("AttemptStageBundleSkillNavigations")]
    public virtual Skill SkillNavigation { get; set; } = null!;
}
