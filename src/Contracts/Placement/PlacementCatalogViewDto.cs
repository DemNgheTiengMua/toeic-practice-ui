using System.Collections.Generic;

namespace Contracts;

public sealed record PlacementCatalogViewDto(
    CertificateOptionDto ActiveCert,
    string? CurrentBandCode,
    string? CurrentBandName,
    int? CurrentTotalScore,
    List<PlacementCatalogItemDto> Exams,
    int? ActivePlacementId,
    int? ActiveAttemptId,
    bool IsRetake = false,
    int AllSkillsRetakeCredits = 5,
    int ListeningAndReadingRetakeCredits = 3,
    int? CreditBalance = null,
    // #386: lets the catalog offer a way back to the result that produced the band on screen.
    int? LatestResultPlacementId = null);
