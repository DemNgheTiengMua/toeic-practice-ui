using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[Index("UserId", "CertId", Name = "IX_LearnerPaths_User")]
[Index("LearnerPathId", "TemplateId", Name = "UQ_LearnerPaths_Id_Template", IsUnique = true)]
[Index("LearnerPathId", "UserId", Name = "UQ_LearnerPaths_Id_User", IsUnique = true)]
public partial class LearnerPath
{
    [Key]
    public int LearnerPathId { get; set; }

    public int UserId { get; set; }

    public int CertId { get; set; }

    public int TemplateId { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string? CurrentBandCode { get; set; }

    public int? PlacementId { get; set; }

    [StringLength(10)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    public DateTimeOffset StartedAt { get; set; }

    [InverseProperty("LearnerPath")]
    public virtual ICollection<AiInsight> AiInsights { get; set; } = new List<AiInsight>();

    [ForeignKey("CertId")]
    [InverseProperty("LearnerPaths")]
    public virtual Certificate Cert { get; set; } = null!;

    [InverseProperty("LearnerPath")]
    public virtual ICollection<CreditTransaction> CreditTransactions { get; set; } = new List<CreditTransaction>();

    [InverseProperty("LearnerPath")]
    public virtual ICollection<LearnerPathStep> LearnerPathSteps { get; set; } = new List<LearnerPathStep>();

    [ForeignKey("TemplateId, CertId")]
    [InverseProperty("LearnerPaths")]
    public virtual PathTemplate PathTemplate { get; set; } = null!;

    [ForeignKey("PlacementId, UserId")]
    [InverseProperty("LearnerPaths")]
    public virtual PlacementTest? PlacementTest { get; set; }

    [ForeignKey("UserId")]
    [InverseProperty("LearnerPaths")]
    public virtual User User { get; set; } = null!;
}
