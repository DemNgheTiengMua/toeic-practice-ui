using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Services.Credits;

namespace Services;

public sealed partial class LearningPathService
{

    public async Task<bool> ResetActivePathAsync(int userId, CancellationToken cancellationToken = default)
    {
        var activeCertId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var path = await db.LearnerPaths
            .FirstOrDefaultAsync(p => p.UserId == userId && p.CertId == activeCertId && p.Status == "active", cancellationToken);

        if (path != null)
        {
            path.Status = "reset";
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        return false;
    }

    public async Task<LearningPathSummaryView> UpgradePathAsync(int userId, CancellationToken cancellationToken = default)
    {
        var activeCertId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var activePath = await db.LearnerPaths
            .FirstOrDefaultAsync(p => p.UserId == userId && p.CertId == activeCertId && p.Status == "active", cancellationToken)
            ?? throw new LearningPathException(LearningPathException.PathNotFound, "No active learning path found.");

        try
        {
            await _creditLedger.ChargePremiumPathAsync(userId, activePath.LearnerPathId, 5, cancellationToken);
        }
        catch (CreditLedgerException ex) when (ex.Code == CreditLedgerException.InsufficientCredits)
        {
            throw new LearningPathException(LearningPathException.InsufficientCredits, "You do not have enough credits to upgrade to premium (5 credits required).");
        }

        return await GetActivePathAsync(userId, cancellationToken);
    }
}
