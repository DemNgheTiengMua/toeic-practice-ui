using BusinessObjects;
using Microsoft.EntityFrameworkCore;

namespace Services.Tests;

/// <summary>
/// #242 / LRN-09: "switching changes the path, the placement and the practice pool together". The
/// shell used to be able to show a new certificate while three screens still read the old one,
/// because "active certificate" was resolved once per service rather than stated as one fact.
/// <para>
/// <b>Why this test reads three services and not one.</b> <c>LearnerContext.Active</c> changing is
/// the cheap half. The criterion names the path, the placement and the practice pool, and each of
/// those is a different reader of <c>GetActiveCertIdAsync</c> — so a switch that updated only the
/// context would pass a test written against the context alone.
/// </para>
/// </summary>
public sealed partial class LearnerServiceTests
{
    private async Task SeedShellCertificateAsync(string code, params string[] modalities)
    {
        await SeedCertificateAsync(code);

        // SeedCertificateAsync writes one listening skill; give this certificate its own set.
        await database.SqlAsync($"""
            DECLARE @cert INT = (SELECT CertId FROM Certificates WHERE Code = '{code}');
            DELETE FROM Sections WHERE CertId = @cert;
            DELETE FROM Skills WHERE CertId = @cert;
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder)
              SELECT @cert, 'S' + CAST(v.number AS VARCHAR(2)), 'Skill ' + CAST(v.number AS VARCHAR(2)), v.modality, v.number
              FROM (VALUES
                {string.Join(",\n                ", modalities.Select((m, i) =>
                    $"({i + 1}, '{m}')"))}
              ) AS v(number, modality);
            """);
    }

    private Task SeedGoalDateAsync(int userId, string certCode, DateOnly date) =>
        database.SqlAsync($"""
            INSERT INTO TargetBands (UserId, CertId, TargetBandCode, TargetDate, WeeklyStudyHours)
            VALUES ({userId}, (SELECT CertId FROM Certificates WHERE Code = '{certCode}'),
                    'HIGH', '{date:yyyy-MM-dd}', 6.0);
            """);

    /// <summary>
    /// <b>The input that passes a weaker guard while being wrong:</b> a test that switches the
    /// certificate and asserts only <c>GetContextAsync().Active</c>. The shell would render the new
    /// code while the practice bank still served the old paper - the exact defect the criterion
    /// names. This asserts all four readers in the same test.
    /// </summary>
    [Fact]
    public async Task One_switch_moves_the_context_the_path_the_placement_and_the_practice_pool_together()
    {
        var four = Unique("F");
        var two = Unique("W");
        await SeedShellCertificateAsync(four, "listening", "reading", "speaking", "writing");
        await SeedShellCertificateAsync(two, "listening", "reading");
        var userId = await SeedLearnerAsync(four);

        var contextBefore = await CreateService().GetContextAsync(userId);
        Assert.Equal(four, contextBefore.Active.Code);
        Assert.Equal(4, contextBefore.Skills.Count);

        await CreateService().SwitchCertificateAsync(userId, two);

        // Reading each service with its OWN context, as the request pipeline does per request: a
        // cached id inside one service is what would hide the defect.
        var context = await CreateService().GetContextAsync(userId);
        Assert.Equal(two, context.Active.Code);
        Assert.Equal([Modality.Listening, Modality.Reading], context.Skills);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var practice = new PracticeService(db, new LearnerService(db));
        var placement = new PlacementService(db, new LearnerService(db));
        var path = new LearningPathService(db, new LearnerService(db));

        Assert.Equal(two, (await practice.GetPracticeSectionsAsync(userId)).Certificate.Code);
        Assert.Equal(two, (await placement.GetPreflightAsync(userId)).CertCode);
        Assert.Equal(two, (await path.GetActivePathAsync(userId)).ActiveCert.Code);
    }

    /// <summary>
    /// The shell's exam-date indicator reads the ACTIVE certificate's goal. <b>The input that passes
    /// the "a date renders" guard while being wrong:</b> reading the learner's newest goal row
    /// regardless of certificate, which would show the other certificate's date after a switch.
    /// </summary>
    [Fact]
    public async Task The_exam_date_is_the_active_certificates_goal_and_null_when_none_is_set()
    {
        var first = Unique("E");
        var second = Unique("G");
        await SeedShellCertificateAsync(first, "listening", "reading");
        await SeedShellCertificateAsync(second, "listening", "reading");
        var userId = await SeedLearnerAsync(first);

        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(90);
        await SeedGoalDateAsync(userId, first, date);

        Assert.Equal(date, (await CreateService().GetContextAsync(userId)).ExamDate);

        // A certificate with no goal reports no date - never a placeholder.
        await CreateService().SwitchCertificateAsync(userId, second);
        Assert.Null((await CreateService().GetContextAsync(userId)).ExamDate);
    }
}
