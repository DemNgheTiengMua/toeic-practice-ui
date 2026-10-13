using BusinessObjects;
using DataAccess;
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
        var timeZoneId = await db.Users
            .Where(u => u.UserId == userId)
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
        var code = certificateCode.Trim();
        // Codes are stored upper-case in a case-insensitive collation; the comparison is the database's.
        var certId = await db.Certificates
            .Where(c => c.IsActive && c.Code == code)
            .Select(c => (int?)c.CertId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new CertificateUnavailableException(code);

        var updated = await db.Users
            .Where(u => u.UserId == userId && !u.IsDeleted)
            .ExecuteUpdateAsync(set => set.SetProperty(u => u.ActiveCertId, certId), cancellationToken);
        if (updated == 0)
        {
            throw new InvalidOperationException($"User {userId} does not exist.");
        }
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
    /// The fallback is not written back — the learner's choice stays theirs to make.</summary>
    private async Task<int> ResolveActiveCertIdAsync(
        int userId, List<ActiveCertificate> certificates, CancellationToken cancellationToken)
    {
        var stored = await db.Users
            .Where(u => u.UserId == userId && !u.IsDeleted)
            .Select(u => new { u.ActiveCertId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"User {userId} does not exist.");

        return certificates.Any(c => c.CertId == stored.ActiveCertId)
            ? stored.ActiveCertId!.Value
            : certificates[0].CertId;
    }
}
