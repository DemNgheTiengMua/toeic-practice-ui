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
/// </summary>
public partial class GradingRubricLock
{
    [Key]
    public int SkillId { get; set; }

    public int CertId { get; set; }

    public DateTimeOffset LockedAt { get; set; }

    [ForeignKey("SkillId, CertId")]
    [InverseProperty("RubricLock")]
    public virtual Skill Skill { get; set; } = null!;
}
