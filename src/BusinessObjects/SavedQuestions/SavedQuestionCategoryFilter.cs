using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record SavedQuestionCategoryFilter(
    int CategoryId,
    string Name,
    int Count);
