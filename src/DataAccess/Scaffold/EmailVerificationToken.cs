using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DataAccess.Scaffold;

[Index("UserId", Name = "IX_EmailVerificationTokens_User")]
public partial class EmailVerificationToken
{
    [Key]
    public int TokenId { get; set; }

    public int UserId { get; set; }

    [MaxLength(256)]
    public byte[] TokenHash { get; set; } = null!;

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? UsedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    [ForeignKey("UserId")]
    [InverseProperty("EmailVerificationTokens")]
    public virtual User User { get; set; } = null!;
}
