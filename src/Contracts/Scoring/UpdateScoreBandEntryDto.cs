using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record UpdateScoreBandEntryDto(
    int? BandId,
    string Code,
    string Name,
    int MinTotal,
    int MaxTotal,
    int DisplayOrder);
