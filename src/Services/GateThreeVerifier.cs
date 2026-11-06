using System;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;

namespace Services;

/// <summary>
/// #196: runs gate three. Two heterogeneous verifiers solve the item with the key and explanation
/// withheld; one of them then solves it again with the passage or transcript removed. The three
/// solves go to <see cref="GateThree.Review"/>, which is where every threshold lives.
/// <para>
/// <b>One seam, three calls.</b> The ablation is <see cref="GateThreeSolveRequest.Blind"/> on the same
/// verifier, not a second interface or a second prompt builder. A verifier that cannot answer without
/// the passage is exactly the verifier that can answer the blind variant, so sharing the seam is what
/// makes the ablation use the same reading of a distribution.
/// </para>
/// <para>
/// <b>Cost.</b> Each review is three provider calls. This class makes none of them itself: the caller
/// passes an <see cref="IVerifierModel"/>, so the test suite passes
/// <see cref="FakeVerifierModel"/> and a real model is an explicit opt-in at composition time. That
/// is the same split the repo already uses for grading (<c>AI_PROVIDER</c> real/fake), and it keeps
/// <c>dotnet test</c> from billing anyone.
/// </para>
/// </summary>
public sealed class GateThreeVerifier(
    IVerifierModel verifierOne,
    IVerifierModel verifierTwo,
    IVerifierModel? ablationSolver = null)
{
    /// <summary>
    /// The ablating solver. Defaults to <c>verifierOne</c> because the item needs one solver without
    /// the passage, not both: a second blind solve doubles the cost to answer a question that one
    /// solve already answers.
    /// </summary>
    private readonly IVerifierModel _ablation = ablationSolver ?? verifierOne;

    public async Task<GateThreeVerdict> ReviewAsync(
        string authorFamily,
        GateThreeSolveRequest request,
        string keyLabel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var first = await verifierOne.SolveAsync(request, cancellationToken);
        var second = await verifierTwo.SolveAsync(request, cancellationToken);
        var blind = await _ablation.SolveAsync(request.Blind(), cancellationToken);

        return GateThree.Review(authorFamily, keyLabel, first, second, blind);
    }
}
