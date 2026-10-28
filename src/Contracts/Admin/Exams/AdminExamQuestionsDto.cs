using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record AdminExamQuestionsDto(
    int ExamId,
    string ExamName,
    int CertId,
    string CertCode,
    IReadOnlyList<AdminQuestionGroupDto> Groups,
    IReadOnlyList<AdminSectionOptionDto> AvailableSections,
    IReadOnlyList<AdminQuestionCategoryOptionDto> AvailableCategories);
