using Contracts;

namespace Services;

/// <summary>
/// #247 / #248: the automated pass that decides whether this database may be cut over.
/// <para>
/// It reports; it never repairs. Every condition it names is one a person resolves - finish a sitting,
/// republish an exam, retire content - and a check that silently fixed them would be making content
/// decisions inside a readiness probe.
/// </para>
/// </summary>
public interface ICutoverReadinessService
{
    Task<CutoverReadinessDto> GetAsync(CancellationToken cancellationToken = default);
}
