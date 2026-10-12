using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DataAccess.Scaffold;

[Keyless]
public partial class vw_MentorPayoutTotal
{
    public int PayoutId { get; set; }

    public int MentorId { get; set; }

    public int? BookingCount { get; set; }

    [Column(TypeName = "decimal(38, 2)")]
    public decimal? GrossAmount { get; set; }
}
