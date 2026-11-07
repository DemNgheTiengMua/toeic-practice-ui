using BusinessObjects;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace DataAccess;

public partial class CompanionDbContext
{
    /// <summary>
    /// #212: the per-skill placement result, and the two guards that make its status derived rather
    /// than decorative.
    /// </summary>
    private static void ConfigurePlacementSkillResults(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlacementSkillResult>(entity =>
        {
            entity.HasKey(e => new { e.PlacementId, e.SkillId })
                .HasName("PK_PlacementSkillResults");

            // Both triggers are declared, or every save to this table fails on the OUTPUT clause
            // instead of on the rule: trg_PlacementSkillResults_SingleWriter refuses a wrong writer,
            // and trg_PlacementSkillResults_CompletionIsEarned refuses a write that leaves a finished
            // placement with nothing measured.
            entity.ToTable(tb =>
            {
                tb.HasTrigger("trg_PlacementSkillResults_SingleWriter");
                tb.HasTrigger("trg_PlacementSkillResults_CompletionIsEarned");
            });

            // An unknown code throws rather than materialising as the pre-A1 floor, which would be a
            // fabricated level. CK_PSR_Status is what refuses it; this is the reader that agrees.
            entity.Property(e => e.ResultStatus)
                .HasConversion(
                    v => v.ToCode(),
                    v => EnumCodes.Parse<PlacementResultStatus>(v))
                .HasMaxLength(24)
                .IsUnicode(false);

            entity.Property(e => e.CefrLevel)
                .HasConversion(
                    v => v!.Value.Code,
                    v => ParseReportableLevel(v))
                .HasMaxLength(10);

            entity.Property(e => e.ProjectedBandCode).HasMaxLength(20).IsUnicode(false);
            entity.Property(e => e.FailureReason).HasMaxLength(24).IsUnicode(false);
            entity.Property(e => e.WrittenByService).HasMaxLength(64).IsUnicode(false);
            entity.Property(e => e.ProjectionVersion).HasDefaultValue(0);

            // The composite pins the row to its placement's certificate, so one certificate's skill
            // cannot be recorded under another's.
            entity.HasOne(d => d.Placement).WithMany(p => p.SkillResults)
                .HasPrincipalKey(p => new { p.PlacementId, p.CertId })
                .HasForeignKey(d => new { d.PlacementId, d.CertId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PSR_PlacementCert");

            entity.HasOne(d => d.SkillNavigation).WithMany(p => p.PlacementSkillResults)
                .HasPrincipalKey(p => new { p.SkillId, p.CertId })
                .HasForeignKey(d => new { d.SkillId, d.CertId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PSR_SkillCert");
        });
    }
}
