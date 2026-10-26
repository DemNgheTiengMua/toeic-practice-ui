using System.Collections.Generic;

namespace Contracts;

public sealed record PlacementCatalogViewDto(
    CertificateOptionDto ActiveCert,
    string? CurrentBandCode,
    string? CurrentBandName,
    int? CurrentTotalScore,
    List<PlacementCatalogItemDto> Exams,
    int? ActivePlacementId,
    int? ActiveAttemptId);
