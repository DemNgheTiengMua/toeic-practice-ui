using System.Collections.Generic;

namespace BusinessObjects;

public sealed record PlacementCatalogView(
    CertificateOption ActiveCert,
    string? CurrentBandCode,
    string? CurrentBandName,
    int? CurrentTotalScore,
    List<PlacementCatalogItem> Exams,
    int? ActivePlacementId,
    int? ActiveAttemptId,
    bool IsRetake = false,
    int AllSkillsRetakeCredits = 5,
    int ListeningAndReadingRetakeCredits = 3,
    int? CreditBalance = null);
