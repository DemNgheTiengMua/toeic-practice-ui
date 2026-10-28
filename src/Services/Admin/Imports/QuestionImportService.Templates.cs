using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Services;

public sealed partial class QuestionImportService
{

    // ---- Dynamic Manifest Template Generation ----

    public async Task<(Stream Stream, string ContentType, string FileName)> GenerateManifestTemplateAsync(int certId, CancellationToken cancellationToken = default)
    {
        var cert = await db.Certificates.AsNoTracking().FirstOrDefaultAsync(c => c.CertId == certId, cancellationToken)
            ?? throw new AdminExamException(ApiError.Codes.CertificateUnavailable, $"Certificate with id {certId} not found.", 404);

        var skills = await db.Skills.AsNoTracking().Where(s => s.CertId == certId).OrderBy(s => s.DisplayOrder).ToListAsync(cancellationToken);
        var sections = await db.Sections.AsNoTracking().Where(s => s.CertId == certId).OrderBy(s => s.DisplayOrder).ToListAsync(cancellationToken);

        var manifestSections = new List<ImportManifestSection>();
        var totalQuestionCount = 0;

        foreach (var skill in skills)
        {
            var skillSections = sections.Where(s => s.SkillId == skill.SkillId).OrderBy(s => s.DisplayOrder).ToList();
            foreach (var sec in skillSections)
            {
                var allowed = ParseAllowedComponents(sec);
                var clusterSize = sec.ClusterSize > 0 ? sec.ClusterSize : 1;
                var placeholderQuestions = new List<ImportManifestQuestion>();

                for (var qIndex = 1; qIndex <= clusterSize; qIndex++)
                {
                    totalQuestionCount++;
                    var qType = allowed.Count > 0 ? allowed[(qIndex - 1) % allowed.Count] : (sec.Modality is "speaking" or "writing" ? "free_response" : "mcq");
                    placeholderQuestions.Add(CreatePlaceholderQuestion(sec, qType, qIndex));
                }

                var group = new ImportManifestGroup
                {
                    UnitNumber = 1,
                    Passage = sec.RequirePassage ? $"[Passage for {sec.Name}] Detailed text or context here." : null,
                    AudioFile = sec.RequireAudio ? "media/sample_audio.mp3" : null,
                    ImageFile = sec.RequireImage ? "media/sample_image.jpg" : null,
                    ImageAlt = sec.RequireImage ? $"Sample image description for {sec.Name}" : null,
                    PrepSeconds = sec.NavigationMode == "SYSTEM_PACED_AUTO" ? 15 : null,
                    ResponseSeconds = sec.NavigationMode == "SYSTEM_PACED_AUTO" ? 45 : null,
                    DisplayOrder = 1,
                    Questions = placeholderQuestions,
                };

                manifestSections.Add(new ImportManifestSection
                {
                    SectionCode = sec.Code,
                    Groups = [group],
                });
            }
        }

        var durationMinutes = skills.Sum(s => s.DurationMinutes ?? 60);
        if (durationMinutes <= 0) durationMinutes = 120;

        var manifest = new ImportManifest
        {
            Schema = "https://raw.githubusercontent.com/extra-efficiency/schemas/main/manifest.schema.json",
            Version = "1.0",
            Exam = new ImportManifestExam
            {
                Title = $"{cert.Code} Practice Exam Template",
                Purpose = "mock",
                DeliveryMode = "strict_exam",
                DurationMinutes = durationMinutes,
                SourceName = $"{cert.Code} Official Template",
                Description = $"Pre-structured skeleton generated dynamically from {cert.Name} section blueprints.",
                QuestionCount = totalQuestionCount,
            },
            Sections = manifestSections,
        };

        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, ImportManifest.Json);
        var stream = new MemoryStream(jsonBytes);
        var fileName = $"{cert.Code.ToLowerInvariant()}_manifest_template.json";
        return (stream, "application/json", fileName);
    }

    private static List<string> ParseAllowedComponents(Section sec)
    {
        if (!string.IsNullOrWhiteSpace(sec.AllowedComponentTypesJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(sec.AllowedComponentTypesJson);
                if (parsed != null && parsed.Count > 0) return parsed;
            }
            catch
            {
            }
        }

        if (sec.Modality is "speaking" or "writing")
        {
            return ["free_response"];
        }

        return sec.OptionCount == 3 ? ["mcq_3"] : ["mcq"];
    }

    private static ImportManifestQuestion CreatePlaceholderQuestion(Section sec, string qType, int displayOrder)
    {
        return qType.ToLowerInvariant() switch
        {
            "free_response" => new ImportManifestQuestion
            {
                Stem = $"[Prompt for {sec.Name}] Respond to the question or task description.",
                DifficultyLevel = 3,
                QuestionType = "free_response",
                PrepSeconds = sec.NavigationMode == "SYSTEM_PACED_AUTO" ? 15 : null,
                ResponseSeconds = sec.Modality == "speaking" ? 45 : 1200,
                MinWords = sec.Modality == "writing" ? 150 : null,
                MaxWords = sec.Modality == "writing" ? 250 : null,
                RequiredKeywords = sec.RequiredKeywordCount > 0
                    ? Enumerable.Range(1, sec.RequiredKeywordCount).Select(k => $"keyword_{k}").ToList()
                    : null,
                DisplayOrder = displayOrder,
                Explanation = "Detailed scoring rationale and rubric expectations.",
                Hint = "Organize thoughts clearly before responding.",
            },

            "multi_select" => new ImportManifestQuestion
            {
                Stem = $"[Question #{displayOrder}] Which TWO of the following statements are supported by the passage?",
                DifficultyLevel = 3,
                QuestionType = "multi_select",
                MaxSelections = 2,
                DisplayOrder = displayOrder,
                Options =
                [
                    new() { Label = "A", Text = "First supported factual claim", IsCorrect = true, Rationale = "Directly verified in paragraph 2." },
                    new() { Label = "B", Text = "Plausible distractor claim", IsCorrect = false, Rationale = "Contradicts paragraph 3." },
                    new() { Label = "C", Text = "Second supported factual claim", IsCorrect = true, Rationale = "Confirmed in final paragraph." },
                    new() { Label = "D", Text = "Unrelated distractor claim", IsCorrect = false, Rationale = "Not mentioned in the text." },
                ],
                Explanation = "Claims A and C are directly substantiated by the stimulus.",
                Hint = "Check each statement against the passage numbers.",
            },

            "inline_completion" => new ImportManifestQuestion
            {
                Stem = $"[Question #{displayOrder}] The new distribution center reduced regional transit times by _____ percent.",
                DifficultyLevel = 3,
                QuestionType = "inline_completion",
                AllowNumber = true,
                MaxWords = 2,
                AcceptedAnswers = ["35", "thirty-five"],
                DisplayOrder = displayOrder,
                Explanation = "The second paragraph explicitly states a 35% reduction.",
                Hint = "Look for numerical percentage metrics.",
            },

            "dropdown" or "drag_drop" => new ImportManifestQuestion
            {
                Stem = $"[Question #{displayOrder}] Match the category label to this item.",
                DifficultyLevel = 3,
                QuestionType = qType.ToLowerInvariant(),
                DisplayOrder = displayOrder,
                Options =
                [
                    new() { Label = "A", Text = "Correct Category Option", IsCorrect = true, Rationale = "Matches standard taxonomy." },
                    new() { Label = "B", Text = "Alternate Category Option", IsCorrect = false, Rationale = "Incorrect classification." },
                    new() { Label = "C", Text = "Tertiary Category Option", IsCorrect = false, Rationale = "Incorrect classification." },
                ],
                Explanation = "Option A correctly classifies the given attribute.",
                Hint = "Review category classifications in stimulus.",
            },

            "mcq_3" => new ImportManifestQuestion
            {
                Stem = $"[Question #{displayOrder}] What was the primary topic discussed by the speakers?",
                DifficultyLevel = 3,
                QuestionType = "mcq_3",
                DisplayOrder = displayOrder,
                Options =
                [
                    new() { Label = "A", Text = "Correct primary topic", IsCorrect = true, Rationale = "Directly stated in initial sentence." },
                    new() { Label = "B", Text = "Secondary mention", IsCorrect = false, Rationale = "Only briefly mentioned." },
                    new() { Label = "C", Text = "Unrelated topic", IsCorrect = false, Rationale = "Not discussed." },
                ],
                Explanation = "The speakers primarily discussed option A.",
                Hint = "Listen for the opening statement.",
            },

            _ => new ImportManifestQuestion
            {
                Stem = $"[Question #{displayOrder}] Which option best completes the sentence?",
                DifficultyLevel = 3,
                QuestionType = "mcq",
                DisplayOrder = displayOrder,
                Options = Enumerable.Range(0, sec.OptionCount is >= 2 and <= 6 ? sec.OptionCount : 4)
                    .Select(i => new ImportManifestOption
                    {
                        Label = ((char)('A' + i)).ToString(),
                        Text = i == 0 ? "Correct answer statement" : $"Distractor statement {(char)('A' + i)}",
                        IsCorrect = i == 0,
                        Rationale = i == 0 ? "Correctly follows grammatical rule." : "Incorrect syntax or distractor.",
                    }).ToList(),
                Explanation = "Option A is the correct answer based on context.",
                Hint = "Consider the grammatical role required in the blank.",
            },
        };
    }
}
