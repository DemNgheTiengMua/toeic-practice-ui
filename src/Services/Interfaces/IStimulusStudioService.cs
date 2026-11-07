using System.Threading;
using System.Threading.Tasks;
using Contracts;

namespace Services;

/// <summary>
/// #203 / S-05: the stimulus-and-children studio. One bundle in, one bundle out.
/// <para>
/// <see cref="AnalyseAsync"/> is what the screen calls on every edit: it runs the same seam the save
/// runs and returns the refusals, the four level gauges, the pairwise overlaps and the audio gaps. A
/// screen that computed any of those itself would be a second answer to one question.
/// </para>
/// </summary>
public interface IStimulusStudioService
{
    /// <summary>The closed twenty for one skill, the six levels, D0.4's types, and D0.3's quotas.</summary>
    Task<StimulusStudioOptionsDto> GetOptionsAsync(string modality, CancellationToken cancellationToken = default);

    /// <summary>
    /// Judges a draft without writing anything. The gate each refusal came from is named, because an
    /// author needs to know whether to change the text, the options or the spans.
    /// </summary>
    Task<StimulusDraftAnalysisDto> AnalyseAsync(StimulusDraftRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the stimulus and its children in one transaction, both as <c>draft</c> and <c>demo</c>.
    /// A bundle the gates refuse writes nothing and the refusal names the gate.
    /// </summary>
    Task<StimulusBundleDto> SaveAsync(StimulusDraftRequest request, CancellationToken cancellationToken = default);
}
