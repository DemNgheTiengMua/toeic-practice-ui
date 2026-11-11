// Throwaway probe: the lossless-roundtrip manifest, split into its three questions, each imported on its
// own so the refusal names the one that cannot land.
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class ZzProbeEach(DatabaseFixture database)
{
    [Fact]
    public async Task Probe()
    {
        var dbName = $"ZzE_{Guid.NewGuid():N}"[..22];
        var connectionString = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            await database.SqlCmdFileToDatabaseAsync(
                dbName, Path.Combine(DatabaseFixture.RepoRootPath, "docs", "seed-demo.sql"));

            int certId, adminId;
            await using (var db = DbContextFactory.Create(connectionString))
            {
                certId = await db.Certificates.Select(c => c.CertId).FirstAsync();
                adminId = await db.Users.Select(u => u.UserId).FirstAsync();
            }

            var report = new List<string>();

            foreach (var (label, section, question) in new[]
            {
                ("multi_select", "P5", """
                  { "stem": "Which TWO factors?", "cefrLevel": "B1", "questionType": "multi_select",
                    "itemType": "sentence_cloze", "maxSelections": 2, "displayOrder": 1,
                    "options": [
                      { "label": "A", "text": "Factor A", "isCorrect": true },
                      { "label": "B", "text": "Factor B", "isCorrect": false },
                      { "label": "C", "text": "Factor C", "isCorrect": true },
                      { "label": "D", "text": "Factor D", "isCorrect": false } ] }
                """),
                ("inline_completion", "P5", """
                  { "stem": "The company was established in _____.", "cefrLevel": "A2",
                    "questionType": "inline_completion", "itemType": "sentence_cloze",
                    "allowNumber": true, "maxWords": 2,
                    "acceptedAnswers": ["1984", "nineteen eighty-four"], "displayOrder": 2 }
                """),
                ("free_response", "W2", """
                  { "stem": "Write a response.", "cefrLevel": "B2", "questionType": "free_response",
                    "itemType": "transactional_correspondence", "responseSeconds": 900,
                    "minWords": 100, "maxWords": 200, "displayOrder": 1 }
                """),
            })
            {
                var manifest = $$"""
                { "version": "1.0",
                  "exam": { "title": "Probe {{label}} title", "purpose": "mock",
                            "deliveryMode": "practice", "durationMinutes": 90, "questionCount": 1 },
                  "sections": [ { "sectionCode": "{{section}}",
                    "groups": [ { "displayOrder": 1, "passage": "A test passage.",
                                  "questions": [ {{question}} ] } ] } ] }
                """.Replace("{{label}}", label)
                   .Replace("{{section}}", section)
                   .Replace("Probe {{label}} title", "Probe " + label + " " + Guid.NewGuid().ToString("N")[..8]);

                var result = await new QuestionImportService(
                    DbContextFactory.Create(connectionString), new RecordingFileStore(), TimeProvider.System)
                    .ImportManifestAsync(adminId, certId, manifest);

                await using var read = DbContextFactory.Create(connectionString);
                var errors = await read.ImportLogs.AsNoTracking()
                    .Where(l => l.BatchId == result.BatchId && l.Level == "error")
                    .Select(l => l.Message)
                    .ToListAsync();

                report.Add($"{label}: {result.Status}" +
                    (errors.Count == 0 ? "" : " -> " + string.Join(" | ", errors)));
            }

            Assert.Fail(string.Join("\n", report));
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }
}
