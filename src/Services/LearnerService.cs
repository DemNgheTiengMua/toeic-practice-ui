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
public sealed partial class LearnerService(CompanionDbContext db, ICreditLedger? creditLedger = null) : ILearnerService
{
    private const int RecentCount = 5;
    private readonly ICreditLedger _creditLedger = creditLedger ?? new CreditLedger(db);

    public async Task<LearnerContext> GetContextAsync(int userId, CancellationToken cancellationToken = default)
    {
        var certificates = await ActiveCertificatesAsync(cancellationToken);
        var activeId = await ResolveActiveCertIdAsync(userId, certificates, cancellationToken);

        var balance = await _creditLedger.GetBalanceAsync(userId, cancellationToken);
        return new LearnerContext(
            certificates.Single(c => c.CertId == activeId).Option,
            certificates.Select(c => c.Option).ToList(),
            balance);
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

        // #159: each number the three blocks print is measured once here. The blocks never derive a
        // second opinion - a hero that guesses its own "next" is how the page grew two of them.
        var skills = await BuildSkillCardsAsync(userId, certId, cancellationToken);
        var goal = await BuildGoalStandingAsync(userId, certId, cancellationToken);
        var nextAction = await BuildNextActionAsync(
            userId, certId, certificates.Single(c => c.CertId == certId).Option,
            attemptCount + practiceCount == 0, skills, goal, unfinishedAttempt, unfinishedPractice, cancellationToken);

        return new LearnerDashboard(
            certificates.Single(c => c.CertId == certId).Option,
            nextAction,
            goal,
            skills,
            attemptCount,
            practiceCount,
            unfinishedAttempt,
            unfinishedPractice);
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
