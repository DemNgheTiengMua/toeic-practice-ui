using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

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

    /// <summary>
    /// #205: the gate's machine code (criterion 1, "the failing rule"), or NULL for a batch-level note.
    /// The prose lives in <see cref="Message"/>; parsing it back into a rule would break the first time
    /// a message was reworded.
    /// </summary>
    [StringLength(40)]
    public string? RuleCode { get; set; }

    /// <summary>
    /// #205: which re-judgement this row is. An upload writes attempt 1; each re-submission writes the
    /// next. The read path takes the highest attempt per <see cref="RowNo"/>, which is what keeps the
    /// list at one row per record while the correction history stays readable.
    /// </summary>
    public int Attempt { get; set; } = 1;

    /// <summary>
    /// #205: the item an accepted record became. NULL for a refusal or a batch-level note. This is what
    /// lets a workbench point a verdict at its staged row instead of joining on
    /// <c>Items.BatchTag</c>, which is the file's own tag rather than the batch's.
    /// </summary>
    public int? ItemId { get; set; }

    public DateTimeOffset LoggedAt { get; set; }

    [ForeignKey("BatchId")]
    [InverseProperty("ImportLogs")]
    public virtual ImportBatch Batch { get; set; } = null!;
}
