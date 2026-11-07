using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>
/// #192: a rubric is locked once it is calibrated. The row's existence IS the lock - there is no
/// <c>IsLocked</c> flag to forget to flip. Triggers keep a locked rubric's criteria and descriptor
/// grid frozen; this entity exists so application code can ask whether a rubric is locked, and so
/// EF never issues an <c>OUTPUT</c> clause against the triggered criteria table.
/// <para>
/// #208: the lock also carries the ladder's verdict. A rubric with all 28 cells describes a shape; the
/// ladder is the evidence that the shape separates adjacent bands. <see cref="LadderOutcome"/> NULL
/// means the ladder never ran, which is the state the lock trigger refuses by name.
/// </para>
/// </summary>
public partial class GradingRubricLock
{
    [Key]
    public int SkillId { get; set; }

    public int CertId { get; set; }

    public DateTimeOffset LockedAt { get; set; }

    /// <summary>
    /// #208: <see cref="Services.RubricCalibrationOutcome"/> as the schema stores it - see
    /// <see cref="RubricLadderOutcomes"/>. NULL is "not run", never a pass.
    /// </summary>
    [MaxLength(20)]
    public string? LadderOutcome { get; set; }

    /// <summary>The runner's sentence, which names the criterion a flat or shallow segment stalled on.</summary>
    [MaxLength(400)]
    public string? LadderReason { get; set; }

    public DateTimeOffset? LadderRunAt { get; set; }

    [ForeignKey("SkillId, CertId")]
    [InverseProperty("RubricLock")]
    public virtual Skill Skill { get; set; } = null!;
}
