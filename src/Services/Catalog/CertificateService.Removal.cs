using Contracts;
using BusinessObjects;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class CertificateService
{
    /// <summary>
    /// Removes a certificate from the catalogue, choosing between destruction and withdrawal by whether
    /// anyone has depended on it.
    /// <para>
    /// <b>Untouched certificates are deleted.</b> One authored and never used holds no learner record:
    /// its skills, sections, exams, items, bands and scales are the admin's own draft, and deleting them
    /// is what "delete" means to the person who clicked it.
    /// </para>
    /// <para>
    /// <b>A studied certificate is withdrawn, never destroyed.</b> Attempts, placements, ledger rows and
    /// paths are a learner's history, and a certificate row is the anchor every one of those composite
    /// keys carries. Destroying it would either fail on a foreign key or, worse, cascade away the record
    /// of what someone did. So <c>IsActive</c> goes false, which removes it from every catalogue without
    /// touching a single learner row.
    /// </para>
    /// <para>
    /// <b>A learner sitting on it is moved first.</b> <c>Users.ActiveCertId</c> points at the certificate
    /// being removed; leaving it there strands them on a certificate no catalogue offers, and the next
    /// page that resolves their active certificate fails. They are moved to another active certificate
    /// before the removal commits, in the same transaction.
    /// </para>
    /// <para>
    /// The last active certificate cannot be removed at all: there would be nothing to move anyone to and
    /// the product has no state where no certificate exists.
    /// </para>
    /// </summary>
    public async Task<CertificateRemovalResultDto> RemoveCertificateAsync(
        int certId, CancellationToken cancellationToken = default)
    {
        var cert = await db.Certificates
            .FirstOrDefaultAsync(c => c.CertId == certId, cancellationToken)
            ?? throw new KeyNotFoundException($"Certificate with ID {certId} was not found.");

        var otherActive = await db.Certificates
            .AsNoTracking()
            .Where(c => c.CertId != certId && c.IsActive)
            .OrderBy(c => c.CertId)
            .Select(c => new { c.CertId, c.Code })
            .ToListAsync(cancellationToken);

        // Refused before anything is written, so a refused removal leaves the certificate exactly as it
        // was - the same "block before you act" rule the credit ledger runs on.
        if (otherActive.Count == 0)
        {
            throw new CertificateRemovalException(
                CertificateRemovalException.LastActiveCertificate,
                $"'{cert.Code}' is the only active certificate. Create or activate another before removing it.");
        }

        // Any learner dependence at all, not just attempts: a placement alone, or a saved question, is
        // history that a composite key carrying CertId would lose.
        var isUsed =
            await db.ExamAttempts.AnyAsync(a => a.CertId == certId, cancellationToken)
            || await db.PlacementTests.AnyAsync(p => p.CertId == certId, cancellationToken)
            || await db.PracticeSessions.AnyAsync(p => p.CertId == certId, cancellationToken)
            || await db.LearnerPaths.AnyAsync(p => p.CertId == certId, cancellationToken)
            || await db.TargetBands.AnyAsync(t => t.CertId == certId, cancellationToken)
            || await db.SavedQuestions.AnyAsync(s => s.CertId == certId, cancellationToken)
            || await db.MentorProfiles.AnyAsync(m => m.CertId == certId, cancellationToken)
            || await db.AiInsights.AnyAsync(i => i.CertId == certId, cancellationToken)
            || await db.Recommendations.AnyAsync(r => r.CertId == certId, cancellationToken);

        var learnersOnIt = await db.Users
            .Where(u => u.ActiveCertId == certId && !u.IsDeleted)
            .ToListAsync(cancellationToken);

        var fallback = otherActive[0];
        foreach (var learner in learnersOnIt)
        {
            learner.ActiveCertId = fallback.CertId;
        }

        var reassigned = learnersOnIt.Count;

        if (isUsed)
        {
            cert.IsActive = false;
            await db.SaveChangesAsync(cancellationToken);

            return new CertificateRemovalResultDto(
                Deleted: false,
                cert.CertId,
                cert.Code,
                reassigned,
                reassigned > 0 ? fallback.Code : null,
                reassigned > 0
                    ? $"'{cert.Code}' was withdrawn from the catalogue (it holds learner history). "
                      + $"{reassigned} learner(s) moved to '{fallback.Code}'."
                    : $"'{cert.Code}' was withdrawn from the catalogue. It holds learner history, so it was "
                      + "deactivated rather than deleted.");
        }

        // Nothing has used it. Everything hanging off the certificate is still the admin's own draft, and
        // the schema's own cascades take the children with it.
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var examIds = await db.Exams.Where(e => e.CertId == certId).Select(e => e.ExamId).ToListAsync(cancellationToken);
        if (examIds.Count > 0)
        {
            var examItems = await db.ExamItems.Where(ei => examIds.Contains(ei.ExamId)).ToListAsync(cancellationToken);
            db.ExamItems.RemoveRange(examItems);
            await db.SaveChangesAsync(cancellationToken);
        }

        db.Certificates.Remove(cert);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return new CertificateRemovalResultDto(
            Deleted: true,
            certId,
            cert.Code,
            reassigned,
            reassigned > 0 ? fallback.Code : null,
            reassigned > 0
                ? $"'{cert.Code}' was deleted. {reassigned} learner(s) moved to '{fallback.Code}'."
                : $"'{cert.Code}' was deleted.");
    }
}

/// <summary>A certificate removal that was refused, with the reason an admin can act on.</summary>
public sealed class CertificateRemovalException(string code, string message) : Exception(message)
{
    public const string LastActiveCertificate = "last_active_certificate";

    public string Code { get; } = code;
}
