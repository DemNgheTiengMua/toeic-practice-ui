using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record QuickIngestSampleTemplateDto(
    string Template,
    string FormatDescription);
