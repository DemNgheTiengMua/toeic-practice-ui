using Microsoft.EntityFrameworkCore;
using BusinessObjects;

namespace Services.Tests;

/// <summary>
/// #147: a certificate created in the wizard must be usable the moment it is saved. Today the
/// wizard writes Certificate, Skill and Section only — ScoreScale and ScoreBand are written by a
/// separate admin page — so a new certificate has no bands, FindBandCodeAsync returns null, and
/// CK_Placement_DoneHasResult rejects every placement submission for it.
/// <para>
/// These drive the public wizard entry point and then read the certificate back through the
/// engine's own paths, so they fail if the wizard stops writing any of the three tables.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CertificateWizardScoringTests(DatabaseFixture database)
{
    [Fact]
    public async Task A_certificate_saved_in_the_wizard_has_scales_bands_and_rubric_criteria()
    {
        var databaseName = $"CertWizScoring_{Guid.NewGuid():N}";
        var connectionString = await database.CreateFreshDatabaseAsync(databaseName);
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var service = new CertificateService(db);

            var saved = await service.SaveCertificateWizardAsync(ResearchWritingCertificate());

            Assert.True(saved.CertId > 0);

            var skills = await db.Skills
                .Where(s => s.CertId == saved.CertId)
                .OrderBy(s => s.DisplayOrder)
                .ToListAsync();
            var skill = Assert.Single(skills);

            // The skill's own scale, so the engine never falls back to a guess.
            Assert.Equal(0, skill.MinScaledScore);
            Assert.Equal(50, skill.MaxScaledScore);
            Assert.Equal(1, skill.ScoreStep);

            var scales = await db.ScoreScales
                .Where(s => s.CertId == saved.CertId)
                .OrderBy(s => s.RawScore)
                .ToListAsync();
            Assert.NotEmpty(scales);

            var bands = await db.ScoreBands
                .Where(b => b.CertId == saved.CertId)
                .OrderBy(b => b.MinTotal)
                .ToListAsync();
            Assert.Equal(2, bands.Count);
            Assert.Equal(0, bands[0].MinTotal);
            Assert.Equal(50, bands[^1].MaxTotal);

            var criteria = await db.GradingCriteria
                .Where(c => c.CertId == saved.CertId)
                .ToListAsync();
            var criterion = Assert.Single(criteria);
            Assert.Equal("TASK", criterion.Code);
            Assert.False(string.IsNullOrWhiteSpace(criterion.RubricDescription));
        }
        finally
        {
            await database.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task A_placement_of_a_certificate_created_in_the_wizard_can_be_submitted()
    {
        // The exact failure #147 exists to remove: no bands meant BandCode stayed null and
        // CK_Placement_DoneHasResult rejected the submission with a SQL error. This drives the
        // real placement submit path, so it fails if the wizard stops writing bands.
        var databaseName = $"CertWizPlacement_{Guid.NewGuid():N}";
        var connectionString = await database.CreateFreshDatabaseAsync(databaseName);
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var certService = new CertificateService(db);
            var saved = await certService.SaveCertificateWizardAsync(ReadingCertificateForPlacement());

            var skill = await db.Skills.SingleAsync(s => s.CertId == saved.CertId);
            var examId = await SeedPlacementExamAsync(db, saved.CertId, skill.SkillId);            var userId = await SeedLearnerAsync(db);

            var placement = new PlacementService(db, new LearnerService(db));
            var info = await placement.StartPlacementTestAsync(userId, examId);
            var take = await placement.GetPlacementTakeAsync(userId, info.PlacementId, null);

            // #217 / PLC-19: answer the one item, so the sitting measures something and produces a
            // result. An untouched paper is now abandoned with no result, which is a different fact and
            // would not exercise the band lookup this test exists for (#147: the band rows exist).
            var answerQuestion = take.CurrentQuestion;
            await placement.SaveAnswerAsync(
                userId, info.PlacementId, answerQuestion.QuestionId, answerQuestion.Options[0].OptionId,
                isFlagged: false);

            // Before #147 this threw a SQL constraint violation: no band rows existed for the
            // certificate, so BandCode could not be filled in.
            var result = await placement.SubmitPlacementTestAsync(userId, info.PlacementId, isAutoSubmit: false);

            Assert.False(string.IsNullOrWhiteSpace(result.ResultBandCode));
        }
        finally
        {
            await database.DropDatabaseAsync(databaseName);
        }
    }

    /// <summary>A one-question placement exam for the certificate the wizard just created, reusing
    /// the section the wizard already wrote so the composite unique key is not violated.</summary>
    /// <summary>
    /// A one-question placement exam for the certificate the wizard just created, reusing the section the
    /// wizard already wrote so the composite unique key is not violated.
    /// <para>
    /// #272: the position is authored on the pool - an item, its version and a binding, because that is
    /// the only content model left. The section must be objective: <c>CK_Items_Modality_RenderFormat</c>
    /// refuses an mcq in a writing section, and the legacy rows this fixture used to write put one there.
    /// </para>
    /// </summary>
    private static async Task<int> SeedPlacementExamAsync(DataAccess.CompanionDbContext db, int certId, int skillId)
    {
        var section = await db.Sections.SingleAsync(s => s.SkillId == skillId);
        section.ExpectedQuestionCount = 1;
        await db.SaveChangesAsync();

        var exam = new BusinessObjects.Entities.Exam
        {
            CertId = certId,
            Name = "Demo Wizard Placement",
            Status = "published",
            DurationMinutes = 30,
            Purpose = "placement",
            QuestionCount = 1,
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        await PoolFixture.AddMcqAsync(
            db, exam.ExamId, certId, section.SectionId, "reading", "Pick one.", displayOrder: 1);

        // #259 / PRN-03: the wizard writes Certificate/Skill/Section/ScoreScale/ScoreBand, and a
        // placement is placed by a CUT-SCORE table. Before #259 this test passed only because the
        // writer fell back to parsing the band code as a CEFR level, which is the crosswalk the rule
        // forbids. The table is seeded here rather than by the wizard because publishing cut scores is
        // a separate admin act (#210 owns it) - this test is about the submit path, not about that.
        db.CefrCutScoreVersions.Add(new BusinessObjects.Entities.CefrCutScoreVersion
        {
            CertId = certId,
            TableVersion = 1,
            ReportsSkillsSeparately = false,
            Source = "wizard-test",
            PublishedAt = new DateOnly(2026, 1, 1),
        });
        db.CefrCutScores.Add(new BusinessObjects.Entities.CefrCutScore
        {
            CertId = certId,
            TableVersion = 1,
            SkillId = skillId,
            CefrLevel = "A2",
            RowKind = "cut",
            MinScaled = 0,
            MaxScaled = 100,
        });
        await db.SaveChangesAsync();

        return exam.ExamId;
    }

    private static async Task<int> SeedLearnerAsync(DataAccess.CompanionDbContext db)
    {
        var role = new BusinessObjects.Entities.Role { Code = "learner", Name = "Learner" };
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        var user = new BusinessObjects.Entities.User
        {
            RoleId = role.RoleId,
            Email = $"wiz-{Guid.NewGuid():N}@example.local",
            PasswordHash = new byte[] { 1 },
            FullName = "Wizard Learner",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.UserId;
    }

    /// <summary>
    /// The same certificate, with a READING skill instead of a writing one.
    /// <para>
    /// #272: a placement must measure something synchronously to produce a band, and the pool refuses an
    /// <c>mcq</c> in a writing section (<c>CK_Items_Modality_RenderFormat</c>). So the placement test needs
    /// a certificate whose section can host an objective position. Everything the wizard writes -
    /// scales, bands, a rubric criterion - is identical; only the modality differs.
    /// </para>
    /// </summary>
    private static Contracts.SaveCertificateWizardRequest ReadingCertificateForPlacement() =>
        new(
            CertId: null,
            Code: "DEMO_WIZ_R",
            Name: "Demo Wizard Reading Certificate",
            Description: null,
            ScoreAggregationRule: "sum",
            PresetCode: "custom",
            IsActive: true,
            Skills:
            [
                new Contracts.SaveSkillWizardDto(
                    SkillId: null,
                    Code: "COMP",
                    Name: "Comprehension",
                    Modality: Contracts.ModalityDto.Reading,
                    DisplayOrder: 1,
                    DurationMinutes: 60,
                    Sections:
                    [
                        new Contracts.SaveSectionWizardDto(
                            SectionId: null,
                            Code: "COMP1",
                            Name: "Comprehension 1",
                            DisplayOrder: 1,
                            ScoreWeight: 1.0m,
                            NavigationMode: Contracts.NavigationModeDto.FullSandbox,
                            TimerPolicyJson: null,
                            LayoutType: Contracts.LayoutTypeDto.StandardVertical,
                            OptionCount: 4,
                            ClusterSize: 1,
                            UnitCount: 1,
                            HideOptionText: false,
                            RequirePassage: false,
                            RequireAudio: false,
                            RequireImage: false,
                            RequiredKeywordCount: 0,
                            ExpectedQuestionCount: 10,
                            AllowedComponentTypesJson: "[\"mcq\"]",
                            ToolsConfigJson: null)
                    ])
            ],
            MinScaledScore: 0,
            MaxScaledScore: 50,
            ScoreStep: 1,
            Bands:
            [
                new Contracts.SaveScoreBandWizardDto(null, "FAIL", "Below passing", 0, 24, 1),
                new Contracts.SaveScoreBandWizardDto(null, "PASS", "Passing", 25, 50, 2)
            ],
            Criteria:
            [
                new Contracts.SaveGradingCriterionWizardDto(
                    null,
                    "COMP",
                    "TASK",
                    "Task achievement",
                    MaxScore: 5m,
                    Weight: 1m,
                    DisplayOrder: 1,
                    RubricDescription: "5 = fully addresses the task; 1 = does not address it.")
            ]);

    /// <summary>A certificate authored the way the ticket describes: two skills, a 0-50 step-1
    /// scale, continuous bands and a rubric criterion.</summary>
    private static Contracts.SaveCertificateWizardRequest ResearchWritingCertificate() =>
        new(
            CertId: null,
            Code: "DEMO_WIZ",
            Name: "Demo Wizard Certificate",
            Description: null,
            ScoreAggregationRule: "sum",
            PresetCode: "custom",
            IsActive: true,
            Skills:
            [
                new Contracts.SaveSkillWizardDto(
                    SkillId: null,
                    Code: "EXPR",
                    Name: "Expression",
                    Modality: Contracts.ModalityDto.Writing,
                    DisplayOrder: 1,
                    DurationMinutes: 60,
                    Sections:
                    [
                        new Contracts.SaveSectionWizardDto(
                            SectionId: null,
                            Code: "EXPR1",
                            Name: "Expression 1",
                            DisplayOrder: 1,
                            ScoreWeight: 1.0m,
                            NavigationMode: Contracts.NavigationModeDto.FullSandbox,
                            TimerPolicyJson: null,
                            LayoutType: Contracts.LayoutTypeDto.StandardVertical,
                            OptionCount: 4,
                            ClusterSize: 1,
                            UnitCount: 1,
                            HideOptionText: false,
                            RequirePassage: false,
                            RequireAudio: false,
                            RequireImage: false,
                            RequiredKeywordCount: 0,
                            ExpectedQuestionCount: 10,
                            AllowedComponentTypesJson: "[\"free_response\"]",
                            ToolsConfigJson: null)
                    ])
            ],
            MinScaledScore: 0,
            MaxScaledScore: 50,
            ScoreStep: 1,
            Bands:
            [
                new Contracts.SaveScoreBandWizardDto(null, "FAIL", "Below passing", 0, 24, 1),
                new Contracts.SaveScoreBandWizardDto(null, "PASS", "Passing", 25, 50, 2)
            ],
            Criteria:
            [
                new Contracts.SaveGradingCriterionWizardDto(
                    null,
                    "EXPR",
                    "TASK",
                    "Task achievement",
                    MaxScore: 5m,
                    Weight: 1m,
                    DisplayOrder: 1,
                    RubricDescription: "5 = fully addresses the task; 1 = does not address it.")
            ]);
}
