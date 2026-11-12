using Contracts;

namespace Services;

/// <summary>
/// #286 / ADR-0013: the source of the dashboard's revision. One implementation answers from SQL; the web
/// app's stream tests substitute a stub so they can control change without a database and without sleeping.
/// </summary>
public interface IDashboardRevisionService
{
    Task<DashboardRevision> GetRevisionAsync(CancellationToken cancellationToken = default);
}
