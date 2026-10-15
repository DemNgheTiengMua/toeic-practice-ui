using System.Data;
using BusinessObjects;
using Contracts;
using DataAccess;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;
using static Contracts.EnumMapping;

namespace Services;

public class ComplaintException(string code, string message, int status = 400) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

public sealed class ComplaintNotFoundException(int complaintId)
    : ComplaintException(ApiError.Codes.ComplaintNotFound, $"Complaint {complaintId} was not found.", 404);

public interface IComplaintService
{
    Task<IReadOnlyList<LearnerComplaintDto>> GetLearnerComplaintsAsync(
        int userId,
        CancellationToken cancellationToken = default);

    Task<EligibleSubjectsDto> GetEligibleSubjectsAsync(
        int userId,
        CancellationToken cancellationToken = default);

    Task<LearnerComplaintDto> FileComplaintAsync(
        int userId,
        FileComplaintRequest request,
        CancellationToken cancellationToken = default);

    Task<AdminComplaintsSummaryDto> GetAdminComplaintsAsync(
        string? q = null,
        string? status = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    Task<AdminComplaintDetailDto?> GetAdminComplaintDetailAsync(
        int complaintId,
        CancellationToken cancellationToken = default);

    Task<AdminComplaintDetailDto> SetComplaintStatusAsync(
        int adminUserId,
        int complaintId,
        ComplaintStatus status,
        CancellationToken cancellationToken = default);

    Task<AdminComplaintDetailDto> ResolveComplaintAsync(
        int adminUserId,
        int complaintId,
        ResolveComplaintRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class ComplaintService(
    CompanionDbContext db,
    TimeProvider clock) : IComplaintService
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

    public async Task<AdminComplaintsSummaryDto> GetAdminComplaintsAsync(
        string? q = null,
        string? status = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;

        var pendingCount = await db.Complaints
            .AsNoTracking()
            .CountAsync(c => c.Status == ComplaintStatus.Open.ToCode() || c.Status == ComplaintStatus.Reviewing.ToCode(), cancellationToken);

        var resolvedCount = await db.Complaints
            .AsNoTracking()
            .CountAsync(c => c.Status == ComplaintStatus.Resolved.ToCode(), cancellationToken);

        var oldestPending = await db.Complaints
            .AsNoTracking()
            .Where(c => c.Status == ComplaintStatus.Open.ToCode() || c.Status == ComplaintStatus.Reviewing.ToCode())
            .OrderBy(c => c.CreatedAt)
            .Select(c => new { c.ComplaintId, c.Type, c.CreatedAt })
            .FirstOrDefaultAsync(cancellationToken);

        IQueryable<Complaint> query = db.Complaints
            .AsNoTracking()
            .Include(c => c.User)
            .Include(c => c.Booking).ThenInclude(b => b!.Mentor)
            .Include(c => c.ExamAttempt).ThenInclude(a => a!.Exam)
            .Include(c => c.Order).ThenInclude(o => o!.Package);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var trimmed = q.Trim().ToLowerInvariant();
            var idString = trimmed.StartsWith("#c-") ? trimmed[3..]
                : trimmed.StartsWith("c-") ? trimmed[2..]
                : trimmed.StartsWith('#') ? trimmed[1..]
                : trimmed;
            var isId = int.TryParse(idString, out var searchId);

            query = query.Where(c =>
                (isId && c.ComplaintId == searchId) ||
                c.User.Email.ToLower().Contains(trimmed) ||
                (c.User.FullName != null && c.User.FullName.ToLower().Contains(trimmed)));
        }

        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var trimmedStatus = status.Trim().ToLower();
            if (trimmedStatus == "pending")
            {
                query = query.Where(c => c.Status == ComplaintStatus.Open.ToCode() || c.Status == ComplaintStatus.Reviewing.ToCode());
            }
            else if (EnumCodes.TryParse<ComplaintStatus>(trimmedStatus, out var parsedStatus))
            {
                query = query.Where(c => c.Status == parsedStatus.ToCode());
            }
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var mappedItems = items.Select(c =>
        {
            var typeDto = EnumCodes.Parse<ComplaintType>(c.Type).MapTo<ComplaintTypeDto>();
            var statusDto = EnumCodes.Parse<ComplaintStatus>(c.Status).MapTo<ComplaintStatusDto>();

            var (subjectType, subjectId, summary) = DescribeSubject(c);

            return new AdminComplaintListItemDto(
                c.ComplaintId,
                c.UserId,
                c.User.FullName ?? c.User.Email,
                c.User.Email,
                typeDto,
                subjectType,
                subjectId,
                summary,
                statusDto,
                c.CreatedAt,
                c.ResolvedAt);
        }).ToList();

        ComplaintTypeDto? oldestType = oldestPending != null
            ? EnumCodes.Parse<ComplaintType>(oldestPending.Type).MapTo<ComplaintTypeDto>()
            : null;

        return new AdminComplaintsSummaryDto(
            mappedItems,
            totalCount,
            page,
            pageSize,
            pendingCount,
            resolvedCount,
            oldestPending?.CreatedAt,
            oldestPending?.ComplaintId,
            oldestType);
    }

    public async Task<AdminComplaintDetailDto?> GetAdminComplaintDetailAsync(
        int complaintId,
        CancellationToken cancellationToken = default)
    {
        var complaint = await db.Complaints
            .AsNoTracking()
            .Include(c => c.User)
            .Include(c => c.ResolvedByUser)
            .Include(c => c.CreditTransaction)
            .Include(c => c.Booking).ThenInclude(b => b!.Mentor)
            .Include(c => c.Booking).ThenInclude(b => b!.Slot)
            .Include(c => c.ExamAttempt).ThenInclude(a => a!.Exam)
            .Include(c => c.Order).ThenInclude(o => o!.Package)
            .SingleOrDefaultAsync(c => c.ComplaintId == complaintId, cancellationToken);

        if (complaint is null) return null;

        var typeDto = EnumCodes.Parse<ComplaintType>(complaint.Type).MapTo<ComplaintTypeDto>();
        var statusDto = EnumCodes.Parse<ComplaintStatus>(complaint.Status).MapTo<ComplaintStatusDto>();
        var (subjectType, subjectId, summary) = DescribeSubject(complaint);
        var details = DescribeSubjectDetailed(complaint);

        return new AdminComplaintDetailDto(
            complaint.ComplaintId,
            complaint.UserId,
            complaint.User.FullName ?? complaint.User.Email,
            complaint.User.Email,
            typeDto,
            complaint.Reason,
            statusDto,
            subjectType,
            subjectId,
            summary,
            details,
            complaint.CreatedAt,
            complaint.Resolution,
            complaint.InternalNotes,
            complaint.ResolvedByUserId,
            complaint.ResolvedByUser?.FullName ?? complaint.ResolvedByUser?.Email,
            complaint.ResolvedAt,
            complaint.ResolvedByGrantTxnId,
            complaint.CreditTransaction?.Delta);
    }

    public async Task<AdminComplaintDetailDto> SetComplaintStatusAsync(
        int adminUserId,
        int complaintId,
        ComplaintStatus status,
        CancellationToken cancellationToken = default)
    {
        if (adminUserId <= 0)
        {
            throw new ComplaintException(ApiError.Codes.Validation, "Invalid admin user ID.", 400);
        }

        if (status is not (ComplaintStatus.Open or ComplaintStatus.Reviewing))
        {
            if (status == ComplaintStatus.Resolved)
            {
                throw new ComplaintException(
                    ApiError.Codes.Validation,
                    "Use the resolve action to resolve a complaint.",
                    400);
            }

            throw new ComplaintException(ApiError.Codes.Validation, "Invalid complaint status.", 400);
        }

        var complaint = await db.Complaints
            .SingleOrDefaultAsync(c => c.ComplaintId == complaintId, cancellationToken);

        if (complaint is null)
        {
            throw new ComplaintNotFoundException(complaintId);
        }

        if (complaint.Status == ComplaintStatus.Resolved.ToCode())
        {
            throw new ComplaintException(
                ApiError.Codes.ComplaintAlreadyResolved,
                "Resolved complaints cannot change status.",
                400);
        }

        complaint.Status = status.ToCode();
        await db.SaveChangesAsync(cancellationToken);

        return (await GetAdminComplaintDetailAsync(complaintId, cancellationToken))!;
    }

    public async Task<AdminComplaintDetailDto> ResolveComplaintAsync(
        int adminUserId,
        int complaintId,
        ResolveComplaintRequest request,
        CancellationToken cancellationToken = default)
    {
        if (adminUserId <= 0)
        {
            throw new ComplaintException(ApiError.Codes.Validation, "Invalid admin user ID.", 400);
        }

        if (string.IsNullOrWhiteSpace(request.Resolution))
        {
            throw new ComplaintException(ApiError.Codes.Validation, "Resolution is required.", 400);
        }

        if (request.Resolution.Trim().Length > 4000)
        {
            throw new ComplaintException(ApiError.Codes.Validation, "Resolution cannot exceed 4000 characters.", 400);
        }

        if (string.IsNullOrWhiteSpace(request.InternalNotes))
        {
            throw new ComplaintException(ApiError.Codes.Validation, "Internal notes are required.", 400);
        }

        if (request.InternalNotes.Trim().Length > 4000)
        {
            throw new ComplaintException(ApiError.Codes.Validation, "Internal notes cannot exceed 4000 characters.", 400);
        }

        if (request.GrantCredits < 0 || request.GrantCredits > 20)
        {
            throw new ComplaintException(ApiError.Codes.Validation, "Grant credits must be between 0 and 20.", 400);
        }

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var complaint = await db.Complaints
            .SingleOrDefaultAsync(c => c.ComplaintId == complaintId, cancellationToken);

        if (complaint is null)
        {
            throw new ComplaintNotFoundException(complaintId);
        }

        if (complaint.Status == ComplaintStatus.Resolved.ToCode())
        {
            throw new ComplaintException(
                ApiError.Codes.ComplaintAlreadyResolved,
                "This complaint has already been resolved.",
                400);
        }

        if (request.GrantCredits > 0)
        {
            var note = $"Complaint #{complaint.ComplaintId}: {request.Resolution.Trim()}";
            if (note.Length > 400) note = note[..400];

            var txn = new CreditTransaction
            {
                UserId = complaint.UserId,
                Reason = CreditReason.AdminGrant.ToCode(),
                Delta = request.GrantCredits,
                AdminUserId = adminUserId,
                Note = note,
                CreatedAt = clock.GetUtcNow(),
            };

            db.CreditTransactions.Add(txn);
            await db.SaveChangesAsync(cancellationToken);

            complaint.ResolvedByGrantTxnId = txn.TxnId;
        }

        complaint.Status = ComplaintStatus.Resolved.ToCode();
        complaint.Resolution = request.Resolution.Trim();
        complaint.InternalNotes = request.InternalNotes.Trim();
        complaint.ResolvedByUserId = adminUserId;
        complaint.ResolvedAt = clock.GetUtcNow();

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return (await GetAdminComplaintDetailAsync(complaintId, cancellationToken))!;
    }

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
