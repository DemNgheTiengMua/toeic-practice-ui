using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DataAccess.Scaffold;

[Index("TemplateId", "DisplayOrder", Name = "IX_PathModules_Template")]
[Index("ModuleId", "CertId", Name = "UQ_PathModules_Id_Cert", IsUnique = true)]
[Index("ModuleId", "TemplateId", Name = "UQ_PathModules_Id_Template", IsUnique = true)]
public partial class PathModule
{
    [Key]
    public int ModuleId { get; set; }

    public int TemplateId { get; set; }

    public int SkillId { get; set; }

    public int CertId { get; set; }

    public int DisplayOrder { get; set; }

    [StringLength(120)]
    public string Title { get; set; } = null!;

    [StringLength(120)]
    public string? GroupName { get; set; }

    [StringLength(1000)]
    public string? Description { get; set; }

    public int? EstimatedMinutes { get; set; }

    [InverseProperty("PathModuleNavigation")]
    public virtual ICollection<PathModuleItem> PathModuleItemPathModuleNavigations { get; set; } = new List<PathModuleItem>();

    [InverseProperty("PathModule")]
    public virtual ICollection<PathModuleItem> PathModuleItemPathModules { get; set; } = new List<PathModuleItem>();

    [InverseProperty("Module")]
    public virtual ICollection<PathModuleTopic> PathModuleTopics { get; set; } = new List<PathModuleTopic>();

    [ForeignKey("TemplateId, CertId")]
    [InverseProperty("PathModules")]
    public virtual PathTemplate PathTemplate { get; set; } = null!;

    [ForeignKey("SkillId, CertId")]
    [InverseProperty("PathModules")]
    public virtual Skill Skill { get; set; } = null!;
}
