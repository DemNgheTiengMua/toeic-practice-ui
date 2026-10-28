using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record SectionGenerateRequest(
    int? QuestionCount = null);
