using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record PreflightStatusView(
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
