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

public sealed partial class ComplaintService(
    CompanionDbContext db,
    TimeProvider clock,
    ICreditLedger? creditLedger = null) : IComplaintService
{
    private readonly ICreditLedger _creditLedger = creditLedger ?? new CreditLedger(db, clock);
}
