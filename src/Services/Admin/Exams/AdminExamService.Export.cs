using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class AdminExamService
{
    /// <summary>
    /// #189: the same manifest builder the import service uses, so a paper exports identically
    /// whichever door the admin walks through. Before this the two had already drifted on #152's
    /// spoken reference - one wrote it, the other silently dropped it.
    /// </summary>
    public async Task<(Stream Stream, string ContentType, string FileName)> ExportExamPackageAsync(int examId, string? format = "zip", CancellationToken cancellationToken = default)
    {
        var exam = await db.Exams.AsNoTracking()
            .Include(e => e.Cert)
            .FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken);

        if (exam == null)
        {
            throw new ExamNotFoundException(examId);
        }

        var manifest = await QuestionExportManifest.BuildAsync(db, exam, format, cancellationToken);

        // The zip writer lives on the import service because it is the one that owns the media store
        // contract for imported files; the studio gets the same bytes by calling it.
        return await QuestionImportService.WritePackageAsync(manifest, exam.Name, format, fileStore, cancellationToken);
    }
}
