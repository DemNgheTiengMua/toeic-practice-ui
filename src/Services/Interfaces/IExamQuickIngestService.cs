using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public interface IExamQuickIngestService
{
    QuickIngestParseResultDto Parse(QuickIngestParseRequest request);
    Task<QuickIngestParseResultDto> ParseStagingAsync(QuickIngestParseRequest request, CancellationToken cancellationToken = default);
    Task<string> GetSampleTemplateAsync(int sectionId, CancellationToken cancellationToken = default);
    Task<QuickIngestCommitResultDto> CommitStagedAsync(int examId, QuickIngestCommitRequest request, CancellationToken cancellationToken = default);
}
