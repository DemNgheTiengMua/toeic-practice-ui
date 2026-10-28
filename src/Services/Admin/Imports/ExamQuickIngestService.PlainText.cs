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

public sealed partial class ExamQuickIngestService
{

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
}
