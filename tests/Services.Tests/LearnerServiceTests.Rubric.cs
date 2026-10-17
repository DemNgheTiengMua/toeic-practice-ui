using BusinessObjects;
using Xunit;

namespace Services.Tests;

public sealed partial class LearnerServiceTests
{
    [Fact]
    public async Task Dashboard_populates_latest_rubric_highlight_from_lowest_scoring_criterion()
    {
        var certCode = Unique("RB");
        await SeedCertificateAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);

        await database.SqlAsync($"""
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{certCode}');

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder)
            VALUES (@cert, 'WRIT', N'Writing', 'writing', 2);
            DECLARE @skill INT = SCOPE_IDENTITY();

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder)
            VALUES (@skill, @cert, 'writing', 'W1', N'Essay {certCode}', 2);
            DECLARE @sec INT = SCOPE_IDENTITY();

            INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, MaxScore, Weight, DisplayOrder) VALUES
              (@cert, @skill, 'GRAM', N'Grammar & Accuracy', 5.00, 1.00, 1),
              (@cert, @skill, 'VOCAB', N'Lexical Resource', 5.00, 1.00, 2);
            DECLARE @critGram INT = (SELECT CriterionId FROM GradingCriteria WHERE CertId = @cert AND Code = 'GRAM');
            DECLARE @critVocab INT = (SELECT CriterionId FROM GradingCriteria WHERE CertId = @cert AND Code = 'VOCAB');

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount)
            VALUES (@cert, N'Writing Practice {certCode}', 'published', 60, 'writing_practice', 1);
            DECLARE @exam INT = SCOPE_IDENTITY();

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
            VALUES (@exam, @cert, @sec, 1);
            DECLARE @grp INT = SCOPE_IDENTITY();

            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, MinWords, MaxWords, DisplayOrder)
            VALUES (@grp, @exam, @sec, @skill, N'Write an essay.', 1, 'free_response', 50, 250, 1);
            DECLARE @q INT = SCOPE_IDENTITY();

            INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt, SubmittedAt, TotalScore, BandCode)
            VALUES ({userId}, @exam, @cert, 'graded', '2026-10-10T09:00:00+00:00', '2026-10-10T10:00:00+00:00', '2026-10-10T09:45:00+00:00', NULL, NULL);
            DECLARE @attempt INT = SCOPE_IDENTITY();

            INSERT INTO FreeResponses (AttemptId, QuestionId, ExamId, SkillId, QuestionType, ResponseText, Status, SubmittedAt)
            VALUES (@attempt, @q, @exam, @skill, 'free_response', N'Sample response text.', 'graded', '2026-10-10T09:45:00+00:00');
            DECLARE @fr INT = SCOPE_IDENTITY();

            INSERT INTO AiGradings (FreeResponseId, SkillId, Status, OverallScaled, BandCode, Model, RequestedAt, CompletedAt)
            VALUES (@fr, @skill, 'done', 65.00, NULL, N'fake-grader', '2026-10-10T09:45:00+00:00', '2026-10-10T09:46:00+00:00');
            DECLARE @grading INT = SCOPE_IDENTITY();

            INSERT INTO AiGradingScores (GradingId, CriterionId, SkillId, Score, Comment) VALUES
              (@grading, @critVocab, @skill, 4.50, N'Good range of connectors.'),
              (@grading, @critGram, @skill, 2.00, N'Frequent tense shifts in body paragraphs.');
            """);

        var dashboard = await CreateService().GetDashboardAsync(userId);

        Assert.NotNull(dashboard.LatestRubricHighlight);
        Assert.Equal("Writing", dashboard.LatestRubricHighlight.SkillName);
        Assert.Equal("Grammar & Accuracy", dashboard.LatestRubricHighlight.CriterionName);
        Assert.Equal(2.00m, dashboard.LatestRubricHighlight.Score);
        Assert.Equal(5.00m, dashboard.LatestRubricHighlight.MaxScore);
        Assert.Equal("Frequent tense shifts in body paragraphs.", dashboard.LatestRubricHighlight.Comment);
        Assert.Equal(ExamPurpose.WritingPractice, dashboard.LatestRubricHighlight.Purpose);
    }

    [Fact]
    public async Task Dashboard_rubric_highlight_respects_certificate_and_learner_isolation()
    {
        var certA = Unique("RA");
        var certB = Unique("RB");
        await SeedCertificateAsync(certA);
        await SeedCertificateAsync(certB);
        var learner1 = await SeedLearnerAsync(certA);
        var learner2 = await SeedLearnerAsync(certA);

        await database.SqlAsync($"""
            DECLARE @certA INT = (SELECT CertId FROM Certificates WHERE Code = '{certA}');
            DECLARE @certB INT = (SELECT CertId FROM Certificates WHERE Code = '{certB}');

            -- Seed speaking rubric for learner2 on certA, and for learner1 on certB
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES (@certA, 'SPK', N'Speaking', 'speaking', 2);
            DECLARE @skillA INT = SCOPE_IDENTITY();
            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder) VALUES (@skillA, @certA, 'speaking', 'S1', N'Read Aloud', 2);
            DECLARE @secA INT = SCOPE_IDENTITY();
            INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, MaxScore, Weight, DisplayOrder) VALUES (@certA, @skillA, 'PRON', N'Pronunciation', 5.00, 1.00, 1);
            DECLARE @critA INT = SCOPE_IDENTITY();
            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount) VALUES (@certA, N'Speaking {certA}', 'published', 20, 'speaking_practice', 1);
            DECLARE @examA INT = SCOPE_IDENTITY();
            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@examA, @certA, @secA, 1);
            DECLARE @grpA INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder) VALUES (@grpA, @examA, @secA, @skillA, N'Speak now.', 1, 'free_response', 1);
            DECLARE @qA INT = SCOPE_IDENTITY();

            INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt, SubmittedAt, TotalScore, BandCode)
            VALUES ({learner2}, @examA, @certA, 'graded', '2026-10-10T09:00:00+00:00', '2026-10-10T10:00:00+00:00', '2026-10-10T09:20:00+00:00', NULL, NULL);
            DECLARE @attA INT = SCOPE_IDENTITY();
            INSERT INTO FreeResponses (AttemptId, QuestionId, ExamId, SkillId, QuestionType, AudioPath, Status, SubmittedAt)
            VALUES (@attA, @qA, @examA, @skillA, 'free_response', N'/audio/1.webm', 'graded', '2026-10-10T09:20:00+00:00');
            DECLARE @frA INT = SCOPE_IDENTITY();
            INSERT INTO AiGradings (FreeResponseId, SkillId, Status, OverallScaled, BandCode, Model, RequestedAt, CompletedAt)
            VALUES (@frA, @skillA, 'done', 50.00, NULL, N'fake', '2026-10-10T09:20:00+00:00', '2026-10-10T09:21:00+00:00');
            DECLARE @grA INT = SCOPE_IDENTITY();
            INSERT INTO AiGradingScores (GradingId, CriterionId, SkillId, Score, Comment)
            VALUES (@grA, @critA, @skillA, 1.50, N'Other learner comment');

            -- Cert B for learner1
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES (@certB, 'SPK', N'Speaking', 'speaking', 2);
            DECLARE @skillB INT = SCOPE_IDENTITY();
            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder) VALUES (@skillB, @certB, 'speaking', 'S1', N'Read Aloud', 2);
            DECLARE @secB INT = SCOPE_IDENTITY();
            INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, MaxScore, Weight, DisplayOrder) VALUES (@certB, @skillB, 'FLUE', N'Fluency', 5.00, 1.00, 1);
            DECLARE @critB INT = SCOPE_IDENTITY();
            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount) VALUES (@certB, N'Speaking {certB}', 'published', 20, 'speaking_practice', 1);
            DECLARE @examB INT = SCOPE_IDENTITY();
            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@examB, @certB, @secB, 1);
            DECLARE @grpB INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder) VALUES (@grpB, @examB, @secB, @skillB, N'Speak B.', 1, 'free_response', 1);
            DECLARE @qB INT = SCOPE_IDENTITY();

            INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt, SubmittedAt, TotalScore, BandCode)
            VALUES ({learner1}, @examB, @certB, 'graded', '2026-10-10T09:00:00+00:00', '2026-10-10T10:00:00+00:00', '2026-10-10T09:20:00+00:00', NULL, NULL);
            DECLARE @attB INT = SCOPE_IDENTITY();
            INSERT INTO FreeResponses (AttemptId, QuestionId, ExamId, SkillId, QuestionType, AudioPath, Status, SubmittedAt)
            VALUES (@attB, @qB, @examB, @skillB, 'free_response', N'/audio/2.webm', 'graded', '2026-10-10T09:20:00+00:00');
            DECLARE @frB INT = SCOPE_IDENTITY();
            INSERT INTO AiGradings (FreeResponseId, SkillId, Status, OverallScaled, BandCode, Model, RequestedAt, CompletedAt)
            VALUES (@frB, @skillB, 'done', 55.00, NULL, N'fake', '2026-10-10T09:20:00+00:00', '2026-10-10T09:21:00+00:00');
            DECLARE @grB INT = SCOPE_IDENTITY();
            INSERT INTO AiGradingScores (GradingId, CriterionId, SkillId, Score, Comment)
            VALUES (@grB, @critB, @skillB, 2.50, N'Cert B fluency note');
            """);

        var dashboardA = await CreateService().GetDashboardAsync(learner1);
        Assert.Null(dashboardA.LatestRubricHighlight);

        await CreateService().SwitchCertificateAsync(learner1, certB);
        var dashboardB = await CreateService().GetDashboardAsync(learner1);
        Assert.NotNull(dashboardB.LatestRubricHighlight);
        Assert.Equal("Fluency", dashboardB.LatestRubricHighlight.CriterionName);
        Assert.Equal("Cert B fluency note", dashboardB.LatestRubricHighlight.Comment);
    }
}
