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

    /// <summary>
    /// #205: NULL for a pool upload. Pool stock is certificate-agnostic (CNT-01), so a pool batch has
    /// no certificate to name; the exam importer always writes one.
    /// </summary>
    public int? CertId { get; set; }

    [StringLength(260)]
    public string SourceName { get; set; } = null!;

    [StringLength(10)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    public int UploadedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// #205: the batch's upload verbatim, so a corrected record can be re-judged against its own group
    /// and offered back to the correction form. NULL for a batch written before this column existed.
    /// </summary>
    public string? SourceDocument { get; set; }

    /// <summary>
    /// #205: the account that last re-opened this batch by re-submitting a record. ADM-02's second arm
    /// reads it, so it is the database's record of who looked again.
    /// </summary>
    public int? LastModifiedBy { get; set; }

    [ForeignKey("CertId")]
    [InverseProperty("ImportBatches")]
    public virtual Certificate Cert { get; set; } = null!;

    [InverseProperty("Batch")]
    public virtual ICollection<ImportLog> ImportLogs { get; set; } = new List<ImportLog>();

    /// <summary>#206: the cells this batch's staged stock was audited in.</summary>

    [ForeignKey("UploadedBy")]
    [InverseProperty("ImportBatches")]
    public virtual User UploadedByNavigation { get; set; } = null!;
}
