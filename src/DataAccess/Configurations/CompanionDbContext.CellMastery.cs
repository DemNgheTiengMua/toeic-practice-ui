using BusinessObjects;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace DataAccess;

public partial class CompanionDbContext
{
    /// <summary>
    /// #237 / D4: the per-cell mastery row.
    /// <para>
    /// The table and its two triggers arrive as <c>docs/schema-changes-237.sql</c> while #250 owns
    /// <c>database-schema.sql</c>; this mapping is written against that delta and compiles either way.
    /// </para>
    /// </summary>
    private static void ConfigureCellMastery(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserCellMastery>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.TopicId, e.CefrLevel })
                .HasName("PK_UserCellMasteries");

            // HasTrigger: two triggers fire on this table (trg_UserCellMasteries_SingleWriter and
            // trg_UserCellMasteries_StateIsEarned). Without the declarations every EF save fails on the
            // OUTPUT clause instead of on the rule (ADR-0001).
            entity.ToTable(tb =>
            {
                tb.HasTrigger("trg_UserCellMasteries_SingleWriter");
                tb.HasTrigger("trg_UserCellMasteries_StateIsEarned");
            });

            entity.HasIndex(e => new { e.UserId, e.NextReviewDueAt }, "IX_UserCellMasteries_User_Due");

            entity.Property(e => e.State).HasDefaultValue("untested", "DF_UserCellMasteries_State");
            entity.Property(e => e.WeightedCorrect).HasDefaultValue(0d, "DF_UserCellMasteries_WC");
            entity.Property(e => e.WeightedObservations).HasDefaultValue(0d, "DF_UserCellMasteries_WN");
            entity.Property(e => e.ObservationCount).HasDefaultValue(0, "DF_UserCellMasteries_N");
            entity.Property(e => e.ConsecutiveWrong).HasDefaultValue(0, "DF_UserCellMasteries_CW");
            entity.Property(e => e.ProjectionVersion).HasDefaultValue(0, "DF_UserCellMasteries_ProjectionVersion");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(sysdatetimeoffset())", "DF_UserCellMasteries_Updated");

            entity.Property(e => e.WrittenByService).HasMaxLength(64).IsUnicode(false);

            // An authored level, not a reportable one: a cell is content, and a mastery row at the
            // pre-A1 floor would be mastery of a cell that cannot hold an item.
            entity.Property(e => e.CefrLevel)
                .HasConversion(v => v.Code, v => ParseAuthoredLevel(v))
                .HasMaxLength(10)
                .IsUnicode(false);

            entity.HasOne(d => d.Topic).WithMany(p => p.UserCellMasteries)
                .HasForeignKey(d => d.TopicId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UserCellMasteries_Topic");

            entity.HasOne(d => d.User).WithMany(p => p.UserCellMasteries)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UserCellMasteries_User");
        });
    }
}
