using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BusinessObjects.Entities;

/// <summary>#155: the last time this learner met this question, and whether it is still answering out
/// its cooldown. A question answered wrongly does not come back for 24 hours; the row is keyed on the
/// learner so "unseen" is a lookup rather than a UNION over two answer tables.</summary>
public partial class UserQuestionHistory
{
    [Key]
    public int UserId { get; set; }

    [Key]
    public int QuestionId { get; set; }

    public DateTimeOffset LastAnsweredAt { get; set; }

    /// <summary>NULL for a skipped question: neither right nor wrong, so no cooldown is started.</summary>
    public bool? LastIsCorrect { get; set; }

    /// <summary>Compared against "now" on every selection, so an expired cooldown needs no write.</summary>
    public DateTimeOffset? CooldownUntil { get; set; }

    [ForeignKey("QuestionId")]
    [InverseProperty("UserQuestionHistories")]
    public virtual Question Question { get; set; } = null!;

    [ForeignKey("UserId")]
    [InverseProperty("UserQuestionHistories")]
    public virtual User User { get; set; } = null!;
}
