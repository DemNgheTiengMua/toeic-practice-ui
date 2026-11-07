using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Services.Credits;

namespace Services;

public sealed partial class MentorService(
    CompanionDbContext db,
    TimeProvider clock,
    ILearnerService? learners = null,
    ISpeakingPracticeService? speakingPracticeService = null,
    ICreditLedger? creditLedger = null,
    ISkillStateWriter? skillStates = null) : IMentorService
{
    private readonly ICreditLedger _creditLedger = creditLedger ?? new CreditLedger(db, clock);

    /// <summary>#223: the standing a mentor receives is read through #211's one reader, never re-derived.</summary>
    private readonly ISkillStateWriter _skillStates = skillStates ?? new SkillStateWriter(db, clock);
}
