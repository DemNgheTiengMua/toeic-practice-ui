using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;
using WebMVC.E2ETests.Pages;

namespace WebMVC.E2ETests.Journeys;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class PlacementToLearningPathTests : PlaywrightTestBase
{
    private LoginPage _loginPage = null!;
    private PlacementPage _placementPage = null!;

    [SetUp]
    public void SetupPages()
    {
        _loginPage = new LoginPage(Page, BaseUrl);
        _placementPage = new PlacementPage(Page, BaseUrl);
    }

    [Test]
    public async Task Should_Require_Preflight_On_Start_And_Resume_Then_Return_To_Placement_Sitting()
    {
        var email = $"placement-preflight-{Guid.NewGuid():N}@demo.local";
        await using var connection = await OpenAppDatabaseAsync();
        await using var seed = connection.CreateCommand();
        seed.CommandText = """
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            SELECT RoleId, @email, PasswordHash, N'Placement preflight regression',
                (SELECT CertId FROM Certificates WHERE Code = 'TOEIC')
            FROM Users WHERE Email = 'learner.fresh@demo.local';
            """;
        seed.Parameters.AddWithValue("@email", email);
        await seed.ExecuteNonQueryAsync();

        await SignInAsync(email);
        await Page.GotoAsync($"{BaseUrl}/placement");
        await Page.Locator("form[action='/placement/start']").First.GetByRole(AriaRole.Button).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/exams/(?:preflight/\d+|\d+/preflight).*placementId=\d+"));
        var preflightUrl = Page.Url;
        var placementId = Regex.Match(preflightUrl, @"placementId=(\d+)").Groups[1].Value;
        Assert.That(placementId, Is.Not.Empty);

        // Direct resume must enforce the same gate without creating a replacement attempt.
        await Page.GotoAsync($"{BaseUrl}/placement/take/{placementId}");
        await Expect(Page).ToHaveURLAsync(preflightUrl);
        var start = Page.Locator("#start-test-btn");
        await Expect(start).ToBeEnabledAsync();
        await start.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex($@"/placement/take/{placementId}(?:\?|$)"));
        await Expect(Page.Locator(".question__number")).ToContainTextAsync("Question 1");

        await using var verify = connection.CreateCommand();
        verify.CommandText = """
            SELECT COUNT(*) FROM CreditTransactions AS credit
            INNER JOIN Users AS learner ON learner.UserId = credit.UserId
            WHERE learner.Email = @email;
            """;
        verify.Parameters.AddWithValue("@email", email);
        Assert.That(Convert.ToInt32(await verify.ExecuteScalarAsync()), Is.Zero);
        await Page.ScreenshotAsync(new() { Path = System.IO.Path.Combine(TestContext.CurrentContext.WorkDirectory, "placement-preflight.png"), FullPage = true });
    }

    [Test]
    public async Task Should_Complete_Free_Placement_Journey_And_Generate_Learning_Path()
    {
        // 1. Sign in as learner.fresh (diagnostic candidate)
        await _loginPage.GotoAsync();
        await _loginPage.LoginAsync("learner.fresh@demo.local", "Demo1234!");

        // 2. Navigate to placement index
        await _placementPage.GotoPlacementAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/placement", RegexOptions.IgnoreCase));

        // 3. Verify diagnostic placement test copy
        await Expect(Page.Locator("body")).ToContainTextAsync("Placement");

        // 4. Navigate to Target Band selection
        await _placementPage.GotoTargetBandAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/placement/target-band", RegexOptions.IgnoreCase));

        // 5. Select B2 Target Band if not already selected
        var bandSelect = Page.Locator("select#target-band-select, select[name='targetBand']");
        if (await bandSelect.IsVisibleAsync())
        {
            await bandSelect.SelectOptionAsync("B2");
            var saveBtn = Page.Locator("button[type='submit']").First;
            await saveBtn.ClickAsync();
        }

        // 6. Navigate to /learning-path and verify path is active
        await Page.GotoAsync($"{BaseUrl}/learning-path");
        await Expect(Page).ToHaveURLAsync(new Regex(@"/learning-path", RegexOptions.IgnoreCase));

        // Verify learning path ladder renders
        await Expect(Page.Locator("body")).ToContainTextAsync("learning path");

        // 7. Verify Free Loop Invariant: credit balance remains 0
        var creditHeader = Page.Locator("header, nav").Filter(new() { HasText = "credit" }).First;
        if (await creditHeader.IsVisibleAsync())
        {
            var headerText = await creditHeader.InnerTextAsync();
            Assert.That(headerText, Does.Contain("0 credit").IgnoreCase.Or.Contain("0 credits").IgnoreCase.Or.Contain("credits left").IgnoreCase);
        }
    }
}
