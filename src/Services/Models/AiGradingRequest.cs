using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Services;

public record AiGradingRequest(
    int FreeResponseId,
    int SkillId,
    int CertId,
    string QuestionStem,
    int? MinWords,
    int? MaxWords,
    string ResponseText,
    IReadOnlyList<GradingCriterionDetail> Criteria,
    int AttemptCount = 1,
    string? AudioPath = null,
    int? DurationMs = null,
    // #152: the reference a spoken answer is judged against - the text to read aloud, or the
    // description of the picture. NULL means the question declares none, so nothing is scored
    // against one.
    string? ReferenceText = null,
    bool ReferenceDescribesImage = false
);
