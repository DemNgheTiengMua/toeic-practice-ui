using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record AdminExamListSummaryDto(
    IReadOnlyList<AdminExamListItemDto> Exams,
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<AdminCertificateOptionDto> Certificates);
