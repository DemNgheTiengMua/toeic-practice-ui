using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[Index("ModuleId", "DisplayOrder", Name = "IX_PMT_Module")]
public partial class PathModuleTopic
{
    [Key]
    public int TopicId { get; set; }

    public int ModuleId { get; set; }

    [StringLength(160)]
    public string Title { get; set; } = null!;

    public int? CategoryId { get; set; }

    public int? QuestionCount { get; set; }

    [StringLength(400)]
    public string? VideoPath { get; set; }

    public int DisplayOrder { get; set; }

    [ForeignKey("CategoryId")]
    [InverseProperty("PathModuleTopics")]
    public virtual QuestionCategory? Category { get; set; }

    [ForeignKey("ModuleId")]
    [InverseProperty("PathModuleTopics")]
    public virtual PathModule Module { get; set; } = null!;
}
