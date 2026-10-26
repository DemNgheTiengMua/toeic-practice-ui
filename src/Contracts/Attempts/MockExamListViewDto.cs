using System.Collections.Generic;

namespace Contracts;

public sealed record MockExamListViewDto(
    CertificateOptionDto Certificate,
    int CreditBalance,
    IReadOnlyList<MockExamItemDto> Exams);
