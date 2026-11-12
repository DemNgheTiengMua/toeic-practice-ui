using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>
/// #269: a free response belongs to one roster position on a pool paper. The legacy <c>QuestionId</c>
/// and its four composite keyed navigations are gone with the <c>Questions</c> table, which is what
/// unblocked a pool-only productive paper from being sat at all.
/// </summary>
[Index("ExamItemId", Name = "IX_FreeResponses_ExamItem")]
[Index("FreeResponseId", "SkillId", Name = "UQ_FreeResp_Id_Skill", IsUnique = true)]
public partial class FreeResponse
{
    [Key]
    public int FreeResponseId { get; set; }

    public int AttemptId { get; set; }

    /// <summary>#250: the roster position this response was recorded against.</summary>
    public int ExamItemId { get; set; }

    public int ExamId { get; set; }

    /// <summary>
    /// #269: the section of the position this response was recorded against.
    /// <para>
    /// Needed because dropping <c>Questions</c> removed <c>FK_FreeResp_QuestionSkill</c>, which used to
    /// pin <see cref="SkillId"/> to the question's skill. Without it a response on a reading position
    /// could claim the speaking skill and <c>AiGradings</c> accepted it, so the rubric it was scored
    /// against belonged to a different skill.
    /// </para>
    /// </summary>
    public int SectionId { get; set; }

    public int SkillId { get; set; }

    [StringLength(30)]
    [Unicode(false)]
    public string QuestionType { get; set; } = null!;

    public string? ResponseText { get; set; }

    [StringLength(400)]
    public string? AudioPath { get; set; }

    public int? DurationMs { get; set; }

    [StringLength(12)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    public DateTimeOffset? DraftSavedAt { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }

    /// <summary>#216 / PLC-15: how many times this task's recording was interrupted.</summary>
    public int RecordingInterruptions { get; set; }

    /// <summary>#216 / PLC-15: the token of the last interruption already counted, so a reload that
    /// re-sends it is the same interruption rather than a second retry.</summary>
    [StringLength(64)]
    public string? RecordingSessionToken { get; set; }

    [InverseProperty("FreeResponse")]
    public virtual AiGrading? AiGradingFreeResponse { get; set; }

    [InverseProperty("FreeResponseNavigation")]
    public virtual ICollection<AiGrading> AiGradingFreeResponseNavigations { get; set; } = new List<AiGrading>();

    [ForeignKey("AttemptId")]
    [InverseProperty("FreeResponseAttempts")]
    public virtual ExamAttempt Attempt { get; set; } = null!;

    [ForeignKey("AttemptId, ExamId")]
    [InverseProperty("FreeResponseExamAttempts")]
    public virtual ExamAttempt ExamAttempt { get; set; } = null!;
}
