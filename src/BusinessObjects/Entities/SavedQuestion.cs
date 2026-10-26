using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[PrimaryKey("UserId", "QuestionId")]
public partial class SavedQuestion
{
    [Key]
    public int UserId { get; set; }

    [Key]
    public int QuestionId { get; set; }

    [StringLength(10)]
    [Unicode(false)]
    public string Source { get; set; } = null!;

    public DateTimeOffset SavedAt { get; set; }

    [ForeignKey("QuestionId")]
    [InverseProperty("SavedQuestions")]
    public virtual Question Question { get; set; } = null!;

    [ForeignKey("UserId")]
    [InverseProperty("SavedQuestions")]
    public virtual User User { get; set; } = null!;
}
