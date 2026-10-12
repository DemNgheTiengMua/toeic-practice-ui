using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DataAccess.Scaffold;

[Index("CertId", "SkillId", "ExamId", "RawScore", Name = "UQ_ScoreScales_Cert_Skill_Exam_Raw", IsUnique = true)]
public partial class ScoreScale
{
    [Key]
    public int ScaleId { get; set; }

    public int CertId { get; set; }

    public int? SkillId { get; set; }

    public int? ExamId { get; set; }

    public int RawScore { get; set; }

    public int ScaledScore { get; set; }

    [ForeignKey("CertId")]
    [InverseProperty("ScoreScales")]
    public virtual Certificate Cert { get; set; } = null!;

    [ForeignKey("ExamId, CertId")]
    [InverseProperty("ScoreScales")]
    public virtual Exam? Exam { get; set; }

    [ForeignKey("SkillId, CertId")]
    [InverseProperty("ScoreScales")]
    public virtual Skill? Skill { get; set; }
}
