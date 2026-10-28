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
            var receipt = await _creditLedger.AdjustCreditsAsync(complaint.UserId, adminUserId, request.GrantCredits, note, cancellationToken);
            complaint.ResolvedByGrantTxnId = receipt.TxnId;
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
}
