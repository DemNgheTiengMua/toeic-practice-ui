using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class AdminExamService(
    CompanionDbContext db,
    TimeProvider clock,
    IFileStore? fileStore = null,
    IAttemptEngine? attemptEngine = null) : IAdminExamService
{

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // #158: preview renders the real sitting projection; sharing the engine keeps the two in step.
    private readonly IAttemptEngine _attemptEngine = attemptEngine ?? new AttemptEngine(db);

    public Task<QuickIngestCommitResultDto> CommitQuickIngestAsync(int examId, QuickIngestCommitRequest request, CancellationToken cancellationToken = default)
    {
        return new ExamQuickIngestService(db, clock).CommitStagedAsync(examId, request, cancellationToken);
    }
}
