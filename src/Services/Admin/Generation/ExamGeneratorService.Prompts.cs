using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Services;

public sealed partial class ExamGeneratorService
{

    private static string BuildSystemPrompt()
    {
        return """
            You are an expert exam author and psychometrician specializing in standardized English language tests (TOEIC, IELTS, VSTEP).
            Your task is to generate complete, pedagogically rigorous exam packages conforming strictly to the package manifest specification (version 1.0).

            Output format requirements:
            - Respond strictly with valid JSON conforming to the manifest format.
            - Wrap your JSON inside a ```json ... ``` code block.
            - Manifest schema:
              {
                "version": "1.0",
                "exam": {
                  "title": "Exam Title",
                  "purpose": "mock",
                  "durationMinutes": 60,
                  "sourceName": "Claude Sonnet 3.7",
                  "description": "Short description"
                },
                "sections": [
                  {
                    "sectionCode": "P5",
                    "groups": [
                      {
                        "displayOrder": 1,
                        "passage": null,
                        "audioFile": null,
                        "transcript": null,
                        "questions": [
                          {
                            "displayOrder": 1,
                            "stem": "Question stem text...",
                            "difficultyLevel": 3,
                            "questionType": "mcq",
                            "categoryCode": "grammar_prep",
                            "explanation": "Pedagogical explanation of the correct choice and why distractors are wrong.",
                            "hint": "Brief pedagogical hint without giving away the answer.",
                            "options": [
                              { "label": "A", "text": "correct option", "isCorrect": true, "rationale": "Why this is correct." },
                              { "label": "B", "text": "distractor option", "isCorrect": false, "rationale": "Trap rationale." },
                              { "label": "C", "text": "distractor option", "isCorrect": false, "rationale": "Trap rationale." },
                              { "label": "D", "text": "distractor option", "isCorrect": false, "rationale": "Trap rationale." }
                            ]
                          }
                        ]
                      }
                    ]
                  }
                ]
              }

            Strict validation constraints:
            1. Respect OptionCount for each section: if OptionCount=3, author exactly 3 options (A, B, C); if OptionCount=4, author 4 options (A, B, C, D).
            2. For MCQ questions, exactly ONE option must have "isCorrect": true.
            3. For multi_select questions, author at least 2 correct options and include "maxSelections".
            4. For inline_completion questions, provide "acceptedAnswers" array of strings and "maxWords".
            5. For free_response questions with RequiredKeywordCount > 0, provide "requiredKeywords" with the exact count of comma-separated words.
            6. Each option must have non-empty "text" and a non-empty "rationale".
            7. Question stems must be complete and non-empty. Difficulty level must be an integer between 1 and 5.
            8. For reading comprehension sections requiring passage, the group must supply a non-empty "passage".
            9. For listening sections, the group must supply an audio script in "transcript" with explicit dialogue speaker turns.
            10. Category codes must match the valid category codes provided.
            """;
    }

    private static string BuildUserPrompt(
        ExamBlueprintDto blueprint,
        Certificate cert,
        List<Section> certSections,
        List<QuestionCategory> certCategories)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Generate a complete standardized exam package for certificate: {cert.Name} (Code: {cert.Code}).");
        sb.AppendLine($"Title: {(!string.IsNullOrWhiteSpace(blueprint.Title) ? blueprint.Title : $"{cert.Code} Standardized Practice Form")}");
        sb.AppendLine($"Exam Purpose: {blueprint.Purpose ?? "mock"}");
        if (!string.IsNullOrWhiteSpace(blueprint.Skill))
        {
            sb.AppendLine($"Target Skill: {blueprint.Skill}");
        }
        sb.AppendLine($"Target CEFR Band: {blueprint.TargetBand ?? "B2"}");
        if (blueprint.TargetBandDistribution is { Count: > 0 })
        {
            var dist = string.Join(", ", blueprint.TargetBandDistribution.Select(kv => $"{kv.Key}: {(int)Math.Round(kv.Value * 100, MidpointRounding.AwayFromZero)}%"));
            sb.AppendLine($"Target CEFR Band Distribution: {dist}");
        }
        sb.AppendLine($"Default Difficulty Level: {blueprint.DifficultyLevel ?? 3} (1=easiest to 5=hardest)");
        if (blueprint.DurationMinutes.HasValue && blueprint.DurationMinutes.Value > 0)
        {
            sb.AppendLine($"Duration: {blueprint.DurationMinutes.Value} minutes");
        }

        sb.AppendLine();
        sb.AppendLine("Sections to generate (respect each section's blueprint):");
        foreach (var sec in blueprint.Sections!)
        {
            var matchSec = certSections.First(s => s.Code.Equals(sec.SectionCode.Trim(), StringComparison.OrdinalIgnoreCase));
            sb.AppendLine($"- Section Code: '{matchSec.Code}' ({matchSec.Name}, Modality: {matchSec.Modality})");
            sb.AppendLine($"  Questions to author: {sec.QuestionCount}");
            sb.AppendLine($"  OptionCount: {matchSec.OptionCount}");
            sb.AppendLine($"  ClusterSize: {matchSec.ClusterSize}");
            sb.AppendLine($"  RequirePassage: {matchSec.RequirePassage}");
            if (!string.IsNullOrWhiteSpace(matchSec.AllowedComponentTypesJson))
            {
                sb.AppendLine($"  Allowed Component Types: {matchSec.AllowedComponentTypesJson}");
            }
            if (matchSec.RequiredKeywordCount > 0)
            {
                sb.AppendLine($"  Required Keyword Count: {matchSec.RequiredKeywordCount}");
            }
            if (sec.DifficultyLevel.HasValue)
            {
                sb.AppendLine($"  Target Difficulty: {sec.DifficultyLevel.Value}");
            }
            if (!string.IsNullOrWhiteSpace(sec.CategoryCode))
            {
                sb.AppendLine($"  Target Category: '{sec.CategoryCode}'");
            }
            if (sec.Topics is { Count: > 0 })
            {
                sb.AppendLine($"  Topics: {string.Join(", ", sec.Topics)}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Valid category codes for this certificate:");
        foreach (var cat in certCategories.Take(15))
        {
            sb.AppendLine($"- {cat.Code}: {cat.Name}");
        }

        sb.AppendLine();
        sb.AppendLine("Generate the manifest JSON now. Output ONLY valid JSON inside ```json ... ```.");
        return sb.ToString();
    }

    private static string BuildSectionSystemPrompt(Section section)
    {
        return $$"""
            You are an expert exam author specializing in standardized tests.
            Your task is to generate questions for section '{{section.Code}}' ({{section.Name}}, Modality: {{section.Modality}}).

            Blueprint constraints:
            - OptionCount: {{section.OptionCount}} (author exactly {{section.OptionCount}} options for MCQ questions: {{(section.OptionCount == 3 ? "A, B, C" : "A, B, C, D")}}).
            - ClusterSize: {{section.ClusterSize}}
            - RequirePassage: {{section.RequirePassage}}
            - RequiredKeywordCount: {{section.RequiredKeywordCount}}
            - AllowedComponentTypes: {{section.AllowedComponentTypesJson ?? "[\"mcq\"]"}}

            Output JSON schema:
            ```json
            {
              "groups": [
                {
                  "passage": "Reading passage if required, else null",
                  "transcript": "Audio dialogue or cue if required, else null",
                  "questions": [
                    {
                      "stem": "Question stem text...",
                      "difficultyLevel": 3,
                      "questionType": "{{(section.OptionCount == 3 ? "mcq_3" : "mcq")}}",
                      "categoryCode": "valid_category_code",
                      "explanation": "Pedagogical explanation...",
                      "hint": "Optional hint...",
                      "maxWords": 2,
                      "maxSelections": 2,
                      "acceptedAnswers": ["accepted answer 1", "accepted answer 2"],
                      "requiredKeywords": "word1, word2",
                      "prepSeconds": 15,
                      "responseSeconds": 60,
                      "minWords": 150,
                      "options": [
                        { "label": "A", "text": "Option text", "isCorrect": true, "rationale": "Rationale" },
                        { "label": "B", "text": "Option text", "isCorrect": false, "rationale": "Rationale" }
                      ]
                    }
                  ]
                }
              ]
            }
            ```

            Respond strictly with valid JSON inside a ```json ... ``` code block.
            """;
    }

    private static string BuildSectionUserPrompt(
        Exam exam,
        Section section,
        int countToGenerate,
        List<QuestionCategory> categories)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Generate {countToGenerate} pedagogically rigorous questions for section '{section.Code}' ({section.Name}) in exam '{exam.Name}'.");
        sb.AppendLine($"Certificate: {exam.Cert.Name} ({exam.Cert.Code})");
        sb.AppendLine($"Modality: {section.Modality}");
        sb.AppendLine($"Target question count: {countToGenerate}");
        sb.AppendLine($"Option count: {section.OptionCount}");
        sb.AppendLine($"Cluster size: {section.ClusterSize}");
        sb.AppendLine($"Require passage: {section.RequirePassage}");
        if (section.RequiredKeywordCount > 0)
        {
            sb.AppendLine($"Mandatory keywords per question: exactly {section.RequiredKeywordCount} comma-separated words in 'requiredKeywords'.");
        }
        if (!string.IsNullOrWhiteSpace(section.AllowedComponentTypesJson))
        {
            sb.AppendLine($"Allowed component types: {section.AllowedComponentTypesJson}");
        }

        if (categories.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Valid category codes:");
            foreach (var cat in categories.Take(12))
            {
                sb.AppendLine($"- {cat.Code}: {cat.Name}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Generate the section questions JSON now inside ```json ... ```.");
        return sb.ToString();
    }

    private static string ExtractJson(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            var end = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (end > 7) return trimmed[7..end].Trim();
        }
        if (trimmed.StartsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            var end = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (end > 3) return trimmed[3..end].Trim();
        }
        var first = trimmed.IndexOf('{');
        var last = trimmed.LastIndexOf('}');
        if (first >= 0 && last > first) return trimmed[first..(last + 1)];
        return trimmed;
    }
}
