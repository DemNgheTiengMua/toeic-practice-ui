using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public interface IExamQuickIngestService
{
    QuickIngestParseResultDto Parse(QuickIngestParseRequest request);
    Task<QuickIngestParseResultDto> ParseStagingAsync(QuickIngestParseRequest request, CancellationToken cancellationToken = default);
    Task<string> GetSampleTemplateAsync(int sectionId, CancellationToken cancellationToken = default);
    Task<QuickIngestCommitResultDto> CommitStagedAsync(int examId, QuickIngestCommitRequest request, CancellationToken cancellationToken = default);
}

public sealed partial class ExamQuickIngestService(
    CompanionDbContext? db = null,
    TimeProvider? clock = null) : IExamQuickIngestService
{
    [GeneratedRegex(@"^\s*(?:(?:Question|Câu|Q)\s*\d+[\.:\)]?|\d+[\.:\)])\s*(?<stem>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex QuestionHeaderRegex();

    [GeneratedRegex(@"^\s*(?:PROMPT|ĐỀ BÀI|DE BAI)\s*[:=\-]\s*(?<stem>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex PromptHeaderRegex();

    [GeneratedRegex(@"^\s*(?:\*\s*)?(?:\(([A-Za-z])\)|\b([A-Za-z])[\.\)])\s*(.*)$")]
    private static partial Regex OptionLineRegex();

    [GeneratedRegex(@"^\s*(?:ANSWER|ĐÁP ÁN|DAP AN|KEY)\s*[:=\-]\s*(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex AnswerKeyRegex();

    [GeneratedRegex(@"^\s*(?:EXPLANATION|GIẢI THÍCH|GIAI THICH)\s*[:=\-]?\s*(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex ExplanationRegex();

    [GeneratedRegex(@"^\s*(?:CATEGORY|CHỦ ĐỀ|CHU DE)\s*[:=\-]?\s*(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex CategoryRegex();

    [GeneratedRegex(@"^\s*(?:PASSAGE|READING PASSAGE|ĐOẠN VĂN|DOAN VAN)\s*[:=\-]?\s*(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex PassageRegex();

    [GeneratedRegex(@"^\s*(?:TRANSCRIPT|AUDIO SCRIPT|AUDIO|BẢNG KÊ|BANG KE)\s*[:=\-]?\s*(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex TranscriptRegex();

    [GeneratedRegex(@"^\s*(?:KEYWORDS?|REQUIRED_KEYWORDS?|TỪ KHÓA|TU KHOA)\s*[:=\-]\s*(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex KeywordsRegex();

    [GeneratedRegex(@"^\s*(?:MAX_WORDS?|SỐ TỪ TỐI ĐA)\s*[:=\-]\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex MaxWordsRegex();

    [GeneratedRegex(@"^\s*(?:MIN_WORDS?|SỐ TỪ TỐI THIỂU)\s*[:=\-]\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex MinWordsRegex();

    [GeneratedRegex(@"^\s*(?:MAX_SELECTIONS?|CHỌN TỐI ĐA)\s*[:=\-]\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex MaxSelectionsRegex();

    [GeneratedRegex(@"^\s*(?:PREP(?:_SECONDS)?|CHUẨN BỊ)\s*[:=\-]\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex PrepRegex();

    [GeneratedRegex(@"^\s*(?:RESPONSE(?:_SECONDS)?|TRẢ LỜI)\s*[:=\-]\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ResponseRegex();

    [GeneratedRegex(@"^\s*(?:HINT|GỢI Ý|GOI Y)\s*[:=\-]\s*(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex HintRegex();

    [GeneratedRegex(@"^\s*(?:TYPE|LOẠI CÂU HỎI)\s*[:=\-]\s*([a-zA-Z0-9_\-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex QuestionTypeRegex();

    [GeneratedRegex(@"^\s*(?:(?:Question|Câu|Q)?\s*(\d+)[\.:\)]?|\b(\d+)[\.:\)])\s*([A-Za-z])\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex RapidAnswerKeyLineRegex();

    public QuickIngestParseResultDto Parse(QuickIngestParseRequest request)
    {
        if (request?.SectionId.HasValue == true && db != null)
        {
            try
            {
                var section = db.Sections.AsNoTracking().FirstOrDefault(s => s.SectionId == request.SectionId.Value);
                if (section != null)
                {
                    return ParseWithBlueprint(request.RawText, section);
                }
            }
            catch
            {
                // Fall back to standalone parse if DB query fails
            }
        }

        return ParseInternal(request?.RawText, null);
    }

    public async Task<QuickIngestParseResultDto> ParseStagingAsync(QuickIngestParseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Section? section = null;
        if (request.SectionId.HasValue && db != null)
        {
            section = await db.Sections.AsNoTracking().FirstOrDefaultAsync(s => s.SectionId == request.SectionId.Value, cancellationToken);
        }

        return ParseInternal(request.RawText, section);
    }

    private QuickIngestParseResultDto ParseWithBlueprint(string? rawText, Section section)
    {
        return ParseInternal(rawText, section);
    }

    private QuickIngestParseResultDto ParseInternal(string? rawText, Section? section)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return new QuickIngestParseResultDto(
                Success: false,
                TotalQuestions: 0,
                ValidQuestions: 0,
                ErrorCount: 1,
                Questions: [],
                GlobalIssues: [new QuickIngestIssueDto(1, "error", "Input text cannot be empty.")]);
        }

        var text = rawText.Trim();

        QuickIngestParseResultDto result;

        // Check if text is JSON
        if (text.StartsWith('[') || (text.StartsWith('{') && (text.Contains("\"questions\"") || text.Contains("\"stem\""))))
        {
            try
            {
                result = ParseJson(text);
            }
            catch (JsonException ex)
            {
                return new QuickIngestParseResultDto(
                    Success: false,
                    TotalQuestions: 0,
                    ValidQuestions: 0,
                    ErrorCount: 1,
                    Questions: [],
                    GlobalIssues: [new QuickIngestIssueDto((int)(ex.LineNumber ?? 1), "error", $"Invalid JSON syntax: {ex.Message}")]);
            }
        }
        else
        {
            result = ParsePlainText(rawText, section);
        }

        if (section != null)
        {
            result = ValidateAgainstSectionBlueprint(result, section);
        }

        return result;
    }

    private static QuickIngestParseResultDto ParsePlainText(string rawText, Section? section)
    {
        var rawLines = rawText.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        var globalIssues = new List<QuickIngestIssueDto>();

        // Check if entire input is rapid answer keys (e.g. 1. A \n 2. C ...)
        if (IsRapidAnswerKeyBatch(rawLines))
        {
            return ParseRapidAnswerKeys(rawLines, section);
        }

        var clusters = SegmentLinesIntoClusters(rawLines);

        if (clusters.Count == 0)
        {
            globalIssues.Add(new QuickIngestIssueDto(1, "error", "No questions could be identified from the input text. Please check format."));
            return new QuickIngestParseResultDto(
                Success: false,
                TotalQuestions: 0,
                ValidQuestions: 0,
                ErrorCount: globalIssues.Count,
                Questions: [],
                GlobalIssues: globalIssues);
        }

        var parsedQuestions = new List<QuickIngestParsedQuestionDto>();
        var qIndex = 1;
        var groupIndex = 1;

        foreach (var cluster in clusters)
        {
            foreach (var block in cluster.QuestionBlocks)
            {
                var parsed = ParseSingleBlock(block, qIndex, cluster.Passage, cluster.Transcript, groupIndex, section);
                parsedQuestions.Add(parsed);
                qIndex++;
            }
            groupIndex++;
        }

        var totalErrors = parsedQuestions.Sum(q => q.Issues.Count(i => i.Severity == "error")) + globalIssues.Count(i => i.Severity == "error");
        var validQuestions = parsedQuestions.Count(q => q.IsValid);

        return new QuickIngestParseResultDto(
            Success: parsedQuestions.Count > 0 && totalErrors == 0,
            TotalQuestions: parsedQuestions.Count,
            ValidQuestions: validQuestions,
            ErrorCount: totalErrors,
            Questions: parsedQuestions,
            GlobalIssues: globalIssues);
    }

    private sealed class RawLine
    {
        public int LineNumber { get; set; }
        public string Text { get; set; } = string.Empty;
    }

    private sealed class QuestionBlock
    {
        public List<RawLine> Lines { get; set; } = [];
        public int StartLineNumber => Lines.FirstOrDefault()?.LineNumber ?? 1;
        public int EndLineNumber => Lines.LastOrDefault()?.LineNumber ?? 1;
    }

    private sealed class ClusterBlock
    {
        public string? Passage { get; set; }
        public string? Transcript { get; set; }
        public List<QuestionBlock> QuestionBlocks { get; set; } = [];
    }

    private static bool IsRapidAnswerKeyBatch(string[] rawLines)
    {
        var nonBlankLines = rawLines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (nonBlankLines.Count == 0) return false;

        var matchesKeyCount = 0;
        foreach (var line in nonBlankLines)
        {
            var trimmed = line.Trim();
            if (RapidAnswerKeyLineRegex().IsMatch(trimmed))
            {
                matchesKeyCount++;
            }
            else if (TranscriptRegex().IsMatch(trimmed) || ExplanationRegex().IsMatch(trimmed))
            {
                // Supplementary lines in rapid mode
            }
            else
            {
                return false;
            }
        }

        return matchesKeyCount > 0 && matchesKeyCount >= nonBlankLines.Count / 2;
    }

    private static QuickIngestParseResultDto ParseRapidAnswerKeys(string[] rawLines, Section? section)
    {
        var parsedQuestions = new List<QuickIngestParsedQuestionDto>();
        var globalIssues = new List<QuickIngestIssueDto>();

        var optCount = section?.OptionCount ?? 4;
        var qIdx = 1;

        QuickIngestParsedQuestionDto? currentQ = null;

        for (var i = 0; i < rawLines.Length; i++)
        {
            var lineNum = i + 1;
            var line = rawLines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var rapidMatch = RapidAnswerKeyLineRegex().Match(line);
            if (rapidMatch.Success)
            {
                if (currentQ != null)
                {
                    parsedQuestions.Add(currentQ);
                }

                var qNumStr = rapidMatch.Groups[1].Success ? rapidMatch.Groups[1].Value : rapidMatch.Groups[2].Value;
                var keyLetter = rapidMatch.Groups[3].Value.ToUpperInvariant();
                var parsedNum = int.TryParse(qNumStr, out var n) ? n : qIdx;

                var options = new List<QuickIngestParsedOptionDto>();
                for (var oi = 0; oi < Math.Max(optCount, 2); oi++)
                {
                    var label = ((char)('A' + oi)).ToString();
                    options.Add(new QuickIngestParsedOptionDto(label, $"({label})", label == keyLetter));
                }

                var issues = new List<QuickIngestIssueDto>();
                var maxLetter = (char)('A' + optCount - 1);
                if (keyLetter.Length == 1 && keyLetter[0] > maxLetter)
                {
                    issues.Add(new QuickIngestIssueDto(lineNum, "error", $"Answer key '{keyLetter}' exceeds section option count ({optCount}, max option is {maxLetter}).", parsedNum));
                }

                currentQ = new QuickIngestParsedQuestionDto(
                    Index: parsedNum,
                    StartLineNumber: lineNum,
                    EndLineNumber: lineNum,
                    Stem: $"Question {parsedNum}",
                    DifficultyLevel: 3,
                    Explanation: null,
                    CategoryCode: null,
                    Options: options,
                    Issues: issues,
                    IsValid: issues.Count == 0,
                    QuestionType: optCount == 3 ? "mcq_3" : "mcq");

                qIdx = parsedNum + 1;
                continue;
            }

            var transMatch = TranscriptRegex().Match(line);
            if (transMatch.Success && currentQ != null)
            {
                var transText = transMatch.Groups[1].Value.Trim();
                currentQ = currentQ with
                {
                    Transcript = transText,
                    Stem = !string.IsNullOrWhiteSpace(transText) ? transText : currentQ.Stem
                };
                continue;
            }

            var expMatch = ExplanationRegex().Match(line);
            if (expMatch.Success && currentQ != null)
            {
                var expText = expMatch.Groups[1].Value.Trim();
                currentQ = currentQ with { Explanation = expText };
                continue;
            }
        }

        if (currentQ != null)
        {
            parsedQuestions.Add(currentQ);
        }

        var totalErrors = parsedQuestions.Sum(q => q.Issues.Count(i => i.Severity == "error"));
        return new QuickIngestParseResultDto(
            Success: parsedQuestions.Count > 0 && totalErrors == 0,
            TotalQuestions: parsedQuestions.Count,
            ValidQuestions: parsedQuestions.Count(q => q.IsValid),
            ErrorCount: totalErrors,
            Questions: parsedQuestions,
            GlobalIssues: globalIssues);
    }

    private static List<ClusterBlock> SegmentLinesIntoClusters(string[] rawLines)
    {
        var indexedLines = new List<RawLine>();
        for (var i = 0; i < rawLines.Length; i++)
        {
            indexedLines.Add(new RawLine { LineNumber = i + 1, Text = rawLines[i] });
        }

        var clusters = new List<ClusterBlock>();
        ClusterBlock? currentCluster = null;
        QuestionBlock? currentBlock = null;

        string? pendingPassage = null;
        string? pendingTranscript = null;
        var hasSeenOptionsInCurrentBlock = false;

        for (var i = 0; i < indexedLines.Count; i++)
        {
            var lineObj = indexedLines[i];
            var trimmed = lineObj.Text.Trim();

            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            // Check Passage header
            var pasMatch = PassageRegex().Match(trimmed);
            if (pasMatch.Success)
            {
                if (currentCluster != null && currentCluster.QuestionBlocks.Count > 0)
                {
                    clusters.Add(currentCluster);
                }

                currentCluster = new ClusterBlock();
                currentBlock = null;

                var passageLines = new List<string>();
                var inlinePas = pasMatch.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(inlinePas)) passageLines.Add(inlinePas);

                // Collect following lines until a question or transcript is found
                while (i + 1 < indexedLines.Count)
                {
                    var nextTrimmed = indexedLines[i + 1].Text.Trim();
                    if (string.IsNullOrWhiteSpace(nextTrimmed))
                    {
                        i++;
                        continue;
                    }

                    if (QuestionHeaderRegex().IsMatch(nextTrimmed) ||
                        PromptHeaderRegex().IsMatch(nextTrimmed) ||
                        PassageRegex().IsMatch(nextTrimmed) ||
                        TranscriptRegex().IsMatch(nextTrimmed))
                    {
                        break;
                    }

                    passageLines.Add(nextTrimmed);
                    i++;
                }

                currentCluster.Passage = string.Join("\n\n", passageLines).Trim();
                continue;
            }

            // Check Transcript header before question
            var trMatch = TranscriptRegex().Match(trimmed);
            if (trMatch.Success && currentBlock == null)
            {
                var transcriptLines = new List<string>();
                var inlineTr = trMatch.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(inlineTr)) transcriptLines.Add(inlineTr);

                while (i + 1 < indexedLines.Count)
                {
                    var nextTrimmed = indexedLines[i + 1].Text.Trim();
                    if (string.IsNullOrWhiteSpace(nextTrimmed))
                    {
                        i++;
                        continue;
                    }

                    if (QuestionHeaderRegex().IsMatch(nextTrimmed) ||
                        PromptHeaderRegex().IsMatch(nextTrimmed) ||
                        PassageRegex().IsMatch(nextTrimmed))
                    {
                        break;
                    }

                    transcriptLines.Add(nextTrimmed);
                    i++;
                }

                if (currentCluster == null) currentCluster = new ClusterBlock();
                currentCluster.Transcript = string.Join("\n", transcriptLines).Trim();
                continue;
            }

            var qMatch = QuestionHeaderRegex().Match(trimmed);
            var pMatch = PromptHeaderRegex().Match(trimmed);
            var isExplicitHeader = qMatch.Success || pMatch.Success;

            var optMatch = OptionLineRegex().Match(trimmed);
            var isAnswerKey = AnswerKeyRegex().IsMatch(trimmed);
            var isExplanation = ExplanationRegex().IsMatch(trimmed);
            var isCategory = CategoryRegex().IsMatch(trimmed);
            var isKeyword = KeywordsRegex().IsMatch(trimmed);

            var isOptionLike = optMatch.Success && !isExplicitHeader && !isAnswerKey && !isExplanation && !isCategory && !isKeyword;

            if (isOptionLike)
            {
                hasSeenOptionsInCurrentBlock = true;
            }

            if (currentCluster == null)
            {
                currentCluster = new ClusterBlock { Passage = pendingPassage, Transcript = pendingTranscript };
            }

            if (isExplicitHeader || (hasSeenOptionsInCurrentBlock && !isOptionLike && !isAnswerKey && !isExplanation && !isCategory && !isKeyword && IsLikelyNextQuestionStart(indexedLines, i)))
            {
                if (currentBlock != null && currentBlock.Lines.Count > 0)
                {
                    currentCluster.QuestionBlocks.Add(currentBlock);
                }

                currentBlock = new QuestionBlock();
                hasSeenOptionsInCurrentBlock = false;
            }
            else if (currentBlock == null)
            {
                currentBlock = new QuestionBlock();
                hasSeenOptionsInCurrentBlock = false;
            }

            currentBlock.Lines.Add(lineObj);
        }

        if (currentBlock != null && currentBlock.Lines.Count > 0 && currentCluster != null)
        {
            currentCluster.QuestionBlocks.Add(currentBlock);
        }

        if (currentCluster != null && currentCluster.QuestionBlocks.Count > 0)
        {
            clusters.Add(currentCluster);
        }

        return clusters;
    }

    private static bool IsLikelyNextQuestionStart(List<RawLine> lines, int currentIndex)
    {
        var nonBlankCount = 0;
        for (var j = currentIndex + 1; j < lines.Count && nonBlankCount < 3; j++)
        {
            var nextTrimmed = lines[j].Text.Trim();
            if (string.IsNullOrWhiteSpace(nextTrimmed)) continue;

            nonBlankCount++;
            if (OptionLineRegex().IsMatch(nextTrimmed) || AnswerKeyRegex().IsMatch(nextTrimmed) || KeywordsRegex().IsMatch(nextTrimmed))
            {
                return true;
            }
        }
        return false;
    }

    private static QuickIngestParsedQuestionDto ParseSingleBlock(
        QuestionBlock block,
        int questionIndex,
        string? clusterPassage,
        string? clusterTranscript,
        int groupIndex,
        Section? section)
    {
        var issues = new List<QuickIngestIssueDto>();
        var stemLines = new List<string>();
        var options = new List<QuickIngestParsedOptionDto>();
        var explanationLines = new List<string>();
        string? categoryCode = null;
        string? explicitType = null;
        string? requiredKeywords = null;
        string? hint = null;
        int? maxWords = null;
        int? minWords = null;
        int? maxSelections = null;
        int? prepSeconds = null;
        int? responseSeconds = null;
        var acceptedAnswers = new List<string>();

        var trailingAnswerKeys = new List<string>();
        var trailingKeyLineNumber = 0;
        var inExplanation = false;

        for (var i = 0; i < block.Lines.Count; i++)
        {
            var lineObj = block.Lines[i];
            var line = lineObj.Text.Trim();
            var lineNum = lineObj.LineNumber;

            if (string.IsNullOrWhiteSpace(line)) continue;

            var catMatch = CategoryRegex().Match(line);
            if (catMatch.Success)
            {
                categoryCode = catMatch.Groups[1].Value.Trim();
                inExplanation = false;
                continue;
            }

            var typeMatch = QuestionTypeRegex().Match(line);
            if (typeMatch.Success)
            {
                explicitType = typeMatch.Groups[1].Value.Trim().ToLowerInvariant();
                inExplanation = false;
                continue;
            }

            var kwMatch = KeywordsRegex().Match(line);
            if (kwMatch.Success)
            {
                requiredKeywords = kwMatch.Groups[1].Value.Trim();
                inExplanation = false;
                continue;
            }

            var mwMatch = MaxWordsRegex().Match(line);
            if (mwMatch.Success && int.TryParse(mwMatch.Groups[1].Value, out var mw))
            {
                maxWords = mw;
                inExplanation = false;
                continue;
            }

            var minwMatch = MinWordsRegex().Match(line);
            if (minwMatch.Success && int.TryParse(minwMatch.Groups[1].Value, out var minw))
            {
                minWords = minw;
                inExplanation = false;
                continue;
            }

            var msMatch = MaxSelectionsRegex().Match(line);
            if (msMatch.Success && int.TryParse(msMatch.Groups[1].Value, out var ms))
            {
                maxSelections = ms;
                inExplanation = false;
                continue;
            }

            var prepMatch = PrepRegex().Match(line);
            if (prepMatch.Success && int.TryParse(prepMatch.Groups[1].Value, out var pSec))
            {
                prepSeconds = pSec;
                inExplanation = false;
                continue;
            }

            var respMatch = ResponseRegex().Match(line);
            if (respMatch.Success && int.TryParse(respMatch.Groups[1].Value, out var rSec))
            {
                responseSeconds = rSec;
                inExplanation = false;
                continue;
            }

            var hintMatch = HintRegex().Match(line);
            if (hintMatch.Success)
            {
                hint = hintMatch.Groups[1].Value.Trim();
                inExplanation = false;
                continue;
            }

            var ansMatch = AnswerKeyRegex().Match(line);
            if (ansMatch.Success)
            {
                var ansRaw = ansMatch.Groups[1].Value.Trim();
                trailingKeyLineNumber = lineNum;
                inExplanation = false;

                // Check if inline completion answers: "answer 1 | answer 2"
                if (ansRaw.Contains('|') || (ansRaw.Length > 2 && !ansRaw.Contains(',')))
                {
                    var parts = ansRaw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    acceptedAnswers.AddRange(parts);
                }
                else
                {
                    // Single or comma-separated keys: "A, C" or "B"
                    var keys = ansRaw.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var k in keys)
                    {
                        var clean = k.Trim('(', ')', '.', ':').ToUpperInvariant();
                        if (clean.Length == 1 && clean[0] is >= 'A' and <= 'Z')
                        {
                            trailingAnswerKeys.Add(clean);
                        }
                    }
                }
                continue;
            }

            var expMatch = ExplanationRegex().Match(line);
            if (expMatch.Success)
            {
                inExplanation = true;
                var expText = expMatch.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(expText)) explanationLines.Add(expText);
                continue;
            }

            if (inExplanation)
            {
                explanationLines.Add(line);
                continue;
            }

            var hasLeadingAsterisk = line.StartsWith('*') || line.StartsWith("（*") || line.StartsWith("(*");
            var optMatch = OptionLineRegex().Match(line);
            if (optMatch.Success && !line.StartsWith("Question", StringComparison.OrdinalIgnoreCase) && !line.StartsWith("Câu", StringComparison.OrdinalIgnoreCase))
            {
                var label = (optMatch.Groups[1].Success ? optMatch.Groups[1].Value : optMatch.Groups[2].Value).ToUpperInvariant();
                var remainder = optMatch.Groups[3].Value.Trim();

                var isCorrect = false;
                if (hasLeadingAsterisk || line.StartsWith($"*{label}", StringComparison.OrdinalIgnoreCase) || line.StartsWith($"*({label})", StringComparison.OrdinalIgnoreCase))
                {
                    isCorrect = true;
                }

                if (remainder.StartsWith('*'))
                {
                    isCorrect = true;
                    remainder = remainder.TrimStart('*', ' ');
                }

                if (remainder.EndsWith('*'))
                {
                    isCorrect = true;
                    remainder = remainder.TrimEnd('*', ' ');
                }

                string? rationale = null;
                var commentIdx = remainder.IndexOf("//", StringComparison.Ordinal);
                if (commentIdx >= 0)
                {
                    rationale = remainder[(commentIdx + 2)..].Trim();
                    remainder = remainder[..commentIdx].Trim();
                }

                options.Add(new QuickIngestParsedOptionDto(label, remainder, isCorrect, rationale));
                continue;
            }

            if (options.Count == 0)
            {
                var pMatch = PromptHeaderRegex().Match(line);
                if (pMatch.Success)
                {
                    var stemPart = pMatch.Groups["stem"].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(stemPart)) stemLines.Add(stemPart);
                    continue;
                }

                var qMatch = QuestionHeaderRegex().Match(line);
                if (qMatch.Success)
                {
                    var stemPart = qMatch.Groups["stem"].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(stemPart)) stemLines.Add(stemPart);
                }
                else
                {
                    stemLines.Add(line);
                }
            }
            else
            {
                var last = options[^1];
                var combinedText = last.Text + " " + line;
                options[^1] = new QuickIngestParsedOptionDto(last.Label, combinedText, last.IsCorrect, last.Rationale);
            }
        }

        // Apply trailing answer keys to options
        if (trailingAnswerKeys.Count > 0)
        {
            for (var o = 0; o < options.Count; o++)
            {
                var opt = options[o];
                var matched = trailingAnswerKeys.Contains(opt.Label, StringComparer.OrdinalIgnoreCase);
                if (matched)
                {
                    options[o] = new QuickIngestParsedOptionDto(opt.Label, opt.Text, true, opt.Rationale);
                }
                else if (options.Count(x => x.IsCorrect) == 0 && trailingAnswerKeys.Count == 1)
                {
                    options[o] = new QuickIngestParsedOptionDto(opt.Label, opt.Text, false, opt.Rationale);
                }
            }

            foreach (var key in trailingAnswerKeys)
            {
                if (!options.Any(o => string.Equals(o.Label, key, StringComparison.OrdinalIgnoreCase)))
                {
                    issues.Add(new QuickIngestIssueDto(trailingKeyLineNumber, "error", $"Answer key '{key}' does not match any parsed option (available: {string.Join(", ", options.Select(x => x.Label))}).", questionIndex));
                }
            }
        }

        var stem = string.Join(" ", stemLines).Trim();
        var explanation = explanationLines.Count > 0 ? string.Join("\n", explanationLines).Trim() : null;

        // Determine question type
        var questionType = explicitType ?? DetermineQuestionType(options, trailingAnswerKeys, acceptedAnswers, requiredKeywords, maxSelections, section);

        var acceptedJson = acceptedAnswers.Count > 0 ? JsonSerializer.Serialize(acceptedAnswers) : null;

        // Validation based on QuestionType
        if (string.IsNullOrWhiteSpace(stem))
        {
            issues.Add(new QuickIngestIssueDto(block.StartLineNumber, "error", "Question stem is empty.", questionIndex));
        }

        if (questionType == "free_response")
        {
            // Free response: no options required
        }
        else if (questionType == "inline_completion")
        {
            if (acceptedAnswers.Count == 0 && string.IsNullOrWhiteSpace(acceptedJson))
            {
                issues.Add(new QuickIngestIssueDto(block.StartLineNumber, "error", "Inline completion requires accepted answer variants (ANSWER: option1 | option2).", questionIndex));
            }
        }
        else if (questionType == "multi_select")
        {
            if (options.Count < 2)
            {
                issues.Add(new QuickIngestIssueDto(block.StartLineNumber, "error", $"Multi-select question must have at least 2 options (found {options.Count}).", questionIndex));
            }
            var correctCount = options.Count(o => o.IsCorrect);
            if (correctCount == 0)
            {
                issues.Add(new QuickIngestIssueDto(block.StartLineNumber, "error", "Multi-select question has no correct options marked (*A. or ANSWER: A, C).", questionIndex));
            }
            if (!maxSelections.HasValue && correctCount > 0)
            {
                maxSelections = correctCount;
            }
        }
        else // mcq or mcq_3
        {
            if (options.Count < 2)
            {
                issues.Add(new QuickIngestIssueDto(block.StartLineNumber, "error", $"Question must have at least 2 options (found {options.Count}).", questionIndex));
            }

            var correctCount = options.Count(o => o.IsCorrect);
            if (options.Count >= 2 && correctCount == 0)
            {
                issues.Add(new QuickIngestIssueDto(block.StartLineNumber, "error", "Question has no correct answer marked (*A. or ANSWER: A).", questionIndex));
            }
            else if (correctCount > 1)
            {
                issues.Add(new QuickIngestIssueDto(block.StartLineNumber, "error", $"Question has multiple correct answers marked ({correctCount} marked correct). Exactly one is required.", questionIndex));
            }
        }

        var duplicateLabels = options.GroupBy(o => o.Label).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicateLabels.Count > 0)
        {
            issues.Add(new QuickIngestIssueDto(block.StartLineNumber, "error", $"Duplicate option label(s) found: {string.Join(", ", duplicateLabels)}.", questionIndex));
        }

        var isValid = issues.All(i => i.Severity != "error");

        return new QuickIngestParsedQuestionDto(
            Index: questionIndex,
            StartLineNumber: block.StartLineNumber,
            EndLineNumber: block.EndLineNumber,
            Stem: stem,
            DifficultyLevel: 3,
            Explanation: explanation,
            CategoryCode: categoryCode,
            Options: options,
            Issues: issues,
            IsValid: isValid,
            QuestionType: questionType,
            Passage: clusterPassage,
            Transcript: clusterTranscript,
            MaxWords: maxWords,
            MaxSelections: maxSelections,
            AcceptedAnswersJson: acceptedJson,
            RequiredKeywords: requiredKeywords,
            Hint: hint,
            GroupNumber: groupIndex,
            PrepSeconds: prepSeconds,
            ResponseSeconds: responseSeconds,
            MinWords: minWords);
    }

    private static string DetermineQuestionType(
        List<QuickIngestParsedOptionDto> options,
        List<string> trailingKeys,
        List<string> acceptedAnswers,
        string? requiredKeywords,
        int? maxSelections,
        Section? section)
    {
        if (!string.IsNullOrWhiteSpace(requiredKeywords)) return "free_response";
        if (acceptedAnswers.Count > 0) return "inline_completion";
        if (trailingKeys.Count > 1 || maxSelections.HasValue) return "multi_select";

        if (section != null)
        {
            if (section.OptionCount == 3) return "mcq_3";
            if (!string.IsNullOrWhiteSpace(section.AllowedComponentTypesJson))
            {
                if (section.AllowedComponentTypesJson.Contains("inline_completion") && options.Count == 0) return "inline_completion";
                if (section.AllowedComponentTypesJson.Contains("free_response") && options.Count == 0) return "free_response";
            }
        }

        return "mcq";
    }

    public static QuickIngestParseResultDto ValidateAgainstSectionBlueprint(QuickIngestParseResultDto result, Section section)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(section);

        var updatedQuestions = new List<QuickIngestParsedQuestionDto>();
        HashSet<string>? allowedTypes = null;

        if (!string.IsNullOrWhiteSpace(section.AllowedComponentTypesJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(section.AllowedComponentTypesJson);
                if (parsed != null && parsed.Count > 0)
                {
                    allowedTypes = new HashSet<string>(parsed, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch
            {
                // Fallback if parsing fails
            }
        }

        foreach (var q in result.Questions)
        {
            var issues = new List<QuickIngestIssueDto>(q.Issues);
            var qType = q.QuestionType.ToLowerInvariant();

            // 1. Allowed Component Types
            if (allowedTypes != null && allowedTypes.Count > 0)
            {
                var isAllowed = allowedTypes.Contains(qType);
                if (!isAllowed)
                {
                    if (qType == "mcq" && allowedTypes.Contains("mcq_3") && section.OptionCount == 3)
                    {
                        isAllowed = true;
                    }
                    else if (qType == "mcq_3" && allowedTypes.Contains("mcq"))
                    {
                        isAllowed = true;
                    }
                }

                if (!isAllowed)
                {
                    issues.Add(new QuickIngestIssueDto(
                        q.StartLineNumber,
                        "error",
                        $"Question component type '{q.QuestionType}' is not allowed in section '{section.Name}' (Allowed: {string.Join(", ", allowedTypes)}).",
                        q.Index));
                }
            }

            // 2. OptionCount
            if (q.Options.Count > 0)
            {
                var maxAllowedChar = (char)('A' + section.OptionCount - 1);
                foreach (var opt in q.Options)
                {
                    if (opt.Label.Length == 1 && opt.Label[0] > maxAllowedChar)
                    {
                        issues.Add(new QuickIngestIssueDto(
                            q.StartLineNumber,
                            "error",
                            $"Option '{opt.Label}' exceeds section allowed OptionCount ({section.OptionCount}, max allowed label is '{maxAllowedChar}').",
                            q.Index));
                    }
                }

                if ((qType == "mcq" || qType == "mcq_3") && q.Options.Count > section.OptionCount)
                {
                    issues.Add(new QuickIngestIssueDto(
                        q.StartLineNumber,
                        "error",
                        $"Question has {q.Options.Count} options, but section allows maximum of {section.OptionCount} options.",
                        q.Index));
                }
            }

            // 3. RequiredKeywordCount
            if (section.RequiredKeywordCount > 0)
            {
                if (string.IsNullOrWhiteSpace(q.RequiredKeywords))
                {
                    issues.Add(new QuickIngestIssueDto(
                        q.StartLineNumber,
                        "error",
                        $"Section requires {section.RequiredKeywordCount} keyword(s), but none were provided (use 'KEYWORDS: word1, word2').",
                        q.Index));
                }
                else
                {
                    var actualCount = q.RequiredKeywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
                    if (actualCount != section.RequiredKeywordCount)
                    {
                        issues.Add(new QuickIngestIssueDto(
                            q.StartLineNumber,
                            "error",
                            $"Section requires {section.RequiredKeywordCount} keyword(s), but found {actualCount} keyword(s).",
                            q.Index));
                    }
                }
            }

            // 4. RequirePassage
            if (section.RequirePassage && string.IsNullOrWhiteSpace(q.Passage))
            {
                issues.Add(new QuickIngestIssueDto(
                    q.StartLineNumber,
                    "error",
                    $"Section requires a reading passage, but no passage was provided.",
                    q.Index));
            }

            var isValid = issues.All(i => i.Severity != "error");
            updatedQuestions.Add(q with { Issues = issues, IsValid = isValid });
        }

        var totalErrors = updatedQuestions.Sum(q => q.Issues.Count(i => i.Severity == "error")) + result.GlobalIssues.Count(i => i.Severity == "error");

        return result with
        {
            Success = updatedQuestions.Count > 0 && totalErrors == 0,
            TotalQuestions = updatedQuestions.Count,
            ValidQuestions = updatedQuestions.Count(q => q.IsValid),
            ErrorCount = totalErrors,
            Questions = updatedQuestions
        };
    }

    private static QuickIngestParseResultDto ParseJson(string jsonText)
    {
        using var doc = JsonDocument.Parse(jsonText);
        var root = doc.RootElement;

        JsonElement questionsElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            questionsElement = root;
        }
        else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("questions", out var qProp) && qProp.ValueKind == JsonValueKind.Array)
        {
            questionsElement = qProp;
        }
        else
        {
            return new QuickIngestParseResultDto(
                Success: false,
                TotalQuestions: 0,
                ValidQuestions: 0,
                ErrorCount: 1,
                Questions: [],
                GlobalIssues: [new QuickIngestIssueDto(1, "error", "JSON must be an array of questions or an object with a 'questions' array.")]);
        }

        var parsedQuestions = new List<QuickIngestParsedQuestionDto>();
        var globalIssues = new List<QuickIngestIssueDto>();
        var qIdx = 1;

        foreach (var item in questionsElement.EnumerateArray())
        {
            var issues = new List<QuickIngestIssueDto>();

            var stem = item.TryGetProperty("stem", out var sProp) ? sProp.GetString() ?? string.Empty : string.Empty;
            var difficulty = item.TryGetProperty("difficultyLevel", out var dProp) && dProp.TryGetInt32(out var diff) ? diff : 3;
            var explanation = item.TryGetProperty("explanation", out var eProp) ? eProp.GetString() : null;
            var categoryCode = item.TryGetProperty("categoryCode", out var cProp) ? cProp.GetString() : null;
            var questionType = item.TryGetProperty("questionType", out var qtProp) ? qtProp.GetString()?.ToLowerInvariant() ?? "mcq" : "mcq";
            var passage = item.TryGetProperty("passage", out var pasProp) ? pasProp.GetString() : null;
            var transcript = item.TryGetProperty("transcript", out var trProp) ? trProp.GetString() : null;
            var requiredKeywords = item.TryGetProperty("requiredKeywords", out var rkProp) ? rkProp.GetString() : null;
            var hint = item.TryGetProperty("hint", out var hProp) ? hProp.GetString() : null;
            var maxWords = item.TryGetProperty("maxWords", out var mwProp) && mwProp.TryGetInt32(out var mwVal) ? (int?)mwVal : null;
            var minWords = item.TryGetProperty("minWords", out var minwProp) && minwProp.TryGetInt32(out var minwVal) ? (int?)minwVal : null;
            var maxSelections = item.TryGetProperty("maxSelections", out var msProp) && msProp.TryGetInt32(out var msVal) ? (int?)msVal : null;
            var prepSec = item.TryGetProperty("prepSeconds", out var psProp) && psProp.TryGetInt32(out var psVal) ? (int?)psVal : null;
            var respSec = item.TryGetProperty("responseSeconds", out var rsProp) && rsProp.TryGetInt32(out var rsVal) ? (int?)rsVal : null;

            string? acceptedJson = null;
            if (item.TryGetProperty("acceptedAnswers", out var aaProp))
            {
                if (aaProp.ValueKind == JsonValueKind.Array)
                {
                    var aaList = aaProp.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                    acceptedJson = JsonSerializer.Serialize(aaList);
                }
                else if (aaProp.ValueKind == JsonValueKind.String)
                {
                    acceptedJson = aaProp.GetString();
                }
            }

            var options = new List<QuickIngestParsedOptionDto>();
            if (item.TryGetProperty("options", out var optProp) && optProp.ValueKind == JsonValueKind.Array)
            {
                var optIdx = 0;
                foreach (var opt in optProp.EnumerateArray())
                {
                    var label = opt.TryGetProperty("label", out var lProp) ? lProp.GetString() ?? ((char)('A' + optIdx)).ToString() : ((char)('A' + optIdx)).ToString();
                    var text = opt.TryGetProperty("text", out var tProp) ? tProp.GetString() ?? string.Empty : string.Empty;
                    var isCorrect = opt.TryGetProperty("isCorrect", out var icProp) && icProp.GetBoolean();
                    var rationale = opt.TryGetProperty("rationale", out var rProp) ? rProp.GetString() : null;

                    options.Add(new QuickIngestParsedOptionDto(label, text, isCorrect, rationale));
                    optIdx++;
                }
            }

            if (string.IsNullOrWhiteSpace(stem))
            {
                issues.Add(new QuickIngestIssueDto(qIdx, "error", "Question stem is empty.", qIdx));
            }

            if (questionType == "free_response")
            {
                // Free response: options not required
            }
            else if (questionType == "inline_completion")
            {
                if (string.IsNullOrWhiteSpace(acceptedJson))
                {
                    issues.Add(new QuickIngestIssueDto(qIdx, "error", "Inline completion requires 'acceptedAnswers' array or string.", qIdx));
                }
            }
            else if (questionType == "multi_select")
            {
                if (options.Count < 2)
                {
                    issues.Add(new QuickIngestIssueDto(qIdx, "error", $"Multi-select question must have at least 2 options (found {options.Count}).", qIdx));
                }
                var correctCount = options.Count(o => o.IsCorrect);
                if (correctCount == 0)
                {
                    issues.Add(new QuickIngestIssueDto(qIdx, "error", "Multi-select question has no correct answer marked (isCorrect: true).", qIdx));
                }
            }
            else // mcq or mcq_3
            {
                if (options.Count < 2)
                {
                    issues.Add(new QuickIngestIssueDto(qIdx, "error", $"Question must have at least 2 options (found {options.Count}).", qIdx));
                }

                var correctCount = options.Count(o => o.IsCorrect);
                if (options.Count >= 2 && correctCount == 0)
                {
                    issues.Add(new QuickIngestIssueDto(qIdx, "error", "Question has no correct answer marked (isCorrect: true).", qIdx));
                }
                else if (correctCount > 1)
                {
                    issues.Add(new QuickIngestIssueDto(qIdx, "error", $"Question has multiple correct answers marked ({correctCount} marked correct). Exactly one is required.", qIdx));
                }
            }

            var isValid = issues.All(i => i.Severity != "error");

            parsedQuestions.Add(new QuickIngestParsedQuestionDto(
                Index: qIdx,
                StartLineNumber: qIdx,
                EndLineNumber: qIdx,
                Stem: stem,
                DifficultyLevel: difficulty,
                Explanation: explanation,
                CategoryCode: categoryCode,
                Options: options,
                Issues: issues,
                IsValid: isValid,
                QuestionType: questionType,
                Passage: passage,
                Transcript: transcript,
                MaxWords: maxWords,
                MinWords: minWords,
                MaxSelections: maxSelections,
                AcceptedAnswersJson: acceptedJson,
                RequiredKeywords: requiredKeywords,
                Hint: hint,
                PrepSeconds: prepSec,
                ResponseSeconds: respSec));

            qIdx++;
        }

        var totalErrors = parsedQuestions.Sum(q => q.Issues.Count(i => i.Severity == "error"));

        return new QuickIngestParseResultDto(
            Success: parsedQuestions.Count > 0 && totalErrors == 0,
            TotalQuestions: parsedQuestions.Count,
            ValidQuestions: parsedQuestions.Count(q => q.IsValid),
            ErrorCount: totalErrors,
            Questions: parsedQuestions,
            GlobalIssues: globalIssues);
    }

    public async Task<string> GetSampleTemplateAsync(int sectionId, CancellationToken cancellationToken = default)
    {
        if (db == null) throw new InvalidOperationException("CompanionDbContext is required to generate sample template.");

        var section = await db.Sections.AsNoTracking().FirstOrDefaultAsync(s => s.SectionId == sectionId, cancellationToken);
        if (section == null)
        {
            throw new AdminExamException("SectionNotFound", $"Section with ID {sectionId} was not found.", 404);
        }

        return GenerateSectionSampleTemplate(section);
    }

    public static string GenerateSectionSampleTemplate(Section section)
    {
        ArgumentNullException.ThrowIfNull(section);

        var allowed = section.AllowedComponentTypesJson ?? string.Empty;

        // 1. Keyword Free-Response Mode (e.g. TOEIC Writing Q1-5 photo sentences)
        if (section.RequiredKeywordCount > 0)
        {
            return """
                PROMPT: Write a sentence based on the photograph using the two keywords provided.
                KEYWORDS: photograph, yesterday
                MIN_WORDS: 5
                MAX_WORDS: 30

                PROMPT: Write a sentence based on the photograph using the two keywords provided.
                KEYWORDS: airport, luggage
                MIN_WORDS: 5
                MAX_WORDS: 30
                """;
        }

        // 2. Free-Response Essay / Speaking Mode (e.g. IELTS Writing Task 2, Speaking Part 2)
        if (string.Equals(section.Modality, "writing", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(section.Modality, "speaking", StringComparison.OrdinalIgnoreCase))
        {
            return """
                PROMPT: Some people think universities should focus on practical job skills, while others argue academic theory is more important. Discuss both views and give your opinion.
                MIN_WORDS: 250
                PREP: 0
                RESPONSE: 2400
                """;
        }

        // 3. Inline Completion Mode
        if (allowed.Contains("inline_completion", StringComparison.OrdinalIgnoreCase))
        {
            return """
                PASSAGE: The university botanical garden is open daily from 9:00 AM to 5:00 PM. Admission is free for university students and staff members with valid identification. Guided tours are conducted every Saturday at 11:00 AM starting from the visitor center.

                1. What time does the botanical garden open each day?
                ANSWER: 9:00 AM | 9 AM | 9am
                MAX_WORDS: 2

                2. What is required for students to receive free admission?
                ANSWER: valid identification | valid ID | ID
                MAX_WORDS: 2
                """;
        }

        // 4. Multi-Select Mode
        if (allowed.Contains("multi_select", StringComparison.OrdinalIgnoreCase))
        {
            return """
                PASSAGE: All laboratory researchers must submit an annual safety audit and equipment inventory before October 15. In addition, chemical spill emergency kits must be inspected monthly by the lab coordinator.

                1. Which documents must researchers submit before October 15?
                *A. Safety audit
                B. Financial budget
                *C. Equipment inventory
                D. Travel reimbursement
                ANSWER: A, C
                MAX_SELECTIONS: 2
                """;
        }

        // 5. Audio-Only 3-Option Mode (e.g. TOEIC Part 2 Q-and-R)
        if (section.OptionCount == 3 || section.HideOptionText)
        {
            return """
                1. A
                TRANSCRIPT: Where is the nearest bus stop?
                2. C
                TRANSCRIPT: When does the flight to Tokyo depart?
                3. B
                TRANSCRIPT: Who is leading the safety orientation?
                """;
        }

        // 6. Clustered Reading Comprehension Mode
        if (section.RequirePassage || section.ClusterSize > 1)
        {
            return """
                PASSAGE: GreenLeaf Logistics has announced an expansion of its refrigerated courier services to meet rising demand from organic food producers. The fleet will add 25 electric delivery vans by the end of Q3.

                1. What is the main purpose of the announcement?
                *A. To announce service expansion
                B. To advertise job openings
                C. To explain rate increases
                D. To report financial earnings
                EXPLANATION: The text states GreenLeaf announced an expansion of refrigerated courier services.

                2. How many new vehicles will be added?
                A. 10
                B. 15
                *C. 25
                D. 50
                EXPLANATION: The fleet will add 25 electric delivery vans.
                """;
        }

        // 7. Standard MCQ Mode (4 options)
        return """
            1. Ms. Tanaka requested that all department managers submit their quarterly budgets by _____ Friday.
            *A. next
            B. nearly
            C. soon
            D. highly
            EXPLANATION: The temporal adjective 'next' correctly precedes a day of the week to denote future timing.
            CATEGORY: grammar_prep

            Question 2: All visitors must present photo identification upon _____ the research building.
            (A) enter
            *(B) entering
            (C) entered
            (D) entrance
            ANSWER: B
            EXPLANATION: The preposition 'upon' must be followed by a gerund ('entering').
            CATEGORY: grammar_gerund
            """;
    }

    public async Task<QuickIngestCommitResultDto> CommitStagedAsync(
        int examId,
        QuickIngestCommitRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Commit request cannot be null.", 400);
        }

        if (db == null)
        {
            throw new InvalidOperationException("CompanionDbContext is required to commit staged questions.");
        }

        var exam = await db.Exams.Include(e => e.Cert).FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken)
            ?? throw new ExamNotFoundException(examId);

        var section = await db.Sections.FirstOrDefaultAsync(s => s.SectionId == request.SectionId && s.CertId == exam.CertId, cancellationToken)
            ?? throw new AdminExamException("SectionNotFound", $"Section with id {request.SectionId} not found for certificate '{exam.Cert.Name}'.", 404);

        if (request.Questions == null || request.Questions.Count == 0)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "No questions provided to commit.", 400);
        }

        // Pre-validate questions
        for (var i = 0; i < request.Questions.Count; i++)
        {
            var q = request.Questions[i];
            if (string.IsNullOrWhiteSpace(q.Stem))
            {
                throw new AdminExamException(ApiError.Codes.Validation, $"Question {i + 1} has an empty stem.", 400);
            }

            var blockingIssue = q.Issues?.FirstOrDefault(iss => iss.Severity == "error");
            if (blockingIssue != null)
            {
                throw new AdminExamException(ApiError.Codes.Validation, $"Question {i + 1} has validation error: {blockingIssue.Message}", 400);
            }

            var qType = q.QuestionType?.ToLowerInvariant() ?? "mcq";
            if (qType == "free_response")
            {
                // Free response: options not required
            }
            else if (qType == "inline_completion")
            {
                if (string.IsNullOrWhiteSpace(q.AcceptedAnswersJson))
                {
                    throw new AdminExamException(ApiError.Codes.Validation, $"Question {i + 1} is inline_completion but has no accepted answers.", 400);
                }
            }
            else if (qType == "multi_select")
            {
                if (q.Options == null || q.Options.Count < 2)
                {
                    throw new AdminExamException(ApiError.Codes.Validation, $"Question {i + 1} must have at least 2 options.", 400);
                }
                if (!q.Options.Any(o => o.IsCorrect))
                {
                    throw new AdminExamException(ApiError.Codes.Validation, $"Question {i + 1} must have at least one correct option.", 400);
                }
            }
            else // mcq / mcq_3
            {
                if (q.Options == null || q.Options.Count < 2)
                {
                    throw new AdminExamException(ApiError.Codes.Validation, $"Question {i + 1} must have at least 2 options.", 400);
                }
                var correctCount = q.Options.Count(o => o.IsCorrect);
                if (correctCount != 1)
                {
                    throw new AdminExamException(ApiError.Codes.Validation, $"Question {i + 1} must have exactly one correct option (found {correctCount}).", 400);
                }
            }
        }

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var isReplace = string.Equals(request.Mode, "replace", StringComparison.OrdinalIgnoreCase);
        int currentDisplayOrder = 1;

        if (isReplace)
        {
            var existingGroups = await db.QuestionGroups
                .Include(g => g.QuestionGroups)
                    .ThenInclude(q => q.QuestionOptionQuestionNavigations)
                .Where(g => g.ExamId == examId && g.SectionId == request.SectionId)
                .ToListAsync(cancellationToken);

            var existingQuestionIds = existingGroups.SelectMany(g => g.QuestionGroups.Select(q => q.QuestionId)).ToList();

            var hasAnswers = await db.AttemptAnswers.AnyAsync(a => existingQuestionIds.Contains(a.QuestionId), cancellationToken)
                || await db.FreeResponses.AnyAsync(r => existingQuestionIds.Contains(r.QuestionId), cancellationToken);

            if (hasAnswers)
            {
                throw new AdminExamException(ApiError.Codes.QuestionHasAttemptAnswers, "Cannot replace section content because completed or active learner attempts exist.", 400);
            }

            foreach (var g in existingGroups)
            {
                foreach (var q in g.QuestionGroups)
                {
                    var saved = await db.SavedQuestions.Where(s => s.QuestionId == q.QuestionId).ToListAsync(cancellationToken);
                    if (saved.Count > 0) db.SavedQuestions.RemoveRange(saved);

                    var pa = await db.PracticeAnswers.Where(p => p.QuestionId == q.QuestionId).ToListAsync(cancellationToken);
                    if (pa.Count > 0) db.PracticeAnswers.RemoveRange(pa);

                    db.QuestionOptions.RemoveRange(q.QuestionOptionQuestionNavigations);
                    db.Questions.Remove(q);
                }
                db.QuestionGroups.Remove(g);
            }

            await db.SaveChangesAsync(cancellationToken);
            currentDisplayOrder = 1;
        }
        else
        {
            var maxGroupOrder = await db.QuestionGroups
                .Where(g => g.ExamId == examId && g.SectionId == request.SectionId)
                .Select(g => (int?)g.DisplayOrder)
                .MaxAsync(cancellationToken) ?? 0;

            var maxQuestionOrder = await db.Questions
                .Where(q => q.ExamId == examId && q.SectionId == request.SectionId)
                .Select(q => (int?)q.DisplayOrder)
                .MaxAsync(cancellationToken) ?? 0;

            currentDisplayOrder = Math.Max(maxGroupOrder, maxQuestionOrder) + 1;
        }

        var categories = await db.QuestionCategories
            .Where(c => c.CertId == exam.CertId)
            .ToDictionaryAsync(c => c.Code, c => c.CategoryId, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var groupsCreated = 0;
        var questionsCreated = 0;

        QuestionGroup? activeGroup = null;
        string? activePassage = null;
        int? activeGroupNum = null;

        foreach (var q in request.Questions)
        {
            var needsNewGroup = activeGroup == null ||
                (!string.IsNullOrWhiteSpace(q.Passage) && !string.Equals(activePassage, q.Passage, StringComparison.Ordinal)) ||
                (q.GroupNumber.HasValue && q.GroupNumber != activeGroupNum);

            if (needsNewGroup)
            {
                activeGroup = new QuestionGroup
                {
                    ExamId = examId,
                    CertId = exam.CertId,
                    SectionId = request.SectionId,
                    DisplayOrder = currentDisplayOrder,
                    Passage = q.Passage,
                    Transcript = q.Transcript,
                    UnitNumber = 1
                };
                db.QuestionGroups.Add(activeGroup);
                await db.SaveChangesAsync(cancellationToken);
                groupsCreated++;

                activePassage = q.Passage;
                activeGroupNum = q.GroupNumber;
            }

            int? categoryId = null;
            if (!string.IsNullOrWhiteSpace(q.CategoryCode) && categories.TryGetValue(q.CategoryCode, out var cid))
            {
                categoryId = cid;
            }

            var qType = q.QuestionType?.ToLowerInvariant() ?? "mcq";

            var question = new Question
            {
                ExamId = examId,
                SectionId = request.SectionId,
                SkillId = section.SkillId,
                GroupId = activeGroup!.GroupId,
                Stem = q.Stem.Trim(),
                DifficultyLevel = q.DifficultyLevel is >= 1 and <= 5 ? q.DifficultyLevel : 3,
                QuestionType = qType,
                Explanation = q.Explanation?.Trim(),
                CategoryId = categoryId,
                PrepSeconds = q.PrepSeconds,
                ResponseSeconds = q.ResponseSeconds,
                MinWords = q.MinWords,
                MaxWords = q.MaxWords,
                MaxSelections = q.MaxSelections,
                AllowNumber = q.AllowNumber,
                AcceptedAnswersJson = q.AcceptedAnswersJson,
                RequiredKeywords = q.RequiredKeywords?.Trim(),
                Hint = q.Hint?.Trim(),
                DisplayOrder = currentDisplayOrder
            };

            db.Questions.Add(question);
            await db.SaveChangesAsync(cancellationToken);
            questionsCreated++;

            if (q.Options != null && q.Options.Count > 0)
            {
                foreach (var opt in q.Options)
                {
                    var option = new QuestionOption
                    {
                        QuestionId = question.QuestionId,
                        QuestionType = question.QuestionType,
                        Label = opt.Label.Trim().ToUpperInvariant(),
                        Text = opt.Text.Trim(),
                        IsCorrect = opt.IsCorrect,
                        Rationale = opt.Rationale?.Trim()
                    };
                    db.QuestionOptions.Add(option);
                }
            }

            currentDisplayOrder++;
        }

        exam.UpdatedAt = clock?.GetUtcNow() ?? DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return new QuickIngestCommitResultDto(
            Success: true,
            QuestionsCommitted: questionsCreated,
            GroupsCreated: groupsCreated,
            Message: $"Successfully committed {questionsCreated} questions to section '{section.Name}'.");
    }
}
