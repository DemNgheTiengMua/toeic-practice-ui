using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record ModuleTopicInfo(
    int TopicId,
    string Title,
    int? QuestionCount,
    string Status,
    string? VideoPath = null,
    bool CanAccessVideo = false);
