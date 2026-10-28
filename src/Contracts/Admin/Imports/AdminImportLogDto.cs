using System;
using System.Collections.Generic;

namespace Contracts;

// Question batch import (ADR-0007): an admin uploads a manifest (.json) or a package (.zip with
// manifest.json and media/), the API validates it as a whole and either creates one draft exam or
// records why not. Every upload that names a real certificate leaves an ImportBatches row.

public sealed record AdminImportLogDto(
    int LogId,
    int? RowNo,
    ImportLogLevelDto Level,
    string Message,
    DateTimeOffset LoggedAt);
