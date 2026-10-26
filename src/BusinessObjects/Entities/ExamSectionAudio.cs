using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[PrimaryKey("ExamId", "SectionId")]
[Table("ExamSectionAudio")]
public partial class ExamSectionAudio
{
    [Key]
    public int ExamId { get; set; }

    [Key]
    public int SectionId { get; set; }

    public int CertId { get; set; }

    [StringLength(400)]
    public string AudioPath { get; set; } = null!;

    public int DurationSec { get; set; }

    public long SizeBytes { get; set; }

    public DateTimeOffset UploadedAt { get; set; }

    [ForeignKey("ExamId, CertId")]
    [InverseProperty("ExamSectionAudios")]
    public virtual Exam Exam { get; set; } = null!;

    [ForeignKey("SectionId, CertId")]
    [InverseProperty("ExamSectionAudios")]
    public virtual Section Section { get; set; } = null!;
}
