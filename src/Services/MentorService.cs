using BusinessObjects;
using DataAccess;
using Services.Credits;

namespace Services;

public sealed partial class MentorService(
    CompanionDbContext db,
    TimeProvider clock,
    ICreditLedger? creditLedger = null) : IMentorService
{
    private readonly ICreditLedger _creditLedger = creditLedger ?? new CreditLedger(db, clock);
}
