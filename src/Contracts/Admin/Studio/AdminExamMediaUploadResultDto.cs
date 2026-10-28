using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record AdminExamMediaUploadResultDto(
    string Url,
    string Path,
    string FileName);
