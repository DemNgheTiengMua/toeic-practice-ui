using Microsoft.EntityFrameworkCore;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class CertificateServiceTests(DatabaseFixture database)
{
    private async Task<int> SeedToeicAsync()
    {
        // A compact slice of the real TOEIC L&R catalog: two skills, sections with question
        // counts, five non-overlapping bands. Mirrors what the certificates page renders.
        await database.SqlAsync("""
            INSERT INTO Certificates (Code, Name, Description, IsActive)
              VALUES ('TOEIC', N'TOEIC Listening & Reading', N'The standard workplace English test.', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes) VALUES
              (@cert, 'LIST', N'Listening', 'listening', 1, 45),
              (@cert, 'READ', N'Reading', 'reading', 2, 75);
            DECLARE @list INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'LIST');
            DECLARE @read INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'READ');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder) VALUES
              (@list, @cert, 'listening', 'P1', N'Photographs', 4, 6, 1),
              (@list, @cert, 'listening', 'P2', N'Question-Response', 3, 25, 2),
              (@read, @cert, 'reading', 'P5', N'Incomplete Sentences', 4, 30, 1);

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'A1', N'Beginner', 120, 220, 1),
              (@cert, 'A2', N'Elementary', 225, 545, 2),
              (@cert, 'B1', N'Intermediate', 550, 780, 3),
              (@cert, 'B2', N'Upper intermediate', 785, 940, 4),
              (@cert, 'C1', N'Advanced', 945, 990, 5);
            """);
        return 0;
    }

    [Fact]
    public async Task GetCatalog_returns_active_certificates_with_skills_sections_and_bands()
    {
        await SeedToeicAsync();
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var service = new CertificateService(db);

        var catalog = await service.GetCatalogAsync();

        var toeic = Assert.Single(catalog, c => c.Code == "TOEIC");
        Assert.Equal("TOEIC", toeic.Code);
        Assert.Equal("The standard workplace English test.", toeic.Description);

        Assert.Equal(new[] { "LIST", "READ" }, toeic.Skills.Select(s => s.Code));
        var listening = toeic.Skills[0];
        Assert.Equal(45, listening.DurationMinutes);
        Assert.Equal(new[] { "P1", "P2" }, listening.Sections.Select(s => s.Code));
        Assert.Equal(6, listening.Sections[0].ExpectedQuestionCount);

        Assert.Equal(new[] { "A1", "A2", "B1", "B2", "C1" }, toeic.Bands.Select(b => b.Code));
        Assert.Equal(120, toeic.Bands.First().MinTotal);
        Assert.Equal(990, toeic.Bands.Last().MaxTotal);
    }

    [Fact]
    public async Task GetCatalog_skips_inactive_certificates()
    {
        await database.SqlAsync("""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('DEAD', N'Retired cert', 0);
            """);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var service = new CertificateService(db);

        var catalog = await service.GetCatalogAsync();

        Assert.DoesNotContain(catalog, c => c.Code == "DEAD");
    }

    [Fact]
    public async Task Database_has_triggers_and_views_from_the_schema_script()
    {
        // The model relies on HasTrigger declarations matching the real database (ADR-0001);
        // this test proves the test database actually carries them.
        await using var db = DbContextFactory.Create(database.ConnectionString);

        var triggerCount = await db.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS Value FROM sys.triggers")
            .SingleAsync();
        // #183: trg_Topics_ClosedVocabulary, which refuses a twenty-first topic.
        // #184: trg_ItemVersions_ScoredFieldsImmutable, which refuses an in-place scored-field edit.
        // #185: trg_ExamItems_ProductionBindsProduction and trg_Exams_TierKeepsContent, the two halves
        // of INV-6. Two rather than one because a rule that fires only when a binding is written is
        // bypassed by promoting the exam instead.
        // #190: trg_Items_LifecycleTransitions and trg_Items_NoHardDelete, the six states and the
        // no-delete rule.
// #191: trg_Attempts_SingleWriter, trg_PlacementTests_SingleWriter and
        // trg_LearnerPathSteps_SingleWriter - one per covered derived fact. The registry names seven;
        // #211 has since covered a fourth (trg_LearnerSkillStates_SingleWriter), so two have no table
        // yet and one is exempt. domain27 asserts the split rather than assuming it.
        // #192: trg_GradingCriteria_FrozenWhenLocked,
        // trg_GradingCriterionDescriptors_FrozenWhenLocked,
        // trg_GradingRubricLocks_CompleteCanonicalRubric and
        // trg_GradingRubricLocks_IrreversibleOnceGraded - the seven-band grid and the lock.
        // #193: trg_GradingCriterionAnchors_FrozenWhenLocked - the three calibrated anchors.
        // #210: trg_CefrCutScores_NoOverlap - per-skill cut ranges may not overlap within one version.
        // #211: trg_LearnerSkillStates_SingleWriter - the fourth covered derived fact. The count moves
        // 42 -> 43 and the registry's split moves 3/3/1 -> 4/2/1; domain27 pins the split.
        // #201: six more, moving 43 -> 49 - trg_ReviewSampleDecisions_NoSelfApproval (ADM-02) and one
        // trg_*_TriageCannotWrite on each of the five tables a prohibited edit reaches (ADM-03):
        // ItemVersions, ExamItems, Sections, GradingCriteria, GradingCriterionDescriptors.
        // Review finding: two more, 49 -> 51 - Exams and Items. A triage-only account could publish an
        // exam and activate an item, which ADM-03's "exam structure" and "item text" both forbid; the
        // five original guards covered assembly and rubric but neither of these two.
        // #212: three more, 51 -> 54 - trg_PlacementSkillResults_SingleWriter (the fifth covered derived
        // fact), trg_PlacementTests_CompletionIsEarned and trg_PlacementSkillResults_CompletionIsEarned
        // (PLC-36's "done needs a measured skill or a recorded failure", which reads another table and so
        // cannot be a CHECK).
        // #206: three more, 54 -> 57 - trg_ReviewStrata_SingleWriter (the sixth covered derived fact,
        // W-01's blind-solve order on ReviewSampleItems, and the batch-scope rule that stops a stratum
        // sampling another author's stock).
        // #226: two more, 57 -> 59 - trg_ExamPublishAudits_SingleWriter (ADM-11's pass has one writer)
        // and trg_Exams_PublishRequiresAudit (the door: a paper is never created published, and only
        // moves into published with a pass at least as new as its last edit).
        // #233: one more, 59 -> 60 - trg_LearnerPathWeeks_SingleWriter, the seventh covered derived fact
        // and the archive's immutability guard.
        // #227: three more, 60 -> 63 - trg_Exams_PublishFreezesEdition (publishing freezes the timers,
        // phase weights, aggregation rule, band grid and cut-score version an attempt is scored with),
        // trg_ExamEditions_SingleWriter (the eighth covered derived fact) and
        // trg_ExamAttempts_EditionIsFixed (an attempt cannot be moved onto another edition).
        // #237: two more, 63 -> 65 - trg_UserCellMasteries_SingleWriter (the ninth covered derived
        // fact) and trg_UserCellMasteries_StateIsEarned (the four states are reachable only through
        // the transitions PLC-68/70/71 name, and entering mastered needs the probability AND the count).
        // #246: one more, 65 -> 66 - trg_Attempts_EpochIsReadOnly, so a preserved attempt
        // (EpochTag = 'legacy_v1') can be read forever and written never: its score came from a scoring
        // model this build no longer has, and a recomputation would silently disagree with the record.
        // #269 audit: one more, 66 -> 67 - trg_PracticeSessions_StatusTransitions. A finished or
        // abandoned practice session is terminal; before it, a finished drill could be reopened with its
        // stamp cleared and its already-counted answers still in place.
        Assert.Equal(67, triggerCount);

        // Named as well as counted, because a count alone cannot tell a rename from a removal.
        foreach (var name in new[]
                 {
                     "trg_GradingCriteria_FrozenWhenLocked",
                     "trg_GradingCriterionDescriptors_FrozenWhenLocked",
                     "trg_GradingCriterionAnchors_FrozenWhenLocked",
                     "trg_CefrCutScores_NoOverlap",
                     "trg_GradingRubricLocks_CompleteCanonicalRubric",
                     "trg_GradingRubricLocks_IrreversibleOnceGraded",
                     "trg_Attempts_SingleWriter",
                     "trg_PlacementTests_SingleWriter",
                     "trg_LearnerPathSteps_SingleWriter",
                     "trg_LearnerSkillStates_SingleWriter",
                     // #212: the fifth covered derived fact, and the completion rule it takes two
                     // triggers to express (the placement side and the skill-row side).
                     "trg_PlacementSkillResults_SingleWriter",
                     "trg_PlacementTests_CompletionIsEarned",
                     "trg_PlacementSkillResults_CompletionIsEarned",
                     // #201: the two invariants, by name. A rename must not pass as a count.
                     "trg_ReviewSampleDecisions_NoSelfApproval",
                     "trg_ItemVersions_TriageCannotWrite",
                     "trg_ExamItems_TriageCannotWrite",
                     "trg_Sections_TriageCannotWrite",
                     "trg_GradingCriteria_TriageCannotWrite",
                     "trg_GradingCriterionDescriptors_TriageCannotWrite",
                     // Review finding: the two that were missing. Triage could publish an exam and
                     // activate an item before these existed.
                     "trg_Exams_TriageCannotWrite",
                     "trg_Items_TriageCannotWrite",
                     // #206: the three the stratified audit adds. The first is the sixth covered
                     // derived fact; the other two close the blind-solve order and the batch scope at
                     // the database, for writes the service does not make.
                     "trg_ReviewStrata_SingleWriter",
                     "trg_ReviewSampleItems_SolveIsBlind",
                     "trg_ReviewSampleItems_ItemIsInItsBatch",
                     // #226: ADM-11's pass has one writer, and the door itself.
                     "trg_ExamPublishAudits_SingleWriter",
                     "trg_Exams_PublishRequiresAudit",
                     // #233: the seventh covered derived fact, and the archive's immutability guard.
                     "trg_LearnerPathWeeks_SingleWriter",
                     // #237: the ninth covered derived fact, and the four-state transition table.
                     "trg_UserCellMasteries_SingleWriter",
                     "trg_UserCellMasteries_StateIsEarned",
                     // #269 audit: a finished practice session is terminal.
                     "trg_PracticeSessions_StatusTransitions",
                 })
        {
            var present = await db.Database
                .SqlQuery<int>($"SELECT COUNT(*) AS Value FROM sys.triggers WHERE name = {name}")
                .SingleAsync();
            Assert.Equal(1, present);
        }


        var viewCount = await db.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS Value FROM sys.views")
            .SingleAsync();
        // #162: vw_LearnerPathIsPremium went with the premium-path feature, leaving eight views.
        // #202: vw_CoverageMatrix is the ninth - one row per coverage cell, always all 120 of them.
        // #235: vw_DailyPracticeVolume is the tenth - the daily ceiling, derived from answers (D12).
        // #243: vw_UserTopicAccuracy is the eleventh - accuracy per (topic, level), the axis D3
        // replaces the flat category aggregation with.
        Assert.Equal(11, viewCount);
    }
}
