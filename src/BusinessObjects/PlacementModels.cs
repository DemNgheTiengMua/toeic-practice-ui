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
    int? CreditBalance = null,
    // #386: the placement whose result is on screen. Without it the catalog showed a band but gave the
    // learner no way back to the paper that produced it - the result page was reachable only by address.
    int? LatestResultPlacementId = null);
