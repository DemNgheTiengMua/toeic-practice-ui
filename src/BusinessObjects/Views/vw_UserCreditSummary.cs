using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Views;

[Keyless]
public partial class vw_UserCreditSummary
{
    public int UserId { get; set; }

    public int? Balance { get; set; }

    public int? CreditsIn { get; set; }

    public int? CreditsPurchased { get; set; }

    public int? CreditsUsed { get; set; }
}
