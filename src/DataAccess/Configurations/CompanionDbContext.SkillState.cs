using BusinessObjects;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace DataAccess;

public partial class CompanionDbContext
{
    /// <summary>
    /// #211: reads a stored per-skill level code back into a <see cref="CefrLevel"/>. A result, so the
    /// pre-A1 floor is legal - unlike authored content, where a <c>pre_a1</c> row is a content defect.
    /// CK_LSS_Level is what refuses anything else; this throws rather than materialising an unknown
    /// code as A1, because a silent floor is a fabricated level.
    /// </summary>
    private static CefrLevel ParseReportableLevel(string code) =>
        CefrLevel.TryParse(code, out var level)
            ? level
            : throw new InvalidOperationException(
                $"'{code}' is not a reportable CEFR level (pre_a1, A1-C2).");

    private static void ConfigureSkillState(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LearnerSkillState>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.CertId, e.SkillId })
                .HasName("PK_LearnerSkillStates");

            // HasTrigger: trg_LearnerSkillStates_SingleWriter refuses a write that declares a writer
            // other than SkillStateWriter, or no writer at all. Without the declaration here every EF
            // save to this table fails on the OUTPUT clause instead of on the rule.
            entity.ToTable(tb => tb.HasTrigger("trg_LearnerSkillStates_SingleWriter"));

            entity.Property(e => e.CefrLevel)
                .HasConversion(v => v.Code, v => ParseReportableLevel(v))
                .HasMaxLength(10)
                .IsUnicode(false);

            entity.Property(e => e.Source).HasMaxLength(16).IsUnicode(false);
            entity.Property(e => e.WrittenByService).HasMaxLength(64).IsUnicode(false);
            entity.Property(e => e.ProjectionVersion).HasDefaultValue(0);

            entity.HasIndex(e => new { e.UserId, e.CertId }, "IX_LSS_UserCert");

            // The composite is the isolation rule: one certificate's skill cannot be recorded under
            // another's. Both must be set, so EF models the pair as required - which is right here,
            // because neither column is nullable.
            entity.HasOne(d => d.SkillNavigation).WithMany(p => p.LearnerSkillStates)
                .HasPrincipalKey(p => new { p.SkillId, p.CertId })
                .HasForeignKey(d => new { d.SkillId, d.CertId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LSS_SkillCert");

            entity.HasOne(d => d.User).WithMany(p => p.LearnerSkillStates)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LSS_User");
        });
    }
}
