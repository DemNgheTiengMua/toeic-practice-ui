using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record AdminImportBatchesDto(
    IReadOnlyList<AdminImportBatchListItemDto> Batches,
    IReadOnlyList<AdminCertificateOptionDto> Certificates);
