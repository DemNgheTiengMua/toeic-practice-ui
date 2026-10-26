using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

using Services.Credits;

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
        // Practice sessions carry no CertId of their own; their section's does.
        var practice = db.PracticeSessions.AsNoTracking().Where(p => p.UserId == userId && p.Section.CertId == certId);

        var recentAttempts = await attempts
            .OrderByDescending(a => a.StartedAt)
            .Take(RecentCount)
            .Select(a => new RecentAttempt(
                a.AttemptId, a.Exam.Name, EnumCodes.Parse<ExamPurpose>(a.Exam.Purpose), EnumCodes.Parse<AttemptStatus>(a.Status), a.StartedAt, a.TotalScore, a.BandCode))
            .ToListAsync(cancellationToken);

        var recentPractice = await practice
            .OrderByDescending(p => p.StartedAt)
            .Take(RecentCount)
            .Select(p => new RecentPractice(
                p.SessionId, p.Section.Name, EnumCodes.Parse<PracticeStatus>(p.Status), p.CorrectCount, p.QuestionCount, p.StartedAt))
            .ToListAsync(cancellationToken);

        // The current level is the latest graded mock: placement and skill practice don't score the whole test.
        var latest = await attempts
            .Where(a => a.Status == AttemptStatus.Graded.ToCode() && a.Exam.Purpose == ExamPurpose.Mock.ToCode() && a.TotalScore != null)
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

        var unfinishedAttempt = await attempts
            .Where(a => a.Status == AttemptStatus.InProgress.ToCode())
            .OrderByDescending(a => a.StartedAt)
            .Select(a => new RecentAttempt(
                a.AttemptId, a.Exam.Name, EnumCodes.Parse<ExamPurpose>(a.Exam.Purpose), EnumCodes.Parse<AttemptStatus>(a.Status), a.StartedAt, a.TotalScore, a.BandCode))
            .FirstOrDefaultAsync(cancellationToken);

        var unfinishedPractice = await practice
            .Where(p => p.Status == PracticeStatus.InProgress.ToCode())
            .OrderByDescending(p => p.StartedAt)
            .Select(p => new RecentPractice(
                p.SessionId, p.Section.Name, EnumCodes.Parse<PracticeStatus>(p.Status), p.CorrectCount, p.QuestionCount, p.StartedAt))
            .FirstOrDefaultAsync(cancellationToken);

        var latestPlacement = await db.PlacementTests
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.CertId == certId && p.Status == PlacementStatus.Done.ToCode() && p.TotalScore != null)
            .OrderByDescending(p => p.TakenAt)
            .Select(p => new
            {
                Total = p.TotalScore!.Value,
                BandCode = p.ResultBandCode,
                BandName = db.ScoreBands
                    .Where(b => b.CertId == certId && b.Code == p.ResultBandCode)
                    .Select(b => b.Name)
                    .FirstOrDefault(),
                p.TakenAt,
            })
            .FirstOrDefaultAsync(cancellationToken);

        string? currentBandCode = null;
        string? currentBandName = null;
        int? currentTotalScore = null;
        string? currentSource = null;

        if (latest is not null && (latestPlacement is null || latest.TakenAt >= latestPlacement.TakenAt))
        {
            currentBandCode = latest.BandCode;
            currentBandName = latest.BandName;
            currentTotalScore = latest.Total;
            currentSource = "mock";
        }
        else if (latestPlacement is not null)
        {
            currentBandCode = latestPlacement.BandCode;
            currentBandName = latestPlacement.BandName;
            currentTotalScore = latestPlacement.Total;
            currentSource = "placement";
        }

        var targetBand = await db.TargetBands
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.CertId == certId)
            .Select(t => new
            {
                t.TargetBandCode,
                BandName = t.ScoreBand.Name,
                MinScore = (int?)t.ScoreBand.MinTotal,
            })
            .FirstOrDefaultAsync(cancellationToken);

        var activePathSteps = await db.LearnerPaths
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.CertId == certId && p.Status == LearnerPathStatus.Active.ToCode())
            .Select(p => new
            {
                Completed = p.LearnerPathSteps.Count(s => s.Status == LearnerPathStepStatus.Done.ToCode()),
                Total = p.LearnerPathSteps.Count,
            })
            .FirstOrDefaultAsync(cancellationToken);

        var targetReadiness = new TargetReadiness(
            CurrentBandCode: currentBandCode,
            CurrentBandName: currentBandName,
            CurrentTotalScore: currentTotalScore,
            CurrentSource: currentSource,
            TargetBandCode: targetBand?.TargetBandCode,
            TargetBandName: targetBand?.BandName,
            TargetMinScore: targetBand?.MinScore,
            CompletedPathSteps: activePathSteps?.Completed ?? 0,
            TotalPathSteps: activePathSteps?.Total ?? 0);

        var sectionIds = await db.Sections
            .AsNoTracking()
            .Where(s => s.CertId == certId)
            .Select(s => s.SectionId)
            .ToListAsync(cancellationToken);

        var sectionViewRows = await db.vw_UserSectionAccuracyAlls
            .AsNoTracking()
            .Where(v => v.UserId == userId && sectionIds.Contains(v.SectionId))
            .ToListAsync(cancellationToken);

        var totalAnswered = sectionViewRows.Sum(r => r.Answered ?? 0);
        var totalCorrect = sectionViewRows.Sum(r => r.Correct ?? 0);
        int? overallAccuracyPercent = totalAnswered > 0
            ? (int)Math.Round((double)totalCorrect / totalAnswered * 100)
            : null;

        var missionSteps = await BuildMissionStepsAsync(userId, certId, cancellationToken);
        var simulatorConfig = await BuildSimulatorConfigAsync(userId, certId, targetReadiness, cancellationToken);
        var weaknessStrips = await BuildWeaknessStripsAsync(userId, certId, cancellationToken);
        var upcomingBooking = await BuildUpcomingBookingAsync(userId, certId, cancellationToken);
        var latestRubricHighlight = await BuildRubricHighlightAsync(userId, certId, cancellationToken);

        return new LearnerDashboard(
            certificates.Single(c => c.CertId == certId).Option,
            latest is null ? null : new LatestResult(latest.Total, latest.BandCode, latest.BandName, latest.TakenAt),
            await attempts.CountAsync(cancellationToken),
            await practice.CountAsync(cancellationToken),
            recentAttempts,
            recentPractice,
            unfinishedAttempt,
            unfinishedPractice,
            targetReadiness,
            missionSteps,
            simulatorConfig,
            weaknessStrips,
            upcomingBooking,
            latestRubricHighlight,
            overallAccuracyPercent);
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
