using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;

namespace DataAccess;

/// <summary>
/// Partial class extending the scaffolded <see cref="CompanionDbContext"/>.
/// Custom model configurations are declared in <see cref="OnModelCreatingPartial"/>
/// so they are never wiped on re-scaffolding (ADR-0001).
/// </summary>
public partial class CompanionDbContext
{
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Question>(entity =>
        {
            entity.Ignore(e => e.QuestionOptionQuestion);
        });

        modelBuilder.Entity<QuestionOption>(entity =>
        {
            entity.Property(e => e.OptionId).ValueGeneratedOnAdd();
            entity.HasOne(d => d.Question).WithMany()
                .HasForeignKey(d => d.QuestionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__QuestionO__Quest__339FAB6E");
        });
    }
}
