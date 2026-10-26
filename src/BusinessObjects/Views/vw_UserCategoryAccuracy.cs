using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Views;

[Keyless]
public partial class vw_UserCategoryAccuracy
{
    public int UserId { get; set; }

    public int? CategoryId { get; set; }

    public int? Answered { get; set; }

    public int? Correct { get; set; }
}
