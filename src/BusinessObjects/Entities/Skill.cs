using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[Index("CertId", "Code", Name = "UQ_Skills_Cert_Code", IsUnique = true)]
[Index("SkillId", "CertId", Name = "UQ_Skills_Id_Cert", IsUnique = true)]
[Index("SkillId", "Modality", Name = "UQ_Skills_Id_Modality", IsUnique = true)]
public partial class Skill
{
    [Key]
    public int SkillId { get; set; }

    public int CertId { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string Code { get; set; } = null!;

    [StringLength(120)]
    public string Name { get; set; } = null!;

    [StringLength(10)]
    [Unicode(false)]
    public string Modality { get; set; } = null!;

    public int DisplayOrder { get; set; }

    public int? DurationMinutes { get; set; }

    // E1.1 (#146): the skill's own scaled-score range and rounding grid. NULL means unconfigured -
    // scoring such a skill raises a configuration error instead of guessing a range.
    public int? MinScaledScore { get; set; }

    public int? MaxScaledScore { get; set; }

    public int? ScoreStep { get; set; }

    // E1.4 (#149): the skill is answered forwards only and offers no review screen. navigation.js
    // used to decide this by comparing the skill's code name; the behaviour is skill data now.
    public bool LockNavigationOneWay { get; set; }

    [InverseProperty("SkillNavigation")]
    public virtual ICollection<AttemptSkillScore> AttemptSkillScoreSkillNavigations { get; set; } = new List<AttemptSkillScore>();

    [InverseProperty("Skill")]
    public virtual ICollection<AttemptSkillScore> AttemptSkillScoreSkills { get; set; } = new List<AttemptSkillScore>();

    [ForeignKey("CertId")]
    [InverseProperty("Skills")]
    public virtual Certificate Cert { get; set; } = null!;

    [InverseProperty("Skill")]
    public virtual ICollection<GradingCriterion> GradingCriteria { get; set; } = new List<GradingCriterion>();

    /// <summary>#192: at most one lock per skill; present means this skill's rubric is locked.</summary>
    [InverseProperty("Skill")]
    public virtual GradingRubricLock? RubricLock { get; set; }

    [InverseProperty("Skill")]
    public virtual ICollection<Lesson> Lessons { get; set; } = new List<Lesson>();

    [InverseProperty("SkillNavigation")]
    public virtual ICollection<MentorProfile> MentorProfileSkillNavigations { get; set; } = new List<MentorProfile>();

    [InverseProperty("Skill")]
    public virtual ICollection<MentorProfile> MentorProfileSkills { get; set; } = new List<MentorProfile>();

    [InverseProperty("Skill")]
    public virtual ICollection<Recommendation> Recommendations { get; set; } = new List<Recommendation>();

    /// <summary>#232: the path steps drilled at this skill.</summary>
    [InverseProperty("Skill")]
    public virtual ICollection<LearnerPathStep> LearnerPathSteps { get; set; } = new List<LearnerPathStep>();

    [InverseProperty("Skill")]
    public virtual ICollection<ScoreScale> ScoreScales { get; set; } = new List<ScoreScale>();

    /// <summary>#210: this skill's rows in every published cut-score version.</summary>
    [InverseProperty("SkillNavigation")]
    public virtual ICollection<CefrCutScore> CutScores { get; set; } = new List<CefrCutScore>();

    /// <summary>#211: this skill's stored state, one row per learner who has been measured in it.</summary>
    [InverseProperty("SkillNavigation")]
    public virtual ICollection<LearnerSkillState> LearnerSkillStates { get; set; } = new List<LearnerSkillState>();

    [InverseProperty("SkillNavigation")]
    public virtual ICollection<Section> SectionSkillNavigations { get; set; } = new List<Section>();

    [InverseProperty("Skill")]
    public virtual ICollection<Section> SectionSkills { get; set; } = new List<Section>();
}
