using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record SavedQuestionCategoryFilterDto(
    int CategoryId,
    string Name,
    int Count);
