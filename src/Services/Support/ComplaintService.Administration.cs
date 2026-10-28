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
}
