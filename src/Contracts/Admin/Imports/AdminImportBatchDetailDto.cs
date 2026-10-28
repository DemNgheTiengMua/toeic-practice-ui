using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record AdminImportBatchDetailDto(
    AdminImportBatchListItemDto Batch,
    IReadOnlyList<AdminImportLogDto> Logs);
