using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record AdminCertificateOptionDto(
    int CertId,
    string Code,
    string Name);
