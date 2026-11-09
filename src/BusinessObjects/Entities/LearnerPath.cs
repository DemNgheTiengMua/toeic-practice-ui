using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[Index("UserId", "CertId", Name = "IX_LearnerPaths_User")]
[Index("LearnerPathId", "UserId", Name = "UQ_LearnerPaths_Id_User", IsUnique = true)]
[Index("LearnerPathId", "CertId", Name = "UQ_LearnerPaths_Id_Cert", IsUnique = true)]
public partial class LearnerPath
{
    [Key]
    public int LearnerPathId { get; set; }

    public int UserId { get; set; }

    public int CertId { get; set; }

    /// <summary>
    /// #232: there is no <c>TemplateId</c>. A path is synthesised from current capability and re-planned
    /// on four named triggers; nothing about it comes from a template row.
    /// </summary>
    [StringLength(20)]
    [Unicode(false)]
    public string? CurrentBandCode { get; set; }

    public int? PlacementId { get; set; }

    [StringLength(10)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>
    /// #233 / D10: the day the live window's first week began. With <see cref="ActiveWeek"/> this is
    /// the whole close rule's input: a week closes when the calendar crosses day 8, or when all its
    /// steps are done (<see cref="PathWindow.DueSlides"/>).
    /// </summary>
    public DateOnly WindowStartDate { get; set; }

    /// <summary>#233: the first live week. Only ever moves forward, and only when a week closes.</summary>
    public int ActiveWeek { get; set; } = 1;

    /// <summary>#233: how many times the window has slid. A second slide at the same instant sees this
    /// move and archives nothing twice.</summary>
    public int WindowSlides { get; set; }

    [InverseProperty("LearnerPath")]
    public virtual ICollection<AiInsight> AiInsights { get; set; } = new List<AiInsight>();

    [ForeignKey("CertId")]
    [InverseProperty("LearnerPaths")]
    public virtual Certificate Cert { get; set; } = null!;

    [InverseProperty("LearnerPath")]
    public virtual ICollection<LearnerPathStep> LearnerPathSteps { get; set; } = new List<LearnerPathStep>();

    /// <summary>#233: the weeks this path has closed, frozen by <c>trg_LearnerPathWeeks_SingleWriter</c>.</summary>
    [InverseProperty("LearnerPath")]
    public virtual ICollection<LearnerPathWeek> LearnerPathWeeks { get; set; } = new List<LearnerPathWeek>();

    [ForeignKey("PlacementId, UserId")]
    [InverseProperty("LearnerPaths")]
    public virtual PlacementTest? PlacementTest { get; set; }

    [ForeignKey("UserId")]
    [InverseProperty("LearnerPaths")]
    public virtual User User { get; set; } = null!;
}
