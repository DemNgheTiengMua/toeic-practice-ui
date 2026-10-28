using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record ReorderSectionItemsRequest(
    int SectionId,
    IReadOnlyList<ReorderSectionItemEntry> Items);
