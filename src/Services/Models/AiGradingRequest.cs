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
    int? DurationMs = null
);
