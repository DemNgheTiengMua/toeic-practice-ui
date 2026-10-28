using System;
using System.Collections.Generic;

namespace Contracts;

// --- Quick Ingest DTOs ---

public sealed record QuickIngestParseRequest(
    string RawText,
    int? SectionId = null,
    int? CertId = null);
