using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Services;

public interface ISpeechSuperClient
{
    Task<SpeechSuperAssessmentResult> AssessAsync(
        Stream audioStream,
        string audioType,
        string? questionPrompt = null,
        string? refText = null,
        string? userId = null,
        CancellationToken cancellationToken = default);
}
