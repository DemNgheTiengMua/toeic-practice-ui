using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;

namespace Services;

public record InsightQuestionDetail(
    int? QuestionId,
    string Stem,
    int? SectionId,
    string SectionName,
    /// <summary>
    /// #271: the cell this question sits in, so the narrator can attribute a miss or a lucky guess to
    /// the same figure the weakness page reports. It was <c>CategoryId</c>, on the retired axis; a
    /// question the pool cannot place has nulls here and is simply never matched to a cell.
    /// </summary>
    int? TopicId,
    string? TopicName,
    CefrLevel? CefrLevel,
    int? SelectedOptionId,
    string? SelectedOptionLabel,
    string? CorrectOptionLabel,
    bool? IsCorrect,
    bool HintUsed,
    string? Explanation,
    int? ItemVersionId = null
);
