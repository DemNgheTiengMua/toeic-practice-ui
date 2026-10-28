using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record ModuleTopicDto(
    int TopicId,
    string Title,
    int? QuestionCount,
    string Status,
    string? VideoPath = null,
    bool CanAccessVideo = false);
