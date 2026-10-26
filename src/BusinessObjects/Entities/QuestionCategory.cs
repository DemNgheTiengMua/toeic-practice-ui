using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[Index("CertId", "Code", Name = "UQ_QuestionCategories_Cert_Code", IsUnique = true)]
public partial class QuestionCategory
{
    [Key]
    public int CategoryId { get; set; }

    public int CertId { get; set; }

    [StringLength(40)]
    [Unicode(false)]
    public string Code { get; set; } = null!;

    [StringLength(120)]
    public string Name { get; set; } = null!;

    [ForeignKey("CertId")]
    [InverseProperty("QuestionCategories")]
    public virtual Certificate Cert { get; set; } = null!;

    [InverseProperty("Category")]
    public virtual ICollection<Lesson> Lessons { get; set; } = new List<Lesson>();

    [InverseProperty("Category")]
    public virtual ICollection<PathModuleTopic> PathModuleTopics { get; set; } = new List<PathModuleTopic>();

    [InverseProperty("Category")]
    public virtual ICollection<Question> Questions { get; set; } = new List<Question>();
}
