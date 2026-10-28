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
}
