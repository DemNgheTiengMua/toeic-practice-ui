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

public record SpeechSuperAssessmentResult(
    bool Success,
    string? ErrorMessage,
    int? ErrId,
    string? Transcription,
    decimal? OverallScore,
    decimal? PronunciationScore,
    decimal? FluencyScore,
    decimal? GrammarScore,
    decimal? VocabularyScore,
    decimal? RelevanceScore,
    IReadOnlyList<SpeechSuperSentence>? Sentences = null,
    string? RawResponseJson = null
);
