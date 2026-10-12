using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DataAccess.Scaffold;

[Index("BookingId", Name = "UQ_Reviews_OnePerBooking", IsUnique = true)]
public partial class Review
{
    [Key]
    public int ReviewId { get; set; }

    public int BookingId { get; set; }

    public int UserId { get; set; }

    public int Rating { get; set; }

    public string? Text { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    [ForeignKey("BookingId, UserId")]
    [InverseProperty("Reviews")]
    public virtual Booking Booking { get; set; } = null!;

    [ForeignKey("UserId")]
    [InverseProperty("Reviews")]
    public virtual User User { get; set; } = null!;
}
