using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>
/// #192: one cell of a criterion's seven-band rubric. <see cref="Band"/> is the CEFR band itself
/// (0 = Pre-A1 ... 6 = C2), so a criterion's rows are what "a descriptor at every band" is counted
/// from. <see cref="Descriptor"/> says what that band looks like in observable terms;
/// <see cref="BinaryGates"/> says what must be true before the band is reached at all.
/// </summary>
public partial class GradingCriterionDescriptor
{
    public int CriterionId { get; set; }

    [Range(0, 6)]
    public byte Band { get; set; }

    public string Descriptor { get; set; } = null!;

    public string BinaryGates { get; set; } = null!;

    [ForeignKey("CriterionId")]
    [InverseProperty("Descriptors")]
    public virtual GradingCriterion Criterion { get; set; } = null!;

    /// <summary>#201 / ADM-03: triage cannot write a rubric descriptor. NULL = seed, fixture.</summary>
    public int? LastWrittenBy { get; set; }
}
