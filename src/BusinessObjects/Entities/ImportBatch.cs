using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

public partial class ImportBatch
{
    [Key]
    public int BatchId { get; set; }

    public int CertId { get; set; }

    [StringLength(260)]
    public string SourceName { get; set; } = null!;

    [StringLength(10)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    public int UploadedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    [ForeignKey("CertId")]
    [InverseProperty("ImportBatches")]
    public virtual Certificate Cert { get; set; } = null!;

    [InverseProperty("Batch")]
    public virtual ICollection<ImportLog> ImportLogs { get; set; } = new List<ImportLog>();

    [ForeignKey("UploadedBy")]
    [InverseProperty("ImportBatches")]
    public virtual User UploadedByNavigation { get; set; } = null!;
}
