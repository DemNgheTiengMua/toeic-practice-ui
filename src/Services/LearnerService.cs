using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using DataAccess;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>Thrown when a learner asks to switch to a certificate that doesn't exist or is retired.</summary>
public sealed class CertificateUnavailableException(string code)
    : Exception($"Certificate '{code}' is not available.")
{
    public string CertificateCode { get; } = code;
}

public interface ILearnerService
{
    Task<LearnerContext> GetContextAsync(int userId, CancellationToken cancellationToken = default);

    Task SwitchCertificateAsync(int userId, string certificateCode, CancellationToken cancellationToken = default);

    Task<LearnerDashboard> GetDashboardAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>The certificate id every learner query filters on. Later slices (practice, attempts,
    /// history, weakness analysis) call this instead of taking a certificate from the request.</summary>
    Task<int> GetActiveCertIdAsync(int userId, CancellationToken cancellationToken = default);
}

/// <summary>The learner's active certificate and everything scoped by it. Every learner query
/// resolves the active certificate here first and filters on its id, so a later slice adding a
/// screen calls <see cref="GetActiveCertIdAsync"/> rather than trusting a certificate from the
/// request.</summary>
public sealed class LearnerService(CompanionDbContext db) : ILearnerService
{
    private const int RecentCount = 5;

    public async Task<LearnerContext> GetContextAsync(int userId, CancellationToken cancellationToken = default)
    {
        var certificates = await ActiveCertificatesAsync(cancellationToken);
        var activeId = await ResolveActiveCertIdAsync(userId, certificates, cancellationToken);

        // The view has no row for an empty ledger; that is a zero balance, not a missing one.
        var balance = await db.vw_UserCreditBalances
            .Where(b => b.UserId == userId)
            .Select(b => b.Balance)
            .SingleOrDefaultAsync(cancellationToken) ?? 0;
        var timeZoneId = await LiveUser(userId)
            .Select(u => u.TimeZoneId)
            .SingleAsync(cancellationToken);

        return new LearnerContext(
            certificates.Single(c => c.CertId == activeId).Option,
            certificates.Select(c => c.Option).ToList(),
            balance,
            timeZoneId);
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
        // Practice sessions carry no CertId of their own; their section's does.
        var practice = db.PracticeSessions.AsNoTracking().Where(p => p.UserId == userId && p.Section.CertId == certId);

        var recentAttempts = await attempts
            .OrderByDescending(a => a.StartedAt)
            .Take(RecentCount)
            .Select(a => new RecentAttempt(
                a.AttemptId, a.Exam.Name, a.Exam.Purpose, a.Status, a.StartedAt, a.TotalScore, a.BandCode))
            .ToListAsync(cancellationToken);

        var recentPractice = await practice
            .OrderByDescending(p => p.StartedAt)
            .Take(RecentCount)
            .Select(p => new RecentPractice(
                p.SessionId, p.Section.Name, p.Status, p.CorrectCount, p.QuestionCount, p.StartedAt))
            .ToListAsync(cancellationToken);

        // The current level is the latest graded mock: placement and skill practice don't score the whole test.
        var latest = await attempts
            .Where(a => a.Status == "graded" && a.Exam.Purpose == "mock" && a.TotalScore != null)
            .OrderByDescending(a => a.SubmittedAt ?? a.StartedAt)
            .Select(a => new
            {
                Total = a.TotalScore!.Value,
                a.BandCode,
                BandName = db.ScoreBands
                    .Where(b => b.CertId == certId && b.Code == a.BandCode)
                    .Select(b => b.Name)
                    .FirstOrDefault(),
                TakenAt = a.SubmittedAt ?? a.StartedAt,
            })
            .FirstOrDefaultAsync(cancellationToken);

        return new LearnerDashboard(
            certificates.Single(c => c.CertId == certId).Option,
            latest is null ? null : new LatestResult(latest.Total, latest.BandCode, latest.BandName, latest.TakenAt),
            await attempts.CountAsync(cancellationToken),
            await practice.CountAsync(cancellationToken),
            recentAttempts,
            recentPractice);
    }

    public async Task<int> GetActiveCertIdAsync(int userId, CancellationToken cancellationToken = default) =>
        await ResolveActiveCertIdAsync(userId, await ActiveCertificatesAsync(cancellationToken), cancellationToken);

    private sealed record ActiveCertificate(int CertId, CertificateOption Option);

    private async Task<List<ActiveCertificate>> ActiveCertificatesAsync(CancellationToken cancellationToken)
    {
        var certificates = await db.Certificates
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.CertId)
            .Select(c => new ActiveCertificate(c.CertId, new CertificateOption(c.Code, c.Name)))
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
