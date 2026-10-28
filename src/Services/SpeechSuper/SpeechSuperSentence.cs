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

public record SpeechSuperSentence(
    string Sentence,
    decimal? Pronunciation,
    int? Start,
    int? End
);
