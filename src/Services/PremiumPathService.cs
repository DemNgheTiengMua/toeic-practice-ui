using System.Linq;
using System.Threading.Tasks;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services.Credits;

namespace Services;

public interface IPremiumPathService
{
    Task UnlockPathAsync(int userId, int learnerPathId);
    Task<bool> IsPathUnlockedAsync(int userId, int learnerPathId);
}

public sealed class PremiumPathService(CompanionDbContext db, ICreditLedger creditLedger) : IPremiumPathService
{
    public async Task UnlockPathAsync(int userId, int learnerPathId)
    {
        // Check if already unlocked (idempotent)
        var alreadyUnlocked = await IsPathUnlockedAsync(userId, learnerPathId);
        if (alreadyUnlocked)
            return;

        try
        {
            // Charge 5 credits for premium path unlock
            await creditLedger.ChargePremiumPathAsync(userId, learnerPathId);
        }
        catch (CreditLedgerException ex) when (ex.Code == CreditLedgerException.InsufficientCredits)
        {
            throw new InvalidOperationException("Insufficient credits to unlock path.", ex);
        }
    }

    public async Task<bool> IsPathUnlockedAsync(int userId, int learnerPathId)
    {
        return await db.CreditTransactions
            .AnyAsync(t => t.UserId == userId
                && t.Reason == "premium_path"
                && t.LearnerPathId == learnerPathId);
    }
}
