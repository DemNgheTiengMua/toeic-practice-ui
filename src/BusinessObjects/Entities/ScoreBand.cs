using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[Index("CertId", "Code", Name = "UQ_ScoreBands_Cert_Code", IsUnique = true)]
public partial class ScoreBand
{
    [Key]
    public int BandId { get; set; }

    public int CertId { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string Code { get; set; } = null!;

    [StringLength(60)]
    public string Name { get; set; } = null!;

    public int MinTotal { get; set; }

    public int MaxTotal { get; set; }

    public int DisplayOrder { get; set; }

    [ForeignKey("CertId")]
    [InverseProperty("ScoreBands")]
    public virtual Certificate Cert { get; set; } = null!;

    [InverseProperty("ScoreBandNavigation")]
    public virtual ICollection<PathTemplate> PathTemplateScoreBandNavigations { get; set; } = new List<PathTemplate>();

    [InverseProperty("ScoreBand")]
    public virtual ICollection<PathTemplate> PathTemplateScoreBands { get; set; } = new List<PathTemplate>();

    [InverseProperty("ScoreBand")]
    public virtual ICollection<Recommendation> Recommendations { get; set; } = new List<Recommendation>();

    [InverseProperty("ScoreBand")]
    public virtual ICollection<TargetBand> TargetBands { get; set; } = new List<TargetBand>();
}
