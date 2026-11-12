using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>
/// #274: the positions THIS attempt was served, in sitting order.
/// <para>
/// This is about which positions were <b>served</b>, not about where an answer is recorded:
/// <see cref="AttemptAnswer.ExamItemId"/> already names the roster position an answer was written
/// against, so an attempt-scoped paper changes no answer, no lock and no projection - only where the
/// list of positions comes from.
/// </para>
/// <para>
/// No writer column, deliberately. This is the same derived fact as
/// <see cref="AttemptStageBundle"/> (the paper a sitting was served), written in the same transaction
/// by the same owner; a second guard over it would be two mechanisms on one fact, which SRS D-09
/// refuses for the credit ledger and ADR-0012 refuses generally.
/// </para>
/// </summary>
[PrimaryKey(nameof(AttemptId), nameof(PositionNumber))]
public partial class AttemptItemResponse
{
    [Key]
    public int AttemptId { get; set; }

    /// <summary>H-5: carried down from the attempt, so the position must be of its own exam.</summary>
    public int ExamId { get; set; }

    /// <summary>The sitting order: 1-based, and what the navigator numbers.</summary>
    [Key]
    public int PositionNumber { get; set; }

    /// <summary>The roster position served. An answer names this same id.</summary>
    public int ExamItemId { get; set; }

    [ForeignKey("AttemptId, ExamId")]
    [InverseProperty("AttemptItemResponseExamAttempts")]
    public virtual ExamAttempt ExamAttempt { get; set; } = null!;

    [ForeignKey("ExamItemId, ExamId")]
    [InverseProperty("AttemptItemResponses")]
    public virtual ExamItem ExamItemExam { get; set; } = null!;
}
