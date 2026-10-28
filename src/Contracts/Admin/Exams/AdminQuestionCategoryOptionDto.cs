using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record AdminQuestionCategoryOptionDto(
    int CategoryId,
    string Code,
    string Name);
