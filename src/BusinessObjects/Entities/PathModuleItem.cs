using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[Index("ModuleId", "DisplayOrder", Name = "IX_PMI_Module")]
[Index("ItemId", "TemplateId", Name = "UQ_PMI_Id_Template", IsUnique = true)]
public partial class PathModuleItem
{
    [Key]
    public int ItemId { get; set; }

    public int ModuleId { get; set; }

    public int CertId { get; set; }

    public int TemplateId { get; set; }

    [StringLength(16)]
    [Unicode(false)]
    public string ItemType { get; set; } = null!;

    public int? RefSectionId { get; set; }

    public int? RefExamId { get; set; }

    public int? DifficultyLevel { get; set; }

    /// <summary>
    /// #228 / D1: the level the step is prescribed at, beside the nullable difficulty it replaces.
    /// Not mapped until the schema gains the column, and never derived from
    /// <see cref="DifficultyLevel"/> - D1 forbids a 1-5 to A1-C2 mapping.
    /// </summary>
    [NotMapped]
    public CefrLevel? CefrLevel { get; set; }

    public int? QuestionCount { get; set; }

    public int DisplayOrder { get; set; }

    public int? RefLessonId { get; set; }

    [ForeignKey("RefExamId, CertId")]
    [InverseProperty("PathModuleItems")]
    public virtual Exam? Exam { get; set; }

    [InverseProperty("PathModuleItem")]
    public virtual ICollection<LearnerPathStep> LearnerPathSteps { get; set; } = new List<LearnerPathStep>();

    [ForeignKey("RefLessonId, CertId")]
    [InverseProperty("PathModuleItems")]
    public virtual Lesson? Lesson { get; set; }

    [ForeignKey("ModuleId, CertId")]
    [InverseProperty("PathModuleItemPathModules")]
    public virtual PathModule PathModule { get; set; } = null!;

    [ForeignKey("ModuleId, TemplateId")]
    [InverseProperty("PathModuleItemPathModuleNavigations")]
    public virtual PathModule PathModuleNavigation { get; set; } = null!;

    [ForeignKey("RefSectionId, CertId")]
    [InverseProperty("PathModuleItems")]
    public virtual Section? Section { get; set; }
}
