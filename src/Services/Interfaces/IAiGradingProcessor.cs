using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Services;

/// <summary>
/// Dedicated processor for asynchronous AI grading of free responses.
/// Drains pending queue, resolves rubric criteria, clamps scores, dispatches to <see cref="IAiGrader"/>,
/// populates <see cref="AiGradingScore"/> rows, and finalizes <see cref="ExamAttempt"/> records.
/// </summary>
public interface IAiGradingProcessor
{
    /// <summary>
    /// Processes up to <paramref name="batchSize"/> pending AI gradings in the queue.
    /// Handles both written (text) and spoken (audio) free responses.
    /// </summary>
    Task<int> ProcessPendingGradingsAsync(int batchSize = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// Grades a single pending AI grading associated with the specified free response ID.
    /// </summary>
    Task<bool> GradeFreeResponseAsync(int freeResponseId, CancellationToken cancellationToken = default);
}
