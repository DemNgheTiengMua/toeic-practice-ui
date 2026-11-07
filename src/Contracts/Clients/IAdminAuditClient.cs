using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Contracts;

/// <summary>
/// #206 / S-03: the stratified audit's calls.
/// <para>
/// <b>Solving and revealing are two methods, never one.</b> <see cref="RevealSampleKeyAsync"/> is the
/// only call that returns an authored key, and the server refuses it until a solve is stored - so the
/// order is enforced at the API, not by a screen remembering to hide a field. A single
/// "get the item with its key" call would make blindness a property of which properties a page chose to
/// render, which is exactly the failure the ticket names.
/// </para>
/// </summary>
public interface IAdminAuditClient
{
    Task<AuditQueueDto> GetAuditQueueAsync(int batchId, CancellationToken cancellationToken = default);
    Task<AuditQueueDto> GetAuditQueueAsync(int batchId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuditQueueAsync(batchId, cancellationToken);

    Task<AuditQueueDto> DrawSampleAsync(int batchId, CancellationToken cancellationToken = default);
    Task<AuditQueueDto> DrawSampleAsync(int batchId, string bearerToken, CancellationToken cancellationToken = default) =>
        DrawSampleAsync(batchId, cancellationToken);

    Task RecordSolveAsync(int sampleItemId, string label, CancellationToken cancellationToken = default);
    Task RecordSolveAsync(int sampleItemId, string label, string bearerToken, CancellationToken cancellationToken = default) =>
        RecordSolveAsync(sampleItemId, label, cancellationToken);

    Task<AuditKeyRevealDto> RevealSampleKeyAsync(int sampleItemId, CancellationToken cancellationToken = default);
    Task<AuditKeyRevealDto> RevealSampleKeyAsync(int sampleItemId, string bearerToken, CancellationToken cancellationToken = default) =>
        RevealSampleKeyAsync(sampleItemId, cancellationToken);

    Task RecordVerdictAsync(int sampleItemId, string verdict, CancellationToken cancellationToken = default);
    Task RecordVerdictAsync(int sampleItemId, string verdict, string bearerToken, CancellationToken cancellationToken = default) =>
        RecordVerdictAsync(sampleItemId, verdict, cancellationToken);

    Task<AuditDecisionDto> DecideStratumAsync(int stratumId, CancellationToken cancellationToken = default);
    Task<AuditDecisionDto> DecideStratumAsync(int stratumId, string bearerToken, CancellationToken cancellationToken = default) =>
        DecideStratumAsync(stratumId, cancellationToken);
}
