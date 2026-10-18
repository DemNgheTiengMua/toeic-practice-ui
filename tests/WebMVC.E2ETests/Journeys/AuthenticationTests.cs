using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;
using WebMVC.E2ETests.Pages;

namespace WebMVC.E2ETests.Journeys;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class AuthenticationTests : PlaywrightTestBase
{
    private LoginPage _loginPage = null!;

    [SetUp]
    public void SetupPages()
    {
        _loginPage = new LoginPage(Page, BaseUrl);
    }

    [Test]
    public async Task Should_SignIn_Successfully_With_Valid_Learner_Credentials()
    {
        await _loginPage.GotoAsync();
        await _loginPage.LoginAsync("learner@demo.local", "Demo1234!");

        // Expect redirect to dashboard or certificates
        await Expect(Page).ToHaveURLAsync(new Regex(@"(Dashboard|certificates)"));

        // Expect authenticated header to be visible with sign out button
        await Expect(_loginPage.SignOutButton).ToBeVisibleAsync();
    }

    [Test]
    public async Task Should_Display_Error_On_Invalid_Credentials()
    {
        await _loginPage.GotoAsync();
        await _loginPage.LoginAsync("learner@demo.local", "WrongPassword!123");

        // Must remain on sign-in page
        await Expect(Page).ToHaveURLAsync(new Regex(@"/auth/sign-in"));

        // Must display failure alert
        await Expect(_loginPage.ErrorAlert).ToBeVisibleAsync();
    }

    [Test]
    public async Task Should_SignOut_Cleanly()
    {
        await _loginPage.GotoAsync();
        await _loginPage.LoginAsync("learner@demo.local", "Demo1234!");
        await Expect(Page).ToHaveURLAsync(new Regex(@"(Dashboard|certificates)", RegexOptions.IgnoreCase));
        await Expect(_loginPage.SignOutButton).ToBeVisibleAsync();

        await _loginPage.SignOutAsync();

        // After sign-out, verify Sign in link is visible
        await Expect(_loginPage.SignInLink).ToBeVisibleAsync();
    }
}
