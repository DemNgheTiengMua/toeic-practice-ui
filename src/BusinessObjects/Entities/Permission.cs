using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

/// <summary>D16 / ADM-01: one of the five staff namespaces. Issued by the seed, never by request
/// code — the schema's CHECK is the closed list.</summary>
[Table("Permissions")]
public partial class Permission
{
    [Key]
    [StringLength(40)]
    [Unicode(false)]
    public string Code { get; set; } = null!;

    [InverseProperty("Permission")]
    public virtual ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
