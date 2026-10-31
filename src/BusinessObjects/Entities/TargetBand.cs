using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[PrimaryKey("UserId", "CertId")]
public partial class TargetBand
{
    [Key]
    public int UserId { get; set; }

    [Key]
    public int CertId { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string TargetBandCode { get; set; } = null!;

    public DateTimeOffset SetAt { get; set; }

    /// <summary>#153: the band the learner starts from. NULL on rows the placement flow wrote before
    /// the goal had a starting point; path generation reads it only when no placement says otherwise.</summary>
    [StringLength(20)]
    [Unicode(false)]
    public string? CurrentBandCode { get; set; }

    /// <summary>#153: the exam date. NULL means the goal has no deadline, and so no verdict.</summary>
    public DateOnly? TargetDate { get; set; }

    /// <summary>#153: hours per week the learner can actually spend. Checked against the 25 h ceiling.</summary>
    [Column(TypeName = "decimal(4, 1)")]
    public decimal? WeeklyStudyHours { get; set; }

    /// <summary>#153: ΔH — the study hours the two bands are apart. Computed by code, stored for the
    /// screen; NULL when the certificate's bands carry no hours.</summary>
    public int? RequiredTotalHours { get; set; }

    /// <summary>#153: feasible, stretch or unrealistic. NULL means there is no answer to give:
    /// no deadline, or a certificate whose bands carry no hours.</summary>
    [StringLength(12)]
    [Unicode(false)]
    public string? FeasibilityStatus { get; set; }

    [ForeignKey("CertId")]
    [InverseProperty("TargetBands")]
    public virtual Certificate Cert { get; set; } = null!;

    /// <summary>The band the learner is aiming at. Named <c>ScoreBand</c> rather than EF's scaffolded
    /// <c>ScoreBandNavigation</c> so existing readers keep meaning the target band.</summary>
    [ForeignKey("CertId, TargetBandCode")]
    [InverseProperty("TargetBands")]
    public virtual ScoreBand ScoreBand { get; set; } = null!;

    /// <summary>#153: the band the learner starts from, when one was recorded.</summary>
    [ForeignKey("CertId, CurrentBandCode")]
    [InverseProperty("CurrentBandTargetBands")]
    public virtual ScoreBand? CurrentScoreBand { get; set; }

    [ForeignKey("UserId")]
    [InverseProperty("TargetBands")]
    public virtual User User { get; set; } = null!;
}
