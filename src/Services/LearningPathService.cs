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

public sealed partial class LearningPathService(
    CompanionDbContext db,
    ILearnerService learners,
    ICreditLedger? creditLedger = null) : ILearningPathService
{
    private readonly ICreditLedger _creditLedger = creditLedger ?? new CreditLedger(db);
}
