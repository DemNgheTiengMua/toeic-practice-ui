using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[Index("QuestionId", Name = "IX_AttemptAnswers_Question")]
public partial class AttemptAnswer
{
    [Key]
    public int AnswerId { get; set; }

    public int AttemptId { get; set; }

    /// <summary>
    /// #250: the legacy question this answer was given against, or NULL for a pool answer.
    ///
    /// It was <c>int</c> (NOT NULL) until #250, which forced every pool answer to invent a question id -
    /// and that is why the sitting path could never reach zero reads of <c>Questions</c>. The database's
    /// <c>CK_AttemptAnswers_HasShape</c> requires at least one of this and <see cref="ItemVersionId"/>:
    /// a row naming neither cannot be rendered, scored or reviewed.
    /// </summary>
    public int? QuestionId { get; set; }

    public int ExamId { get; set; }

    /// <summary>
    /// #186: the pool item and the exact version the learner answered, so a re-versioned or retired
    /// item cannot change what this attempt renders. Both are NULL for an answer recorded against a
    /// paper that the pool has not assembled; the database refuses a half-filled pair.
    /// </summary>
    public int? ItemId { get; set; }

    public int? ItemVersionId { get; set; }

    /// <summary>
    /// #186: which of the pinned version's four options was picked. An <see cref="ItemVersion"/>
    /// stores labels A–D rather than option rows, so there is no id to point at;
    /// <see cref="SelectedOptionId"/> keeps its meaning (and its foreign key) for a legacy answer and
    /// is NULL whenever this is set.
    /// </summary>
    public string? SelectedLabel { get; set; }

    public int? SelectedOptionId { get; set; }

    public string? ResponseText { get; set; }

    public string? SelectedOptionIdsJson { get; set; }

    public bool? IsCorrect { get; set; }

    public bool IsFlagged { get; set; }

    [ForeignKey("AttemptId")]
    [InverseProperty("AttemptAnswerAttempts")]
    public virtual ExamAttempt Attempt { get; set; } = null!;

    [ForeignKey("AttemptId, ExamId")]
    [InverseProperty("AttemptAnswerExamAttempts")]
    public virtual ExamAttempt ExamAttempt { get; set; } = null!;

    [ForeignKey("QuestionId")]
    [InverseProperty("AttemptAnswerQuestions")]
    public virtual Question Question { get; set; } = null!;

    [ForeignKey("QuestionId, ExamId")]
    [InverseProperty("AttemptAnswerQuestionNavigations")]
    public virtual Question QuestionNavigation { get; set; } = null!;

    [ForeignKey("SelectedOptionId, QuestionId")]
    [InverseProperty("AttemptAnswers")]
    public virtual QuestionOption? QuestionOption { get; set; }

    /// <summary>#186: the version this answer pinned (NULL for an answer with no pool pin).</summary>
    [ForeignKey("ItemVersionId, ItemId")]
    [InverseProperty("AttemptAnswers")]
    public virtual ItemVersion? ItemVersion { get; set; }
}
