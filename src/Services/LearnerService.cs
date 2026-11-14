using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

using Services.Credits;

namespace Services;

/// <summary>The learner's active certificate and everything scoped by it. Every learner query
/// resolves the active certificate here first and filters on its id, so a later slice adding a
/// screen calls <see cref="GetActiveCertIdAsync"/> rather than trusting a certificate from the
/// request.</summary>
public sealed partial class LearnerService(
    CompanionDbContext db,
    ICreditLedger? creditLedger = null,
    ISkillStateWriter? skillStates = null,
    TimeProvider? clock = null) : ILearnerService
{
    private const int RecentCount = 5;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ICreditLedger _creditLedger = creditLedger ?? new CreditLedger(db, clock ?? TimeProvider.System);

    // #241 / #211: the ONE reader of a learner's per-skill state. Defaulted rather than required,
    // following this file's own `creditLedger ?? new CreditLedger(db)` convention: every existing
    // caller passes a context and nothing else, and the container's registration is what a host that
    // wants its own clock substitutes.
    private readonly ISkillStateWriter _skillStates = skillStates ?? new SkillStateWriter(db, clock ?? TimeProvider.System);

    public async Task<LearnerContext> GetContextAsync(int userId, CancellationToken cancellationToken = default)
    {
        var certificates = await ActiveCertificatesAsync(cancellationToken);
        var activeId = await ResolveActiveCertIdAsync(userId, certificates, cancellationToken);

        var balance = await _creditLedger.GetBalanceAsync(userId, cancellationToken);

        // #242: the shell's six doors read the certificate's own skill list, and its exam-date
        // indicator reads the goal row. Both are scoped to the ACTIVE certificate, which is why they
        // are read here: one switch moves the doors, the date and the practice pool together.
        var skills = await db.Skills
            .AsNoTracking()
            .Where(s => s.CertId == activeId)
            .OrderBy(s => s.DisplayOrder)
            .ThenBy(s => s.SkillId)
            .Select(s => s.Modality)
            .ToListAsync(cancellationToken);

        var examDate = await db.TargetBands
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.CertId == activeId)
            .Select(t => t.TargetDate)
            .FirstOrDefaultAsync(cancellationToken);

        return new LearnerContext(
            certificates.Single(c => c.CertId == activeId).Option,
            certificates.Select(c => c.Option).ToList(),
            balance,
            skills.Select(EnumCodes.Parse<Modality>).ToList(),
            examDate);
    }

    public async Task SwitchCertificateAsync(int userId, string certificateCode, CancellationToken cancellationToken = default)
    {
        // Validated the same way as the auth inputs: DataAnnotations on a BusinessObjects record
        // are the server-side authority, so a blank code is a ValidationException, not a 404.
        var input = new SwitchCertificateInput((certificateCode ?? "").Trim());
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);

        // Codes are stored upper-case in a case-insensitive collation; the comparison is the database's.
        var certId = await db.Certificates
            .Where(c => c.IsActive && c.Code == input.Code)
            .Select(c => (int?)c.CertId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new CertificateUnavailableException(input.Code);

        await SetActiveCertIdAsync(userId, certId, cancellationToken);
    }

    public async Task<LearnerDashboard> GetDashboardAsync(int userId, CancellationToken cancellationToken = default)
    {
        var certificates = await ActiveCertificatesAsync(cancellationToken);
        var certId = await ResolveActiveCertIdAsync(userId, certificates, cancellationToken);

        var attempts = db.ExamAttempts.AsNoTracking().Where(a => a.UserId == userId && a.CertId == certId);
        // #187: a session names its certificate directly, so a pool drill appears here too. Its label
        // is the section's for a section drill and the topic's for a pool drill.
        var practice = db.PracticeSessions.AsNoTracking().Where(p => p.UserId == userId && p.CertId == certId);

        var unfinishedAttempt = await attempts
            .Where(a => a.Status == AttemptStatus.InProgress.ToCode())
            .OrderByDescending(a => a.StartedAt)
            .Select(a => new RecentAttempt(
                a.AttemptId, a.Exam.Name, EnumCodes.Parse<ExamPurpose>(a.Exam.Purpose), EnumCodes.Parse<AttemptStatus>(a.Status), a.StartedAt, a.TotalScore, a.BandCode,
                // A placement is reached by its own id, not the attempt's (#159 review).
                a.PlacementTests.Select(p => (int?)p.PlacementId).FirstOrDefault()))
            .FirstOrDefaultAsync(cancellationToken);

        var unfinishedPractice = await practice
            .Where(p => p.Status == PracticeStatus.InProgress.ToCode())
            .OrderByDescending(p => p.StartedAt)
            .Select(p => new RecentPractice(
                p.SessionId, p.Section != null ? p.Section.Name : p.Topic != null ? p.Topic.Name : "", EnumCodes.Parse<PracticeStatus>(p.Status), p.CorrectCount, p.QuestionCount, p.StartedAt))
            .FirstOrDefaultAsync(cancellationToken);

        var attemptCount = await attempts.CountAsync(cancellationToken);
        var practiceCount = await practice.CountAsync(cancellationToken);

        // #241 / #211: the per-skill vector is read ONCE, from its one reader, and every block that
        // prints a level reads this. A second read is how the hero and the skill rows come to
        // disagree about one learner's band in the same page load.
        var standing = await _skillStates.ReadAsync(userId, certId, cancellationToken);

        // #159: each number the three blocks print is measured once here. The blocks never derive a
        // second opinion - a hero that guesses its own "next" is how the page grew two of them.
        var skills = await BuildSkillCardsAsync(userId, certId, standing, cancellationToken);
        var goal = await BuildGoalStandingAsync(userId, certId, standing, cancellationToken);

        // #235 / D12: the ceiling is derived from the answer rows, so one read here is what lets the
        // cascade see it without either recomputing or reading the view a second time. The view's day
        // is the UTC day (PracticeAnswers.AnsweredAt is UTC), which is why today is read the same way.
        var nowUtc = _clock.GetUtcNow();
        var utcToday = nowUtc.UtcDateTime.Date;
        var today = await db.vw_DailyPracticeVolumes
            .AsNoTracking()
            .Where(v => v.UserId == userId && v.PractisedOn == utcToday)
            .FirstOrDefaultAsync(cancellationToken);
        var ceilingReached = today is not null
            && (today.AnsweredCount >= FreeDrillState.DailyObjectiveCeiling || today.ProductiveTaskCount >= 4);

        var nextAction = await BuildNextActionAsync(
            userId, certId, certificates.Single(c => c.CertId == certId).Option,
            attemptCount + practiceCount == 0, skills, goal, standing, ceilingReached,
            unfinishedAttempt, unfinishedPractice, cancellationToken);

        var confirmedStatus = BookingStatus.Confirmed.ToCode();
        var upcomingBookings = await db.Bookings
            .AsNoTracking()
            .Where(b => b.UserId == userId && b.Status == confirmedStatus && b.Slot.EndAt > nowUtc)
            .OrderBy(b => b.Slot.StartAt)
            .Select(b => new UpcomingMentorBooking(
                b.BookingId,
                b.MentorId,
                b.Mentor.DisplayName,
                b.Mentor.Cert.Code,
                b.Mentor.Skill != null ? b.Mentor.Skill.Name : null,
                b.Slot.StartAt,
                b.Slot.EndAt,
                b.MeetLink ?? b.Mentor.MeetLink,
                b.FocusArea))
            .ToListAsync(cancellationToken);

        return new LearnerDashboard(
            certificates.Single(c => c.CertId == certId).Option,
            nextAction,
            goal,
            skills,
            attemptCount,
            practiceCount,
            unfinishedAttempt,
            unfinishedPractice,
            new StandingSummary(
                standing?.Overall,
                standing?.LimitingSkillCode,
                skills.FirstOrDefault(s => s.SkillId == standing?.LimitingSkillId)?.Name,
                standing is null || standing.HasNoMeasurement),
            upcomingBookings);
    }

    public async Task<int> GetActiveCertIdAsync(int userId, CancellationToken cancellationToken = default) =>
        await ResolveActiveCertIdAsync(userId, await ActiveCertificatesAsync(cancellationToken), cancellationToken);


    private async Task<List<ActiveCertificate>> ActiveCertificatesAsync(CancellationToken cancellationToken)
    {
        var certificates = await db.Certificates
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.CertId)
            .Select(c => new ActiveCertificate(c.CertId, new CertificateOption(c.Code, c.Name, c.SectionNoun)))
            .ToListAsync(cancellationToken);
        if (certificates.Count == 0)
        {
            throw new InvalidOperationException("No active certificate exists; run docs/seed-demo.sql.");
        }

        return certificates;
    }

    /// <summary>The stored choice while it is still offered; otherwise the first active certificate.
    /// A learner who never chose gets that default saved on first use, so adding a certificate
    /// later can't move them. A choice that was retired is not overwritten: if the certificate
    /// comes back, so does the learner's own choice.</summary>
    private async Task<int> ResolveActiveCertIdAsync(
        int userId, List<ActiveCertificate> certificates, CancellationToken cancellationToken)
    {
        var stored = await LiveUser(userId)
            .Select(u => new { u.ActiveCertId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw UserMissing(userId);

        if (certificates.Any(c => c.CertId == stored.ActiveCertId))
        {
            return stored.ActiveCertId!.Value;
        }

        var fallback = certificates[0].CertId;
        if (stored.ActiveCertId is null)
        {
            await SetActiveCertIdAsync(userId, fallback, cancellationToken);
        }

        return fallback;
    }

    private async Task SetActiveCertIdAsync(int userId, int certId, CancellationToken cancellationToken)
    {
        var updated = await LiveUser(userId)
            .ExecuteUpdateAsync(set => set.SetProperty(u => u.ActiveCertId, certId), cancellationToken);
        if (updated == 0)
        {
            throw UserMissing(userId);
        }
    }

    private IQueryable<User> LiveUser(int userId) => db.Users.Where(u => u.UserId == userId && !u.IsDeleted);

    private static InvalidOperationException UserMissing(int userId) => new($"User {userId} does not exist.");
}
