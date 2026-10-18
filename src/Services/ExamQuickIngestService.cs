using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Contracts;

namespace Services;

public interface IExamQuickIngestService
{
    QuickIngestParseResultDto Parse(QuickIngestParseRequest request);
}

public sealed partial class ExamQuickIngestService : IExamQuickIngestService
{
    [GeneratedRegex(@"^\s*(?:(?:Question|Câu|Q)\s*\d+[\.:\)]?|\d+[\.:\)])\s*(?<stem>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex QuestionHeaderRegex();

    [GeneratedRegex(@"^\s*(?:\*\s*)?(?:\(([A-Za-z])\)|\b([A-Za-z])[\.\)])\s*(.*)$")]
    private static partial Regex OptionLineRegex();

    [GeneratedRegex(@"^\s*(?:ANSWER|ĐÁP ÁN|DAP AN|KEY)\s*[:=\-]\s*([A-Za-z])\b", RegexOptions.IgnoreCase)]
    private static partial Regex AnswerKeyRegex();

    [GeneratedRegex(@"^\s*(?:EXPLANATION|GIẢI THÍCH|GIAI THICH)\s*[:=\-]?\s*(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex ExplanationRegex();

    [GeneratedRegex(@"^\s*(?:CATEGORY|CHỦ ĐỀ|CHU DE)\s*[:=\-]?\s*(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex CategoryRegex();

    public QuickIngestParseResultDto Parse(QuickIngestParseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.RawText))
        {
            return new QuickIngestParseResultDto(
                Success: false,
                TotalQuestions: 0,
                ValidQuestions: 0,
                ErrorCount: 1,
                Questions: [],
                GlobalIssues: [new QuickIngestIssueDto(1, "error", "Input text cannot be empty.")]);
        }

        var text = request.RawText.Trim();

        // Check if text is JSON
        if (text.StartsWith('[') || (text.StartsWith('{') && (text.Contains("\"questions\"") || text.Contains("\"stem\""))))
        {
            try
            {
                return ParseJson(text);
            }
            catch (JsonException ex)
            {
                // If it looks like JSON but has syntax error, report line number error
                return new QuickIngestParseResultDto(
                    Success: false,
                    TotalQuestions: 0,
                    ValidQuestions: 0,
                    ErrorCount: 1,
                    Questions: [],
                    GlobalIssues: [new QuickIngestIssueDto((int)(ex.LineNumber ?? 1), "error", $"Invalid JSON syntax: {ex.Message}")]);
            }
        }

        return ParsePlainText(request.RawText);
    }

    private static QuickIngestParseResultDto ParsePlainText(string rawText)
    {
        var rawLines = rawText.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        var parsedQuestions = new List<QuickIngestParsedQuestionDto>();
        var globalIssues = new List<QuickIngestIssueDto>();

        // We divide the lines into Question Blocks
        // A block begins when a Question header is matched, or when after an explanation/options a new stem is encountered.
        var blocks = SegmentLinesIntoBlocks(rawLines);

        if (blocks.Count == 0)
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

        var qIndex = 1;
        foreach (var block in blocks)
        {
            var parsed = ParseSingleBlock(block, qIndex);
            parsedQuestions.Add(parsed);
            qIndex++;
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

    private static List<QuestionBlock> SegmentLinesIntoBlocks(string[] rawLines)
    {
        var indexedLines = new List<RawLine>();
        for (var i = 0; i < rawLines.Length; i++)
        {
            indexedLines.Add(new RawLine { LineNumber = i + 1, Text = rawLines[i] });
        }

        var blocks = new List<QuestionBlock>();
        QuestionBlock? currentBlock = null;
        var hasSeenOptionsInCurrentBlock = false;

        for (var i = 0; i < indexedLines.Count; i++)
        {
            var line = indexedLines[i];
            var trimmed = line.Text.Trim();

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                // Blank line might separate questions if options have already been seen
                continue;
            }

            var qMatch = QuestionHeaderRegex().Match(trimmed);
            var isExplicitHeader = qMatch.Success;

            // Also check if line looks like a stem following options/explanation of previous block
            var optMatch = OptionLineRegex().Match(trimmed);
            var isAnswerKey = AnswerKeyRegex().IsMatch(trimmed);
            var isExplanation = ExplanationRegex().IsMatch(trimmed);
            var isCategory = CategoryRegex().IsMatch(trimmed);

            var isOptionLike = optMatch.Success && !isExplicitHeader && !isAnswerKey && !isExplanation && !isCategory;

            if (isOptionLike)
            {
                hasSeenOptionsInCurrentBlock = true;
            }

            if (isExplicitHeader || (hasSeenOptionsInCurrentBlock && !isOptionLike && !isAnswerKey && !isExplanation && !isCategory && IsLikelyNextQuestionStart(indexedLines, i)))
            {
                if (currentBlock != null && currentBlock.Lines.Count > 0)
                {
                    blocks.Add(currentBlock);
                }

                currentBlock = new QuestionBlock();
                hasSeenOptionsInCurrentBlock = false;
            }
            else if (currentBlock == null)
            {
                currentBlock = new QuestionBlock();
                hasSeenOptionsInCurrentBlock = false;
            }

            currentBlock.Lines.Add(line);
        }

        if (currentBlock != null && currentBlock.Lines.Count > 0)
        {
            blocks.Add(currentBlock);
        }

        return blocks;
    }

    private static bool IsLikelyNextQuestionStart(List<RawLine> lines, int currentIndex)
    {
        // If there's an option line following within the next 3 non-empty lines, this line is likely a stem
        var nonBlankCount = 0;
        for (var j = currentIndex + 1; j < lines.Count && nonBlankCount < 3; j++)
        {
            var nextTrimmed = lines[j].Text.Trim();
            if (string.IsNullOrWhiteSpace(nextTrimmed)) continue;

            nonBlankCount++;
            if (OptionLineRegex().IsMatch(nextTrimmed))
            {
                return true;
            }
        }
        return false;
    }

    private static QuickIngestParsedQuestionDto ParseSingleBlock(QuestionBlock block, int questionIndex)
    {
        var issues = new List<QuickIngestIssueDto>();
        var stemLines = new List<string>();
        var options = new List<QuickIngestParsedOptionDto>();
        var explanationLines = new List<string>();
        string? categoryCode = null;
        string? trailingAnswerKey = null;
        var trailingKeyLineNumber = 0;

        var inExplanation = false;

        for (var i = 0; i < block.Lines.Count; i++)
        {
            var lineObj = block.Lines[i];
            var line = lineObj.Text.Trim();
            var lineNum = lineObj.LineNumber;

            if (string.IsNullOrWhiteSpace(line)) continue;

            // Check Category
            var catMatch = CategoryRegex().Match(line);
            if (catMatch.Success)
            {
                categoryCode = catMatch.Groups[1].Value.Trim();
                inExplanation = false;
                continue;
            }

            // Check Answer Key
            var ansMatch = AnswerKeyRegex().Match(line);
            if (ansMatch.Success)
            {
                trailingAnswerKey = ansMatch.Groups[1].Value.Trim().ToUpperInvariant();
                trailingKeyLineNumber = lineNum;
                inExplanation = false;
                continue;
            }

            // Check Explanation header
            var expMatch = ExplanationRegex().Match(line);
            if (expMatch.Success)
            {
                inExplanation = true;
                var expText = expMatch.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(expText))
                {
                    explanationLines.Add(expText);
                }
                continue;
            }

            if (inExplanation)
            {
                explanationLines.Add(line);
                continue;
            }

            // Check Option line
            // Check for inline asterisk before label: *A. or *(A)
            var hasLeadingAsterisk = line.StartsWith('*') || line.StartsWith("（*") || line.StartsWith("(*");
            var cleanLineForOption = line.TrimStart('*', ' ');

            var optMatch = OptionLineRegex().Match(line);
            if (optMatch.Success && !line.StartsWith("Question", StringComparison.OrdinalIgnoreCase) && !line.StartsWith("Câu", StringComparison.OrdinalIgnoreCase))
            {
                var label = (optMatch.Groups[1].Success ? optMatch.Groups[1].Value : optMatch.Groups[2].Value).ToUpperInvariant();
                var remainder = optMatch.Groups[3].Value.Trim();

                var isCorrect = false;

                // Check asterisk in front: *A.
                if (hasLeadingAsterisk || line.StartsWith($"*{label}", StringComparison.OrdinalIgnoreCase) || line.StartsWith($"*({label})", StringComparison.OrdinalIgnoreCase))
                {
                    isCorrect = true;
                }

                // Check asterisk at start of remainder: A. *text
                if (remainder.StartsWith('*'))
                {
                    isCorrect = true;
                    remainder = remainder.TrimStart('*', ' ');
                }

                // Check asterisk at end or surrounding: A. *text*
                if (remainder.EndsWith('*'))
                {
                    isCorrect = true;
                    remainder = remainder.TrimEnd('*', ' ');
                }

                // Check for inline rationale: "// Rationale..."
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

            // If options haven't started yet, this line is part of Question header / stem
            if (options.Count == 0)
            {
                var qMatch = QuestionHeaderRegex().Match(line);
                if (qMatch.Success)
                {
                    var stemPart = qMatch.Groups["stem"].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(stemPart))
                    {
                        stemLines.Add(stemPart);
                    }
                }
                else
                {
                    stemLines.Add(line);
                }
            }
            else
            {
                // After options but before explanation: could be multi-line option text
                if (options.Count > 0)
                {
                    var last = options[^1];
                    var combinedText = last.Text + " " + line;
                    options[^1] = new QuickIngestParsedOptionDto(last.Label, combinedText, last.IsCorrect, last.Rationale);
                }
            }
        }

        // If trailing answer key was found, apply it to options
        if (!string.IsNullOrEmpty(trailingAnswerKey))
        {
            var matchedOption = false;
            for (var o = 0; o < options.Count; o++)
            {
                var opt = options[o];
                if (string.Equals(opt.Label, trailingAnswerKey, StringComparison.OrdinalIgnoreCase))
                {
                    options[o] = new QuickIngestParsedOptionDto(opt.Label, opt.Text, true, opt.Rationale);
                    matchedOption = true;
                }
                else if (options.Count(x => x.IsCorrect) == 0)
                {
                    options[o] = new QuickIngestParsedOptionDto(opt.Label, opt.Text, false, opt.Rationale);
                }
            }

            if (!matchedOption)
            {
                issues.Add(new QuickIngestIssueDto(trailingKeyLineNumber, "error", $"Answer key '{trailingAnswerKey}' does not match any parsed option (available: {string.Join(", ", options.Select(x => x.Label))}).", questionIndex));
            }
        }

        var stem = string.Join(" ", stemLines).Trim();
        var explanation = explanationLines.Count > 0 ? string.Join("\n", explanationLines).Trim() : null;

        // Validation rules
        if (string.IsNullOrWhiteSpace(stem))
        {
            issues.Add(new QuickIngestIssueDto(block.StartLineNumber, "error", "Question stem is empty.", questionIndex));
        }

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

        // Check duplicate option labels
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
            IsValid: isValid);
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
                IsValid: isValid));

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
}
