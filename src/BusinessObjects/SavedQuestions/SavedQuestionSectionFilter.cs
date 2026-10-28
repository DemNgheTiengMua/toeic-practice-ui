using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record SavedQuestionSectionFilter(
    int SectionId,
    string Code,
    string Name,
    int Count);
