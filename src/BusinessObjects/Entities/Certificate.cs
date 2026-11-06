using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[Index("Code", Name = "UQ_Certificates_Code", IsUnique = true)]
public partial class Certificate
{
    [Key]
    public int CertId { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string Code { get; set; } = null!;

    [StringLength(120)]
    public string Name { get; set; } = null!;

    [StringLength(500)]
    public string? Description { get; set; }

    public bool IsActive { get; set; }

    [StringLength(30)]
    public string ScoreAggregationRule { get; set; } = "sum";

    [StringLength(50)]
    public string? PresetCode { get; set; }

    // #161: the noun before a section code ("Part 3" vs "Section 3"). A label, so it is data: the
    // weakness page used to switch on Code == "TOEIC", which no fourth certificate could satisfy.
    [StringLength(30)]
    public string SectionNoun { get; set; } = "Section";

    [InverseProperty("Cert")]
    public virtual ICollection<AiInsight> AiInsights { get; set; } = new List<AiInsight>();

    [InverseProperty("Cert")]
    public virtual ICollection<ExamAttempt> ExamAttempts { get; set; } = new List<ExamAttempt>();

    [InverseProperty("Cert")]
    public virtual ICollection<Exam> Exams { get; set; } = new List<Exam>();

    [InverseProperty("Cert")]
    public virtual ICollection<GradingCriterion> GradingCriteria { get; set; } = new List<GradingCriterion>();

    [InverseProperty("Cert")]
    public virtual ICollection<ImportBatch> ImportBatches { get; set; } = new List<ImportBatch>();

    [InverseProperty("Cert")]
    public virtual ICollection<LearnerPath> LearnerPaths { get; set; } = new List<LearnerPath>();

    [InverseProperty("Cert")]
    public virtual ICollection<Lesson> Lessons { get; set; } = new List<Lesson>();

    [InverseProperty("Cert")]
    public virtual ICollection<MentorProfile> MentorProfiles { get; set; } = new List<MentorProfile>();

    [InverseProperty("Cert")]
    public virtual ICollection<PlacementTest> PlacementTests { get; set; } = new List<PlacementTest>();

    /// <summary>#187: drills carry their certificate directly, so a pool drill needs no section.</summary>
    [InverseProperty("Cert")]
    public virtual ICollection<PracticeSession> PracticeSessions { get; set; } = new List<PracticeSession>();

    [InverseProperty("Cert")]
    public virtual ICollection<QuestionCategory> QuestionCategories { get; set; } = new List<QuestionCategory>();

    [InverseProperty("Cert")]
    public virtual ICollection<Recommendation> Recommendations { get; set; } = new List<Recommendation>();

    /// <summary>#224: the saved bank entries filed under this certificate.</summary>
    [InverseProperty("Cert")]
    public virtual ICollection<SavedQuestion> SavedQuestions { get; set; } = new List<SavedQuestion>();

    [InverseProperty("Cert")]
    public virtual ICollection<ScoreBand> ScoreBands { get; set; } = new List<ScoreBand>();

    /// <summary>#210: the per-skill CEFR cut-score table versions this certificate publishes.</summary>
    [InverseProperty("Cert")]
    public virtual ICollection<CefrCutScoreVersion> CutScoreVersions { get; set; } = new List<CefrCutScoreVersion>();

    [InverseProperty("Cert")]
    public virtual ICollection<ScoreScale> ScoreScales { get; set; } = new List<ScoreScale>();

    [InverseProperty("Cert")]
    public virtual ICollection<Skill> Skills { get; set; } = new List<Skill>();

    [InverseProperty("Cert")]
    public virtual ICollection<TargetBand> TargetBands { get; set; } = new List<TargetBand>();

    [InverseProperty("ActiveCert")]
    public virtual ICollection<User> Users { get; set; } = new List<User>();
}
