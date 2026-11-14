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

        // #244 / D21: the payload the narrative is allowed to describe. It replaces a band string, so
        // the model reads levels, not a score range that happens to be spelled like a level.
        if (request.Facts is { } facts)
        {
            prompt.AppendLine("[STORED FACTS - THE ONLY LEVELS, SKILLS AND FIGURES YOU MAY NAME]");
            prompt.AppendLine(facts.Describe());
            prompt.AppendLine("A skill marked 'not measured' has no level. Never give it one, never call it strong or weak, and never make it your weakest skill.");
            prompt.AppendLine("Never name a skill or modality that is absent from the list above - this certificate does not examine it.");
            prompt.AppendLine();
        }

        if (request.TopicAccuracies is { Count: > 0 })
        {
            var sortedTopics = request.TopicAccuracies.OrderBy(c => c.AccuracyPercentage).ToList();
            prompt.AppendLine("[ACCURACY BY TOPIC AND LEVEL - SORTED FROM WEAKEST TO STRONGEST]");
            foreach (var topic in sortedTopics.Take(10))
            {
                var tag = topic.AccuracyPercentage < 60 ? "[CRITICAL FOCUS]" : (topic.AccuracyPercentage < 80 ? "[MODERATE GAP]" : "[SOLID STRENGTH]");
                prompt.AppendLine($"- {tag} {topic.TopicName} ({topic.TopicCode}) at {topic.LevelCode}: {topic.Correct}/{topic.Answered} ({topic.AccuracyPercentage}%)");
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
            prompt.AppendLine("[QUALITATIVE ERROR FINGERPRINTS & SECOND-GUESSING SIGNALS (TOP 5)]");
            foreach (var fp in request.ErrorFingerprints.Take(5))
            {
                var hesitation = fp.WasFlagged ? " [FLAGGED / HESITATION DETECTED]" : "";
                prompt.AppendLine($"- Stem: \"{fp.Stem}\"{hesitation}");
                prompt.AppendLine($"  Learner Chose: \"{fp.ChosenOptionText}\"");
                prompt.AppendLine($"  Correct Answer: \"{fp.CorrectOptionText}\"");
                if (!string.IsNullOrEmpty(fp.TopicName))
                    prompt.AppendLine($"  Topic: {fp.TopicName}");
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
        prompt.AppendLine("  \"narrative\": \"<concise Vietnamese diagnostic narrative (3 to 4 sentences) grounded in the learner's Certificate, CurrentBand -> TargetBand, scaled scores, weakest/strongest topics, and error fingerprints>\",");
        prompt.AppendLine("  \"trend\": \"Improving\" | \"Steady\" | \"Declining\",");
        prompt.AppendLine("  \"strengths\": [\"<top strength with accuracy %>\"],");
        prompt.AppendLine("  \"weaknesses\": [\"<core weakness with accuracy % or error pattern>\"],");
        prompt.AppendLine("  \"recommendations\": [");
        prompt.AppendLine("    {");
        prompt.AppendLine("      \"text\": \"<prescriptive, concrete practice directive in Vietnamese citing the exact section/topic name>\",");
        prompt.AppendLine("      \"severity\": \"High\" | \"Medium\" | \"Low\"");
        prompt.AppendLine("    }");
        prompt.AppendLine("  ]");
        prompt.AppendLine("}");
        prompt.AppendLine("Output JSON only, no markdown formatting.");
        return prompt.ToString();
    }
}
