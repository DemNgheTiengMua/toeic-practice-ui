using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using BusinessObjects;
using Contracts;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;

namespace Services.Tests;

/// <summary>Question batch import (ADR-0007, issue #2 story 81), driven through the service against
/// the real schema. The three files in docs/samples are the happy path, so the documented format
/// and the importer cannot drift apart.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class QuestionImportServiceTests(DatabaseFixture database)
{
    private static string SamplePath(string name) => Path.Combine(DatabaseFixture.RepoRootPath, "docs", "samples", name);

    private static JsonNode Sample(string name) => JsonNode.Parse(File.ReadAllText(SamplePath(name)))!;

    private QuestionImportService CreateService(RecordingFileStore? store = null) =>
        new(DbContextFactory.Create(database.ConnectionString), store ?? new RecordingFileStore(), TimeProvider.System);

    /// <summary>A private certificate shaped like TOEIC (the codes the samples use) and an admin.</summary>
    private async Task<(int CertId, int AdminId)> SeedAsync()
    {
        var code = $"IM{Guid.NewGuid():N}"[..18];
        var email = $"import-{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'admin') INSERT INTO Roles (Code, Name) VALUES ('admin', N'Admin');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, EmailVerifiedAt)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'admin'), '{email}', 0x01, N'Import Admin', SYSDATETIMEOFFSET());

            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{code}', N'Import cert', 1);
            DECLARE @c INT = SCOPE_IDENTITY();
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@c, 'LIST', N'Listening', 'listening', 1), (@c, 'READ', N'Reading', 'reading', 2),
              (@c, 'SPEA', N'Speaking', 'speaking', 3), (@c, 'WRIT', N'Writing', 'writing', 4);
            DECLARE @l INT = (SELECT SkillId FROM Skills WHERE CertId = @c AND Code = 'LIST');
            DECLARE @r INT = (SELECT SkillId FROM Skills WHERE CertId = @c AND Code = 'READ');
            DECLARE @s INT = (SELECT SkillId FROM Skills WHERE CertId = @c AND Code = 'SPEA');
            DECLARE @w INT = (SELECT SkillId FROM Skills WHERE CertId = @c AND Code = 'WRIT');
            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, DisplayOrder) VALUES
              (@l, @c, 'listening', 'P1', N'Photographs', 4, 1), (@l, @c, 'listening', 'P2', N'Question-Response', 3, 2),
              (@r, @c, 'reading', 'P5', N'Incomplete Sentences', 4, 1), (@r, @c, 'reading', 'P7', N'Reading Comprehension', 4, 2),
              (@s, @c, 'speaking', 'S3', N'Respond to questions', 4, 1), (@w, @c, 'writing', 'W2', N'Respond to a written request', 4, 1);
            INSERT INTO QuestionCategories (CertId, Code, Name) VALUES
              (@c, 'grammar_prep', N'Preposition'), (@c, 'reading_inference', N'Inference'),
              (@c, 'listening_photo', N'Photographs'), (@c, 'listening_detail', N'Detail');
            INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, MaxScore, Weight, DisplayOrder) VALUES
              (@c, @s, 'PRON', N'Pronunciation', 3, 1, 1), (@c, @s, 'INT', N'Intonation', 3, 1, 2),
              (@c, @s, 'GRAM', N'Grammar', 3, 1, 3), (@c, @s, 'REL', N'Relevance', 3, 1, 4),
              (@c, @w, 'GRAM', N'Grammar', 5, 1, 1), (@c, @w, 'VOCAB', N'Vocabulary', 5, 1, 2),
              (@c, @w, 'ORG', N'Organization', 5, 1, 3), (@c, @w, 'REL', N'Relevance', 5, 1, 4);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var certId = await db.Certificates.Where(c => c.Code == code).Select(c => c.CertId).SingleAsync();
        var adminId = await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
        return (certId, adminId);
    }

    private static Stream Utf8(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    private static Stream Zip(params (string Name, byte[] Bytes)[] entries)
    {
        var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in entries)
            {
                using var entry = zip.CreateEntry(name).Open();
                entry.Write(bytes);
            }
        }

        buffer.Position = 0;
        return buffer;
    }

    private Task<AdminImportResultDto> ImportJsonAsync(QuestionImportService service, int certId, int adminId, JsonNode manifest, string fileName = "manifest.json") =>
        service.ImportAsync(adminId, certId, fileName, Utf8(manifest.ToJsonString()));

    private async Task<List<ImportLog>> LogsAsync(int batchId)
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.ImportLogs.Where(l => l.BatchId == batchId).OrderBy(l => l.LogId).ToListAsync();
    }

    private async Task<int> ExamCountAsync(int certId)
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Exams.CountAsync(e => e.CertId == certId);
    }

    /// <summary>The first MCQ question of the first group of the first section.</summary>
    private static JsonNode FirstQuestion(JsonNode manifest) => manifest["sections"]![0]!["groups"]![0]!["questions"]![0]!;

    [Fact]
    public async Task Text_only_sample_creates_a_draft_exam_with_its_questions_options_and_categories()
    {
        var (certId, adminId) = await SeedAsync();

        var result = await ImportJsonAsync(CreateService(), certId, adminId, Sample("sample-reading-mcq.json"), "sample-reading-mcq.json");

        Assert.Equal(ImportBatchStatusDto.Done, result.Status);
        Assert.Equal(0, result.ErrorCount);
        Assert.Equal(2, result.QuestionCount);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var batch = await db.ImportBatches.SingleAsync(b => b.BatchId == result.BatchId);
        Assert.Equal("done", batch.Status);
        Assert.Equal("sample-reading-mcq.json", batch.SourceName);
        Assert.Equal(adminId, batch.UploadedBy);
        Assert.NotNull(batch.CompletedAt);

        var exam = await db.Exams.SingleAsync(e => e.ExamId == result.ExamId);
        Assert.Equal("TOEIC Reading Sample (Part 5 & Part 7)", exam.Name);
        Assert.Equal("draft", exam.Status);
        Assert.Equal("mock", exam.Purpose);
        Assert.Equal(45, exam.DurationMinutes);

        var questions = await db.Questions.Include(q => q.Category).Include(q => q.QuestionOptionQuestionNavigations)
            .Where(q => q.ExamId == exam.ExamId).OrderBy(q => q.SectionId).ToListAsync();
        Assert.Equal(2, questions.Count);
        Assert.All(questions, q => Assert.Equal(4, q.QuestionOptionQuestionNavigations.Count));
        Assert.All(questions, q => Assert.Single(q.QuestionOptionQuestionNavigations, o => o.IsCorrect));
        Assert.Equal("grammar_prep", questions[0].Category!.Code);
        Assert.NotNull(questions[0].Hint);
        Assert.All(questions[0].QuestionOptionQuestionNavigations, o => Assert.NotNull(o.Rationale));

        var passage = await db.QuestionGroups.Where(g => g.ExamId == exam.ExamId && g.Passage != null).SingleAsync();
        Assert.StartsWith("Notice to all building tenants", passage.Passage);
    }

    [Fact]
    public async Task Free_response_sample_creates_a_mock_exam_with_timing_word_limits_and_known_criteria()
    {
        var (certId, adminId) = await SeedAsync();

        var result = await ImportJsonAsync(CreateService(), certId, adminId, Sample("sample-free-response.json"));

        Assert.Equal(ImportBatchStatusDto.Done, result.Status);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var questions = await db.Questions.Where(q => q.ExamId == result.ExamId).OrderBy(q => q.ResponseSeconds).ToListAsync();
        Assert.Equal(2, questions.Count);
        Assert.All(questions, q => Assert.Equal("free_response", q.QuestionType));
        Assert.Equal((45, 60), (questions[0].PrepSeconds!.Value, questions[0].ResponseSeconds!.Value));
        Assert.Equal((100, 200), (questions[1].MinWords!.Value, questions[1].MaxWords!.Value));
        Assert.Null(questions[0].CategoryId);
    }

    [Fact]
    public async Task Zip_sample_stores_its_media_and_points_the_groups_at_the_stored_files()
    {
        var (certId, adminId) = await SeedAsync();
        var store = new RecordingFileStore();

        await using var package = File.OpenRead(SamplePath("sample-listening-package.zip"));
        var result = await CreateService(store).ImportAsync(adminId, certId, "sample-listening-package.zip", package);

        Assert.Equal(ImportBatchStatusDto.Done, result.Status);
        Assert.Equal(3, store.Files.Count);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var groups = await db.QuestionGroups.Where(g => g.ExamId == result.ExamId).OrderBy(g => g.DisplayOrder).ToListAsync();
        Assert.Equal(2, groups.Count);
        // The column holds the URL the site serves the file at; the store holds it under imports/.
        Assert.All(groups, g => Assert.StartsWith($"/media/imports/{result.BatchId}/", g.AudioPath));
        Assert.All(groups, g => Assert.True(store.Files.ContainsKey(g.AudioPath!["/media/".Length..]), $"{g.AudioPath} not stored"));
        Assert.True(store.Files.ContainsKey(groups[0].ImagePath!["/media/".Length..]));
        Assert.Equal("A person writing notes in a notebook at an office desk.", groups[0].ImageAlt);
        Assert.Equal(2, store.Files.Values.Count(bytes => bytes.Length == 16044)); // both .wav clips intact
    }

    [Fact]
    public async Task Invalid_manifest_records_every_problem_creates_nothing_and_marks_the_batch_failed()
    {
        var (certId, adminId) = await SeedAsync();
        var manifest = Sample("sample-reading-mcq.json");
        var first = FirstQuestion(manifest);
        first["options"]![1]!["isCorrect"] = true;                 // two correct options
        first["options"]![2]!["label"] = "A";                      // duplicate label
        first["categoryCode"] = "no_such_category";
        manifest["sections"]![1]!["sectionCode"] = "P9";            // unknown section
        manifest["sections"]![0]!["groups"]![0]!["audioStartMs"] = 500; // half an audio slice

        var result = await ImportJsonAsync(CreateService(), certId, adminId, manifest);

        Assert.Equal(ImportBatchStatusDto.Failed, result.Status);
        Assert.Null(result.ExamId);
        Assert.Equal(0, await ExamCountAsync(certId));

        var errors = (await LogsAsync(result.BatchId)).Where(l => l.Level == "error").Select(l => l.Message).ToList();
        Assert.Equal(5, result.ErrorCount);
        Assert.Contains(errors, m => m.Contains("exactly one correct option"));
        Assert.Contains(errors, m => m.Contains("Option label 'A' is used twice"));
        Assert.Contains(errors, m => m.Contains("Unknown category 'no_such_category'"));
        Assert.Contains(errors, m => m.Contains("Unknown section 'P9'"));
        Assert.Contains(errors, m => m.Contains("audioStartMs and audioEndMs"));

        await using var db = DbContextFactory.Create(database.ConnectionString);
        Assert.Equal("failed", (await db.ImportBatches.SingleAsync(b => b.BatchId == result.BatchId)).Status);
    }

    [Fact]
    public async Task Each_error_names_the_question_it_is_about()
    {
        var (certId, adminId) = await SeedAsync();
        var manifest = Sample("sample-reading-mcq.json");
        manifest["sections"]![1]!["groups"]![0]!["questions"]![0]!["difficultyLevel"] = 9;

        var result = await ImportJsonAsync(CreateService(), certId, adminId, manifest);

        var error = Assert.Single(await LogsAsync(result.BatchId), l => l.Level == "error");
        Assert.Equal(2, error.RowNo);
        Assert.Contains("Section P7, group 1, question 1", error.Message);
        Assert.Contains("difficultyLevel must be between 1 and 5", error.Message);
    }

    [Fact]
    public async Task Mcq_and_free_response_field_rules_mirror_the_schema_checks()
    {
        var (certId, adminId) = await SeedAsync();
        var manifest = Sample("sample-free-response.json");
        var speaking = FirstQuestion(manifest);
        speaking["minWords"] = 50;
        speaking["maxWords"] = 10;                                   // max below min
        speaking["criteria"]![0]!["code"] = "FLUENCY";               // not a criterion of this skill
        speaking["options"] = new JsonArray(new JsonObject { ["label"] = "A", ["text"] = "x", ["isCorrect"] = true });

        var result = await ImportJsonAsync(CreateService(), certId, adminId, manifest);

        var errors = (await LogsAsync(result.BatchId)).Where(l => l.Level == "error").Select(l => l.Message).ToList();
        Assert.Contains(errors, m => m.Contains("maxWords must be at least minWords"));
        Assert.Contains(errors, m => m.Contains("Unknown grading criterion 'FLUENCY'"));
        Assert.Contains(errors, m => m.Contains("free_response questions cannot have options"));
        Assert.Equal(3, result.ErrorCount);
    }

    [Fact]
    public async Task Mcq_with_timing_or_too_few_options_is_rejected()
    {
        var (certId, adminId) = await SeedAsync();
        var manifest = Sample("sample-reading-mcq.json");
        var first = FirstQuestion(manifest);
        first["responseSeconds"] = 30;
        var second = manifest["sections"]![1]!["groups"]![0]!["questions"]![0]!;
        second["options"] = new JsonArray(new JsonObject { ["label"] = "A", ["text"] = "Only", ["isCorrect"] = true });

        var result = await ImportJsonAsync(CreateService(), certId, adminId, manifest);

        var errors = (await LogsAsync(result.BatchId)).Where(l => l.Level == "error").Select(l => l.Message).ToList();
        Assert.Contains(errors, m => m.Contains("mcq questions cannot set prepSeconds, responseSeconds, minWords or maxWords"));
        Assert.Contains(errors, m => m.Contains("at least 2 options"));
    }

    [Fact]
    public async Task Skill_practice_exam_only_takes_sections_of_its_own_modality()
    {
        var (certId, adminId) = await SeedAsync();
        var manifest = Sample("sample-free-response.json");
        manifest["exam"]!["purpose"] = "speaking_practice";          // but the second section is W2

        var result = await ImportJsonAsync(CreateService(), certId, adminId, manifest);

        Assert.Equal(ImportBatchStatusDto.Failed, result.Status);
        var error = Assert.Single(await LogsAsync(result.BatchId), l => l.Level == "error");
        Assert.Contains("A speaking_practice exam can only hold speaking sections; W2 is writing", error.Message);
    }

    [Fact]
    public async Task Sections_are_looked_up_in_the_chosen_certificate_only()
    {
        var (certId, adminId) = await SeedAsync();
        var (otherCertId, _) = await SeedAsync();
        await database.SqlAsync($"""
            DECLARE @r INT = (SELECT SkillId FROM Skills WHERE CertId = {otherCertId} AND Code = 'READ');
            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder) VALUES (@r, {otherCertId}, 'reading', 'P6', N'Only elsewhere', 3);
            """);
        var manifest = Sample("sample-reading-mcq.json");
        manifest["sections"]![1]!["sectionCode"] = "P6";

        var result = await ImportJsonAsync(CreateService(), certId, adminId, manifest);

        Assert.Contains(await LogsAsync(result.BatchId), l => l.Message.Contains("Unknown section 'P6'"));
    }

    [Fact]
    public async Task Zip_package_must_contain_every_media_file_the_manifest_names()
    {
        var (certId, adminId) = await SeedAsync();
        var manifest = Sample("sample-listening-manifest.json");
        var package = Zip(("manifest.json", Encoding.UTF8.GetBytes(manifest.ToJsonString())),
                          ("media/part1-photo-01.jpg", [0xFF, 0xD8, 0xFF]));
        var store = new RecordingFileStore();

        var result = await CreateService(store).ImportAsync(adminId, certId, "listening.zip", package);

        Assert.Equal(ImportBatchStatusDto.Failed, result.Status);
        var errors = (await LogsAsync(result.BatchId)).Where(l => l.Level == "error").Select(l => l.Message).ToList();
        Assert.Contains(errors, m => m.Contains("'media/part1-photo-01.wav' is not in the package"));
        Assert.Contains(errors, m => m.Contains("'media/part2-question-01.wav' is not in the package"));
        Assert.Empty(store.Files);
    }

    [Fact]
    public async Task A_plain_json_upload_cannot_reference_media_and_unsupported_extensions_are_refused()
    {
        var (certId, adminId) = await SeedAsync();
        var manifest = Sample("sample-listening-manifest.json");
        manifest["sections"]![1]!["groups"]![0]!["audioFile"] = "media/clip.exe";

        var result = await ImportJsonAsync(CreateService(), certId, adminId, manifest);

        var errors = (await LogsAsync(result.BatchId)).Where(l => l.Level == "error").Select(l => l.Message).ToList();
        Assert.Contains(errors, m => m.Contains("Media files need a .zip package; 'media/part1-photo-01.wav'"));
        Assert.Contains(errors, m => m.Contains("'media/clip.exe' must be one of .mp3, .wav"));
    }

    [Fact]
    public async Task Media_paths_cannot_escape_the_media_folder()
    {
        var (certId, adminId) = await SeedAsync();
        var manifest = Sample("sample-listening-manifest.json");
        manifest["sections"]![0]!["groups"]![0]!["audioFile"] = "media/../manifest.wav";
        var package = Zip(("manifest.json", Encoding.UTF8.GetBytes(manifest.ToJsonString())),
                          ("manifest.wav", [1, 2, 3]));

        var result = await CreateService().ImportAsync(adminId, certId, "escape.zip", package);

        Assert.Contains(await LogsAsync(result.BatchId), l => l.Message.Contains("must be a file inside media/"));
    }

    [Fact]
    public async Task Unreadable_input_fails_the_batch_with_a_readable_message()
    {
        var (certId, adminId) = await SeedAsync();
        var service = CreateService();

        var broken = await service.ImportAsync(adminId, certId, "broken.json", Utf8("{ \"exam\": "));
        var unknownField = await service.ImportAsync(adminId, certId, "typo.json", Utf8("{ \"exma\": {} }"));
        var notZip = await service.ImportAsync(adminId, certId, "fake.zip", Utf8("not a zip"));
        var noManifest = await service.ImportAsync(adminId, certId, "empty.zip", Zip(("media/a.wav", [1])));

        Assert.Contains(await LogsAsync(broken.BatchId), l => l.Message.StartsWith("manifest.json is not valid JSON"));
        Assert.Contains(await LogsAsync(unknownField.BatchId), l => l.Message.Contains("exma"));
        Assert.Contains(await LogsAsync(notZip.BatchId), l => l.Message.Contains("not a readable .zip"));
        Assert.Contains(await LogsAsync(noManifest.BatchId), l => l.Message.Contains("no manifest.json at its top level"));
        Assert.All([broken, unknownField, notZip, noManifest], r => Assert.Equal(ImportBatchStatusDto.Failed, r.Status));
    }

    [Fact]
    public async Task Exam_header_is_validated_and_a_duplicate_title_is_refused()
    {
        var (certId, adminId) = await SeedAsync();
        var service = CreateService();
        Assert.Equal(ImportBatchStatusDto.Done, (await ImportJsonAsync(service, certId, adminId, Sample("sample-reading-mcq.json"))).Status);

        var again = await ImportJsonAsync(CreateService(), certId, adminId, Sample("sample-reading-mcq.json"));
        var bad = Sample("sample-reading-mcq.json");
        bad["exam"]!["purpose"] = "practice_drill";
        bad["exam"]!["durationMinutes"] = 0;
        bad["exam"]!["title"] = "Fresh title";
        var badHeader = await ImportJsonAsync(CreateService(), certId, adminId, bad);

        Assert.Contains(await LogsAsync(again.BatchId), l => l.Message.Contains("already has an exam named"));
        var errors = (await LogsAsync(badHeader.BatchId)).Select(l => l.Message).ToList();
        Assert.Contains(errors, m => m.Contains("Unknown exam purpose 'practice_drill'"));
        Assert.Contains(errors, m => m.Contains("durationMinutes must be greater than 0"));
        Assert.Equal(1, await ExamCountAsync(certId));
    }

    [Fact]
    public async Task A_media_write_failure_rolls_back_rows_and_removes_files_already_written()
    {
        var (certId, adminId) = await SeedAsync();
        var store = new RecordingFileStore { FailOnSave = 3 };

        await using var package = File.OpenRead(SamplePath("sample-listening-package.zip"));
        var result = await CreateService(store).ImportAsync(adminId, certId, "listening.zip", package);

        Assert.Equal(ImportBatchStatusDto.Failed, result.Status);
        Assert.Empty(store.Files);
        Assert.Equal(0, await ExamCountAsync(certId));
        Assert.Contains(await LogsAsync(result.BatchId), l => l.Message.Contains("could not be stored"));
    }

    [Fact]
    public async Task Unknown_certificate_is_refused_before_any_batch_is_written()
    {
        var (_, adminId) = await SeedAsync();

        var noCert = await Assert.ThrowsAsync<AdminExamException>(() =>
            ImportJsonAsync(CreateService(), int.MaxValue, adminId, Sample("sample-reading-mcq.json")));
        Assert.Equal(ApiError.Codes.CertificateUnavailable, noCert.Code);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        Assert.False(await db.ImportBatches.AnyAsync(b => b.CertId == int.MaxValue));
    }

    [Fact]
    public async Task Batches_list_newest_first_with_counts_and_detail_shows_the_log()
    {
        var (certId, adminId) = await SeedAsync();
        var ok = await ImportJsonAsync(CreateService(), certId, adminId, Sample("sample-reading-mcq.json"), "first.json");
        var broken = await CreateService().ImportAsync(adminId, certId, "second.json", Utf8("nope"));

        var list = await CreateService().GetBatchesAsync(certId);

        Assert.Equal([broken.BatchId, ok.BatchId], list.Batches.Select(b => b.BatchId).ToArray());
        Assert.Equal(1, list.Batches[0].ErrorCount);
        Assert.Equal(ImportBatchStatusDto.Done, list.Batches[1].Status);
        Assert.Contains(list.Certificates, c => c.CertId == certId);

        var detail = await CreateService().GetBatchAsync(ok.BatchId);
        Assert.Equal("first.json", detail.Batch.SourceName);
        Assert.Contains(detail.Logs, l => l.Level == ImportLogLevelDto.Info && l.Message.Contains("2 questions"));

        var missing = await Assert.ThrowsAsync<AdminExamException>(() => CreateService().GetBatchAsync(int.MaxValue));
        Assert.Equal(ApiError.Codes.ImportBatchNotFound, missing.Code);
    }

    [Fact]
    public async Task Standalone_json_manifest_larger_than_5_MB_is_rejected()
    {
        var (certId, adminId) = await SeedAsync();
        // A valid small manifest padded with whitespace to exceed 5 MB
        var small = Sample("sample-reading-mcq.json").ToJsonString();
        var padded = new MemoryStream();
        using (var writer = new StreamWriter(padded, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(small);
            writer.Write(new string(' ', 6 * 1024 * 1024));
        }
        padded.Position = 0;

        var result = await CreateService().ImportAsync(adminId, certId, "large.json", padded);

        Assert.Equal(ImportBatchStatusDto.Failed, result.Status);
        Assert.Contains(await LogsAsync(result.BatchId), l => l.Message.Contains("manifest is larger than 5 MB"));
    }

    [Fact]
    public async Task Media_paths_with_leading_backslash_are_rejected()
    {
        var (certId, adminId) = await SeedAsync();
        var manifest = Sample("sample-listening-manifest.json");
        manifest["sections"]![0]!["groups"]![0]!["audioFile"] = "\\media\\clip.wav";
        var package = Zip(("manifest.json", Encoding.UTF8.GetBytes(manifest.ToJsonString())),
                          ("media/clip.wav", [1, 2, 3]));

        var result = await CreateService().ImportAsync(adminId, certId, "backslash.zip", package);

        Assert.Equal(ImportBatchStatusDto.Failed, result.Status);
        Assert.Contains(await LogsAsync(result.BatchId), l => l.Message.Contains("must be a file inside media/"));
    }

    [Fact]
    public async Task Generated_toeic_and_ielts_sample_packages_import_cleanly()
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var toeicId = await db.Certificates.Where(c => c.Code == "TOEIC").Select(c => c.CertId).FirstOrDefaultAsync();
        var ieltsId = await db.Certificates.Where(c => c.Code == "IELTS").Select(c => c.CertId).FirstOrDefaultAsync();
        var adminId = await db.Users.Where(u => u.Email == "admin@demo.local").Select(u => u.UserId).FirstOrDefaultAsync();

        if (toeicId != 0 && adminId != 0)
        {
            // 1. TOEIC Standalone JSON
            await using var toeicJson = File.OpenRead(SamplePath("sample-exam-toeic-2skill.json"));
            var res1 = await CreateService().ImportAsync(adminId, toeicId, "sample-exam-toeic-2skill.json", toeicJson);
            Assert.Equal(ImportBatchStatusDto.Done, res1.Status);
            Assert.True(res1.QuestionCount > 0);

            // 2. TOEIC Package ZIP
            await using var toeicZip = File.OpenRead(SamplePath("sample-exam-toeic-2skill-package.zip"));
            var res2 = await CreateService().ImportAsync(adminId, toeicId, "sample-exam-toeic-2skill-package.zip", toeicZip);
            Assert.Equal(ImportBatchStatusDto.Done, res2.Status);
            Assert.True(res2.QuestionCount > 0);
        }

        if (ieltsId != 0 && adminId != 0)
        {
            // 3. IELTS Standalone JSON
            await using var ieltsJson = File.OpenRead(SamplePath("sample-exam-ielts-4skill.json"));
            var res3 = await CreateService().ImportAsync(adminId, ieltsId, "sample-exam-ielts-4skill.json", ieltsJson);
            Assert.Equal(ImportBatchStatusDto.Done, res3.Status);
            Assert.True(res3.QuestionCount > 0);

            // 4. IELTS Package ZIP
            await using var ieltsZip = File.OpenRead(SamplePath("sample-exam-ielts-4skill-package.zip"));
            var res4 = await CreateService().ImportAsync(adminId, ieltsId, "sample-exam-ielts-4skill-package.zip", ieltsZip);
            Assert.Equal(ImportBatchStatusDto.Done, res4.Status);
            Assert.True(res4.QuestionCount > 0);
        }
    }
}
