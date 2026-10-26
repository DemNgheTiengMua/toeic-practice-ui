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

    [ForeignKey("CertId")]
    [InverseProperty("TargetBands")]
    public virtual Certificate Cert { get; set; } = null!;

    [ForeignKey("CertId, TargetBandCode")]
    [InverseProperty("TargetBands")]
    public virtual ScoreBand ScoreBand { get; set; } = null!;

    [ForeignKey("UserId")]
    [InverseProperty("TargetBands")]
    public virtual User User { get; set; } = null!;
}
