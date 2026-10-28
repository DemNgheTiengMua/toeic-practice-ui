using System;
using System.Collections.Generic;

namespace Contracts;

// --- Reorder and Audit DTOs ---

public sealed record ReorderSectionItemEntry(
    int? GroupId,
    int? QuestionId,
    int DisplayOrder);
