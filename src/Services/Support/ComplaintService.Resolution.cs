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

    /// <summary>
    /// #350: resolving is one compensation ending, chosen explicitly.
    /// <para>
    /// <b>The make-good is bounded by what the learner spent</b>, read from the ledger rather than a
    /// constant. A fixed 20 was wrong in both directions: it refused the correct compensation for a
    /// subject that spent 51, and it would have paid 20 for a subject that spent 1. Equality is the
    /// ceiling, because returning the loss is the point and exceeding it is a gift, not a make-good.
    /// </para>
    /// <para>
    /// <b>The refund arm moves no money and resolves nothing.</b> It refuses a subject that never moved
    /// a value, records the promise in the internal notes, and leaves the complaint open for
    /// <c>RefundService</c> to drive on approval or settlement (#348). Creating the refund here would
    /// collapse two permissions into one and make a promise indistinguishable from a payment.
    /// </para>
    /// </summary>
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

        if (request.GrantCredits < 0)
        {
            throw new ComplaintException(ApiError.Codes.Validation, "Grant credits cannot be negative.", 400);
        }

        // One ending, and only its own field: a credit count alongside "no compensation" is the shape
        // that used to move money by accident.
        if (request.Compensation != ComplaintCompensationDto.MakeGood && request.GrantCredits > 0)
        {
            throw new ComplaintException(
                ApiError.Codes.Validation,
                "Grant credits belong to the make-good ending; choose that ending to grant them.",
                400);
        }

        if (request.Compensation == ComplaintCompensationDto.MakeGood && request.GrantCredits <= 0)
        {
            throw new ComplaintException(
                ApiError.Codes.Validation,
                "A make-good must grant at least one credit; choose no compensation instead.",
                400);
        }

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var complaint = await db.Complaints
            .Include(c => c.Order)
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

        var (creditsSpent, moneyPaid, impliedMethod) =
            await DescribeCompensationContextAsync(complaint, cancellationToken);

        if (request.Compensation == ComplaintCompensationDto.MakeGood)
        {
            AssertMakeGoodAllowed(complaint, creditsSpent, impliedMethod, request.GrantCredits);
        }

        if (request.Compensation == ComplaintCompensationDto.Refund)
        {
            if (impliedMethod is null || (creditsSpent == 0 && moneyPaid is null))
            {
                throw new ComplaintException(
                    ApiError.Codes.ComplaintNothingToRefund,
                    "Nothing to refund on this subject: the learner was never charged for it.",
                    400);
            }

            // The promise, in the one place that costs nothing: the refund desk raises the row, and
            // #348's lifecycle resolves this complaint when (and only when) value actually moves.
            // The incident is now under review, which is the state a rejection returns it to.
            var promise = $"Refund to be recorded ({impliedMethod}). {request.InternalNotes.Trim()}";
            complaint.Status = ComplaintStatus.Reviewing.ToCode();
            complaint.InternalNotes = promise;

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return (await GetAdminComplaintDetailAsync(complaintId, cancellationToken))!;
        }

        if (request.Compensation == ComplaintCompensationDto.MakeGood)
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

    /// <summary>
    /// Credits are the make-good's unit, so a money subject has no make-good to offer: returning credits
    /// for an order hands back a currency the learner never paid in. The ceiling is the spend itself.
    /// </summary>
    private static void AssertMakeGoodAllowed(
        Complaint complaint,
        int creditsSpent,
        RefundMethodDto? impliedMethod,
        int grantCredits)
    {
        if (impliedMethod == RefundMethodDto.Money)
        {
            throw new ComplaintException(
                ApiError.Codes.Validation,
                "This subject is an order, which was paid in money. Record a refund instead of granting credits.",
                400);
        }

        if (creditsSpent == 0)
        {
            throw new ComplaintException(
                ApiError.Codes.Validation,
                "This subject was never charged, so there are no credits to make good.",
                400);
        }

        if (grantCredits > creditsSpent)
        {
            throw new ComplaintException(
                ApiError.Codes.Validation,
                $"A make-good may return at most {creditsSpent} credit{(creditsSpent == 1 ? string.Empty : "s")}, "
                    + $"the amount spent on this subject; {grantCredits} was requested.",
                400);
        }
    }
}
