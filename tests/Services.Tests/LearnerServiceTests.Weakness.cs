using BusinessObjects;

namespace Services.Tests;

public sealed partial class LearnerServiceTests
{
    private Task SeedAdditionalSectionAsync(string certCode, string sectionCode, string sectionName, int displayOrder) =>
        database.SqlAsync($"""
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');
            DECLARE @skill INT = (SELECT TOP 1 SkillId FROM Skills WHERE CertId = @cert);
            UPDATE Sections SET DisplayOrder = 1 WHERE CertId = @cert AND Code = 'P1';
            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder)
            VALUES (@skill, @cert, 'listening', '{sectionCode}', N'{sectionName}', {displayOrder});
            """);

    private Task SeedCategorizedPracticeAnswersAsync(
        int userId,
        string certCode,
        string sectionCode,
        string categoryCode,
        string categoryName,
        int correctCount,
        int totalCount) =>
        database.SqlAsync($"""
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');
            DECLARE @skill INT = (SELECT TOP 1 SkillId FROM Skills WHERE CertId = @cert);
            DECLARE @sec INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = '{sectionCode}');
            DECLARE @exam INT = (SELECT TOP 1 ExamId FROM Exams WHERE CertId = @cert AND Purpose = 'mock');

            IF NOT EXISTS (SELECT 1 FROM QuestionCategories WHERE CertId = @cert AND Code = '{categoryCode}')
              INSERT INTO QuestionCategories (CertId, Code, Name)
              VALUES (@cert, '{categoryCode}', N'{categoryName}');
            DECLARE @cat INT = (SELECT CategoryId FROM QuestionCategories WHERE CertId = @cert AND Code = '{categoryCode}');

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
            VALUES (@exam, @cert, @sec, 1);
            DECLARE @grp INT = SCOPE_IDENTITY();

            INSERT INTO PracticeSessions (UserId, SectionId, Modality, QuestionCount, Status, CorrectCount, FinishedAt)
            VALUES ({userId}, @sec, 'listening', {totalCount}, 'finished', {correctCount}, SYSDATETIMEOFFSET());
            DECLARE @sess INT = SCOPE_IDENTITY();

            DECLARE @i INT = 1;
            WHILE @i <= {totalCount}
            BEGIN
              INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, CategoryId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
              VALUES (@grp, @exam, @sec, @skill, @cat, CONCAT(N'Q-', '{categoryCode}-', @i), 2, 'mcq', @i);
              DECLARE @q INT = SCOPE_IDENTITY();

              INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
                (@q, 'mcq', 'A', N'Correct', 1),
                (@q, 'mcq', 'B', N'Wrong', 0);

              DECLARE @isCorr BIT = CASE WHEN @i <= {correctCount} THEN 1 ELSE 0 END;
              DECLARE @opt INT = (SELECT OptionId FROM QuestionOptions WHERE QuestionId = @q AND IsCorrect = @isCorr);

              INSERT INTO PracticeAnswers (SessionId, QuestionId, SectionId, SelectedOptionId, IsCorrect, DisplayOrder)
              VALUES (@sess, @q, @sec, @opt, @isCorr, @i);

              SET @i = @i + 1;
            END
            """);

    private Task SeedSectionPracticeAnswersAsync(
        int userId,
        string certCode,
        string sectionCode,
        int correctCount,
        int totalCount) =>
        database.SqlAsync($"""
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');
            DECLARE @skill INT = (SELECT TOP 1 SkillId FROM Skills WHERE CertId = @cert);
            DECLARE @sec INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = '{sectionCode}');
            DECLARE @exam INT = (SELECT TOP 1 ExamId FROM Exams WHERE CertId = @cert AND Purpose = 'mock');

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
            VALUES (@exam, @cert, @sec, 1);
            DECLARE @grp INT = SCOPE_IDENTITY();

            INSERT INTO PracticeSessions (UserId, SectionId, Modality, QuestionCount, Status, CorrectCount, FinishedAt)
            VALUES ({userId}, @sec, 'listening', {totalCount}, 'finished', {correctCount}, SYSDATETIMEOFFSET());
            DECLARE @sess INT = SCOPE_IDENTITY();

            DECLARE @i INT = 1;
            WHILE @i <= {totalCount}
            BEGIN
              INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
              VALUES (@grp, @exam, @sec, @skill, CONCAT(N'Q-Sec-', '{sectionCode}-', @i), 2, 'mcq', @i);
              DECLARE @q INT = SCOPE_IDENTITY();

              INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
                (@q, 'mcq', 'A', N'Correct', 1),
                (@q, 'mcq', 'B', N'Wrong', 0);

              DECLARE @isCorr BIT = CASE WHEN @i <= {correctCount} THEN 1 ELSE 0 END;
              DECLARE @opt INT = (SELECT OptionId FROM QuestionOptions WHERE QuestionId = @q AND IsCorrect = @isCorr);

              INSERT INTO PracticeAnswers (SessionId, QuestionId, SectionId, SelectedOptionId, IsCorrect, DisplayOrder)
              VALUES (@sess, @q, @sec, @opt, @isCorr, @i);

              SET @i = @i + 1;
            END
            """);

    [Fact]
    public async Task Weakness_for_new_learner_populates_untested_sections_ordered_by_display_order()
    {
        var code = Unique("WU");
        await SeedCertificateAsync(code);
        // Insert P3 (order 3) before P2 (order 2) to verify DisplayOrder sorting
        await SeedAdditionalSectionAsync(code, "P3", $"Conversations {code}", displayOrder: 3);
        await SeedAdditionalSectionAsync(code, "P2", $"Question-Response {code}", displayOrder: 2);
        var userId = await SeedLearnerAsync(code);

        var dashboard = await CreateService().GetDashboardAsync(userId);

        Assert.Equal(new[] { "P1", "P2", "P3" }, dashboard.WeaknessStrips.Select(w => w.Code));
        Assert.All(dashboard.WeaknessStrips, strip =>
        {
            Assert.Equal("section", strip.Scope);
            Assert.Equal("UNTESTED", strip.Tier);
            Assert.Equal(0, strip.Answered);
            Assert.Equal(0, strip.Correct);
            Assert.Equal(0, strip.AccuracyPercent);
            Assert.Equal("/practice", strip.ActionUrl);
        });
    }

    [Fact]
    public async Task Weakness_classifies_tiers_sorts_by_accuracy_then_answered_then_display_order_and_caps_at_five()
    {
        var code = Unique("WT");
        await SeedCertificateAsync(code);
        await SeedAdditionalSectionAsync(code, "P2", $"Question-Response {code}", displayOrder: 2);
        var userId = await SeedLearnerAsync(code);

        // 1. CRITICAL (< 50%): 1/4 = 25%
        await SeedCategorizedPracticeAnswersAsync(userId, code, "P1", "cat_crit", "Critical Cat", correctCount: 1, totalCount: 4);
        // 2. BORDER (50-64%): 6/10 = 60% (higher Answered = 10 wins tie-break over Answered = 5)
        await SeedCategorizedPracticeAnswersAsync(userId, code, "P2", "cat_bord_10", "Border 10", correctCount: 6, totalCount: 10);
        // 3. BORDER (50-64%): 3/5 = 60% (Answered = 5, in P1 with DisplayOrder = 1)
        await SeedCategorizedPracticeAnswersAsync(userId, code, "P1", "cat_bord_5", "Border 5", correctCount: 3, totalCount: 5);
        // 4. STABLE (65-79%): 7/10 = 70%
        await SeedCategorizedPracticeAnswersAsync(userId, code, "P1", "cat_stab", "Stable Cat", correctCount: 7, totalCount: 10);
        // 5. MASTERY (>= 80%): 4/5 = 80%
        await SeedCategorizedPracticeAnswersAsync(userId, code, "P2", "cat_mast_80", "Mastery 80", correctCount: 4, totalCount: 5);
        // 6. MASTERY (>= 80%): 5/5 = 100% (6th item, capped out)
        await SeedCategorizedPracticeAnswersAsync(userId, code, "P2", "cat_mast_100", "Mastery 100", correctCount: 5, totalCount: 5);

        var dashboard = await CreateService().GetDashboardAsync(userId);

        Assert.Equal(5, dashboard.WeaknessStrips.Count);
        Assert.Equal(
            new[] { "cat_crit", "cat_bord_10", "cat_bord_5", "cat_stab", "cat_mast_80" },
            dashboard.WeaknessStrips.Select(w => w.Code));
        Assert.Equal(
            new[] { "CRITICAL", "BORDER", "BORDER", "STABLE", "MASTERY" },
            dashboard.WeaknessStrips.Select(w => w.Tier));
        Assert.Equal(
            new[] { 25, 60, 60, 70, 80 },
            dashboard.WeaknessStrips.Select(w => w.AccuracyPercent));
        Assert.All(dashboard.WeaknessStrips, strip =>
        {
            Assert.Equal("category", strip.Scope);
            Assert.Equal("/practice", strip.ActionUrl);
        });
    }

    [Fact]
    public async Task Weakness_tie_breaks_equal_accuracy_and_answered_by_display_order_for_uncategorized_sections()
    {
        var code = Unique("WS");
        await SeedCertificateAsync(code);
        await SeedAdditionalSectionAsync(code, "P3", $"Conversations {code}", displayOrder: 3);
        await SeedAdditionalSectionAsync(code, "P2", $"Question-Response {code}", displayOrder: 2);
        var userId = await SeedLearnerAsync(code);

        // Seed P3 first and P2 second with identical 2/4 = 50% (BORDER), and P1 with 4/5 = 80% (MASTERY)
        await SeedSectionPracticeAnswersAsync(userId, code, "P3", correctCount: 2, totalCount: 4);
        await SeedSectionPracticeAnswersAsync(userId, code, "P2", correctCount: 2, totalCount: 4);
        await SeedSectionPracticeAnswersAsync(userId, code, "P1", correctCount: 4, totalCount: 5);

        var dashboard = await CreateService().GetDashboardAsync(userId);

        Assert.Equal(new[] { "P2", "P3", "P1" }, dashboard.WeaknessStrips.Select(w => w.Code));
        Assert.Equal(new[] { "BORDER", "BORDER", "MASTERY" }, dashboard.WeaknessStrips.Select(w => w.Tier));
        Assert.All(dashboard.WeaknessStrips, s => Assert.Equal("section", s.Scope));
    }

    [Fact]
    public async Task Weakness_isolates_active_certificate_and_current_learner()
    {
        var toeic = Unique("WT");
        var ielts = Unique("WI");
        await SeedCertificateAsync(toeic);
        await SeedCertificateAsync(ielts);

        var mine = await SeedLearnerAsync(toeic);
        var theirs = await SeedLearnerAsync(toeic);

        // Another learner has critical weakness on TOEIC
        await SeedCategorizedPracticeAnswersAsync(theirs, toeic, "P1", "toeic_trap", "TOEIC Trap", correctCount: 0, totalCount: 5);

        // Current learner has weakness only on IELTS
        await SeedCategorizedPracticeAnswersAsync(mine, ielts, "P1", "ielts_trap", "IELTS Trap", correctCount: 1, totalCount: 5);

        // On TOEIC, current learner only sees TOEIC's untested section P1
        var toeicDash = await CreateService().GetDashboardAsync(mine);
        var toeicStrip = Assert.Single(toeicDash.WeaknessStrips);
        Assert.Equal("section", toeicStrip.Scope);
        Assert.Equal("P1", toeicStrip.Code);
        Assert.Equal("UNTESTED", toeicStrip.Tier);
        Assert.Equal(0, toeicStrip.Answered);

        // Switching to IELTS shows current learner's IELTS category weakness
        await CreateService().SwitchCertificateAsync(mine, ielts);
        var ieltsDash = await CreateService().GetDashboardAsync(mine);
        var ieltsStrip = Assert.Single(ieltsDash.WeaknessStrips);
        Assert.Equal("category", ieltsStrip.Scope);
        Assert.Equal("ielts_trap", ieltsStrip.Code);
        Assert.Equal("CRITICAL", ieltsStrip.Tier);
        Assert.Equal(20, ieltsStrip.AccuracyPercent);
        Assert.Equal(5, ieltsStrip.Answered);
        Assert.Equal(1, ieltsStrip.Correct);
    }
}

