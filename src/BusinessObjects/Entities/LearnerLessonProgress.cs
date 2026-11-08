using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[Table("LearnerLessonProgress")]
[Index("UserId", "LessonId", "Module", Name = "UQ_LLP_UserLessonModule", IsUnique = true)]
public partial class LearnerLessonProgress
{
    [Key]
    public int ProgressId { get; set; }

    public int UserId { get; set; }

    public int LessonId { get; set; }

    public int CertId { get; set; }

    public int? LastViewedCardId { get; set; }

    public int CardsCompleted { get; set; }

    public int TotalCards { get; set; }

    /// <summary>#240: progress is per learner per lesson PER MODULE, so a remedial visit has its own
    /// place to resume and its own "complete". One row per lesson would let a finished primary module
    /// tick off an unread remedial one.</summary>
    [StringLength(10)]
    [Unicode(false)]
    public string Module { get; set; } = LessonModule.Primary;

    public bool IsCompleted { get; set; }

    public DateTimeOffset FirstOpenedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    [ForeignKey("LastViewedCardId")]
    [InverseProperty("LearnerLessonProgresses")]
    public virtual LessonCard? LastViewedCard { get; set; }

    [ForeignKey("LessonId, CertId")]
    [InverseProperty("LearnerLessonProgresses")]
    public virtual Lesson Lesson { get; set; } = null!;

    [ForeignKey("UserId")]
    [InverseProperty("LearnerLessonProgresses")]
    public virtual User User { get; set; } = null!;
}
