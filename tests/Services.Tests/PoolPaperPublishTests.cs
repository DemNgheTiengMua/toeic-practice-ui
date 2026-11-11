using System;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class PoolPaperPublishTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private CompanionDbContext Db() => DbContextFactory.Create(database.ConnectionString);

    /// <summary>
    /// #269: a paper authored ONLY on the pool can be published.
    /// <para>
    /// INV-1 counted a section's positions from <c>Questions</c> alone, so a pool-authored paper
    /// reported "section declares 1 item(s) but holds 0" for every section and the publish door
    /// refused it. Measured before the fix: <c>CanPublish=false</c> on a complete roster. A paper the
    /// sitting engine can fully serve was unpublishable, which would have made the pool migration
    /// unshippable even though every reader worked.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_paper_with_only_pool_positions_passes_the_publish_audit()
    {
        var certCode = Unique("PP1");
        int examId;
        int authorId;

        await using (var db = Db())
        {
            var cert = new Certificate { Code = certCode, Name = $"Cert {certCode}", IsActive = true };
            db.Certificates.Add(cert);
            await db.SaveChangesAsync();

            // An author account: a clearance decision names who made it.
            var role = new Role { Code = "auth", Name = "Author" };
            if (!await db.Roles.AnyAsync(r => r.Code == "auth"))
            {
                db.Roles.Add(role);
                await db.SaveChangesAsync();
            }
            var authorRole = await db.Roles.SingleAsync(r => r.Code == "auth");
            var author = new User { RoleId = authorRole.RoleId, Email = $"author-{certCode}@test.local", PasswordHash = [0x01] };
            db.Users.Add(author);
            await db.SaveChangesAsync();
            authorId = author.UserId;

            // ADM-02: the account that clears a sample must not be the one that uploaded it. That rule
            // is the point of the audit, so the fixture needs a second, independent reviewer.
            var reviewer = new User { RoleId = authorRole.RoleId, Email = $"reviewer-{certCode}@test.local", PasswordHash = [0x01] };
            db.Users.Add(reviewer);
            await db.SaveChangesAsync();

            var skill = new Skill
            {
                CertId = cert.CertId, Code = "READ", Name = "Reading", Modality = "reading",
                DisplayOrder = 1, MinScaledScore = 0, MaxScaledScore = 100, ScoreStep = 1,
                // INV-5: the paper's timers must sum to something, so a served skill declares one.
                DurationMinutes = 30,
            };
            db.Skills.Add(skill);
            await db.SaveChangesAsync();

            var section = new Section
            {
                CertId = cert.CertId, SkillId = skill.SkillId, Modality = "reading",
                Code = "P5", Name = "Incomplete Sentences", DisplayOrder = 1,
                ExpectedQuestionCount = 2, NavigationMode = "FULL_SANDBOX",
            };
            db.Sections.Add(section);
            await db.SaveChangesAsync();

            db.ScoreBands.Add(new ScoreBand
            {
                CertId = cert.CertId, Code = "B1", Name = "Threshold", MinTotal = 0, MaxTotal = 990, DisplayOrder = 1,
            });
            await db.SaveChangesAsync();

            var exam = new Exam
            {
                CertId = cert.CertId, Name = $"Pool paper {certCode}", Status = "draft",
                DurationMinutes = 30, Purpose = "mock", QuestionCount = 2,
            };
            db.Exams.Add(exam);
            await db.SaveChangesAsync();
            examId = exam.ExamId;

            // Exactly what the section declares, and nothing else: no Questions rows at all.
            var q1 = await PoolFixture.AddMcqAsync(db, examId, cert.CertId, section.SectionId, "reading", "Pool Q1", displayOrder: 1);
            var q2 = await PoolFixture.AddMcqAsync(db, examId, cert.CertId, section.SectionId, "reading", "Pool Q2", displayOrder: 2);

            Assert.Empty(await db.Questions.Where(q => q.ExamId == examId).ToListAsync());

            // INV-3 is a real rule, not a leftover: an item reaches a learner only after a human
            // clearance names it (#205's per-item 'clear', or #206's approved stratum). Pool stock
            // authored directly has neither by construction, so the fixture must clear it - the same
            // step the authoring flow performs.
            var batch = new ImportBatch
            {
                CertId = cert.CertId, SourceName = "pool-publish-test.xlsx",
                Status = ImportBatchStatus.Done.ToCode(), UploadedBy = authorId,
            };
            db.ImportBatches.Add(batch);
            await db.SaveChangesAsync();

            foreach (var itemId in new[] { q1.ItemId, q2.ItemId })
            {
                db.ImportLogs.Add(new ImportLog
                {
                    BatchId = batch.BatchId, ItemId = itemId, Level = "info",
                    Message = "accepted by the pool publish test",
                });
                db.ReviewSampleDecisions.Add(new ReviewSampleDecision
                {
                    BatchId = batch.BatchId, ItemId = itemId, ActorUserId = reviewer.UserId,
                    Decision = "clear", DecidedAt = DateTimeOffset.UtcNow,
                });
            }

            await db.SaveChangesAsync();
        }

        var admin = new AdminExamService(Db(), TimeProvider.System);
        var audit = await admin.AuditExamPublishReadinessAsync(examId);

        Assert.True(audit.CanPublish,
            "a complete pool-only paper was refused: "
            + string.Join(" | ", audit.Issues.Select(i => i.Message)));
    }
}
