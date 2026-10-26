using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record PreflightStatusDto(
    int AttemptId,
    int ExamId,
    string ExamName,
    string CertCode,
    string CertName,
    int DurationMinutes,
    bool RequiresPreflight,
    bool RequiresAudio,
    bool RequiresMicrophone,
    bool IsVerified,
    DateTimeOffset? VerifiedAt,
    IReadOnlyList<string> AudioUrls);
