using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[Index("LearnerPathId", Name = "IX_Steps_LearnerPath")]
[Index("LearnerPathId", "WeekNumber", "StepId", Name = "IX_Steps_LearnerPath_Week")]
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

    /// <summary>#154: the planning week (1–4) this step belongs to. NULL on steps written before
    /// #154 or by seed/manual scripts, which have no plan behind them.</summary>
    public int? WeekNumber { get; set; }

    /// <summary>#154: how long the step is planned to take. The week's total is what
    /// <c>TargetBands.WeeklyStudyHours</c> is spent on.</summary>
    public int? EstimatedMinutes { get; set; }

    /// <summary>#154: the accuracy this step has to reach for its week to count as on track.</summary>
    public int TargetAccuracyPct { get; set; } = 70;

    /// <summary>#154: measured accuracy (correct/answered), never guessed. NULL for a lesson, which
    /// has nothing to score.</summary>
    [Column(TypeName = "decimal(5, 2)")]
    public decimal? ActualAccuracyPct { get; set; }

    /// <summary>#191: which service declared this closure. Required by the trigger when the step is
    /// closed; NULL while the step moves between open states.</summary>
    [StringLength(64)]
    [Unicode(false)]
    public string? WrittenByService { get; set; }

    /// <summary>#191: monotonic ordering token, so an older out-of-order callback cannot overwrite a
    /// newer result.</summary>
    public int ProjectionVersion { get; set; }

    [ForeignKey("LearnerPathId, TemplateId")]
    [InverseProperty("LearnerPathSteps")]
    public virtual LearnerPath LearnerPath { get; set; } = null!;

    [ForeignKey("PathModuleItemId, TemplateId")]
    [InverseProperty("LearnerPathSteps")]
    public virtual PathModuleItem PathModuleItem { get; set; } = null!;

    [InverseProperty("LearnerPathStep")]
    public virtual ICollection<PracticeSession> PracticeSessions { get; set; } = new List<PracticeSession>();
}
