using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DataAccess.Scaffold;

[Index("TemplateId", "CertId", Name = "UQ_PathTemplates_Id_Cert", IsUnique = true)]
public partial class PathTemplate
{
    [Key]
    public int TemplateId { get; set; }

    public int CertId { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string FromBandCode { get; set; } = null!;

    [StringLength(20)]
    [Unicode(false)]
    public string ToBandCode { get; set; } = null!;

    [StringLength(120)]
    public string Name { get; set; } = null!;

    [ForeignKey("CertId")]
    [InverseProperty("PathTemplates")]
    public virtual Certificate Cert { get; set; } = null!;

    [InverseProperty("PathTemplate")]
    public virtual ICollection<LearnerPath> LearnerPaths { get; set; } = new List<LearnerPath>();

    [InverseProperty("PathTemplate")]
    public virtual ICollection<PathModule> PathModules { get; set; } = new List<PathModule>();

    [ForeignKey("CertId, FromBandCode")]
    [InverseProperty("PathTemplateScoreBands")]
    public virtual ScoreBand ScoreBand { get; set; } = null!;

    [ForeignKey("CertId, ToBandCode")]
    [InverseProperty("PathTemplateScoreBandNavigations")]
    public virtual ScoreBand ScoreBandNavigation { get; set; } = null!;
}
