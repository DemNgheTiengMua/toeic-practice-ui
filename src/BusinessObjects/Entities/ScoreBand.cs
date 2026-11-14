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

    /// <summary>#153: hours of study from zero to reach this band. NULL for band codes that are not
    /// CEFR levels — a goal on such a certificate is "cannot judge", never an invented number.</summary>
    public int? CumulativeStudyHours { get; set; }

    [ForeignKey("CertId")]
    [InverseProperty("ScoreBands")]
    public virtual Certificate Cert { get; set; } = null!;

    [InverseProperty("ScoreBand")]
    public virtual ICollection<Recommendation> Recommendations { get; set; } = new List<Recommendation>();

    [InverseProperty("ScoreBand")]
    public virtual ICollection<TargetBand> TargetBands { get; set; } = new List<TargetBand>();

    /// <summary>#153: target-band rows that name this band as the learner's starting point.</summary>
    [InverseProperty("CurrentScoreBand")]
    public virtual ICollection<TargetBand> CurrentBandTargetBands { get; set; } = new List<TargetBand>();

    /// <summary>#156: calibration proposals that started from this band.</summary>

    /// <summary>#156: calibration proposals that pointed at this band.</summary>
}
