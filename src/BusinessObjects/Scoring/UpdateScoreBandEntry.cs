using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record UpdateScoreBandEntry(
    int? BandId,
    string Code,
    string Name,
    int MinTotal,
    int MaxTotal,
    int DisplayOrder);
