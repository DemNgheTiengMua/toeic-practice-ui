using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;

namespace Services;

public record ErrorFingerprint(
    string Stem,
    string? ChosenOptionText,
    string? CorrectOptionText,
    string? Explanation,
    bool WasFlagged,
    string? CategoryName = null
);
