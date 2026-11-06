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

    /// <summary>
    /// #228 / D1: the level the step is prescribed at. #231 removed the 1-5 <c>DifficultyLevel</c> this
    /// replaces; the column is not added here because <c>PathModuleItems</c> is dropped by the path
    /// rebuild (#232), which carries the level on <c>LearnerPathSteps</c> instead.
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
