using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>#155: one learner's mastery of one category. The row is the whole state of the scheduler:
/// counters that prove a threshold, and the date the category may next be drilled. There is no
/// "untested" row - absence of a row is <see cref="MasteryState.Untested"/>.</summary>
[Index("UserId", "CertId", "NextReviewDueAt", Name = "IX_UCM_User_Due")]
public partial class UserCategoryMastery
{
    [Key]
    public int UserId { get; set; }

    [Key]
    public int CategoryId { get; set; }

    /// <summary>Carried from the category so a mastery row can never straddle two certificates.</summary>
    public int CertId { get; set; }

    [StringLength(12)]
    [Unicode(false)]
    public string State { get; set; } = null!;

    public int AnsweredCount { get; set; }

    public int CorrectCount { get; set; }

    /// <summary>Consecutive correct answers in this category; any wrong answer resets it to 0.</summary>
    public int Streak { get; set; }

    /// <summary>Consecutive wrong answers; two in a row is a lapse out of <see cref="MasteryState.Mastered"/>.</summary>
    public int ConsecutiveWrong { get; set; }

    public DateTimeOffset? NextReviewDueAt { get; set; }

    public DateTimeOffset? LastAnsweredAt { get; set; }

    /// <summary>Distinct days on which this category was answered; Mastered needs two of them.</summary>
    public int SessionDays { get; set; }

    public DateOnly? LastSessionDay { get; set; }

    public DateTimeOffset? MasteredAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    [ForeignKey("CategoryId, CertId")]
    [InverseProperty("UserCategoryMasteries")]
    public virtual QuestionCategory Category { get; set; } = null!;

    [ForeignKey("UserId")]
    [InverseProperty("UserCategoryMasteries")]
    public virtual User User { get; set; } = null!;
}
