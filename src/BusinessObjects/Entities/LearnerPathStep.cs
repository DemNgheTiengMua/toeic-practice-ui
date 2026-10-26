using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[Index("LearnerPathId", Name = "IX_Steps_LearnerPath")]
public partial class LearnerPathStep
{
    [Key]
    public int StepId { get; set; }

    public int LearnerPathId { get; set; }

    public int PathModuleItemId { get; set; }

    public int TemplateId { get; set; }

    [StringLength(12)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    [Column(TypeName = "decimal(6, 2)")]
    public decimal? Score { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    [ForeignKey("LearnerPathId, TemplateId")]
    [InverseProperty("LearnerPathSteps")]
    public virtual LearnerPath LearnerPath { get; set; } = null!;

    [ForeignKey("PathModuleItemId, TemplateId")]
    [InverseProperty("LearnerPathSteps")]
    public virtual PathModuleItem PathModuleItem { get; set; } = null!;

    [InverseProperty("LearnerPathStep")]
    public virtual ICollection<PracticeSession> PracticeSessions { get; set; } = new List<PracticeSession>();
}
