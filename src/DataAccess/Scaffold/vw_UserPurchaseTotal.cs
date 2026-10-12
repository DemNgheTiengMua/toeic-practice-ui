using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DataAccess.Scaffold;

[Keyless]
public partial class vw_UserPurchaseTotal
{
    public int UserId { get; set; }

    [Column(TypeName = "decimal(38, 2)")]
    public decimal? AmountPurchased { get; set; }

    public int? PaidOrderCount { get; set; }
}
