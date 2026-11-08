using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>
/// #187: one answered question of a drill, in one of two shapes.
/// <para>
/// A legacy answer names its <see cref="QuestionId"/> and <see cref="SectionId"/>; a pool answer names
/// the item and the exact version the learner answered, exactly as <see cref="AttemptAnswer"/> does.
/// <c>CK_PracticeAnswers_Shape</c> refuses a row that is half of either. The pool pin is what makes a
/// re-versioned or retired item unable to change a finished session's summary.
/// </para>
/// </summary>
[Index("SessionId", "QuestionId", Name = "UQ_PracticeAnswers_Question", IsUnique = true)]
[Index("SessionId", "ItemVersionId", Name = "UQ_PracticeAnswers_Version", IsUnique = true)]
[Index("SessionId", "CardId", Name = "UQ_PracticeAnswers_Card", IsUnique = true)]
public partial class PracticeAnswer
{
    [Key]
    public int AnswerId { get; set; }

    public int SessionId { get; set; }

    /// <summary>NULL for a pool answer, which has no <see cref="Question"/> row to point at.</summary>
    public int? QuestionId { get; set; }

    /// <summary>NULL for a pool answer, which is contained by its cell rather than by a section.</summary>
    public int? SectionId { get; set; }

    /// <summary>#187: the pool item answered. NULL for a legacy answer.</summary>
    public int? ItemId { get; set; }

    /// <summary>#187: the exact version answered, so a later correction cannot change this session.</summary>
    public int? ItemVersionId { get; set; }

    /// <summary>
    /// #240: the micro-check card answered, for a lesson sitting. NULL for a question answer and for a
    /// pool answer.
    /// <para>
    /// This is what makes a micro-check reach mastery: the answer is an ordinary <c>PracticeAnswers</c>
    /// row, in the ledger a drill writes, so <c>vw_UserCategoryAccuracy</c> and the cell mastery the
    /// path sizes its steps from both see it. A private counter beside the ledger would satisfy the
    /// ticket on paper and be invisible to both.
    /// </para>
    /// </summary>
    public int? CardId { get; set; }

    /// <summary>
    /// #187: which of the pinned version's four options was picked. An <see cref="ItemVersion"/>
    /// stores labels A–D rather than option rows, so there is no id to point at;
    /// <see cref="SelectedOptionId"/> keeps its meaning (and its foreign key) for a legacy answer.
    /// </summary>
    public string? SelectedLabel { get; set; }

    public int? SelectedOptionId { get; set; }

    public string? ResponseText { get; set; }

    public string? SelectedOptionIdsJson { get; set; }

    public bool? IsCorrect { get; set; }

    public bool HintUsed { get; set; }

    public int DisplayOrder { get; set; }

    /// <summary>#156: when this answer was last written. Null for rows written before the column
    /// existed — the system then genuinely does not know how long that answer took.</summary>
    public DateTime? AnsweredAt { get; set; }

    [ForeignKey("QuestionId")]
    [InverseProperty("PracticeAnswerQuestions")]
    public virtual Question? Question { get; set; }

    [ForeignKey("SessionId")]
    [InverseProperty("PracticeAnswerSessions")]
    public virtual PracticeSession Session { get; set; } = null!;

    /// <summary>#187: the version this answer pinned (NULL for a legacy answer).</summary>
    [ForeignKey("ItemVersionId, ItemId")]
    [InverseProperty("PracticeAnswers")]
    public virtual ItemVersion? ItemVersion { get; set; }
}
