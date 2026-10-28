using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record SectionGenerationResultDto(
    int ExamId,
    int SectionId,
    int QuestionsGenerated,
    int GroupsGenerated,
    string Message);
