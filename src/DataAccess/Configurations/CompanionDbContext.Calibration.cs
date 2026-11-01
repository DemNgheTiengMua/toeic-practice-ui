using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace DataAccess;

public partial class CompanionDbContext
{
    private static void ConfigureCalibration(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CalibrationDecision>(entity =>
        {
            entity.HasKey(e => e.DecisionId).HasName("PK_CalibrationDecisions");

            entity.Property(e => e.DecidedAt).HasDefaultValueSql("(sysdatetimeoffset())");
            entity.Property(e => e.Decision).HasDefaultValue(CalibrationDecisionKind.Deferred);
            entity.Property(e => e.ObservedAccuracyPct).HasPrecision(5, 2);

            entity.HasOne(d => d.PlacedBand).WithMany(p => p.CalibrationPlacedDecisions)
                .HasPrincipalKey(p => new { p.CertId, p.Code })
                .HasForeignKey(d => new { d.CertId, d.PlacedBandCode })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Calibration_PlacedBand");

            entity.HasOne(d => d.ProposedBand).WithMany(p => p.CalibrationProposedDecisions)
                .HasPrincipalKey(p => new { p.CertId, p.Code })
                .HasForeignKey(d => new { d.CertId, d.ProposedBandCode })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Calibration_ProposedBand");

            entity.HasOne(d => d.User).WithMany(p => p.CalibrationDecisions)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Calibration_User");
        });
    }
}
