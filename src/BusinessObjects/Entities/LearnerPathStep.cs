using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>
/// #232 / D10: the step is a coordinate, not a line in a template.
/// <para>
/// <c>(SkillId, TopicId, TargetCefr, StepType, SizingTier)</c> is the whole of what a step is. There is
/// no <c>PathModuleItemId</c>: a step that needed a module row to say what it was could not be
/// re-planned, and re-planning is the point of a path.
/// </para>
/// <para>
/// <c>TargetCefr</c> is this skill's own level. The certificate's target band is a different quantity
/// on a different axis (D1/D6) and is never read into it.
/// </para>
/// </summary>
[Index("LearnerPathId", Name = "IX_Steps_LearnerPath")]
[Index("LearnerPathId", "WeekNumber", "StepId", Name = "IX_Steps_LearnerPath_Week")]
[Index("LearnerPathId", "WeekNumber", "WeekSequence", Name = "IX_Steps_LearnerPath_Sequence")]
[Index("TopicId", "TargetCefr", "StepType", Name = "IX_Steps_Coordinate")]
public partial class LearnerPathStep
{
    [Key]
    public int StepId { get; set; }

    public int LearnerPathId { get; set; }

    /// <summary>#232: carried down from the path so every coordinate below is pinned to one certificate.</summary>
    public int CertId { get; set; }

    /// <summary>The skill this step practises. One modality, per CONTEXT.md.</summary>
    public int SkillId { get; set; }

    [StringLength(10)]
    [Unicode(false)]
    public string Modality { get; set; } = null!;

    /// <summary>The canonical topic (#183): a step cannot invent a twenty-first.</summary>
    public int TopicId { get; set; }

    /// <summary>The level <b>this skill</b> is practised at. Never the certificate's target band.</summary>
    [StringLength(10)]
    [Unicode(false)]
    public string TargetCefr { get; set; } = null!;

    /// <summary>One of D10's three execution paths; see <see cref="PathStepType"/>.</summary>
    [StringLength(20)]
    [Unicode(false)]
    public string StepType { get; set; } = null!;

    /// <summary>One of D10's three sizes; see <see cref="SizingTier"/>.</summary>
    [StringLength(12)]
    [Unicode(false)]
    public string SizingTier { get; set; } = null!;

    /// <summary>A lesson step's reader target. NULL for a drill and for a mock section.</summary>
    public int? RefLessonId { get; set; }

    /// <summary>A mock section's exam. NULL for a drill and for a lesson.</summary>
    public int? RefExamId { get; set; }

    [StringLength(12)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    [Column(TypeName = "decimal(6, 2)")]
    public decimal? Score { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>#154: the planning week (1–4) this step belongs to. NULL on steps written by hand
    /// or by seed, which have no plan behind them.</summary>
    public int? WeekNumber { get; set; }

    /// <summary>#232: absolute ordering inside the week. Ordering is pedagogical, so it is a field
    /// the planner sets — never the row's insertion order.</summary>
    public int WeekSequence { get; set; }

    /// <summary>#232: the count the sizing tier resolved to for this cell. NULL for a lesson, which
    /// is sized in minutes alone.</summary>
    public int? PlannedItemCount { get; set; }

    /// <summary>#154: how long the step is planned to take.</summary>
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

    /// <summary>#191: monotonic ordering token, so an older out-of-order callback cannot overwrite a
    /// newer result.</summary>

    [ForeignKey("LearnerPathId, CertId")]
    [InverseProperty("LearnerPathSteps")]
    public virtual LearnerPath LearnerPath { get; set; } = null!;

    [ForeignKey("SkillId, Modality")]
    [InverseProperty("LearnerPathSteps")]
    public virtual Skill Skill { get; set; } = null!;

    [ForeignKey("TopicId")]
    [InverseProperty("LearnerPathSteps")]
    public virtual Topic Topic { get; set; } = null!;

    [ForeignKey("RefLessonId, CertId")]
    [InverseProperty("LearnerPathSteps")]
    public virtual Lesson? Lesson { get; set; }

    [ForeignKey("RefExamId, CertId")]
    [InverseProperty("LearnerPathSteps")]
    public virtual Exam? Exam { get; set; }

    [InverseProperty("LearnerPathStep")]
    public virtual ICollection<PracticeSession> PracticeSessions { get; set; } = new List<PracticeSession>();
}
