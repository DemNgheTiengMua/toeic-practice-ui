using BusinessObjects;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services.Credits;

namespace Services;

public interface ISkillPracticeCatalogService
{
    /// <summary>The published practice exams of a free-response skill, for the active certificate.</summary>
    Task<SkillPracticeExamList> GetExamsAsync(int userId, ExamPurpose purpose, CancellationToken cancellationToken = default);
}

/// <summary>#159: the list behind the Writing and Speaking doors. A free-response skill is answered by
/// voice or by text and graded against the certificate's rubric, so it has no typed drill and cannot be
/// reached from the practice page. This is the one place that lists its published exams.</summary>
public sealed class SkillPracticeCatalogService(
    CompanionDbContext db,
    ILearnerService learners,
    ICreditLedger? creditLedger = null) : ISkillPracticeCatalogService
{
    private readonly ICreditLedger _creditLedger = creditLedger ?? new CreditLedger(db);

    public async Task<SkillPracticeExamList> GetExamsAsync(
        int userId,
        ExamPurpose purpose,
        CancellationToken cancellationToken = default)
    {
        var certId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var cert = await db.Certificates
            .AsNoTracking()
            .Where(c => c.CertId == certId)
            .Select(c => new CertificateOption(c.Code, c.Name))
            .SingleAsync(cancellationToken);

        var balance = await _creditLedger.GetBalanceAsync(userId, cancellationToken);

        var purposeCode = purpose.ToCode();
        var exams = await db.Exams
            .AsNoTracking()
            .Where(e => e.CertId == certId
                && e.Purpose == purposeCode
                && e.Status == ExamStatus.Published.ToCode()
                && !e.IsDeleted)
            .OrderBy(e => e.Name)
            .Select(e => new { e.ExamId, e.Name, e.Description, e.DurationMinutes })
            .ToListAsync(cancellationToken);

        if (exams.Count == 0)
        {
            return new SkillPracticeExamList(cert, balance, []);
        }

        var examIds = exams.Select(e => e.ExamId).ToList();
        var attempts = await db.ExamAttempts
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.CertId == certId && examIds.Contains(a.ExamId))
            .OrderByDescending(a => a.SubmittedAt ?? a.StartedAt)
            .ThenByDescending(a => a.AttemptId)
            .Select(a => new { a.AttemptId, a.ExamId, a.Status, a.SubmittedAt, a.StartedAt })
            .ToListAsync(cancellationToken);

        // The AI grading is what costs a credit, and it is taken on submit, not start.
        var cost = purpose == ExamPurpose.SpeakingPractice
            ? SpeakingPracticeService.SpeakingPracticeCreditCost
            : WritingPracticeService.WritingPracticeCreditCost;

        var items = exams.Select(e =>
        {
            var forExam = attempts.Where(a => a.ExamId == e.ExamId).ToList();
            var active = forExam.FirstOrDefault(a => a.Status == AttemptStatus.InProgress.ToCode());
            var graded = forExam
                .Where(a => a.Status == AttemptStatus.Graded.ToCode())
                .OrderByDescending(a => a.SubmittedAt ?? a.StartedAt)
                .FirstOrDefault();

            return new SkillPracticeExamItem(
                e.ExamId,
                e.Name,
                e.Description,
                e.DurationMinutes,
                cost,
                active?.AttemptId,
                graded?.AttemptId,
                graded is null ? null : graded.SubmittedAt ?? graded.StartedAt);
        }).ToList();

        return new SkillPracticeExamList(cert, balance, items);
    }
}
