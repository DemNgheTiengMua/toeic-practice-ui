using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DataAccess.Scaffold;

[Keyless]
public partial class vw_UserSectionAccuracy
{
    public int UserId { get; set; }

    public int SectionId { get; set; }

    [StringLength(120)]
    public string SectionName { get; set; } = null!;

    public int? Answered { get; set; }

    public int? Correct { get; set; }
}
