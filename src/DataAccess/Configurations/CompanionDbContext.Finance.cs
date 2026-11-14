using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace DataAccess;

public partial class CompanionDbContext
{
    private static void ConfigureFinance(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(e => e.OrderId).HasName("PK__Orders__C3905BCFA292CBC6");

            entity.ToTable(tb => tb.HasTrigger("trg_Orders_CreditedStaysPaid"));

            entity.HasIndex(e => e.CreatedAt, "IX_Orders_Open").HasFilter("([Status] IN ('pending', 'reconciling'))");

            entity.HasIndex(e => new { e.Gateway, e.GatewayTxnRef }, "UX_Orders_GatewayTxnRef")
                .IsUnique()
                .HasFilter("([GatewayTxnRef] IS NOT NULL)");

            entity.HasIndex(e => e.UserId, "UX_Orders_OnePendingPerUser")
                .IsUnique()
                .HasFilter("([Status] IN ('pending', 'reconciling'))");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetimeoffset())");
            entity.Property(e => e.Status).HasDefaultValue("pending");
            entity.Property(e => e.Credits).HasDefaultValue(1);

            entity.HasOne(d => d.User).WithOne(p => p.Order)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Orders__UserId__24B26D99");
        });



        modelBuilder.Entity<PaymentEvent>(entity =>
        {
            entity.HasKey(e => e.EventId).HasName("PK__PaymentE__7944C8104787C6C1");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetimeoffset())", "DF_PaymentEvents_CreatedAt");

            entity.HasOne(d => d.Order).WithMany(p => p.PaymentEvents)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__PaymentEv__Order__2E3BD7D3");
        });



        modelBuilder.Entity<CreditTransaction>(entity =>
        {
            entity.HasKey(e => e.TxnId).HasName("PK__CreditTr__C19608544ABB5611");

            entity.ToTable(tb =>
                {
                    tb.HasTrigger("trg_CreditTransactions_AppendOnly");
                    tb.HasTrigger("trg_CreditTransactions_BookingAmounts");
                    tb.HasTrigger("trg_CreditTransactions_ChargeMatchesExamPurpose");
                    tb.HasTrigger("trg_CreditTransactions_NoNegativeBalance");
                    tb.HasTrigger("trg_CreditTransactions_NoRewrite");
                    tb.HasTrigger("trg_CreditTransactions_PurchaseMatchesOrder");
                });

            entity.HasIndex(e => new { e.AttemptId, e.Reason }, "UX_CreditTxn_Attempt_Reason")
                .IsUnique()
                .HasFilter("([AttemptId] IS NOT NULL)");

            entity.HasIndex(e => new { e.BookingId, e.Reason }, "UX_CreditTxn_Booking")
                .IsUnique()
                .HasFilter("([BookingId] IS NOT NULL)");

            entity.HasIndex(e => e.OrderId, "UX_CreditTxn_Order")
                .IsUnique()
                .HasFilter("([OrderId] IS NOT NULL)");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetimeoffset())");

            entity.HasOne(d => d.AdminUser).WithMany(p => p.CreditTransactionAdminUsers).HasConstraintName("FK_CreditTxn_Admin");

            entity.HasOne(d => d.User).WithMany(p => p.CreditTransactionUsers)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__CreditTra__UserI__39AD8A7F");

            entity.HasOne(d => d.ExamAttempt).WithMany(p => p.CreditTransactions)
                .HasPrincipalKey(p => new { p.AttemptId, p.UserId })
                .HasForeignKey(d => new { d.AttemptId, d.UserId })
                .HasConstraintName("FK_CreditTxn_Attempt");

            entity.HasOne(d => d.Booking).WithMany(p => p.CreditTransactions)
                .HasPrincipalKey(p => new { p.BookingId, p.UserId })
                .HasForeignKey(d => new { d.BookingId, d.UserId })
                .HasConstraintName("FK_CreditTxn_Booking");

            entity.HasOne(d => d.Order).WithMany(p => p.CreditTransactions)
                .HasPrincipalKey(p => new { p.OrderId, p.UserId })
                .HasForeignKey(d => new { d.OrderId, d.UserId })
                .HasConstraintName("FK_CreditTxn_Order");
        });



        // #280: a refund posts its credit through the ledger, so its configuration sits beside the
        // credit transaction's. The two triggers are declared here or every EF save on this table fails
        // on the OUTPUT clause (ADR-0001).
        modelBuilder.Entity<Refund>(entity =>
        {
            entity.HasKey(e => e.RefundId).HasName("PK__Refunds__725AB920C69EA814");

            entity.ToTable(tb =>
                {
                    tb.HasTrigger("trg_Refunds_AppendOnly");
                    tb.HasTrigger("trg_Refunds_CreditTxnIsRefundCredit");
                });

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetimeoffset())");
            entity.Property(e => e.Status).HasDefaultValue("requested");

            // Filtered: a refund with no order is legal, so the index only covers the ones that have one.
            entity.HasIndex(e => e.OrderId, "IX_Refunds_Order").HasFilter("([OrderId] IS NOT NULL)");

            entity.HasOne(d => d.DecidedByUser).WithMany(p => p.RefundDecidedByUsers).HasConstraintName("FK_Refunds_DecidedBy");

            entity.HasOne(d => d.RequestedByUser).WithMany(p => p.RefundRequestedByUsers)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Refunds_RequestedBy");

            entity.HasOne(d => d.SettledByUser).WithMany(p => p.RefundSettledByUsers).HasConstraintName("FK_Refunds_SettledBy");

            entity.HasOne(d => d.User).WithMany(p => p.RefundUsers)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Refunds__UserId__2FBA0BF1");

            entity.HasOne(d => d.Complaint).WithMany(p => p.Refunds)
                .HasPrincipalKey(p => new { p.ComplaintId, p.UserId })
                .HasForeignKey(d => new { d.ComplaintId, d.UserId })
                .HasConstraintName("FK_Refunds_ComplaintUser");

            entity.HasOne(d => d.CreditTransaction).WithMany(p => p.Refunds)
                .HasPrincipalKey(p => new { p.TxnId, p.UserId })
                .HasForeignKey(d => new { d.CreditTxnId, d.UserId })
                .HasConstraintName("FK_Refunds_CreditTxn");

            entity.HasOne(d => d.Order).WithMany(p => p.Refunds)
                .HasPrincipalKey(p => new { p.OrderId, p.UserId })
                .HasForeignKey(d => new { d.OrderId, d.UserId })
                .HasConstraintName("FK_Refunds_OrderUser");
        });



        modelBuilder.Entity<Complaint>(entity =>
        {
            entity.HasKey(e => e.ComplaintId).HasName("PK__Complain__740D898F85AEDFE4");

            entity.ToTable(tb => tb.HasTrigger("trg_Complaints_GrantMustBeAdminGrant"));

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysdatetimeoffset())");
            entity.Property(e => e.Status).HasDefaultValue("open");

            entity.HasOne(d => d.ResolvedByUser).WithMany(p => p.ComplaintResolvedByUsers).HasConstraintName("FK_Complaints_ResolvedBy");

            entity.HasOne(d => d.User).WithMany(p => p.ComplaintUsers)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Complaint__UserI__7E8CC4B1");

            entity.HasOne(d => d.ExamAttempt).WithMany(p => p.Complaints)
                .HasPrincipalKey(p => new { p.AttemptId, p.UserId })
                .HasForeignKey(d => new { d.AttemptId, d.UserId })
                .HasConstraintName("FK_Complaints_AttemptUser");

            entity.HasOne(d => d.Booking).WithMany(p => p.Complaints)
                .HasPrincipalKey(p => new { p.BookingId, p.UserId })
                .HasForeignKey(d => new { d.BookingId, d.UserId })
                .HasConstraintName("FK_Complaints_BookingUser");

            entity.HasOne(d => d.Order).WithMany(p => p.Complaints)
                .HasPrincipalKey(p => new { p.OrderId, p.UserId })
                .HasForeignKey(d => new { d.OrderId, d.UserId })
                .HasConstraintName("FK_Complaints_OrderUser");

            entity.HasOne(d => d.CreditTransaction).WithMany(p => p.Complaints)
                .HasPrincipalKey(p => new { p.TxnId, p.UserId })
                .HasForeignKey(d => new { d.ResolvedByGrantTxnId, d.UserId })
                .HasConstraintName("FK_Complaints_GrantTxn");
        });

        modelBuilder.HasSequence("SEQ_PaymentNo");
    }
}
