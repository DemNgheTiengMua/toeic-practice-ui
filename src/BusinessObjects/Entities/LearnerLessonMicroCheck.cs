using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>
/// #159 review: one row per micro-check a learner has answered. It exists so the same card cannot be
/// graded twice: without it, every POST moved mastery again and one known answer posted repeatedly
/// farmed a category to Mastered - the "click until green" the drill half closed in #155.
/// </summary>
[PrimaryKey("UserId", "CardId")]
public partial class LearnerLessonMicroCheck
{
    [Key]
    public int UserId { get; set; }

    [Key]
    public int CardId { get; set; }

    public bool IsCorrect { get; set; }

    /// <summary>NULL on a row written before the timestamp existed; those answers still count.</summary>
    public DateTimeOffset? AnsweredAt { get; set; }

    [ForeignKey("CardId")]
    [InverseProperty("LearnerLessonMicroChecks")]
    public virtual LessonCard Card { get; set; } = null!;

    [ForeignKey("UserId")]
    [InverseProperty("LearnerLessonMicroChecks")]
    public virtual User User { get; set; } = null!;
}
