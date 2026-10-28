using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Microsoft.Extensions.Logging;

namespace Services;

public sealed partial class ClaudeAiInsightGenerator
{
    private static string BuildPrompt(AiInsightGenerationRequest request)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("Analyze the learner's test performance and generate an insightful, highly specific pedagogical diagnostic review.");
        prompt.AppendLine();
        prompt.AppendLine("[LEARNER PROFILE & TARGET]");
        prompt.AppendLine($"Certificate: {request.CertName} (ID: {request.CertId})");
        prompt.AppendLine($"Activity / Source: {request.ActivityTitle}");
        prompt.AppendLine($"Current Band / Score: {request.CurrentBandCode ?? "N/A"} ({request.ScaledScore})");
        prompt.AppendLine($"Target Band / Goal: {request.TargetBandCode ?? "Not specified"} ({request.TargetBandName})");
        var overallAccuracy = request.AnsweredCount > 0 ? (int)Math.Round((double)request.CorrectCount / request.AnsweredCount * 100) : 0;
        prompt.AppendLine($"Overall Accuracy: {request.CorrectCount} correct out of {request.AnsweredCount} answered ({overallAccuracy}%) across {request.TotalQuestions} questions");
        prompt.AppendLine();

        if (request.CategoryAccuracies is { Count: > 0 })
        {
            var sortedCats = request.CategoryAccuracies.OrderBy(c => c.AccuracyPercentage).ToList();
            prompt.AppendLine("[ACCURACY BY QUESTION CATEGORY - SORTED FROM WEAKEST TO STRONGEST]");
            foreach (var cat in sortedCats.Take(10))
            {
                var tag = cat.AccuracyPercentage < 60 ? "[CRITICAL FOCUS]" : (cat.AccuracyPercentage < 80 ? "[MODERATE GAP]" : "[SOLID STRENGTH]");
                prompt.AppendLine($"- {tag} {cat.CategoryName} ({cat.CategoryCode}): {cat.Correct}/{cat.Answered} ({cat.AccuracyPercentage}%)");
            }
            prompt.AppendLine();
        }

        if (request.SectionAccuracies is { Count: > 0 })
        {
            var sortedSecs = request.SectionAccuracies.OrderBy(s => s.AccuracyPercentage).ToList();
            prompt.AppendLine("[ACCURACY BY SECTION - SORTED FROM LOWEST ACCURACY]");
            foreach (var sec in sortedSecs.Take(8))
            {
                prompt.AppendLine($"- {sec.SectionName} ({sec.SectionCode}): {sec.Correct}/{sec.Answered} ({sec.AccuracyPercentage}%)");
            }
            prompt.AppendLine();
        }

        if (request.ErrorFingerprints is { Count: > 0 })
        {
            prompt.AppendLine("[QUALITATIVE ERROR FINGERPRINTS & SECOND-GUESSING SIGNALS]");
            foreach (var fp in request.ErrorFingerprints)
            {
                var hesitation = fp.WasFlagged ? " [FLAGGED / HESITATION DETECTED]" : "";
                prompt.AppendLine($"- Stem: \"{fp.Stem}\"{hesitation}");
                prompt.AppendLine($"  Learner Chose: \"{fp.ChosenOptionText}\"");
                prompt.AppendLine($"  Correct Answer: \"{fp.CorrectOptionText}\"");
                if (!string.IsNullOrEmpty(fp.CategoryName))
                    prompt.AppendLine($"  Category: {fp.CategoryName}");
                if (!string.IsNullOrEmpty(fp.Explanation))
                    prompt.AppendLine($"  Trap Rationale: {fp.Explanation}");
            }
            prompt.AppendLine();
        }

        if (request.WritingEvidences is { Count: > 0 })
        {
            prompt.AppendLine("[LEARNER WRITING EXCERPTS & RUBRIC EVALUATION]");
            foreach (var we in request.WritingEvidences)
            {
                prompt.AppendLine($"- Prompt: \"{we.Stem}\" ({we.WordCount} words)");
                if (!string.IsNullOrEmpty(we.ResponseText))
                {
                    var snippet = we.ResponseText.Length > 300 ? we.ResponseText[..300] + "..." : we.ResponseText;
                    prompt.AppendLine($"  Learner Excerpt: \"{snippet}\"");
                }
                if (we.Scores.Count > 0)
                {
                    var scoreList = string.Join(", ", we.Scores.Select(s => $"{s.CriterionName}: {s.Score}/{s.MaxScore}"));
                    prompt.AppendLine($"  Rubric Scores: {scoreList}");
                }
                if (!string.IsNullOrEmpty(we.AiComments))
                {
                    prompt.AppendLine($"  Examiner Comments: {we.AiComments}");
                }
            }
            prompt.AppendLine();
        }

        prompt.AppendLine("Return a strictly valid JSON object with the following schema:");
        prompt.AppendLine("{");
        prompt.AppendLine("  \"narrative\": \"<3 to 4 detailed sentences: (1) Current standing and concrete score gap to target band; (2) Top strength category/section cited by name with accuracy; (3) Primary vulnerability category cited with exact error rate and diagnosed linguistic root cause; (4) Immediate tactical focus module to drill next>\",");
        prompt.AppendLine("  \"trend\": \"Improving\" | \"Steady\" | \"Declining\",");
        prompt.AppendLine("  \"recommendations\": [");
        prompt.AppendLine("    {");
        prompt.AppendLine("      \"text\": \"<prescriptive, concrete practice directive citing the exact section/category name and specific study method>\",");
        prompt.AppendLine("      \"severity\": \"High\" | \"Medium\" | \"Low\"");
        prompt.AppendLine("    }");
        prompt.AppendLine("  ]");
        prompt.AppendLine("}");
        prompt.AppendLine("Output JSON only, no markdown formatting.");
        return prompt.ToString();
    }
}
