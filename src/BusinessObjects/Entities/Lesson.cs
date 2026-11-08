using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[Index("LessonId", "CertId", Name = "UQ_Lessons_Id_Cert", IsUnique = true)]
public partial class Lesson
{
    [Key]
    public int LessonId { get; set; }

    public int CertId { get; set; }

    public int SkillId { get; set; }

    /// <summary>#240: the skill's modality, carried down so the topic's own modality can be pinned by
    /// a composite foreign key. A reading lesson on a listening topic is refused by the database.</summary>
    [StringLength(10)]
    [Unicode(false)]
    public string Modality { get; set; } = null!;

    /// <summary>#240: the canonical topic of #183's twenty. With <see cref="CefrLevel"/> and
    /// <see cref="SkillId"/> this is the cell a lesson is addressed by.</summary>
    public int TopicId { get; set; }

    /// <summary>#240: the lesson's own level. D0.5's 120 lessons are one per (skill, topic, level).</summary>
    public CefrLevel CefrLevel { get; set; }

    /// <summary>The legacy diagnostic axis. Not the cell: <see cref="TopicId"/> is.</summary>
    public int? CategoryId { get; set; }

    [StringLength(160)]
    public string Title { get; set; } = null!;

    [StringLength(500)]
    public string? Summary { get; set; }

    public int EstimatedMinutes { get; set; }

    public int DisplayOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    [ForeignKey("CategoryId")]
    [InverseProperty("Lessons")]
    public virtual QuestionCategory? Category { get; set; }

    [ForeignKey("CertId")]
    [InverseProperty("Lessons")]
    public virtual Certificate Cert { get; set; } = null!;

    [InverseProperty("Lesson")]
    public virtual ICollection<LearnerLessonProgress> LearnerLessonProgresses { get; set; } = new List<LearnerLessonProgress>();

    [InverseProperty("Lesson")]
    public virtual ICollection<LessonCard> LessonCards { get; set; } = new List<LessonCard>();

    /// <summary>#232: path steps whose lesson reader is this lesson.</summary>
    [InverseProperty("Lesson")]
    public virtual ICollection<LearnerPathStep> LearnerPathSteps { get; set; } = new List<LearnerPathStep>();

    [ForeignKey("SkillId, CertId")]
    [InverseProperty("Lessons")]
    public virtual Skill Skill { get; set; } = null!;

    /// <summary>#240: the canonical topic this lesson teaches.</summary>
    [ForeignKey("TopicId")]
    [InverseProperty("Lessons")]
    public virtual Topic Topic { get; set; } = null!;
}
