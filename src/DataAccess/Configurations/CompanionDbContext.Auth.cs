using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace DataAccess;

public partial class CompanionDbContext
{
    private static void ConfigureAuth(ModelBuilder modelBuilder)
    {


        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.RoleId).HasName("PK__Roles__8AFACE1AE028D7CB");
        });



        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PK__Users__1788CC4C33908FE3");

            entity.HasIndex(e => e.Email, "UX_Users_Email")
                .IsUnique()
                .HasFilter("([IsDeleted]=(0))");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetimeoffset())", "DF_Users_CreatedAt");
            entity.Property(e => e.SecurityStamp).HasDefaultValueSql("(CONVERT([varchar](36),newid()))", "DF_Users_SecurityStamp");
            entity.Property(e => e.TimeZoneId).HasDefaultValue("Asia/Ho_Chi_Minh", "DF_Users_TimeZoneId");

            entity.HasOne(d => d.ActiveCert).WithMany(p => p.Users).HasConstraintName("FK_Users_ActiveCert");

            entity.HasOne(d => d.Role).WithMany(p => p.Users)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Users_Role");
        });



        modelBuilder.Entity<EmailVerificationToken>(entity =>
        {
            entity.HasKey(e => e.TokenId).HasName("PK__EmailVer__658FEEEA5F8B9DDD");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetimeoffset())", "DF_EmailVerificationTokens_CreatedAt");

            entity.HasOne(d => d.User).WithMany(p => p.EmailVerificationTokens)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_EmailVerificationTokens_User");
        });



        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.HasKey(e => e.TokenId).HasName("PK__Password__658FEEEA66FD11FE");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetimeoffset())", "DF_PasswordResetTokens_CreatedAt");

            entity.HasOne(d => d.User).WithMany(p => p.PasswordResetTokens)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PasswordResetTokens_User");
        });
    }
}
