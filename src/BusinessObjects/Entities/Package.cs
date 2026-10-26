using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

public partial class Package
{
    [Key]
    public int PackageId { get; set; }

    [StringLength(120)]
    public string Name { get; set; } = null!;

    public int Credits { get; set; }

    [Column(TypeName = "decimal(12, 2)")]
    public decimal Price { get; set; }

    public bool IsActive { get; set; }

    public bool IsFeatured { get; set; }

    [StringLength(1000)]
    public string? Features { get; set; }

    public int DisplayOrder { get; set; }

    [InverseProperty("Package")]
    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();
}
