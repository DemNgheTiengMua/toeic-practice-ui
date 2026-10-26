using System.Collections.Generic;

namespace BusinessObjects;

public sealed record PlacementCatalogView(
    CertificateOption ActiveCert,
    string? CurrentBandCode,
    string? CurrentBandName,
    int? CurrentTotalScore,
    List<PlacementCatalogItem> Exams,
    int? ActivePlacementId,
    int? ActiveAttemptId);
