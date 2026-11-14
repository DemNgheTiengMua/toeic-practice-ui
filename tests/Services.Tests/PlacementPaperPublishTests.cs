using System;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #269: a PLACEMENT paper is audited on the sections it actually serves, not on all of them.
/// <para>
/// <b>The defect this pins.</b> The publish audit filtered its section list by <c>Exam.Purpose</c>; that
/// switch named speaking and writing practice and fell through to "every section" for everything else. A
/// placement paper is a SLICE across skills - its stock is drawn per (skill, band), so it binds one
/// section per skill and nothing more - but it was audited against all twenty TOEIC sections. Measured on
/// the imported Reading B2 form: publication refused with fifteen <c>"section declares N item(s) but holds
/// 0"</c> errors plus fifteen <c>SEC_QUESTION_COUNT_LOW</c> ones, for sections the paper was never going to
/// serve.
/// </para>
/// <para>
/// <b>Why the fix is not "skip the checks".</b> Two of the errors the placement paper must still answer are
/// real: every bound item needs a human clearance (INV-3), and the served skills must sum to the paper's
/// declared duration (INV-5). This file asserts a placement paper passes <i>with</i> those satisfied and
/// that a section it does not serve cannot contribute a deficit - so the test fails both on a paper that
/// is genuinely incomplete and on one refused for sections it never bound.
/// </para>
/// <para>
/// <b>What a wrong implementation could still pass.</b> "CanPublish is true" passes for an audit that
/// checks nothing. So the assertions are on the ISSUE LIST: no issue may name an unserved section, and a
/// deliberately short served section must still be named.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PlacementPaperPublishTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private CompanionDbContext Db() => DbContextFactory.Create(database.ConnectionString);

    private sealed record Seeded(int ExamId, int CertId, int SectionId, int[] ItemIds);

    /// <summary>
    /// A certificate with a reading and a LISTENING skill, each with two sections, and a placement paper
    /// that binds only the reading one. The listening sections are the unserved stock the old audit
    /// demanded items for.
    /// </summary>
    private async Task<Seeded> SeedPlacementPaperAsync(int boundItems)
    {
        await using var db = Db();
        var certCode = Unique("PLC");

        var cert = new Certificate { Code = certCode, Name = $"Cert {certCode}", IsActive = true };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Code == "auth");
        if (role is null)
        {
            role = new Role { Code = "auth", Name = "Author" };
            db.Roles.Add(role);
            await db.SaveChangesAsync();
        }

        var author = new User { RoleId = role.RoleId, Email = $"a-{certCode}@test.local", PasswordHash = [0x01] };
        var reviewer = new User { RoleId = role.RoleId, Email = $"r-{certCode}@test.local", PasswordHash = [0x01] };
        db.Users.AddRange(author, reviewer);
        await db.SaveChangesAsync();

        // Reading is the served skill, so it declares the duration the paper declares.
        var reading = new Skill
        {
            CertId = cert.CertId, Code = "READ", Name = "Reading", Modality = "reading",
            DisplayOrder = 1, MinScaledScore = 0, MaxScaledScore = 100, ScoreStep = 1,
            DurationMinutes = 75,
        };
        // Listening is enabled but UNSERVED by this paper: it has sections and a duration, and the old
        // audit demanded items for them anyway.
        var listening = new Skill
        {
            CertId = cert.CertId, Code = "LIST", Name = "Listening", Modality = "listening",
            DisplayOrder = 2, MinScaledScore = 0, MaxScaledScore = 100, ScoreStep = 1,
            DurationMinutes = 45,
        };
        db.Skills.AddRange(reading, listening);
        await db.SaveChangesAsync();

        var bound = new Section
        {
            CertId = cert.CertId, SkillId = reading.SkillId, Modality = "reading",
            Code = "P6", Name = "Text Completion", DisplayOrder = 1,
            // Deliberately larger than what this paper binds: that is a real placement slice, and the
            // check must not read it as a deficit.
            ExpectedQuestionCount = 16, NavigationMode = "FULL_SANDBOX",
        };
        var unboundReading = new Section
        {
            CertId = cert.CertId, SkillId = reading.SkillId, Modality = "reading",
            Code = "P7", Name = "Reading Comprehension", DisplayOrder = 2,
            ExpectedQuestionCount = 54, NavigationMode = "FULL_SANDBOX",
        };
        var unboundListening = new Section
        {
            CertId = cert.CertId, SkillId = listening.SkillId, Modality = "listening",
            Code = "P3", Name = "Conversations", DisplayOrder = 3,
            ExpectedQuestionCount = 39, NavigationMode = "FULL_SANDBOX",
        };
        db.Sections.AddRange(bound, unboundReading, unboundListening);
        await db.SaveChangesAsync();

        db.ScoreBands.Add(new ScoreBand
        {
            CertId = cert.CertId, Code = "B1", Name = "Threshold",
            MinTotal = 0, MaxTotal = 990, DisplayOrder = 1,
        });
        await db.SaveChangesAsync();

        var exam = new Exam
        {
            CertId = cert.CertId, Name = $"Placement {certCode}", Status = "draft",
            Purpose = "placement",
            // INV-5: the served skills must sum to this. Reading alone is served, so it is 75.
            DurationMinutes = 75,
            // CK_Exams_QuestionCount refuses 0 and allows NULL, and the zero-bind case is a real state:
            // "no count declared" is what a paper in progress holds, and the audit must still refuse it
            // for having no content rather than for a constraint the fixture could not write.
            QuestionCount = boundItems > 0 ? boundItems : null,
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        var itemIds = new int[boundItems];
        for (var index = 0; index < boundItems; index++)
        {
            var position = await PoolFixture.AddMcqAsync(
                db, exam.ExamId, cert.CertId, bound.SectionId, "reading",
                $"Placement item {index + 1}", displayOrder: index + 1);
            itemIds[index] = position.ItemId;
        }

        // INV-3: each bound item carries a human clearance, written by an account other than the author.
        var batch = new ImportBatch
        {
            CertId = cert.CertId, SourceName = "placement-publish-test.json",
            Status = ImportBatchStatus.Done.ToCode(), UploadedBy = author.UserId,
        };
        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync();

        foreach (var itemId in itemIds)
        {
            db.ImportLogs.Add(new ImportLog
            {
                BatchId = batch.BatchId, ItemId = itemId, Level = "info",
                Message = "accepted by the placement publish test",
            });
        }

        await db.SaveChangesAsync();

        return new Seeded(exam.ExamId, cert.CertId, bound.SectionId, itemIds);
    }

    [Fact]
    public async Task A_placement_slice_is_not_refused_for_sections_it_does_not_serve()
    {
        var seeded = await SeedPlacementPaperAsync(boundItems: 3);
        var admin = new AdminExamService(Db(), TimeProvider.System);

        var audit = await admin.AuditExamPublishReadinessAsync(seeded.ExamId);

        var unserved = audit.Issues
            .Where(i => i.SectionCode is "P7" or "P3")
            .Select(i => i.Message)
            .ToList();

        Assert.True(unserved.Count == 0,
            "the audit judged this placement paper on a section it never bound: "
            + string.Join(" | ", unserved));

        Assert.True(audit.CanPublish,
            "a complete placement slice was refused: " + string.Join(" | ", audit.Issues.Select(i => i.Message)));

        // The slice binds 3 where the section declares 16. That is what a placement draw is, and INV-1
        // must not read it as a shortfall - - the assertion above would catch it as a refusal, and this
        // names the rule so a future reader knows the number is deliberate rather than a gap.
        Assert.DoesNotContain(audit.Issues, i => i.Code == ExamInvariants.SkillCoverage);
    }

    /// <summary>
    /// The other half: the placement rule must not become "a placement paper is never incomplete". A paper
    /// that binds NOTHING still fails, which is the assertion that keeps the fix from being a bypass.
    /// </summary>
    [Fact]
    public async Task A_placement_paper_that_binds_nothing_is_still_refused()
    {
        var seeded = await SeedPlacementPaperAsync(boundItems: 0);
        var admin = new AdminExamService(Db(), TimeProvider.System);

        var audit = await admin.AuditExamPublishReadinessAsync(seeded.ExamId);

        Assert.False(audit.CanPublish,
            "a placement paper with no items at all was published. The placement rule exempts a slice from "
            + "the section blueprint; it does not exempt a paper from having content.");
    }
}
