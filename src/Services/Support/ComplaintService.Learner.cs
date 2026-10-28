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
    public async Task<IReadOnlyList<LearnerComplaintDto>> GetLearnerComplaintsAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        var complaints = await db.Complaints
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .Include(c => c.CreditTransaction)
            .Include(c => c.Booking).ThenInclude(b => b!.Mentor)
            .Include(c => c.Booking).ThenInclude(b => b!.Slot)
            .Include(c => c.ExamAttempt).ThenInclude(a => a!.Exam)
            .Include(c => c.Order).ThenInclude(o => o!.Package)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

        return complaints.Select(ToLearnerComplaintDto).ToList();
    }

    public async Task<EligibleSubjectsDto> GetEligibleSubjectsAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        var bookings = await db.Bookings
            .AsNoTracking()
            .Where(b => b.UserId == userId)
            .Include(b => b.Mentor)
            .Include(b => b.Slot)
            .OrderByDescending(b => b.CreatedAt)
            .Take(20)
            .Select(b => new ComplaintEligibleBookingDto(
                b.BookingId,
                b.Mentor.DisplayName,
                b.Slot != null ? b.Slot.StartAt : b.CreatedAt,
                b.Status))
            .ToListAsync(cancellationToken);

        var attempts = await db.ExamAttempts
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .Include(a => a.Exam)
            .OrderByDescending(a => a.StartedAt)
            .Take(20)
            .Select(a => new ComplaintEligibleAttemptDto(
                a.AttemptId,
                a.Exam.Name,
                a.StartedAt,
                a.Status,
                a.TotalScore))
            .ToListAsync(cancellationToken);

        var orders = await db.Orders
            .AsNoTracking()
            .Where(o => o.UserId == userId)
            .Include(o => o.Package)
            .OrderByDescending(o => o.CreatedAt)
            .Take(20)
            .Select(o => new ComplaintEligibleOrderDto(
                o.OrderId,
                o.OrderCode,
                o.Package.Name,
                o.Amount,
                o.CreatedAt,
                o.Status))
            .ToListAsync(cancellationToken);

        return new EligibleSubjectsDto(bookings, attempts, orders);
    }

    public async Task<LearnerComplaintDto> FileComplaintAsync(
        int userId,
        FileComplaintRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ComplaintException(ApiError.Codes.Validation, "Reason is required.", 400);
        }

        if (request.Reason.Trim().Length > 4000)
        {
            throw new ComplaintException(ApiError.Codes.Validation, "Reason cannot exceed 4000 characters.", 400);
        }

        var subjectCount = (request.BookingId.HasValue ? 1 : 0)
            + (request.AttemptId.HasValue ? 1 : 0)
            + (request.OrderId.HasValue ? 1 : 0);

        if (subjectCount != 1)
        {
            throw new ComplaintException(
                ApiError.Codes.ComplaintInvalidSubject,
                "A complaint must reference exactly one booking, exam attempt, or order.",
                400);
        }

        var domainType = request.Type.MapTo<ComplaintType>();

        if (domainType is ComplaintType.MentorNoShow or ComplaintType.MentorQuality)
        {
            if (!request.BookingId.HasValue)
            {
                throw new ComplaintException(
                    ApiError.Codes.ComplaintInvalidSubject,
                    "Mentor complaints must reference a booking.",
                    400);
            }

            var bookingExists = await db.Bookings
                .AnyAsync(b => b.BookingId == request.BookingId.Value && b.UserId == userId, cancellationToken);
            if (!bookingExists)
            {
                throw new ComplaintException(
                    ApiError.Codes.ComplaintInvalidSubject,
                    "The referenced booking does not exist or does not belong to you.",
                    400);
            }
        }
        else if (domainType is ComplaintType.GradingIssue or ComplaintType.TechnicalError)
        {
            if (!request.AttemptId.HasValue)
            {
                throw new ComplaintException(
                    ApiError.Codes.ComplaintInvalidSubject,
                    "Exam complaints must reference an exam attempt.",
                    400);
            }

            var attemptExists = await db.ExamAttempts
                .AnyAsync(a => a.AttemptId == request.AttemptId.Value && a.UserId == userId, cancellationToken);
            if (!attemptExists)
            {
                throw new ComplaintException(
                    ApiError.Codes.ComplaintInvalidSubject,
                    "The referenced exam attempt does not exist or does not belong to you.",
                    400);
            }
        }
        else if (domainType is ComplaintType.PaymentIssue)
        {
            if (!request.OrderId.HasValue)
            {
                throw new ComplaintException(
                    ApiError.Codes.ComplaintInvalidSubject,
                    "Payment complaints must reference an order.",
                    400);
            }

            var orderExists = await db.Orders
                .AnyAsync(o => o.OrderId == request.OrderId.Value && o.UserId == userId, cancellationToken);
            if (!orderExists)
            {
                throw new ComplaintException(
                    ApiError.Codes.ComplaintInvalidSubject,
                    "The referenced order does not exist or does not belong to you.",
                    400);
            }
        }

        var complaint = new Complaint
        {
            UserId = userId,
            Type = domainType.ToCode(),
            Reason = request.Reason.Trim(),
            Status = ComplaintStatus.Open.ToCode(),
            BookingId = request.BookingId,
            AttemptId = request.AttemptId,
            OrderId = request.OrderId,
            CreatedAt = clock.GetUtcNow(),
        };

        db.Complaints.Add(complaint);
        await db.SaveChangesAsync(cancellationToken);

        // Re-load to build DTO with navigation summaries
        return (await GetLearnerComplaintsAsync(userId, cancellationToken))
            .First(c => c.ComplaintId == complaint.ComplaintId);
    }
}
