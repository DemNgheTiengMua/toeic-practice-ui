using System.Threading;
using System.Threading.Tasks;

namespace Services;

/// <summary>
/// #276: the stage-one draw as a caller sees it - one fixed per-band distribution, for every objective
/// skill the certificate examines.
/// <para>
/// <b>Why an interface exists for a class with one caller.</b> The dead-service guard
/// (<c>DeadServiceGuardTests</c>) fails when a public service class has neither a production caller nor a
/// DI registration. This pair had neither for two tickets: the draws were complete, tested, and never
/// executed. An interface plus a registration is what makes the guard's claim checkable rather than a
/// paper assertion, and it is the shape every other service in this layer already has.
/// </para>
/// </summary>
public interface IPlacementStageOneDrawService
{
    /// <summary>
    /// Draws one stage one for every objective skill of a certificate.
    /// </summary>
    /// <param name="certId">The certificate whose skills are examined.</param>
    /// <param name="contentTier">The paper's own tier - a placement never draws another tier's stock.</param>
    Task<PlacementStageOneDraw> DrawAsync(
        int certId,
        string contentTier,
        CancellationToken cancellationToken = default);
}
