using System.Collections.Generic;

namespace BusinessObjects;

public sealed record MockExamListView(
    CertificateOption Certificate,
    int CreditBalance,
    IReadOnlyList<MockExamItem> Exams);
