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

public sealed partial class AiInsightService(
    CompanionDbContext db,
    IAiInsightGenerator generator,
    ILogger<AiInsightService> logger,
    ILearnerService? learners = null) : IAiInsightService
{
    private static readonly ConcurrentDictionary<int, int> RetryCounts = new();
    private readonly ILearnerService _learners = learners ?? new LearnerService(db);
}
