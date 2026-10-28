using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class PracticeService(
    CompanionDbContext db,
    ILearnerService learners,
    Microsoft.Extensions.Configuration.IConfiguration? config = null) : IPracticeService
{
    public const int DefaultMaxSavedSessionsPerCertificate = 20;

    /// <summary>The maximum number of recent standalone practice sessions (and their associated answer logs)
    /// retained per learner per certificate. Older sessions exceeding this limit are pruned to keep storage
    /// bounded in the long run while retaining enough historical data for weakness analysis and AI mentor insights.</summary>
    public int MaxSavedSessionsPerCertificate { get; set; } =
        config != null && int.TryParse(config["Practice:MaxSavedSessionsPerCertificate"], out var configuredMax) && configuredMax > 0
            ? configuredMax
            : DefaultMaxSavedSessionsPerCertificate;
}
