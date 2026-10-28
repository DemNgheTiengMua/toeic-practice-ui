using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;

namespace Services;

public record WritingEvidence(
    int QuestionId,
    string Stem,
    string? ResponseText,
    int WordCount,
    IReadOnlyList<WritingRubricScore> Scores,
    string? AiComments
);
