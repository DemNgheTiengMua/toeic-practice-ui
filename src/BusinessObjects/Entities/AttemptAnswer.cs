using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>
/// #269: one answer is one roster position. The legacy <c>QuestionId</c> and its three navigations are
/// gone with the <c>Questions</c> table; <see cref="ExamItemId"/>, <see cref="ItemId"/> and
/// <see cref="ItemVersionId"/> are now all required, because a position and the version it pinned are
/// the only things that identify what was asked.
/// </summary>
public partial class AttemptAnswer
{
    [Key]
    public int AnswerId { get; set; }

    public int AttemptId { get; set; }

    public int ExamId { get; set; }

    /// <summary>
    /// #186: the pool item and the exact version the learner answered, so a re-versioned or retired
    /// item cannot change what this attempt renders.
    /// </summary>
    public int ItemId { get; set; }

    public int ItemVersionId { get; set; }

    /// <summary>
    /// #250: the roster position this answer was recorded against.
    ///
    /// Needed on top of <see cref="ItemId"/> and <see cref="ItemVersionId"/> because a paper may bind
    /// one item version at two positions, so the version pair does not say which question of the paper
    /// was answered. The position is the answer's identity, and it is what the navigator counts.
    /// </summary>
    public int ExamItemId { get; set; }

    /// <summary>
    /// #186: which of the pinned version's four options was picked. An <see cref="ItemVersion"/>
    /// stores labels A-D rather than option rows, so there is no id to point at;
    /// <see cref="SelectedOptionId"/> is kept nullable for the text and multi-select shapes and is NULL
    /// whenever a label is set.
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

    /// <summary>#186: the version this answer pinned.</summary>
    [ForeignKey("ItemVersionId, ItemId")]
    [InverseProperty("AttemptAnswers")]
    public virtual ItemVersion ItemVersion { get; set; } = null!;
}
