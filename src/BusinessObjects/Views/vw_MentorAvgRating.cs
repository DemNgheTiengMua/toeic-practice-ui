using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Views;

[Keyless]
public partial class vw_MentorAvgRating
{
    public int MentorId { get; set; }

    [Column(TypeName = "decimal(38, 6)")]
    public decimal? AvgRating { get; set; }

    public int? ReviewCount { get; set; }
}
