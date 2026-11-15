using System.Data;
using System.Linq.Expressions;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using static Contracts.EnumMapping;
using Services.Credits;

namespace Services;

public sealed partial class ComplaintService
{

    private static LearnerComplaintDto ToLearnerComplaintDto(Complaint c)
    {
        var typeDto = EnumCodes.Parse<ComplaintType>(c.Type).MapTo<ComplaintTypeDto>();
        var statusDto = EnumCodes.Parse<ComplaintStatus>(c.Status).MapTo<ComplaintStatusDto>();
        var (_, _, summary) = DescribeSubject(c);

        return new LearnerComplaintDto(
            c.ComplaintId,
            typeDto,
            c.Reason,
            statusDto,
            c.Resolution,
            c.ResolvedAt,
            c.ResolvedByGrantTxnId,
            c.CreditTransaction?.Delta,
            c.CreatedAt,
            c.BookingId,
            c.AttemptId,
            c.OrderId,
            summary);
    }

    /// <summary>
    /// #347: what the learner actually spent on the complaint's subject, so a compensation amount is
    /// read rather than guessed. Credits come from the ledger and nowhere else - an attempt can carry
    /// both a sitting charge and an AI-grading charge, so a cost constant would be correct for exactly
    /// one of the two subjects and silently wrong for the other. Money is the order's own amount,
    /// because an order is money that arrived.
    /// </summary>
    private async Task<(int CreditsSpent, decimal? MoneyPaid, RefundMethodDto? ImpliedMethod)>
        DescribeCompensationContextAsync(Complaint c, CancellationToken cancellationToken)
    {
        if (c.BookingId.HasValue)
        {
            var bookingId = c.BookingId.Value;
            return (await DebitsForSubjectAsync(t => t.BookingId == bookingId, cancellationToken), null, RefundMethodDto.Credit);
        }

        if (c.AttemptId.HasValue)
        {
            var attemptId = c.AttemptId.Value;
            return (await DebitsForSubjectAsync(t => t.AttemptId == attemptId, cancellationToken), null, RefundMethodDto.Credit);
        }

        if (c.OrderId.HasValue)
        {
            return (0, c.Order?.Amount, RefundMethodDto.Money);
        }

        return (0, null, null);
    }

    // shortcut: only debits are summed, so a cancelled-then-refunded booking still reports the charge
    // it originally paid. Netting the cancellation row in would hide a real charge behind a refund
    // that has its own record; revisit if the compensation figure ever drives money automatically.
    private async Task<int> DebitsForSubjectAsync(
        Expression<Func<CreditTransaction, bool>> subject,
        CancellationToken cancellationToken)
    {
        var debits = await db.CreditTransactions
            .AsNoTracking()
            .Where(subject)
            .Where(t => t.Delta < 0)
            .SumAsync(t => (int?)t.Delta, cancellationToken);

        return -(debits ?? 0);
    }

    private static (string SubjectType, int SubjectId, string Summary) DescribeSubject(Complaint c)
    {
        if (c.BookingId.HasValue)
        {
            var mentorName = c.Booking?.Mentor?.DisplayName ?? "Mentor";
            return ("Booking", c.BookingId.Value, $"Booking #{c.BookingId.Value} ({mentorName})");
        }

        if (c.AttemptId.HasValue)
        {
            var examTitle = c.ExamAttempt?.Exam?.Name ?? "Exam";
            return ("ExamAttempt", c.AttemptId.Value, $"Exam #{c.AttemptId.Value} ({examTitle})");
        }

        if (c.OrderId.HasValue)
        {
            var orderCode = c.Order?.OrderCode ?? $"#{c.OrderId.Value}";
            return ("Order", c.OrderId.Value, $"Order {orderCode}");
        }

        return ("Unknown", 0, "Unknown");
    }

    private static string? DescribeSubjectDetailed(Complaint c)
    {
        if (c.Booking != null)
        {
            var mentorName = c.Booking.Mentor?.DisplayName ?? "Unknown mentor";
            var time = c.Booking.Slot != null
                ? c.Booking.Slot.StartAt.ToString("dd MMM yyyy, HH:mm")
                : c.Booking.CreatedAt.ToString("dd MMM yyyy, HH:mm");
            return $"{mentorName} — {time} ({c.Booking.Status})";
        }

        if (c.ExamAttempt != null)
        {
            var examTitle = c.ExamAttempt.Exam?.Name ?? "Exam";
            var score = c.ExamAttempt.TotalScore != null ? $", score {c.ExamAttempt.TotalScore}" : "";
            return $"{examTitle} — started {c.ExamAttempt.StartedAt:dd MMM yyyy, HH:mm} ({c.ExamAttempt.Status}{score})";
        }

        if (c.Order != null)
        {
            return $"Order {c.Order.OrderCode} — {c.Order.Credits} credits ({c.Order.Amount:N0} ₫) ({c.Order.Status}, created {c.Order.CreatedAt:dd MMM yyyy})";
        }

        return null;
    }
}
