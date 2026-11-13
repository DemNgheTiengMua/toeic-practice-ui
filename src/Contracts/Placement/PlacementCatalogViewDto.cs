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
    int? CreditBalance = null);
