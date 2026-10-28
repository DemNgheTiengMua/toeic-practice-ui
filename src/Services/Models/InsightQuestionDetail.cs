using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;

namespace Services;

public record InsightQuestionDetail(
    int QuestionId,
    string Stem,
    int SectionId,
    string SectionName,
    int? CategoryId,
    string? CategoryName,
    int? SelectedOptionId,
    string? SelectedOptionLabel,
    string? CorrectOptionLabel,
    bool? IsCorrect,
    bool HintUsed,
    string? Explanation
);
