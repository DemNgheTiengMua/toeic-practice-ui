using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DataAccess.Scaffold;

[PrimaryKey("AttemptId", "SkillId")]
public partial class AttemptSkillScore
{
    [Key]
    public int AttemptId { get; set; }

    [Key]
    public int SkillId { get; set; }

    public int CertId { get; set; }

    public int RawScore { get; set; }

    public int ScaledScore { get; set; }

    [ForeignKey("AttemptId")]
    [InverseProperty("AttemptSkillScoreAttempts")]
    public virtual ExamAttempt Attempt { get; set; } = null!;

    [ForeignKey("AttemptId, CertId")]
    [InverseProperty("AttemptSkillScoreExamAttempts")]
    public virtual ExamAttempt ExamAttempt { get; set; } = null!;

    [ForeignKey("SkillId")]
    [InverseProperty("AttemptSkillScoreSkills")]
    public virtual Skill Skill { get; set; } = null!;

    [ForeignKey("SkillId, CertId")]
    [InverseProperty("AttemptSkillScoreSkillNavigations")]
    public virtual Skill SkillNavigation { get; set; } = null!;
}
