using System.Data;
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
            var pkg = c.Order.Package?.Name ?? $"{c.Order.Amount:N0} ₫";
            return $"Order {c.Order.OrderCode} — {pkg} ({c.Order.Status}, created {c.Order.CreatedAt:dd MMM yyyy})";
        }

        return null;
    }
}
