using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DataAccess.Scaffold;

[Table("ImportLog")]
[Index("BatchId", Name = "IX_ImportLog_Batch")]
public partial class ImportLog
{
    [Key]
    public int LogId { get; set; }

    public int BatchId { get; set; }

    public int? RowNo { get; set; }

    [StringLength(5)]
    [Unicode(false)]
    public string Level { get; set; } = null!;

    [StringLength(1000)]
    public string Message { get; set; } = null!;

    public DateTimeOffset LoggedAt { get; set; }

    [ForeignKey("BatchId")]
    [InverseProperty("ImportLogs")]
    public virtual ImportBatch Batch { get; set; } = null!;
}
