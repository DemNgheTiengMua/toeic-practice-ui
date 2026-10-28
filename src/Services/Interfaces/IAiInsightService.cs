using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Services;

public interface IAiInsightService
{
    Task<AiInsightView> GetOrCreatePracticeInsightAsync(int userId, int sessionId, CancellationToken cancellationToken = default);
    Task<AiInsightView> GetOrCreateExamInsightAsync(int userId, int attemptId, CancellationToken cancellationToken = default);
    Task<AiInsightView> GetOrCreateDashboardInsightAsync(int userId, int? certId = null, CancellationToken cancellationToken = default);
    Task<AiInsightView> GetOrCreatePathInsightAsync(int userId, CancellationToken cancellationToken = default);
    Task RetryPracticeInsightAsync(int userId, int sessionId, CancellationToken cancellationToken = default);
    Task RetryExamInsightAsync(int userId, int attemptId, CancellationToken cancellationToken = default);
    Task RetryDashboardInsightAsync(int userId, int? certId = null, CancellationToken cancellationToken = default);
    Task RetryPathInsightAsync(int userId, CancellationToken cancellationToken = default);
    Task RetryInsightAsync(int userId, int insightId, CancellationToken cancellationToken = default);
    Task<int> ProcessPendingInsightsAsync(CancellationToken cancellationToken = default);
}
