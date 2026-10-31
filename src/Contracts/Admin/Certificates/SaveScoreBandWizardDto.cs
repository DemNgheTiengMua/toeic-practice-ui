
namespace Contracts;

/// <summary>E1.2 (#147): one score band authored in the certificate wizard. Bands must tile the
/// certificate's total range with no gap and no overlap; the validator enforces that before save.</summary>
public sealed record SaveScoreBandWizardDto(
    int? BandId,
    string Code,
    string Name,
    int MinTotal,
    int MaxTotal,
    int DisplayOrder);
