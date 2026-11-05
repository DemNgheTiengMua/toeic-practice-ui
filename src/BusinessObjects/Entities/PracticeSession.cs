using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>
/// #187: a drill. It is either a section drill — one certificate section, unchanged since before the
/// pool — or a pool drill, drawn from a cell (topic and level) with no exam and no section behind it.
/// The two shapes are exclusive by <c>CK_PracticeSessions_Shape</c>: a row that named both would leave
/// "which store does this session read?" with no single answer.
/// <para>
/// <see cref="CertId"/> is the session's own fact rather than something read back through a section.
/// A pool session has no section to derive it from, and a session that cannot name its certificate
/// cannot be pruned, counted or resumed.
/// </para>
/// </summary>
[Index("UserId", "StartedAt", Name = "IX_Practice_User")]
[Index("SessionId", "SectionId", Name = "UQ_PracticeSessions_Id_Section", IsUnique = true)]
[Index("SessionId", "UserId", Name = "UQ_PracticeSessions_Id_User", IsUnique = true)]
public partial class PracticeSession
{
    [Key]
    public int SessionId { get; set; }

    public int UserId { get; set; }

    /// <summary>#187: the certificate this drill belongs to, whether it names a section or a cell.</summary>
    public int CertId { get; set; }

    /// <summary>#187: NULL for a pool drill, which is drawn from <see cref="TopicId"/> instead.</summary>
    public int? SectionId { get; set; }

    /// <summary>#187: the pool cell drilled. NULL for a section drill.</summary>
    public int? TopicId { get; set; }

    /// <summary>#187: the cell's level. NULL for a section drill.</summary>
    public CefrLevel? CefrLevel { get; set; }

    [StringLength(10)]
    [Unicode(false)]
    public string Modality { get; set; } = null!;

    public int QuestionCount { get; set; }

    [StringLength(12)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    public int CorrectCount { get; set; }

    [StringLength(12)]
    [Unicode(false)]
    public string QuestionSource { get; set; } = null!;

    public int? LearnerPathStepId { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    [InverseProperty("PracticeSession")]
    public virtual ICollection<AiInsight> AiInsights { get; set; } = new List<AiInsight>();

    [ForeignKey("LearnerPathStepId")]
    [InverseProperty("PracticeSessions")]
    public virtual LearnerPathStep? LearnerPathStep { get; set; }

    [InverseProperty("Session")]
    public virtual ICollection<PracticeAnswer> PracticeAnswerSessions { get; set; } = new List<PracticeAnswer>();

    [InverseProperty("PracticeSession")]
    public virtual ICollection<Recommendation> Recommendations { get; set; } = new List<Recommendation>();

    [ForeignKey("SectionId")]
    [InverseProperty("PracticeSessionSections")]
    public virtual Section? Section { get; set; }

    /// <summary>
    /// #187: the pool cell a pool drill was drawn from. Mapped on <see cref="TopicId"/> alone, which is
    /// a real key of <c>Topics</c>; the composite (TopicId, Modality) guard that stops a listening
    /// session on a reading topic is the database's, because EF cannot compose a nullable column into
    /// a principal key without refusing the save.
    /// </summary>
    [ForeignKey("TopicId")]
    [InverseProperty("PracticeSessions")]
    public virtual Topic? Topic { get; set; }

    [ForeignKey("UserId")]
    [InverseProperty("PracticeSessions")]
    public virtual User User { get; set; } = null!;

    [ForeignKey("CertId")]
    [InverseProperty("PracticeSessions")]
    public virtual Certificate Cert { get; set; } = null!;
}
