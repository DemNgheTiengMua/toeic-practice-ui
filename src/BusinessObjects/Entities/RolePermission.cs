using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>D16: the grant. A role may hold any combination of the five namespaces, which is what
/// lets a small team cover several duties without inventing a role.</summary>
[Table("RolePermissions")]
[PrimaryKey(nameof(RoleId), nameof(PermissionCode))]
public partial class RolePermission
{
    public int RoleId { get; set; }

    [StringLength(40)]
    [Unicode(false)]
    public string PermissionCode { get; set; } = null!;

    [ForeignKey("RoleId")]
    [InverseProperty("RolePermissions")]
    public virtual Role Role { get; set; } = null!;

    [ForeignKey("PermissionCode")]
    [InverseProperty("RolePermissions")]
    public virtual Permission Permission { get; set; } = null!;
}
