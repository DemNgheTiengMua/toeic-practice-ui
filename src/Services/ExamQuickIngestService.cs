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
}
