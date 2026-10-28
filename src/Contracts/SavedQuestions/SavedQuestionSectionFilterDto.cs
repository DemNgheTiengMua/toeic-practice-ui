using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record SavedQuestionSectionFilterDto(
    int SectionId,
    string Code,
    string Name,
    int Count);
